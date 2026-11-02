using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Builder;
using Wip.Modus.Hosting;
using Wip.Runtime.Runtime;
using Wip.Shell.Interactive;

namespace Wip.ShellHost.Hosting;

public static class WipShellHostFactory
{
    public static WipShellHost CreateDefault(WipShellHostOptions options, TextReader input, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var builder = CreateShellBuilder();
        RegisterConfiguredModelProvider(builder, options.EffectiveConfig.ProviderConfig);
        Wip.Modus.Hosting.IModusWipBridge bridge = new Wip.Modus.Hosting.ModusWipBridge(
            options.PluginsPath,
            options.EffectiveConfig.UserPluginsPath,
            builder.WorkflowRegistrations);
        var pluginLifetimeGate = new WipShellPluginLifetimeGate();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());
        var commandLoop = new WipShellCommandLoop(
            orchestrator,
            input,
            output,
            builder,
            string.IsNullOrWhiteSpace(options.EffectiveConfig.RepositoryPath)
                ? options.EffectiveConfig.WorkspaceRoot
                : options.EffectiveConfig.RepositoryPath,
            bridge,
            (parts, cancellationToken) => TryHandleHostCommandAsync(parts, output, options.EffectiveConfig, cancellationToken),
            pluginLifetimeGate,
            workspaceProvider: null,
            validationCommands: options.EffectiveConfig.ValidationCommands,
            policyId: options.EffectiveConfig.PolicyId);
        var shellEngine = new WipShellEngine(commandLoop);

