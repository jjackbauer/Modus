using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Contracts.Profiles;

namespace Wip.Factory.Adaptation;

/// <summary>Everything the host knows at the moment it authorises a profile.</summary>
public sealed record AgentProfileAuthorizationContext(
    AgentProfile Profile,
    FactoryAgentDefinition Definition,
    CapabilityId? CapabilityId = null,
    PluginOrigin? PluginOrigin = null,
    SessionId? SessionId = null);

/// <summary>The authorization verdict for one dispatch.</summary>
public sealed record AgentProfileAuthorizationDecision(
    bool Allowed,
    IReadOnlyList<string> DeniedPermissions,
    string? Reason)
{
    public static AgentProfileAuthorizationDecision Accept() => new(true, [], null);

    public static AgentProfileAuthorizationDecision Deny(IReadOnlyList<string> denied, string reason)
        => new(false, denied, reason);
}

/// <summary>
/// Resolves a profile's <i>requested</i> permissions into an authorization verdict (F-D.6). The ceiling
/// is checked first, then the host's <see cref="IPermissionEvaluator"/>; a request can never widen the
/// ceiling, and the caller must refuse dispatch before creating any session when this denies.
/// </summary>
public sealed class AgentProfilePermissionResolver
{
    private readonly IPermissionEvaluator? _evaluator;

    public AgentProfilePermissionResolver(IPermissionEvaluator? evaluator = null) => _evaluator = evaluator;

    public AgentProfileAuthorizationDecision Resolve(AgentProfileAuthorizationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requested = context.Profile.Draft.Permissions.Requested;

        var outsideCeiling = requested
            .Where(permission => !context.Definition.PermissionCeiling.Contains(permission, StringComparer.Ordinal))
            .ToArray();

        if (outsideCeiling.Length > 0)
        {
            return AgentProfileAuthorizationDecision.Deny(
                outsideCeiling,
                "Requested permissions exceed the definition ceiling: " + string.Join(", ", outsideCeiling) + ".");
        }

        if (_evaluator is null)
        {
            return AgentProfileAuthorizationDecision.Accept();
        }

        var denied = new List<string>();
        string? reason = null;

        foreach (var permission in requested)
        {
            var decision = _evaluator.Evaluate(new PermissionRequest(
                permission,
                context.CapabilityId,
                context.PluginOrigin,
                context.SessionId,
                IsBuiltIn: true));

            if (!decision.Allowed)
            {
                denied.Add(permission);
                reason ??= decision.Reason;
            }
        }

        return denied.Count == 0
            ? AgentProfileAuthorizationDecision.Accept()
            : AgentProfileAuthorizationDecision.Deny(denied, reason ?? "Permission denied by the host evaluator.");
    }
}

/// <summary>
/// The nine factory roles as declared definitions plus v1 profiles (F-D.1, F-D.2). The role identity
/// lives in the definition and profile, not in nine bespoke C# classes (source §3.2).
/// </summary>
public static class FactoryRolePack
{
    private static readonly string[] AnalyticalPermissions =
    [
        PermissionCatalog.WorkspaceRead, PermissionCatalog.ArtifactRead,
        PermissionCatalog.ArtifactWrite, PermissionCatalog.ModelInvoke
    ];

    private static readonly string[] ModifyingPermissions =
    [
        PermissionCatalog.WorkspaceRead, PermissionCatalog.WorkspaceWrite,
        PermissionCatalog.ArtifactRead, PermissionCatalog.ArtifactWrite,
        PermissionCatalog.ModelInvoke, PermissionCatalog.Process
    ];

    private static readonly string[] ReleasedPermissions =
    [
        PermissionCatalog.WorkspaceRead, PermissionCatalog.ArtifactRead,
        PermissionCatalog.ArtifactWrite, PermissionCatalog.ModelInvoke, PermissionCatalog.Process
    ];

    private static readonly string[] OperationalPermissions =
    [
        PermissionCatalog.WorkspaceRead, PermissionCatalog.ArtifactRead,
        PermissionCatalog.ArtifactWrite, PermissionCatalog.ModelInvoke, PermissionCatalog.Network
    ];

