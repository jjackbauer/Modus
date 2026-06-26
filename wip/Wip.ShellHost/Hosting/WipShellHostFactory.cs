using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
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
        builder.AddWorkflow<ShellLinearWorkflow, ShellWorkflowRequest, ShellWorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");
        builder.AddWorkflow<ShellLinearWorkflow, ShellWorkflowRequest, ShellWorkflowResult>(
            workflowId: new WorkflowId("workflow.safe-change"),
            displayName: "Safe change workflow");
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

    private sealed class ShellLinearWorkflow : IWorkflow<ShellWorkflowRequest, ShellWorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.linear");

        public ValueTask<ShellWorkflowResult> ExecuteAsync(
            ShellWorkflowRequest request,
            WorkflowContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ShellWorkflowResult(request.Task));
    }
}