        var container = new WipShellHostContainer(orchestrator, shellEngine, bridge, pluginLifetimeGate);
        return new WipShellHost(container, pluginStartupMode: options.EffectiveConfig.PluginStartupMode);
    }

    private static async Task<bool> TryHandleHostCommandAsync(
        string[] parts,
        TextWriter output,
        WipShellHostEffectiveConfig effectiveConfig,
        CancellationToken cancellationToken)
    {
        if (parts.Length == 0)
            return false;

        var command = parts[0].ToLowerInvariant();
        if (command is not ("config" or "effective-config"))
            return false;

        if (parts.Length != 1)
        {
            await output.WriteLineAsync("Usage: config");
            return true;
        }

        await output.WriteLineAsync("Effective configuration:");
        await output.WriteLineAsync($"repositoryPath: {effectiveConfig.RepositoryPath}");
        await output.WriteLineAsync($"workspaceRoot: {effectiveConfig.WorkspaceRoot}");
        await output.WriteLineAsync($"configPath: {effectiveConfig.ConfigPath}");
        await output.WriteLineAsync($"configSource: {effectiveConfig.ConfigSource.ToString().ToLowerInvariant()}");
        await output.WriteLineAsync($"wipRoot: {effectiveConfig.WipRoot}");
        await output.WriteLineAsync($"sessionsPath: {effectiveConfig.SessionsPath}");
        await output.WriteLineAsync($"worktreesPath: {effectiveConfig.WorktreesPath}");
        await output.WriteLineAsync($"pluginsPath: {effectiveConfig.PluginsPath}");
        await output.WriteLineAsync($"userPluginsPath: {effectiveConfig.UserPluginsPath}");
        await output.WriteLineAsync($"defaultWorkflowId: {effectiveConfig.DefaultWorkflowId}");
        await output.WriteLineAsync($"policy: {effectiveConfig.PolicyId}");
        await output.WriteLineAsync($"pluginStartupMode: {effectiveConfig.PluginStartupMode}");
        await output.WriteLineAsync($"validationCommands: {string.Join(" | ", effectiveConfig.ValidationCommands)}");

        if (effectiveConfig.ProviderConfig is null)
        {
            await output.WriteLineAsync("provider: none");
        }
        else if (effectiveConfig.ProviderConfig.Provider == WipShellModelProviderKind.DeepSeek
            && effectiveConfig.ProviderConfig.DeepSeek is { } deepSeek)
        {
            await output.WriteLineAsync("provider: deepseek");
            await output.WriteLineAsync($"provider.deepseek.baseUrl: {deepSeek.BaseUrl}");
            await output.WriteLineAsync($"provider.deepseek.model: {deepSeek.ModelIdentifier}");
            await output.WriteLineAsync($"provider.deepseek.timeoutSeconds: {(int)deepSeek.Timeout.TotalSeconds}");
            await output.WriteLineAsync($"provider.deepseek.keySource: environment:{deepSeek.KeySourceReference}");
        }

        await output.FlushAsync(cancellationToken);
        return true;
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<SessionId, SessionSnapshot> _sessions = new();

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            if (_sessions.TryGetValue(sessionId, out var snapshot))
                return ValueTask.FromResult<SessionSnapshot?>(snapshot);

            return ValueTask.FromResult<SessionSnapshot?>(null);
        }
    }

    private sealed class NoOpSessionEventPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private static WipBuilder CreateShellBuilder()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);
        builder.AddPolicy<ShellWorkflowPolicy, ShellWorkflowPolicyRequest>(new PolicyId("policy.shell"));
        builder.AddValidator<ShellPlanValidator, ShellPlanValidationRequest, ShellPlanValidationResult>(
            new CapabilityId("validator.shell.plan"),
            "Shell Plan Validator");
        builder.AddAgent<ShellPlanStageAgent, PlanStageRequest, PlanStageResult>(
            new CapabilityId("agent.shell.plan"),
            "Shell plan stage agent");
        builder.AddTool<ShellWorkflowRunTool, ShellWorkflowRequest, ShellWorkflowResult>(
            new CapabilityId("tool.shell.workflow.run"),
            "Shell workflow run tool");
        builder.AddWorkflow<ShellLinearWorkflow, ShellWorkflowRequest, ShellWorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");
        builder.AddWorkflow<ShellLinearWorkflow, ShellWorkflowRequest, ShellWorkflowResult>(
            workflowId: new WorkflowId("workflow.safe-change"),
            displayName: "Safe change workflow");
        BonesShellHostRegistration.RegisterWhenEnabled(builder);
        return builder;
    }

    private static void RegisterConfiguredModelProvider(WipBuilder builder, WipShellHostProviderConfig? providerConfig)
    {
        if (providerConfig is null)
            return;

        if (providerConfig.Provider != WipShellModelProviderKind.DeepSeek)
            return;

        if (providerConfig.DeepSeek is null)
        {
            throw new InvalidOperationException(
                "Shell host provider configuration is invalid: DeepSeek settings are required when ModelProvider is set to 'deepseek'.");
        }

        var deepSeek = providerConfig.DeepSeek;
        builder.Services.AddSingleton(new DeepSeekModelProviderOptions(
            baseUrl: new Uri(deepSeek.BaseUrl, UriKind.Absolute),
            apiKey: $"env:{deepSeek.KeySourceReference}",
            timeout: deepSeek.Timeout));
        builder.Services.AddSingleton(static _ => new HttpClient());

        builder.AddModelProvider<DeepSeekModelProvider, DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>(
            capabilityId: new CapabilityId("model-provider.deepseek.chat"),
            displayName: "DeepSeek Chat Completion Provider");
    }

    private sealed record ShellWorkflowPolicyRequest(string Operation);

    private sealed class ShellWorkflowPolicy : IPolicy<ShellWorkflowPolicyRequest>
    {
        public PolicyId PolicyId => new("policy.shell");

        public ValueTask<PolicyDecision> EvaluateAsync(
            ShellWorkflowPolicyRequest request,
            PolicyContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(PolicyDecision.Allow());
    }

    private sealed record ShellWorkflowRequest(string Task);

    private sealed record ShellWorkflowResult(string Outcome);

    private sealed record ShellPlanValidationRequest(string Plan);

    private sealed record ShellPlanValidationResult(bool IsValid, string Reason);

    private sealed class ShellPlanValidator : IValidator<ShellPlanValidationRequest, ShellPlanValidationResult>
    {
        public ValueTask<ShellPlanValidationResult> ExecuteAsync(
            ShellPlanValidationRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ShellPlanValidationResult(true, "ok"));
    }

    private sealed class ShellWorkflowRunTool : ITool<ShellWorkflowRequest, ShellWorkflowResult>
    {
        public ValueTask<ShellWorkflowResult> ExecuteAsync(
            ShellWorkflowRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ShellWorkflowResult(request.Task));
    }

    private sealed class ShellPlanStageAgent : IAgent<PlanStageRequest, PlanStageResult>
    {
        public ValueTask<PlanStageResult> ExecuteAsync(
            PlanStageRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new PlanStageResult(true));
    }

    private sealed class ShellLinearWorkflow : IWorkflow<ShellWorkflowRequest, ShellWorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.linear");

        public ValueTask<ShellWorkflowResult> ExecuteAsync(
            ShellWorkflowRequest request,
            WorkflowContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ShellWorkflowResult(request.Task));
    }

    internal static class BonesShellHostRegistration
{
    public const string EnableEnvironmentVariable = "WIP_BONES_E2E";

