using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Identifiers;
using Wip.Builder;
using Wip.Factory.Adaptation;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Contracts.Profiles;

namespace Wip.Factory.Wip.Fanout;

/// <summary>One bounded execution request produced by the factory for a pinned profile.</summary>
public sealed record FactoryWorkDispatch(
    SessionId SessionId,
    WorkItemId WorkItemId,
    StageName Stage,
    AgentDefinitionId AgentDefinitionId,
    AgentProfileVersion AgentProfileVersion,
    string Task,
    string? SystemPrompt = null);

/// <summary>What one dispatch produced. Tokens are separated so cost is derivable (F-E.7).</summary>
public sealed record FactoryAgentResult(
    SessionId SessionId,
    AgentProfileVersion AgentProfileVersion,
    string ModelId,
    bool Completed,
    string? FailureCategory,
    int InputTokens,
    int OutputTokens,
    IReadOnlyList<string> OutputArtifactRefs)
{
    public bool Succeeded => Completed;
}

/// <summary>
/// The only seam through which the factory executes an agent. The production implementation delegates
/// to <c>WipRuntimeOrchestrator.RunAgentAsync</c>, so the factory never owns a model/tool loop
/// (F-E.2, source §9.1).
/// </summary>
public interface IFactoryAgentExecutionPort
{
    ValueTask<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        WipBuilder builder,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the pinned profile, verifies every referenced capability is admitted, authorizes the
/// profile's requested permissions, opens a scope from the <b>host</b> provider, and executes one
/// bounded session (F-E.1–F-E.9).
/// </summary>
public sealed class FactoryAgentDispatcher
{
    public const string ProfileNotPinnedCategory = "profile-not-pinned";
    public const string CapabilityUnavailableCategory = "capability-unavailable";
    public const string PermissionDeniedCategory = "permission-denied";
    public const string EmptyModelContentCategory = "empty-model-content";
    public const string NoSessionCategory = "session-not-found";

    private readonly IAgentProfileRegistry _profiles;
    private readonly IReadOnlyDictionary<AgentDefinitionId, FactoryAgentDefinition> _definitions;
    private readonly ICapabilityCatalog _catalog;
    private readonly AgentProfilePermissionResolver _authorization;
    private readonly IFactoryAgentExecutionPort _port;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WipBuilder _builder;

    public FactoryAgentDispatcher(
        IAgentProfileRegistry profiles,
        IEnumerable<FactoryAgentDefinition> definitions,
        ICapabilityCatalog catalog,
        AgentProfilePermissionResolver authorization,
        IFactoryAgentExecutionPort port,
        IServiceScopeFactory scopeFactory,
        WipBuilder builder)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        ArgumentNullException.ThrowIfNull(definitions);
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _port = port ?? throw new ArgumentNullException(nameof(port));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _definitions = definitions.ToDictionary(static definition => definition.AgentDefinitionId);
    }

    public async ValueTask<FactoryAgentResult> ExecuteAsync(
        FactoryWorkDispatch dispatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        if (!_definitions.TryGetValue(dispatch.AgentDefinitionId, out var definition))
        {
            return Refuse(dispatch, ProfileNotPinnedCategory, "definition-not-registered", 0, 0);
        }

        var profile = await _profiles
            .GetAsync(new AgentProfileId("factory." + ShortName(dispatch.AgentDefinitionId)), dispatch.AgentProfileVersion, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return Refuse(dispatch, ProfileNotPinnedCategory, "version-not-published", 0, 0);
        }

        // Every capability the profile pins must be admitted; refuse before any session work (F-E.8).
        foreach (var reference in profile.Draft.Skills.Concat(profile.Draft.Tools))
        {
            if (!_catalog.TryResolve(reference.CapabilityId, reference.Version, out _))
            {
                return Refuse(
                    dispatch,
                    CapabilityUnavailableCategory,
                    $"{reference.CapabilityId.Value}@{reference.Version.Value}",
                    0,
                    0);
            }
        }

        // Authorization precedes execution so a denial creates no session side effects (F-D.6).
        var authorization = _authorization.Resolve(new AgentProfileAuthorizationContext(
            profile,
            definition,
            SessionId: dispatch.SessionId));

        if (!authorization.Allowed)
        {
            return Refuse(dispatch, PermissionDeniedCategory, authorization.Reason ?? "denied", 0, 0);
        }

        var executionPolicy = profile.Draft.ExecutionPolicy;
        var request = new AgentExecutionRequest(
            dispatch.SessionId,
            dispatch.Task,
            profile.Draft.Model.PreferredModel,
            dispatch.SystemPrompt,
            tools: null,
            initialMessages: null,
            new AgentLoopOptions
            {
                MaxSteps = executionPolicy.MaxSteps,
                MaxTotalTokens = executionPolicy.MaxTotalTokens,
                Timeout = TimeSpan.FromSeconds(executionPolicy.TimeoutSeconds),
                MaxConsecutiveToolFailures = executionPolicy.MaxConsecutiveToolFailures
            });

        // The scope comes from the host provider; the factory never builds its own (F-E.4).
        using var scope = _scopeFactory.CreateScope();
        var result = await _port
            .ExecuteAsync(request, _builder, scope.ServiceProvider, cancellationToken)
            .ConfigureAwait(false);

        var inputTokens = result.Usage?.InputTokens ?? 0;
        var outputTokens = result.Usage?.OutputTokens ?? 0;

        if (result.Succeeded && string.IsNullOrWhiteSpace(result.FinalMessage?.Content))
        {
            // A reasoning model can consume tokens and return no content (RISK-003). That is a failure,
            // never a completed step that would record an empty artifact as success.
            return Refuse(dispatch, EmptyModelContentCategory, "model-returned-no-content", inputTokens, outputTokens);
        }

        return new FactoryAgentResult(
            dispatch.SessionId,
            profile.Version,
            request.ModelId,
            result.Succeeded,
            result.Succeeded ? null : result.Status.ToString(),
            inputTokens,
            outputTokens,
            []);
    }

    private static string ShortName(AgentDefinitionId definitionId)
        => definitionId.Value.StartsWith("factory.", StringComparison.Ordinal)
            ? definitionId.Value["factory.".Length..]
            : definitionId.Value;

    private static FactoryAgentResult Refuse(
        FactoryWorkDispatch dispatch,
        string category,
        string detail,
        int inputTokens,
        int outputTokens) =>
        new(dispatch.SessionId, dispatch.AgentProfileVersion, string.Empty, false, category + ":" + detail, inputTokens, outputTokens, []);
}
