using System.Collections.Concurrent;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
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

        var userPluginsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".wip",
            "plugins");

        Wip.Modus.Hosting.IModusWipBridge bridge = new Wip.Modus.Hosting.ModusWipBridge(
            options.PluginsPath,
            userPluginsPath);
        var pluginLifetimeGate = new WipShellPluginLifetimeGate();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());
        var commandLoop = new WipShellCommandLoop(
            orchestrator,
            input,
            output,
            bridge,
            (parts, cancellationToken) => TryHandleHostCommandAsync(parts, output, options.EffectiveConfig, cancellationToken),
            pluginLifetimeGate);
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
        await output.WriteLineAsync($"source: {effectiveConfig.SourceFile}");
        await output.WriteLineAsync($"policy: {effectiveConfig.PolicyId}");
        await output.WriteLineAsync($"pluginsPath: {effectiveConfig.PluginsPath}");
        await output.WriteLineAsync($"pluginStartupMode: {effectiveConfig.PluginStartupMode}");
        await output.WriteLineAsync($"workspaceRoot: {effectiveConfig.WorkspaceRoot}");
        await output.WriteLineAsync($"validationCommands: {string.Join(" | ", effectiveConfig.ValidationCommands)}");
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
}
