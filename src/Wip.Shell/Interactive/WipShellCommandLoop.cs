using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Modus.Hosting;
using Wip.Runtime.Runtime;
using System.Text;

namespace Wip.Shell.Interactive;

public sealed class WipShellCommandLoop
{
    private static readonly IReadOnlyDictionary<string, CommandScope> CommandScopes = new Dictionary<string, CommandScope>(StringComparer.OrdinalIgnoreCase)
    {
        ["help"] = CommandScope.Global,
        ["start"] = CommandScope.Global,
        ["attach"] = CommandScope.Global,
        ["status"] = CommandScope.Session,
        ["transition"] = CommandScope.Session,
        ["detach"] = CommandScope.Session,
        ["plugins"] = CommandScope.Global,
        ["workflows"] = CommandScope.Global,
        ["debug-logs"] = CommandScope.Global,
    };

    private readonly Func<string[], CancellationToken, Task<bool>>? _customCommandHandler;
    private readonly WipRuntimeOrchestrator _orchestrator;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly IModusWipBridge? _diagnosticsBridge;
    private readonly WipShellPluginLifetimeGate _pluginLifetimeGate;
    private readonly SemaphoreSlim _diagnosticsOutputGate = new(1, 1);
    private SessionSnapshot? _activeSession;

    public WipShellCommandLoop(
        WipRuntimeOrchestrator orchestrator,
        TextReader input,
        TextWriter output,
        IModusWipBridge? diagnosticsBridge = null,
        Func<string[], CancellationToken, Task<bool>>? customCommandHandler = null,
        WipShellPluginLifetimeGate? pluginLifetimeGate = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _diagnosticsBridge = diagnosticsBridge;
        _customCommandHandler = customCommandHandler;
        _pluginLifetimeGate = pluginLifetimeGate ?? new WipShellPluginLifetimeGate();
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _output.WriteAsync(GetPrompt());
            await _output.FlushAsync(cancellationToken);

            var line = await _input.ReadLineAsync(cancellationToken);
            if (line is null)
                return 0;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var shouldExit = await DispatchAsync(line.Trim(), cancellationToken);
            if (shouldExit)
                return 0;
        }
    }

    private async Task<bool> DispatchAsync(string commandLine, CancellationToken cancellationToken)
    {
        var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();

        if (command is "exit" or "quit")
            return true;

        if (!HasCommandContext(command))
        {
            if (_customCommandHandler is not null && await _customCommandHandler(parts, cancellationToken))
                return false;

            await WriteLineAsync($"Unknown command '{parts[0]}'. Use 'help' to list commands.");
            return false;
        }

        if (RequiresActiveSession(command) && _activeSession is null)
        {
            await WriteSessionCommandGuidanceAsync();
            return false;
        }

        switch (command)
        {
            case "help":
                await WriteLineAsync("Available commands: help, start <workflow-id> <repository-path> <worktree-path>, attach <repository-path> <session-id>, detach, status, transition <state>, plugins [load|unload], workflows, debug-logs, config, effective-config, exit");
                return false;

            case "start":
                await HandleStartAsync(parts, cancellationToken);
                return false;

            case "attach":
                await HandleAttachAsync(parts, cancellationToken);
                return false;

            case "detach":
                await HandleDetachAsync(cancellationToken);
                return false;

            case "status":
                await HandleStatusAsync();
                return false;

            case "transition":
                await HandleTransitionAsync(parts, cancellationToken);
                return false;

            case "plugins":
                await HandlePluginsAsync(parts, cancellationToken);
                return false;

            case "workflows":
                await HandleWorkflowsAsync();
                return false;

            case "debug-logs":
                await HandleDebugLogsAsync();
                return false;

            default:
                await WriteLineAsync($"Unknown command '{parts[0]}'. Use 'help' to list commands.");
                return false;
        }
    }

    private async Task HandleStartAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length != 4)
        {
            await WriteLineAsync("Usage: start <workflow-id> <repository-path> <worktree-path>");
            return;
        }

        try
        {
            _activeSession = await _orchestrator.StartSessionAsync(
                workflowId: new WorkflowId(parts[1]),
                repositoryPath: parts[2],
                worktreePath: parts[3],
                cancellationToken: cancellationToken);

            await WriteLineAsync($"Session started: {_activeSession.SessionId}");
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Failed to start session: {ex.Message}");
        }
    }

    private async Task HandleAttachAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length != 3)
        {
            await WriteLineAsync("Usage: attach <repository-path> <session-id>");
            return;
        }

        try
        {
            _activeSession = await _orchestrator.AttachSessionAsync(
                repositoryPath: parts[1],
                sessionId: new SessionId(parts[2]),
                cancellationToken: cancellationToken);

            await WriteLineAsync($"Session attached: {_activeSession.SessionId}");
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Failed to attach session: {ex.Message}");
        }
    }

    private async Task HandleDetachAsync(CancellationToken cancellationToken)
    {
        var detached = await _orchestrator.DetachSessionAsync(cancellationToken);
        _activeSession = null;

        await WriteLineAsync(detached ? "Session detached." : "No active session is attached.");
    }

    private async Task HandleStatusAsync()
    {
        var activeSession = _activeSession;
        if (activeSession is null)
        {
            await WriteSessionCommandGuidanceAsync();
            return;
        }

        await WriteLineAsync($"Session {activeSession.SessionId} is {activeSession.State} for workflow {activeSession.WorkflowId}.");
    }

    private async Task HandleTransitionAsync(string[] parts, CancellationToken cancellationToken)
    {
        var activeSession = _activeSession;
        if (activeSession is null)
        {
            await WriteSessionCommandGuidanceAsync();
            return;
        }

        if (parts.Length != 2 || !Enum.TryParse<SessionState>(parts[1], ignoreCase: true, out var targetState))
        {
            await WriteLineAsync("Usage: transition <Created|Editing|Validating|AwaitingApproval|Approved|Merged>");
            return;
        }

        try
        {
            _activeSession = await _orchestrator.TransitionAsync(activeSession.SessionId, targetState, cancellationToken);
            await WriteLineAsync($"Session transitioned to {_activeSession.State}.");
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Failed to transition session: {ex.Message}");
        }
    }

    private static bool HasCommandContext(string command)
        => CommandScopes.ContainsKey(command);

    private static bool RequiresActiveSession(string command)
        => CommandScopes.TryGetValue(command, out var scope)
            && scope == CommandScope.Session;

    private Task WriteSessionCommandGuidanceAsync()
        => WriteLineAsync("This command requires an active session. Use 'start <workflow-id> <repository-path> <worktree-path>' or 'attach <repository-path> <session-id>'.");

    private async Task HandlePluginsAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length == 1)
        {
            await RenderPluginsDiagnosticsAsync();
            return;
        }

        if (parts.Length != 2)
        {
            await WriteLineAsync("Usage: plugins [load|unload]");
            return;
        }

        switch (parts[1].ToLowerInvariant())
        {
            case "load":
                await HandlePluginsLoadAsync(cancellationToken);
                return;

            case "unload":
                await HandlePluginsUnloadAsync(cancellationToken);
                return;

            default:
                await WriteLineAsync("Usage: plugins [load|unload]");
                return;
        }
    }

    private async Task HandlePluginsLoadAsync(CancellationToken cancellationToken)
    {
        if (_diagnosticsBridge is null)
        {
            await WriteLineAsync("Plugin diagnostics are unavailable in this shell instance.");
            return;
        }

        var runManifest = _diagnosticsBridge.GetRunManifest();
        if (runManifest.Plugins.Count > 0 || !_pluginLifetimeGate.TryReserveLoad())
        {
            await WriteLineAsync("Plugins are already loaded for this host container lifetime.");
            return;
        }

        var loadedCount = await _diagnosticsBridge.LoadPluginsAsync(cancellationToken);
        await WriteLineAsync($"Plugins loaded: {loadedCount}.");
    }

    private async Task HandlePluginsUnloadAsync(CancellationToken cancellationToken)
    {
        if (_diagnosticsBridge is null)
        {
            await WriteLineAsync("Plugin diagnostics are unavailable in this shell instance.");
            return;
        }

        var runManifest = _diagnosticsBridge.GetRunManifest();
        if (runManifest.Plugins.Count == 0)
        {
            await WriteLineAsync("Plugins are not currently loaded.");
            return;
        }

        await _diagnosticsBridge.StopPluginsAsync(cancellationToken);
        await WriteLineAsync("Plugins unloaded.");
    }

    private async Task RenderPluginsDiagnosticsAsync()
    {
        var lines = new List<string>();

        if (_diagnosticsBridge is null)
        {
            lines.Add("Plugin diagnostics are unavailable in this shell instance.");
            await WriteDeterministicBlockAsync(lines);
            return;
        }

        var runManifest = _diagnosticsBridge.GetRunManifest();
        if (runManifest.Plugins.Count == 0)
        {
            lines.Add("No plugins are currently loaded.");
        }
        else
        {
            lines.Add("Loaded plugins:");
            foreach (var plugin in runManifest.Plugins
                         .OrderBy(static item => item.PluginId, StringComparer.Ordinal)
                         .ThenBy(static item => item.PluginVersion, StringComparer.Ordinal)
                         .ThenBy(static item => item.PluginName, StringComparer.Ordinal)
                         .ThenBy(static item => item.AssemblyName, StringComparer.Ordinal)
                         .ThenBy(static item => item.AssemblyVersion, StringComparer.Ordinal))
            {
                var capabilities = plugin.Capabilities.Count == 0
                    ? "(none)"
                    : string.Join(", ", plugin.Capabilities.OrderBy(static item => item, StringComparer.Ordinal));
                var permissions = plugin.RequiredPermissions.Count == 0
                    ? "(none)"
                    : string.Join(", ", plugin.RequiredPermissions.OrderBy(static item => item, StringComparer.Ordinal));

                lines.Add($"- {plugin.PluginId} [{plugin.PluginName}] v{plugin.PluginVersion} assembly={plugin.AssemblyName}@{plugin.AssemblyVersion}");
                lines.Add($"  capabilities: {capabilities}");
                lines.Add($"  permissions: {permissions}");
            }
        }

        var diagnostics = _diagnosticsBridge.GetLoadDiagnostics();
        if (diagnostics.Count > 0)
        {
            lines.Add("Plugin diagnostics:");
            foreach (var diagnostic in diagnostics.OrderBy(static item => item, StringComparer.Ordinal))
                lines.Add($"- {diagnostic}");
        }

        await WriteDeterministicBlockAsync(lines);
    }

    private async Task HandleWorkflowsAsync()
    {
        var lines = new List<string>();

        if (_diagnosticsBridge is null)
        {
            lines.Add("Workflow diagnostics are unavailable in this shell instance.");
            await WriteDeterministicBlockAsync(lines);
            return;
        }

        var runManifest = _diagnosticsBridge.GetRunManifest();
        if (runManifest.Workflows.Count == 0)
        {
            lines.Add("No workflows are currently registered.");
            await WriteDeterministicBlockAsync(lines);
            return;
        }

        lines.Add("Registered workflows:");
        foreach (var workflow in runManifest.Workflows
                     .OrderBy(static item => item.WorkflowId, StringComparer.Ordinal)
                     .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
                     .ThenBy(static item => item.RequestType, StringComparer.Ordinal)
                     .ThenBy(static item => item.ResultType, StringComparer.Ordinal))
        {
            lines.Add($"- {workflow.WorkflowId} [{workflow.DisplayName}] request={workflow.RequestType} result={workflow.ResultType}");
        }

        await WriteDeterministicBlockAsync(lines);
    }

    private async Task HandleDebugLogsAsync()
    {
        var lines = new List<string>();

        if (_diagnosticsBridge is not IModusWipDebugChannel debugChannel)
        {
            lines.Add("Modus debug logs are unavailable in this shell instance.");
            await WriteDeterministicBlockAsync(lines);
            return;
        }

        var debugSnapshot = debugChannel.GetDebugLogSnapshot();
        lines.Add($"Host run correlation: {debugSnapshot.RunCorrelationId}");

        if (debugSnapshot.Entries.Count == 0)
        {
            lines.Add("No Modus debug logs were emitted for the current host run.");
            await WriteDeterministicBlockAsync(lines);
            return;
        }

        lines.Add("Modus debug logs:");
        foreach (var entry in debugSnapshot.Entries
                     .OrderBy(static item => item.TimestampUtc)
                     .ThenBy(static item => item.Source, StringComparer.Ordinal)
                     .ThenBy(static item => item.Message, StringComparer.Ordinal))
        {
            lines.Add($"- [{entry.Level}] {entry.Source} :: {entry.Message} (run={entry.RunCorrelationId})");
        }

        await WriteDeterministicBlockAsync(lines);
    }

    private string GetPrompt()
        => _activeSession is null ? "wip> " : $"wip[{_activeSession.SessionId}]> ";

    private Task WriteLineAsync(string line)
        => _output.WriteLineAsync(line);

    private async Task WriteDeterministicBlockAsync(IReadOnlyList<string> lines)
    {
        await _diagnosticsOutputGate.WaitAsync();
        try
        {
            var buffer = new StringBuilder();
            foreach (var line in lines)
                buffer.AppendLine(line);

            await _output.WriteAsync(buffer.ToString());
        }
        finally
        {
            _diagnosticsOutputGate.Release();
        }
    }

    private enum CommandScope
    {
        Global,
        Session,
    }
}