    /// <summary>The nine role definitions, in source §8 order.</summary>
    public static IReadOnlyList<FactoryAgentDefinition> Definitions { get; } =
    [
        Definition("requirements-analyst", "Requirements Analyst",
            "Turn a submitted requirement into an aligned, testable statement.",
            ["clarify ambiguity", "publish acceptance criteria"],
            AnalyticalPermissions, "factory.agent.requirements"),
        Definition("tactical-architecture-analyst", "Tactical Architecture Analyst",
            "Refine an aligned requirement into constraints and decisions.",
            ["identify change impact", "record design decisions"],
            AnalyticalPermissions, "factory.validator.architecture-conformance"),
        Definition("implementations-analyst", "Implementations Analyst",
            "Produce minimal, verified changes satisfying refined requirements.",
            ["edit the worktree", "run build and tests"],
            ModifyingPermissions, "factory.agent.implementation"),
        Definition("review-analyst", "Review Analyst",
            "Execute independent checks and publish a verdict bound to the head SHA.",
            ["run validators", "classify findings"],
            ReleasedPermissions, "factory.agent.review"),
        Definition("deliver-manager", "Deliver Manager",
            "Decide whether an accepted review may be merged.",
            ["check policy and drift", "request operator approval"],
            AnalyticalPermissions, "factory.agent.delivery-manager"),
        Definition("deliver-analyst", "Deliver Analyst",
            "Validate a merged candidate against a delivery environment.",
            ["start the target", "probe readiness"],
            ReleasedPermissions, "factory.agent.delivery"),
        Definition("release-manager", "Release Manager",
            "Authorize a release and exercise its rollback path.",
            ["authorize release", "exercise rollback"],
            ReleasedPermissions, "factory.agent.release"),
        Definition("strategic-architecture-analyst", "Strategic Architecture Analyst",
            "Publish versioned guidelines and architectural debt requirements.",
            ["analyse drift", "publish guidelines"],
            AnalyticalPermissions, "factory.agent.strategic-architecture"),
        Definition("operations-analyst", "Operations Analyst",
            "Turn production telemetry into an operational requirement.",
            ["read telemetry", "validate a diagnosis"],
            OperationalPermissions, "factory.agent.operations")
    ];

    /// <summary>
    /// The nine v1 profiles. Each requests the permissions its definition already carries, so v1 is
    /// publishable without authority expansion.
    /// </summary>
    public static IReadOnlyList<AgentProfileDraft> Profiles { get; } =
        Definitions.Select(CreateInitialProfile).ToArray();

    private static FactoryAgentDefinition Definition(
        string definitionId,
        string role,
        string objective,
        IReadOnlyList<string> responsibilities,
        IReadOnlyList<string> permissions,
        string requiredCapability) =>
        new(
            new AgentDefinitionId("factory." + definitionId),
            role,
            objective,
            responsibilities,
            permissions,
            [new EvaluationPolicyId($"eval://{definitionId}/v1")],
            [requiredCapability]);

    private static AgentProfileDraft CreateInitialProfile(FactoryAgentDefinition definition)
    {
        var slug = definition.AgentDefinitionId.Value.Replace("factory.", string.Empty, StringComparison.Ordinal);

        return new AgentProfileDraft(
            new AgentProfileId("factory." + slug),
            definition.AgentDefinitionId,
            new AgentProfileVersion(1),
            ParentVersion: null,
            ChangeReason: "Initial published version.",
            Role: definition.Role,
            Objective: definition.Objective,
            InstructionArtifactRef: $"artifact://agent-prompts/{slug}@1",
            Model: new AgentProfileModelPolicy("model.deepseek", "deepseek-chat"),
            Skills: [],
            // Every profile pins at least one tool so capability resolution is a real check (F-D.4),
            // not a vacuous loop over an empty list.
            Tools:
            [
                new AgentProfileCapabilityRef(
                    new CapabilityId("tool.repo-read"),
                    new CapabilityVersion("1.0.0"))
            ],
            ContextPolicy: new AgentProfileContextPolicy(
                "relevant-architecture-and-requirements",
                "work-item-and-agent-experience",
                50000),
            ExecutionPolicy: new AgentProfileExecutionPolicy(24, 2400, 140000, 3),
            Evaluation: new AgentProfileEvaluation(
                definition.AllowedEvaluationPolicies[0],
                definition.EffectiveRequiredGates,
                "shadow-before-promotion"),
            Permissions: new AgentProfilePermissions(
                definition.PermissionCeiling,
                $"policy://factory/{slug}@1"));
    }
}