    public static bool IsEnabled()
        => string.Equals(
            Environment.GetEnvironmentVariable(EnableEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

    public static void RegisterWhenEnabled(WipBuilder builder)
    {
        if (!IsEnabled())
            return;

        var repositoryPath = Directory.GetCurrentDirectory();
        var artifactStore = new Wip.Artifacts.Local.WipArtifactStoreLocal(repositoryPath);

        builder.Services.AddSingleton<IArtifactStore>(artifactStore);
        builder.Services.AddSingleton(new BonesMatchSimulator());
        builder.Services.AddSingleton(new BonesGameEngine());
        builder.Services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>, StubBonesStrategyModelProvider>();
        builder.Services.AddSingleton<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>, StubBonesPlayTurnModelProvider>();
        builder.Services.AddSingleton<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>, StubBonesEnhancementModelProvider>();
        builder.Services.AddSingleton<IBonesPublicMatchFeed, NoOpBonesPublicMatchFeed>();

        builder.AddBonesLearningWorkflowWithViewer(new Uri("http://127.0.0.1:8765"));
    }

    private sealed class NoOpBonesPublicMatchFeed : IBonesPublicMatchFeed
    {
        public void PublishMatchState(
            SessionId sessionId,
            BonesGameId matchId,
            int matchSeed,
            BonesMatchResult matchResult,
            bool isComplete)
        {
        }
    }

    private sealed class StubBonesStrategyModelProvider
        : IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>
    {
        public ValueTask<ModelProviderResponse<BonesStrategyAuthoringResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyAuthoringRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            const string markdown = """
                # Bones Strategy

                ## Rules
                - Prefer matching open ends before passing.
                - Track board state transitions from observation transcripts.
                - Use GetLegalMoves options only; pass when the legal set is empty.
                """;

            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyAuthoringResult>(
                    payload: new BonesStrategyAuthoringResult(markdown),
                    providerId: "bones-strategy-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(120, 48),
                    correlationId: request.CorrelationId));
        }
    }

    private sealed class StubBonesPlayTurnModelProvider
        : IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>
    {
        public ValueTask<ModelProviderResponse<BonesPlayTurnResult>> ExecuteAsync(
            ModelProviderRequest<BonesPlayTurnRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesPlayTurnResult>(
                    payload: new BonesPlayTurnResult(request.Payload.AllowedMoves[0].MoveId.Value),
                    providerId: "bones-play-turn-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(64, 16),
                    correlationId: request.CorrelationId));
    }

    private sealed class StubBonesEnhancementModelProvider
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            const string markdown = """
                # Bones Strategy

                ## Rules
                - Prefer matching open ends before passing.
                - After a loss, prioritize blocking opponent scoring lines earlier in the round.
                - Use GetLegalMoves options only; pass when the legal set is empty.

                ## Enhancements
                - Refined tactics based on match outcome review.
                """;

            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult(markdown),
                    providerId: "bones-enhance-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(140, 60),
                    correlationId: request.CorrelationId));
        }
    }
}
}
