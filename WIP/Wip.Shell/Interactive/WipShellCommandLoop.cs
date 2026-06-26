using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Wip.Agent.Basic;
using Wip.Builder;
using Wip.Modus.Hosting;
using Wip.Runtime.Runtime;
using System.Reflection;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Wip.Workspaces.Git;
using Wip.Policy.LocalSafe;
using Wip.Shell.History;
using Wip.Shell.Prompting;

namespace Wip.Shell.Interactive;

public sealed class WipShellCommandLoop
{
    private const string ApprovalReviewRecoveryGuidance = "Run 'review' to regenerate review evidence for the current diff hash, then retry 'approve'.";

    private static readonly string[] GlobalCommandSurface =
    [
        "help",
        "init",
        "repo",
        "config",
        "sessions",
        "session start \"<task>\"",
        "session attach <session-id>",
        "use workflow <id>",
        "workflows",
        "plugins [load|unload]",
        "debug-logs",
        "exit",
        "dotnet <restore|build|test|validate>"
    ];

    private static readonly string[] SessionCommandSurface =
    [
        "plan",
        "run",
        "diff",
        "checkpoint",
        "validate",
        "review",
        "approve",
        "merge",
        "artifacts",
        "status",
        "detach",
        "archive",
        "abort"
    ];

    private static readonly string[] HelpCommandSurface =
    [
        "help",
        "init",
        "repo",
        "config",
        "sessions",
        "session start \"<task>\"",
        "session attach <session-id>",
        "use workflow <id>",
        .. SessionCommandSurface,
        "workflows",
        "plugins [load|unload]",
        "debug-logs",
        "exit",
        "dotnet <restore|build|test|validate>"
    ];

    private static readonly IReadOnlyDictionary<string, CommandScope> CommandScopes = new Dictionary<string, CommandScope>(StringComparer.OrdinalIgnoreCase)
    {
        ["help"] = CommandScope.Global,
        ["init"] = CommandScope.Global,
        ["repo"] = CommandScope.Global,
        ["config"] = CommandScope.Global,
        ["sessions"] = CommandScope.Global,
        ["session"] = CommandScope.Global,
        ["use"] = CommandScope.Global,
        ["plan"] = CommandScope.Session,
        ["run"] = CommandScope.Session,
        ["diff"] = CommandScope.Session,
        ["checkpoint"] = CommandScope.Session,
        ["validate"] = CommandScope.Session,
        ["review"] = CommandScope.Session,
        ["approve"] = CommandScope.Session,
        ["merge"] = CommandScope.Session,
        ["artifacts"] = CommandScope.Session,
        ["status"] = CommandScope.Session,
        ["detach"] = CommandScope.Session,
        ["archive"] = CommandScope.Session,
        ["abort"] = CommandScope.Session,
        ["plugins"] = CommandScope.Global,
        ["workflows"] = CommandScope.Global,
        ["debug-logs"] = CommandScope.Global,
        ["dotnet"] = CommandScope.Global,
    };

    private static readonly string HelpText = $"Available commands: {string.Join(", ", HelpCommandSurface)}";
    private const string DotNetFlowUsage = "Usage: dotnet <restore|build|test|validate>";
    private const string DotNetFlowSequence = "Sequence: dotnet restore -> dotnet build -> dotnet test -> dotnet validate";
    private static readonly JsonSerializerOptions PersistedSessionJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions RepositoryConfigJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly Func<string[], CancellationToken, Task<bool>>? _customCommandHandler;
    private readonly WipRuntimeOrchestrator _orchestrator;
    private readonly WipWorkspaceProviderGit _workspaceProvider;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly WipBuilder? _builder;
    private readonly string _repositoryRoot;
    private readonly IModusWipBridge? _diagnosticsBridge;
    private readonly IReadOnlyList<string> _validationCommands;
    private readonly string _policyId;
    private readonly LocalSafePolicy? _localSafePolicy;
    private readonly WipShellPluginLifetimeGate _pluginLifetimeGate;
    private readonly ICommandHistory _history;
    private readonly RuntimePromptContextComposer _promptContextComposer;
    private readonly RuntimePromptContextPolicyGate _promptContextPolicyGate;
    private readonly Func<SessionSnapshot, RuntimePromptContext>? _promptContextFactory;
    private readonly SemaphoreSlim _diagnosticsOutputGate = new(1, 1);
    private SessionSnapshot? _activeSession;
    private WorkflowId? _selectedWorkflowId;

    private sealed record ProviderPlanDraft(
        string Markdown,
        IReadOnlyList<string> Steps,
        string ProviderId,
        string ModelId,
        string CorrelationId);

    private sealed record PromptContextRejectedFragment(
        string PluginId,
        string Reason);

    private sealed record PromptContextInvocationDiagnostics(
        string SessionId,
        string Task,
        string ContextHash,
        string ProviderId,
        string ModelId,
        string CorrelationId,
        IReadOnlyList<string> ContributingPluginIds,
        IReadOnlyList<PromptContextRejectedFragment> RejectedFragments,
        IReadOnlyList<string> PluginManifestDiagnostics,
        DateTimeOffset ProducedAtUtc);

    private sealed record ProviderPlanAttempt(
        ProviderPlanDraft? Draft,
        string? FallbackReason);

    public WipShellCommandLoop(
        WipRuntimeOrchestrator orchestrator,
        TextReader input,
        TextWriter output,
        WipBuilder? builder = null,
        string? repositoryRoot = null,
        IModusWipBridge? diagnosticsBridge = null,
        Func<string[], CancellationToken, Task<bool>>? customCommandHandler = null,
        WipShellPluginLifetimeGate? pluginLifetimeGate = null,
        WipWorkspaceProviderGit? workspaceProvider = null,
        IReadOnlyList<string>? validationCommands = null,
        string? policyId = null,
        ICommandHistory? history = null,
        Func<SessionSnapshot, RuntimePromptContext>? promptContextFactory = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _workspaceProvider = workspaceProvider ?? new WipWorkspaceProviderGit();
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _builder = builder;
        _repositoryRoot = Path.GetFullPath(repositoryRoot ?? Directory.GetCurrentDirectory());
        _diagnosticsBridge = diagnosticsBridge;
        _customCommandHandler = customCommandHandler;
        _pluginLifetimeGate = pluginLifetimeGate ?? new WipShellPluginLifetimeGate();
        _promptContextComposer = new RuntimePromptContextComposer();
        _promptContextPolicyGate = new RuntimePromptContextPolicyGate();
        _validationCommands = (validationCommands ?? Array.Empty<string>())
            .Where(static command => !string.IsNullOrWhiteSpace(command))
            .Select(static command => command.Trim())
            .ToArray();
        _policyId = string.IsNullOrWhiteSpace(policyId) ? "local-safe" : policyId.Trim();
        _localSafePolicy = string.Equals(_policyId, "local-safe", StringComparison.OrdinalIgnoreCase)
            ? new LocalSafePolicy()
            : null;
        _history = history ?? new PersistentCommandHistory(_repositoryRoot);
        _promptContextFactory = promptContextFactory;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await WriteStartupBannerAsync(cancellationToken);

        while (true)
        {
            await _output.WriteAsync(GetPrompt());
            await _output.FlushAsync(cancellationToken);

            var line = await _input.ReadLineAsync(cancellationToken);
            if (line is null)
                return 0;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var trimmedLine = line.Trim();
            
            // Track command in history
            await _history.AddAsync(trimmedLine, cancellationToken);
            
            var shouldExit = await DispatchAsync(trimmedLine, cancellationToken);
            if (shouldExit)
                return 0;
        }
    }

    private async Task WriteStartupBannerAsync(CancellationToken cancellationToken)
    {
        var entryAssembly = Assembly.GetEntryAssembly() ?? typeof(WipShellCommandLoop).Assembly;
        var productName = entryAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        if (string.IsNullOrWhiteSpace(productName))
            productName = entryAssembly.GetName().Name ?? "Wip.ShellHost";

        var version = entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
            version = entryAssembly.GetName().Version?.ToString() ?? "0.0.0";

        var loadedPluginCount = _diagnosticsBridge?.GetRunManifest().Plugins.Count ?? 0;

        await WriteLineAsync($"Product: {productName} Version: {version}");
        await WriteLineAsync($"Repository path: {_repositoryRoot}");
        await WriteLineAsync($"Loaded plugin count: {loadedPluginCount}");
        await WriteLineAsync($"Active policy profile: {_policyId}");
        await WriteLineAsync($"Command history: {GetCommandHistoryStartupStatus()}");
        await WriteLineAsync("Hint: run 'help' to list available commands.");
        await _output.FlushAsync(cancellationToken);
    }

    private string GetCommandHistoryStartupStatus()
    {
        if (_history is PersistentCommandHistory persistentHistory)
        {
            var status = persistentHistory.GetStatus();
            if (status.PersistenceEnabled)
                return $"persistent ({status.HistoryFilePath})";

            return status.Description;
        }

        return "in-memory fallback (custom-history-provider)";
    }

    private async Task<bool> DispatchAsync(string commandLine, CancellationToken cancellationToken)
    {
        var parts = Tokenize(commandLine);
        if (parts.Length == 0)
            return false;

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
            await WriteSessionCommandGuidanceAsync(parts[0]);
            return false;
        }

        switch (command)
        {
            case "help":
                await WriteLineAsync(HelpText);
                return false;

            case "init":
                await HandleInitAsync();
                return false;

            case "repo":
                await HandleRepoAsync();
                return false;

            case "config":
                await HandleConfigAsync();
                return false;

            case "sessions":
                await HandleSessionsAsync();
                return false;

            case "session":
                await HandleSessionAsync(parts, cancellationToken);
                return false;

            case "use":
                await HandleUseAsync(parts, cancellationToken);
                return false;

            case "plan":
                await HandlePlanAsync(cancellationToken);
                return false;

            case "run":
                await HandleRunAsync(cancellationToken);
                return false;

            case "diff":
                await HandleDiffAsync(cancellationToken);
                return false;

            case "checkpoint":
                await HandleCheckpointAsync(cancellationToken);
                return false;

            case "validate":
                await HandleValidateAsync(cancellationToken);
                return false;

            case "review":
                await HandleReviewAsync(cancellationToken);
                return false;

            case "approve":
                await HandleApproveAsync(cancellationToken);
                return false;

            case "merge":
                await HandleMergeAsync(cancellationToken);
                return false;

            case "artifacts":
                await HandleArtifactsAsync(cancellationToken);
                return false;

            case "status":
                await HandleStatusAsync(cancellationToken);
                return false;

            case "archive":
                await HandleArchiveAsync(parts, cancellationToken);
                return false;

            case "abort":
                await HandleAbortAsync(cancellationToken);
                return false;

            case "detach":
                await HandleDetachAsync(cancellationToken);
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

            case "dotnet":
                await HandleDotNetFlowAsync(parts, cancellationToken);
                return false;

            default:
                await WriteLineAsync($"Unknown command '{parts[0]}'. Use 'help' to list commands.");
                return false;
        }
    }

    private async Task HandleSessionAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length < 2)
        {
            await WriteLineAsync("Usage: session start \"<task>\" | session attach <session-id>");
            return;
        }

        switch (parts[1].ToLowerInvariant())
        {
            case "start":
                await HandleSessionStartAsync(parts, cancellationToken);
                return;

            case "attach":
                await HandleSessionAttachAsync(parts, cancellationToken);
                return;

            default:
                await WriteLineAsync("Usage: session start \"<task>\" | session attach <session-id>");
                return;
        }
    }

    private async Task HandleSessionStartAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length != 3)
        {
            await WriteLineAsync("Usage: session start \"<task>\"");
            return;
        }

        var taskDescription = parts[2];
        var repositoryPath = ResolveRepositoryPath();
        var workflowId = _selectedWorkflowId ?? new WorkflowId(GetEffectiveRepositoryConfig(repositoryPath).DefaultWorkflowId);

        if (_builder is not null && !IsWorkflowRegisteredInBuilder(workflowId))
        {
            await WriteLineAsync($"Workflow '{workflowId.Value}' is not registered in the active builder. Use 'workflows' to inspect available workflow IDs.");
            return;
        }

        try
        {
            var startedSession = await _orchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: workflowId,
                    TaskDescription: taskDescription,
                    RepositoryPath: repositoryPath),
                cancellationToken);

            _activeSession = await _orchestrator.AttachSessionAsync(
                repositoryPath: repositoryPath,
                sessionId: startedSession.SessionId,
                cancellationToken: cancellationToken);
            _selectedWorkflowId = _activeSession.WorkflowId;

            await WriteLineAsync($"Session started: {_activeSession.SessionId} task=\"{taskDescription}\" workflow={workflowId}");
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Failed to start session: {ex.Message}");
        }
    }

    private async Task HandleDotNetFlowAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length != 2)
        {
            await WriteDotNetFlowUsageAsync();
            return;
        }

        switch (parts[1].ToLowerInvariant())
        {
            case "restore":
                await ExecuteDotNetFlowStageAsync(ValidationCommandKind.Restore, cancellationToken);
                return;

            case "build":
                await ExecuteDotNetFlowStageAsync(ValidationCommandKind.Build, cancellationToken);
                return;

            case "test":
                await ExecuteDotNetFlowStageAsync(ValidationCommandKind.Test, cancellationToken);
                return;

            case "validate":
                if (_activeSession is null)
                {
                    await WriteSessionCommandGuidanceAsync("validate");
                    return;
                }

                await HandleValidateAsync(cancellationToken);
                return;

            default:
                await WriteDotNetFlowUsageAsync();
                return;
        }
    }

    private async Task WriteDotNetFlowUsageAsync()
        => await WriteDeterministicBlockAsync(
        [
            DotNetFlowUsage,
            DotNetFlowSequence
        ]);

    private async Task ExecuteDotNetFlowStageAsync(ValidationCommandKind stage, CancellationToken cancellationToken)
    {
        try
        {
            var stageLabel = stage.ToString().ToLowerInvariant();
            var workingDirectory = _activeSession?.WorktreePath ?? ResolveRepositoryPath();

            SessionValidationCommandResult result;
            var mode = "executed";

            if (_validationCommands.Count == 0)
            {
                mode = "simulated";
                result = CreateSimulatedValidationCommandResult(
                    $"dotnet {stageLabel} (simulated)",
                    $"Simulated {stageLabel} stage for shell-only mode.");
            }
            else
            {
                var parsedCommands = _validationCommands
                    .Select(ParseValidationCommand)
                    .ToArray();
                var configuredCommand = parsedCommands.FirstOrDefault(command => command.Kind == stage);
                if (configuredCommand is null)
                {
                    mode = "simulated";
                    result = CreateSimulatedValidationCommandResult(
                        $"dotnet {stageLabel} (simulated)",
                        $"No configured dotnet {stageLabel} command was found. Configure ValidationCommands to execute this stage.");
                }
                else
                {
                    if (_activeSession is not null)
                    {
                        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
                            ?? throw new InvalidOperationException("Active session became unavailable.");
                        await EnforceLocalSafeValidationPolicyAsync(snapshot, [configuredCommand], cancellationToken);
                    }

                    result = await RunValidationCommandAsync(configuredCommand, workingDirectory, cancellationToken);
                }
            }

            await WriteDeterministicBlockAsync(
            [
                $"DotNet flow stage completed: stage={stageLabel} mode={mode}",
                $"command: {result.Command}",
                $"exitCode: {result.ExitCode} timedOut={result.TimedOut}"
            ]);
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"DotNet flow stage failed: {ex.Message}");
        }
    }

    private async Task HandleSessionAttachAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length != 3)
        {
            await WriteLineAsync("Usage: session attach <session-id>");
            return;
        }

        var repositoryPath = ResolveRepositoryPath();

        try
        {
            _activeSession = await _orchestrator.AttachSessionAsync(
                repositoryPath: repositoryPath,
                sessionId: new SessionId(parts[2]),
                cancellationToken: cancellationToken);
            _selectedWorkflowId = _activeSession.WorkflowId;

            await WriteLineAsync($"Session attached: {_activeSession.SessionId}");
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Failed to attach session: {ex.Message}");
        }
    }

    private async Task HandleDetachAsync(CancellationToken cancellationToken)
    {
        var sessionId = _activeSession?.SessionId;
        await _orchestrator.DetachSessionAsync(cancellationToken);
        _activeSession = null;

        await WriteLineAsync(sessionId.HasValue
            ? $"Session detached: {sessionId.Value}."
            : "No active session is attached.");
    }

    private async Task HandleUseAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length != 3 || !parts[1].Equals("workflow", StringComparison.OrdinalIgnoreCase))
        {
            await WriteLineAsync("Usage: use workflow <id>");
            return;
        }

        if (_builder is null)
        {
            await WriteLineAsync("Cannot select workflow: no active builder is configured for this shell instance.");
            return;
        }

        if (!IsWorkflowRegisteredInBuilder(new WorkflowId(parts[2])))
        {
            await WriteLineAsync($"Workflow '{parts[2]}' is not registered in the active builder. Use 'workflows' to inspect available workflow IDs.");
            return;
        }

        _selectedWorkflowId = new WorkflowId(parts[2]);

        if (_activeSession is not null)
        {
            try
            {
                _activeSession = await _orchestrator.SelectWorkflowAsync(_activeSession.SessionId, _selectedWorkflowId.Value, cancellationToken);
            }
            catch (Exception ex)
            {
                await WriteLineAsync($"Failed to select workflow: {ex.Message}");
                return;
            }
        }

        var lines = new List<string> { $"Workflow selected: {_selectedWorkflowId}" };
        if (_activeSession is not null)
            lines.Add($"Active session workflow: {_activeSession.WorkflowId} state={_activeSession.State}");

        await WriteDeterministicBlockAsync(lines);
    }

    private static bool HasCommandContext(string command)
        => CommandScopes.ContainsKey(command);

    private bool IsWorkflowRegisteredInBuilder(WorkflowId workflowId)
        => _builder is not null
            && _builder.WorkflowRegistrations.Any(registration => registration.WorkflowId.Equals(workflowId));

    private static bool RequiresActiveSession(string command)
        => CommandScopes.TryGetValue(command, out var scope)
            && scope == CommandScope.Session;

    private Task WriteSessionCommandGuidanceAsync(string command)
        => WriteDeterministicBlockAsync(
        [
            $"Command '{command}' requires an active session.",
            "Start a session with: session start \"<task>\"",
            "Attach a session with: session attach <session-id>",
            $"Session commands: {string.Join(", ", SessionCommandSurface)}",
            $"Global commands available without a session: {string.Join(", ", GlobalCommandSurface)}"
        ]);

    private async Task HandleInitAsync()
    {
        var repositoryPath = ResolveRepositoryPath();
        var config = GetEffectiveRepositoryConfig(repositoryPath);

        var layoutEntries = new[]
        {
            (Label: ".wip", Path: config.WipRoot),
            (Label: "sessions", Path: config.SessionsPath),
            (Label: "worktrees", Path: config.WorktreesPath),
            (Label: "plugins", Path: config.PluginsPath)
        };

        var lines = new List<string> { $"Repository initialized: {repositoryPath}" };
        foreach (var entry in layoutEntries)
        {
            var existed = Directory.Exists(entry.Path);
            Directory.CreateDirectory(entry.Path);
            lines.Add($"- {entry.Label}: {(existed ? "existing" : "created")} path={entry.Path}");
        }

        var configExisted = File.Exists(config.ConfigPath);
        var persistedConfig = config with { ConfigSource = RepositoryConfigSource.Disk };
        var configJson = JsonSerializer.Serialize(ToRepositoryConfigFile(persistedConfig), RepositoryConfigJsonOptions);
        await File.WriteAllTextAsync(config.ConfigPath, configJson);
        lines.Add($"- config: {(configExisted ? "existing" : "created")} path={config.ConfigPath}");

        await WriteDeterministicBlockAsync(lines);
    }

    private async Task HandleRepoAsync()
    {
        var repositoryPath = ResolveRepositoryPath();
        var config = GetEffectiveRepositoryConfig(repositoryPath);
        var lines = new List<string>
        {
            $"Repository path: {repositoryPath}",
            $"workspaceRoot: {config.WorkspaceRoot}",
            $"configPath: {config.ConfigPath} exists={File.Exists(config.ConfigPath)}",
            $"configSource: {config.ConfigSource.ToString().ToLowerInvariant()}",
            $"wipRoot: {config.WipRoot}",
            $"sessionsPath: {config.SessionsPath} exists={Directory.Exists(config.SessionsPath)}",
            $"worktreesPath: {config.WorktreesPath} exists={Directory.Exists(config.WorktreesPath)}",
            $"pluginsPath: {config.PluginsPath} exists={Directory.Exists(config.PluginsPath)}",
            $"defaultWorkflowId: {config.DefaultWorkflowId}",
            $"selectedWorkflow: {(_selectedWorkflowId?.Value ?? config.DefaultWorkflowId)}",
            _activeSession is null
                ? "activeSession: (none)"
                : $"activeSession: {_activeSession.SessionId} state={_activeSession.State} workflow={_activeSession.WorkflowId}"
        };

        await WriteDeterministicBlockAsync(lines);
    }

    private async Task HandleConfigAsync()
    {
        var config = GetEffectiveRepositoryConfig(ResolveRepositoryPath());

        await WriteDeterministicBlockAsync(
        [
            "Effective configuration:",
            $"repositoryPath: {config.RepositoryPath}",
            $"workspaceRoot: {config.WorkspaceRoot}",
            $"configPath: {config.ConfigPath}",
            $"configSource: {config.ConfigSource.ToString().ToLowerInvariant()}",
            $"wipRoot: {config.WipRoot}",
            $"sessionsPath: {config.SessionsPath}",
            $"worktreesPath: {config.WorktreesPath}",
            $"pluginsPath: {config.PluginsPath}",
            $"defaultWorkflowId: {config.DefaultWorkflowId}"
        ]);
    }

    private async Task HandleSessionsAsync()
    {
        var sessionsDirectory = Path.Combine(ResolveRepositoryPath(), ".wip", "sessions");
        if (!Directory.Exists(sessionsDirectory))
        {
            await WriteLineAsync($"No persisted sessions found at {sessionsDirectory}.");
            return;
        }

        var persistedSessions = Directory
            .EnumerateFiles(sessionsDirectory, "session-state.json", SearchOption.AllDirectories)
            .Select(TryReadPersistedSession)
            .Where(static snapshot => snapshot is not null)
            .Cast<PersistedSessionSnapshot>()
            .OrderByDescending(static snapshot => snapshot.UpdatedAtUtc)
            .ThenBy(static snapshot => snapshot.SessionId, StringComparer.Ordinal)
            .ToArray();

        if (persistedSessions.Length == 0)
        {
            await WriteLineAsync($"No persisted sessions found at {sessionsDirectory}.");
            return;
        }

        var lines = new List<string> { "Persisted sessions:" };
        foreach (var session in persistedSessions)
        {
            var activeMarker = _activeSession is not null && string.Equals(_activeSession.SessionId.Value, session.SessionId, StringComparison.Ordinal)
                ? " active=yes"
                : string.Empty;
            lines.Add($"- {session.SessionId} task=\"{session.TaskDescription}\" state={session.State} workflow={session.WorkflowId} updated={session.UpdatedAtUtc:O}{activeMarker}");
        }

        await WriteDeterministicBlockAsync(lines);
    }

    private async Task HandleLifecycleAdvanceAsync(
        string command,
        SessionState expectedCurrent,
        SessionState targetState,
        CancellationToken cancellationToken)
    {
        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");

        if (snapshot.State == targetState)
        {
            await WriteLineAsync($"Command '{command}' already satisfied for session {snapshot.SessionId}: state={snapshot.State}.");
            return;
        }

        if (snapshot.State != expectedCurrent)
        {
            await WriteLineAsync($"Command '{command}' requires session state {expectedCurrent}. Current state is {snapshot.State}.");
            return;
        }

        try
        {
            _activeSession = await _orchestrator.TransitionAsync(snapshot.SessionId, targetState, cancellationToken);
            await WriteLineAsync($"Command '{command}' completed for session {_activeSession.SessionId}: state={_activeSession.State}.");
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Command '{command}' failed: {ex.Message}");
        }
    }

    private async Task HandlePlanAsync(CancellationToken cancellationToken)
    {
        if (_builder is null)
        {
            await WriteLineAsync("Cannot generate plan: no active builder is configured for this shell instance.");
            return;
        }

        if (_builder.PolicyRegistrations.Count == 0)
        {
            await WriteLineAsync("Cannot generate plan: no policy is registered in the active builder.");
            return;
        }

        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");

        try
        {
            var artifactStore = new SessionArtifactStore(snapshot);
            var task = string.IsNullOrWhiteSpace(snapshot.TaskDescription) ? $"Plan session {snapshot.SessionId.Value}" : snapshot.TaskDescription;
            var providerPlanAttempt = await TryGenerateProviderPlanDraftAsync(snapshot, task, cancellationToken);
            var providerPlan = providerPlanAttempt.Draft;
            PromptContextInvocationDiagnostics? promptContextInvocationDiagnostics = null;

            if (providerPlan is not null)
            {
                var promptContext = BuildRuntimePromptContext(snapshot);
                promptContextInvocationDiagnostics = BuildPromptContextInvocationDiagnostics(snapshot, task, promptContext, providerPlan);

                var promptContextDiagnosticsArtifact = await artifactStore.SaveAsync(
                    snapshot.SessionId,
                    BuildPromptContextInvocationDiagnosticsArtifact(promptContextInvocationDiagnostics),
                    cancellationToken);

                await WriteLineAsync($"Prompt context diagnostics artifact: {promptContextDiagnosticsArtifact.RelativePath}");
            }

            var result = await _orchestrator.PlanAsync(
                snapshot.SessionId,
                task,
                _builder,
                _builder.PolicyRegistrations[0].PolicyId,
                new PlanOnlyAgent(artifactStore),
                cancellationToken);

            var effectivePlanMarkdown = providerPlan?.Markdown ?? result.Plan.Markdown;
            var effectivePlanSteps = providerPlan?.Steps ?? result.Plan.Steps;

            var agentPlanArtifact = await artifactStore.SaveAsync(
                snapshot.SessionId,
                BuildAgentPlanArtifact(
                    new AgentPlan(
                        SessionId: result.Session.SessionId.Value,
                        WorkflowId: result.Session.WorkflowId.Value,
                        Task: task,
                        RepositoryPath: result.Session.RepositoryPath,
                        WorktreePath: result.Session.WorktreePath,
                        PolicyId: _builder.PolicyRegistrations[0].PolicyId.Value,
                        ToolCapabilityIds: _builder.CapabilityDescriptors
                            .Where(static descriptor => descriptor.Kind == CapabilityKind.Tool)
                            .Select(static descriptor => descriptor.CapabilityId.Value)
                            .OrderBy(static capabilityId => capabilityId, StringComparer.Ordinal)
                            .ToArray(),
                        ValidatorCapabilityIds: _builder.CapabilityDescriptors
                            .Where(static descriptor => descriptor.Kind == CapabilityKind.Validator)
                            .Select(static descriptor => descriptor.CapabilityId.Value)
                            .OrderBy(static capabilityId => capabilityId, StringComparer.Ordinal)
                            .ToArray(),
                        Steps: effectivePlanSteps,
                        Markdown: effectivePlanMarkdown,
                        ProviderCorrelationId: providerPlan?.CorrelationId)),
                cancellationToken);

            _activeSession = await _orchestrator.GetSessionAsync(snapshot.SessionId, cancellationToken) ?? result.Session;
            _selectedWorkflowId = _activeSession.WorkflowId;

            var lines = new List<string>
            {
                $"Plan generated: workflow={_activeSession.WorkflowId} state={_activeSession.State}",
                $"Plan artifact: {result.Plan.PlanArtifact.RelativePath}",
                $"AgentPlan artifact: {agentPlanArtifact.RelativePath}",
                "Plan steps:"
            };

            if (providerPlan is not null)
            {
                lines.Add($"Model provider used: provider={providerPlan.ProviderId} model={providerPlan.ModelId}");
                lines.Add($"Model provider correlation: {providerPlan.CorrelationId}");
            }
            else if (!string.IsNullOrWhiteSpace(providerPlanAttempt.FallbackReason))
            {
                lines.Add($"Model provider skipped: {providerPlanAttempt.FallbackReason}");
            }

            lines.AddRange(effectivePlanSteps.Select(static step => $"- {step}"));

            if (promptContextInvocationDiagnostics is not null)
            {
                lines.Add($"Prompt context diagnostics: contextHash={promptContextInvocationDiagnostics.ContextHash} provider={promptContextInvocationDiagnostics.ProviderId} model={promptContextInvocationDiagnostics.ModelId} correlation={promptContextInvocationDiagnostics.CorrelationId}");
            }

            await WriteDeterministicBlockAsync(lines);
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Plan failed: {ex.Message}");
        }
    }

    private async ValueTask<ProviderPlanAttempt> TryGenerateProviderPlanDraftAsync(
        SessionSnapshot snapshot,
        string task,
        CancellationToken cancellationToken)
    {
        if (_builder is null)
            return new ProviderPlanAttempt(null, null);

        var providerDescriptor = _builder.CapabilityDescriptors
            .Where(static descriptor => descriptor.Kind == CapabilityKind.ModelProvider)
            .Where(static descriptor => descriptor.RequestType == typeof(ModelProviderRequest<DeepSeekChatCompletionRequest>))
            .Where(static descriptor => descriptor.ResultType == typeof(ModelProviderResponse<DeepSeekChatCompletionResult>))
            .OrderBy(static descriptor => descriptor.CapabilityId.Value, StringComparer.Ordinal)
            .FirstOrDefault();

        if (providerDescriptor is null)
            return new ProviderPlanAttempt(null, null);

        try
        {
            using var services = _builder.Services.BuildServiceProvider();
            var capability = services.GetService(providerDescriptor.CapabilityType)
                ?? throw new InvalidOperationException($"Registered model provider '{providerDescriptor.CapabilityId.Value}' could not be resolved from builder services.");

            if (capability is not IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult> provider)
            {
                throw new InvalidOperationException(
                    $"Registered model provider '{providerDescriptor.CapabilityId.Value}' does not implement the expected DeepSeek capability contract.");
            }

            var providerCorrelationId = $"plan-session-{snapshot.SessionId.Value}-operation-plan";
            var promptContext = BuildRuntimePromptContext(snapshot);
            var gateResult = _promptContextPolicyGate.Evaluate(
                snapshot,
                promptContext,
                _builder.PolicyRegistrations
                    .Select(static registration => registration.PolicyId.Value)
                    .ToArray());

            if (!gateResult.IsAccepted)
            {
                return new ProviderPlanAttempt(
                    null,
                    $"prompt-context-policy-gate:{gateResult.ReasonCode}");
            }

            var composedContextPayload = promptContext.RenderComposedProviderContextPayload(task);

            var response = await provider.ExecuteAsync(
                new ModelProviderRequest<DeepSeekChatCompletionRequest>(
                    payload: new DeepSeekChatCompletionRequest(
                    [
                        new DeepSeekChatMessage(
                            "system",
                            composedContextPayload),
                        new DeepSeekChatMessage(
                            "user",
                            "Generate a deterministic numbered implementation plan from the provided context. Return plain text numbered steps only.")
                    ]),
                    modelId: "deepseek-chat",
                    correlationId: providerCorrelationId),
                new CapabilityContext(snapshot.SessionId, snapshot.WorktreePath),
                cancellationToken);

            if (!string.Equals(response.CorrelationId, providerCorrelationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Registered model provider '{providerDescriptor.CapabilityId.Value}' returned correlation '{response.CorrelationId ?? "(null)"}' that did not match '{providerCorrelationId}'.");
            }

            if (string.IsNullOrWhiteSpace(response.Payload.Content))
            {
                throw new InvalidOperationException(
                    $"Registered model provider '{providerDescriptor.CapabilityId.Value}' returned empty plan content.");
            }

            var markdown = response.Payload.Content.Trim();
            var steps = NormalizeProviderPlanSteps(
                ParseProviderPlanSteps(markdown),
                promptContext.ForbiddenEcosystems);
            return new ProviderPlanAttempt(
                new ProviderPlanDraft(markdown, steps, response.ProviderId, response.ModelId, providerCorrelationId),
                null);
        }
        catch (InvalidOperationException ex) when (TryGetMissingProviderApiKeyReason(ex, out var fallbackReason))
        {
            return new ProviderPlanAttempt(null, fallbackReason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Model-provider plan generation failed: {ex.Message}", ex);
        }
    }

    private static bool TryGetMissingProviderApiKeyReason(Exception exception, out string fallbackReason)
    {
        fallbackReason = string.Empty;

        const string missingApiKeyPrefix = "DeepSeek provider configuration is invalid: Environment variable '";
        const string missingApiKeySuffix = "' is required but was not found or empty.";

        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (!message.StartsWith(missingApiKeyPrefix, StringComparison.Ordinal)
                || !message.EndsWith(missingApiKeySuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var variableName = message[missingApiKeyPrefix.Length..^missingApiKeySuffix.Length];
            if (string.IsNullOrWhiteSpace(variableName))
            {
                continue;
            }

            fallbackReason = $"provider-credentials-missing:{variableName}";
            return true;
        }

        return false;
    }
    private RuntimePromptContext BuildRuntimePromptContext(SessionSnapshot snapshot)
    {
        if (_builder is null)
            throw new InvalidOperationException("Cannot build runtime prompt context without an active builder.");

        if (_promptContextFactory is not null)
        {
            var context = _promptContextFactory(snapshot);
            return context ?? throw new InvalidOperationException("Prompt context factory returned null.");
        }

        return _promptContextComposer.Compose(
            snapshot,
            _builder,
            _repositoryRoot,
            _policyId,
            _validationCommands,
            _diagnosticsBridge,
            _selectedWorkflowId);
    }

    private static IReadOnlyList<string> ParseProviderPlanSteps(string markdown)
    {
        var lines = markdown
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Where(static line => !line.StartsWith("#", StringComparison.Ordinal))
            .Select(static line => TryTrimPlanListPrefix(line, out var trimmed) ? trimmed : null)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => line!)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length == 0)
            throw new InvalidOperationException("Provider output did not contain any numbered plan steps.");

        return lines;
    }

    private static IReadOnlyList<string> NormalizeProviderPlanSteps(
        IReadOnlyList<string> steps,
        IReadOnlyList<string> forbiddenEcosystems)
    {
        if (steps.Count == 0 || forbiddenEcosystems.Count == 0)
            return steps;

        var normalizedSteps = new List<string>(steps.Count);

        foreach (var step in steps)
        {
            if (TryRewriteForbiddenToolchainStep(step, forbiddenEcosystems, out var normalizedStep))
            {
                normalizedSteps.Add(normalizedStep);
                continue;
            }

            normalizedSteps.Add(step);
        }

        return normalizedSteps;
    }

    private static bool TryRewriteForbiddenToolchainStep(
        string step,
        IReadOnlyList<string> forbiddenEcosystems,
        out string normalizedStep)
    {
        foreach (var ecosystem in forbiddenEcosystems)
        {
            if (!ContainsForbiddenEcosystemToken(step, ecosystem))
                continue;

            normalizedStep = $"Plan step blocked by .NET-only policy: {ecosystem}";
            return true;
        }

        normalizedStep = step;
        return false;
    }

    private static bool ContainsForbiddenEcosystemToken(string step, string ecosystem)
    {
        if (string.IsNullOrWhiteSpace(step) || string.IsNullOrWhiteSpace(ecosystem))
            return false;

        var pattern = $"(?<![A-Za-z0-9_.-]){Regex.Escape(ecosystem.Trim())}(?![A-Za-z0-9_.-])";
        return Regex.IsMatch(step, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool TryTrimPlanListPrefix(string line, out string trimmedLine)
    {
        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
        {
            trimmedLine = line[2..].Trim();
            return !string.IsNullOrWhiteSpace(trimmedLine);
        }

        var markerIndex = line.IndexOfAny(['.', ')']);
        if (markerIndex > 0 && line[..markerIndex].All(static character => char.IsDigit(character)))
        {
            trimmedLine = line[(markerIndex + 1)..].Trim();
            return !string.IsNullOrWhiteSpace(trimmedLine);
        }

        trimmedLine = string.Empty;
        return false;
    }

    private async Task HandleRunAsync(CancellationToken cancellationToken)
    {
        if (_builder is null)
        {
            await WriteLineAsync("Cannot run workflow: no active builder is configured for this shell instance.");
            return;
        }

        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");

        try
        {
            if (!await EnsureWorkflowSelectionForRunAsync(snapshot, cancellationToken))
                return;

            var result = await _orchestrator.RunWorkflowAsync(snapshot.SessionId, _builder, _selectedWorkflowId, cancellationToken);
            _activeSession = await _orchestrator.GetSessionAsync(snapshot.SessionId, cancellationToken) ?? snapshot;
            _selectedWorkflowId = _activeSession.WorkflowId;
            var artifactStore = new SessionArtifactStore(_activeSession);
            var summaryArtifact = await artifactStore.SaveAsync(
                _activeSession.SessionId,
                BuildWorkflowExecutionArtifact(result),
                cancellationToken);
            var agentRunResultArtifact = await artifactStore.SaveAsync(
                _activeSession.SessionId,
                BuildAgentRunResultArtifact(AgentRunResult.FromWorkflowExecution(result)),
                cancellationToken);

            var lines = new List<string>
            {
                $"Workflow executed: {result.WorkflowId}",
                $"Workflow artifact: {summaryArtifact.RelativePath}",
                $"AgentRunResult artifact: {agentRunResultArtifact.RelativePath}"
            };
            foreach (var stage in result.Stages)
            {
                var transition = stage.AppliedTransition ? "applied" : "skipped";
                var mappedInput = string.IsNullOrWhiteSpace(stage.MappedInputContractName)
                    ? string.Empty
                    : $" mappedInput={stage.MappedInputContractName}";
                lines.Add($"- {stage.Descriptor.Stage} state={stage.StateAfterStage} transition={transition}{mappedInput}");
            }

            lines.Add($"Active session state: {_activeSession.State}");
            await WriteDeterministicBlockAsync(lines);
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Run failed: {ex.Message}");
        }
    }

    private async Task<bool> EnsureWorkflowSelectionForRunAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_builder is null)
            return false;

        var registrations = _builder.WorkflowRegistrations
            .OrderBy(static registration => registration.WorkflowId.Value, StringComparer.Ordinal)
            .ToArray();

        if (registrations.Length == 0)
        {
            await WriteLineAsync("Cannot run workflow: no workflows are registered in the active builder.");
            return false;
        }

        if (_selectedWorkflowId.HasValue && registrations.Any(registration => registration.WorkflowId.Equals(_selectedWorkflowId.Value)))
            return true;

        if (registrations.Any(registration => registration.WorkflowId.Equals(snapshot.WorkflowId)))
        {
            _selectedWorkflowId = snapshot.WorkflowId;
            return true;
        }

        var config = GetEffectiveRepositoryConfig(snapshot.RepositoryPath);
        if (!string.IsNullOrWhiteSpace(config.DefaultWorkflowId))
        {
            var configuredDefaultWorkflowId = new WorkflowId(config.DefaultWorkflowId.Trim());
            if (registrations.Any(registration => registration.WorkflowId.Equals(configuredDefaultWorkflowId)))
            {
                _activeSession = await _orchestrator.SelectWorkflowAsync(snapshot.SessionId, configuredDefaultWorkflowId, cancellationToken);
                _selectedWorkflowId = _activeSession.WorkflowId;
                return true;
            }
        }

        if (registrations.Length == 1)
        {
            var singleWorkflowId = registrations[0].WorkflowId;
            _activeSession = await _orchestrator.SelectWorkflowAsync(snapshot.SessionId, singleWorkflowId, cancellationToken);
            _selectedWorkflowId = _activeSession.WorkflowId;
            return true;
        }

        var lines = new List<string>
        {
            "Run blocked: workflow selection is ambiguous.",
            "No default workflow could be resolved from the active session or repository configuration.",
            $"Session workflow: {snapshot.WorkflowId}",
            $"Configured defaultWorkflowId: {config.DefaultWorkflowId}",
            "Registered workflows:"
        };

        foreach (var registration in registrations)
            lines.Add($"- {registration.WorkflowId.Value} [{registration.Descriptor.DisplayName}]");

        lines.Add("Select a workflow with: use workflow <id>");
        lines.Add("Then run 'run' again.");

        await WriteDeterministicBlockAsync(lines);
        return false;
    }

    private async Task HandleDiffAsync(CancellationToken cancellationToken)
    {
        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");

        try
        {
            var artifactStore = new SessionArtifactStore(snapshot);
            var worktreeDiff = await BuildWorktreeDiffSnapshotAsync(snapshot, cancellationToken);
            var result = await _orchestrator.GenerateDiffAsync(
                snapshot.SessionId,
                new SessionDiffRequest(worktreeDiff.DiffHash, worktreeDiff.ChangedFiles, worktreeDiff.Patch),
                artifactStore,
                cancellationToken);

            var diffSummaryArtifact = await artifactStore.SaveAsync(
                snapshot.SessionId,
                BuildDiffSummaryArtifact(result),
                cancellationToken);

            var lines = new List<string>
            {
                $"Diff context: session={snapshot.SessionId} state={snapshot.State}",
                $"repository: {snapshot.RepositoryPath}",
                $"worktree: {snapshot.WorktreePath}",
                $"diffHash: {result.DiffHash}",
                $"Diff artifact: {result.DiffArtifact.RelativePath}",
                $"Diff summary artifact: {diffSummaryArtifact.RelativePath}"
            };

            if (result.ChangedFiles.Count == 0)
            {
                lines.Add("worktree files: (none)");
                lines.Add("Changed files: (none)");
            }
            else
            {
                lines.Add("worktree files:");
                foreach (var file in result.ChangedFiles)
                    lines.Add($"- {file}");

                lines.Add("Changed files:");
                foreach (var file in result.ChangedFiles)
                    lines.Add($"- {file}");
            }

            await WriteDeterministicBlockAsync(lines);
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Diff failed: {ex.Message}");
        }
    }

    private async Task HandleCheckpointAsync(CancellationToken cancellationToken)
    {
        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");
        var sessionDirectory = GetSessionDirectory(snapshot);

        try
        {
            var artifactStore = new SessionArtifactStore(snapshot);
            var worktreeDiff = await BuildWorktreeDiffSnapshotAsync(snapshot, cancellationToken);
            var result = await _orchestrator.CreateCheckpointAsync(
                snapshot.SessionId,
                new SessionCheckpointRequest(
                    Name: $"shell-checkpoint-{snapshot.State.ToString().ToLowerInvariant()}",
                    DiffHash: worktreeDiff.DiffHash,
                    ChangedFiles: worktreeDiff.ChangedFiles,
                    Patch: worktreeDiff.Patch),
                artifactStore,
                cancellationToken);

            await WriteDeterministicBlockAsync(
            [
                $"Checkpoint completed: session={snapshot.SessionId} state={snapshot.State}",
                $"diffHash: {worktreeDiff.DiffHash}",
                $"Checkpoint artifact: {result.CheckpointArtifact.RelativePath}",
                $"sessionStatePath: {Path.Combine(sessionDirectory, "session-state.json")}",
                $"eventJournalPath: {Path.Combine(sessionDirectory, "event-journal.ndjson")}"
            ]);
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Checkpoint failed: {ex.Message}");
        }
    }

    private async Task HandleValidateAsync(CancellationToken cancellationToken)
    {
        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");

        try
        {
            var artifactStore = new SessionArtifactStore(snapshot);
            var worktreeDiff = await BuildWorktreeDiffSnapshotAsync(snapshot, cancellationToken);
            var runtimeValidation = TryCreateRuntimeValidationRequest();
            SessionValidationResult result;
            ValidationArtifactPayload validationReport;
            SessionValidationCommandResult? restoreResult = null;

            if (runtimeValidation is not null)
            {
                result = await _orchestrator.ValidateAsync(
                    snapshot.SessionId,
                    new SessionValidationRequest(
                        BuildSucceeded: false,
                        TestSucceeded: false,
                        DiffHash: worktreeDiff.DiffHash,
                        Summary: $"Shell governance validation recorded for diff {worktreeDiff.DiffHash}.",
                        RuntimeValidation: runtimeValidation),
                    artifactStore,
                    cancellationToken);

                validationReport = await ReadValidationArtifactPayloadAsync(snapshot, result.ValidationArtifact, cancellationToken)
                    ?? throw new InvalidOperationException("Latest validation artifact could not be parsed.");
            }
            else
            {
                var commandResults = await ExecuteValidationCommandsAsync(snapshot, cancellationToken);
                restoreResult = commandResults.FirstOrDefault(static result => IsDotNetCommand(result.Command, "restore"));
                var buildResult = commandResults.First(static result => IsDotNetCommand(result.Command, "build"));
                var testResult = commandResults.First(static result => IsDotNetCommand(result.Command, "test"));
                var restoreSummary = restoreResult is null
                    ? "restoreExitCode=n/a"
                    : $"restoreExitCode={restoreResult.ExitCode}";
                result = await _orchestrator.ValidateAsync(
                    snapshot.SessionId,
                    new SessionValidationRequest(
                        BuildSucceeded: buildResult.Succeeded,
                        TestSucceeded: testResult.Succeeded,
                        DiffHash: worktreeDiff.DiffHash,
                        Summary: $"Shell governance validation recorded for diff {worktreeDiff.DiffHash} ({restoreSummary}; buildExitCode={buildResult.ExitCode}; testExitCode={testResult.ExitCode}).",
                        CommandResults: commandResults),
                    artifactStore,
                    cancellationToken);

                validationReport = new ValidationArtifactPayload(
                    BuildSucceeded: buildResult.Succeeded,
                    TestSucceeded: testResult.Succeeded,
                    DiffHash: worktreeDiff.DiffHash,
                    Summary: $"Shell governance validation recorded for diff {worktreeDiff.DiffHash} ({restoreSummary}; buildExitCode={buildResult.ExitCode}; testExitCode={testResult.ExitCode}).",
                    ProducedAtUtc: DateTimeOffset.UtcNow,
                    BuildCommand: buildResult.Command,
                    BuildExitCode: buildResult.ExitCode,
                    BuildTimedOut: buildResult.TimedOut,
                    TestCommand: testResult.Command,
                    TestExitCode: testResult.ExitCode,
                    TestTimedOut: testResult.TimedOut);
            }

            _activeSession = await _orchestrator.GetSessionAsync(snapshot.SessionId, cancellationToken) ?? result.Session;
            _selectedWorkflowId = _activeSession.WorkflowId;

            await WriteDeterministicBlockAsync(
            [
                $"Validation completed: session={_activeSession.SessionId} state={_activeSession.State}",
                $"Command 'validate' completed for session {_activeSession.SessionId}: state={_activeSession.State}.",
                $"validation={(result.Succeeded ? "Passed" : "Failed")}",
                restoreResult is null
                    ? "restoreCommand: (not configured)"
                    : $"restoreCommand: {restoreResult.Command} exitCode={restoreResult.ExitCode} timedOut={restoreResult.TimedOut}",
                $"buildCommand: {validationReport.BuildCommand ?? "(missing)"} exitCode={validationReport.BuildExitCode?.ToString() ?? "(missing)"} timedOut={validationReport.BuildTimedOut?.ToString() ?? "(missing)"}",
                $"testCommand: {validationReport.TestCommand ?? "(missing)"} exitCode={validationReport.TestExitCode?.ToString() ?? "(missing)"} timedOut={validationReport.TestTimedOut?.ToString() ?? "(missing)"}",
                $"diffHash: {validationReport.DiffHash ?? result.DiffHash ?? "(missing)"}",
                $"Validation artifact: {result.ValidationArtifact.RelativePath}"
            ]);
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Validation failed: {ex.Message}");
        }
    }

    private SessionRuntimeDotNetValidationRequest? TryCreateRuntimeValidationRequest()
    {
        if (_validationCommands.Count == 0)
            return null;

        var parsed = _validationCommands
            .Select(ParseValidationCommand)
            .ToArray();
        var build = parsed.FirstOrDefault(static command => command.Kind == ValidationCommandKind.Build);
        var test = parsed.FirstOrDefault(static command => command.Kind == ValidationCommandKind.Test);

        if (build is null || test is null)
            return null;

        var buildProjectPath = GetValidationProjectPath(build);
        var testProjectPath = GetValidationProjectPath(test);
        if (buildProjectPath is null || testProjectPath is null)
            return null;

        return new SessionRuntimeDotNetValidationRequest(
            BuildProjectPath: buildProjectPath,
            TestProjectPath: testProjectPath,
            DotNetExecutablePath: build.Executable);
    }

    private async Task<IReadOnlyList<SessionValidationCommandResult>> ExecuteValidationCommandsAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_validationCommands.Count == 0)
        {
            return
            [
                CreateSimulatedValidationCommandResult(
                    "dotnet restore (simulated)",
                    "Simulated restore validation for shell-only mode."),
                CreateSimulatedValidationCommandResult(
                    "dotnet build (simulated)",
                    "Simulated build validation for shell-only mode."),
                CreateSimulatedValidationCommandResult(
                    "dotnet test (simulated)",
                    "Simulated test validation for shell-only mode.")
            ];
        }

        var parsed = _validationCommands
            .Select(ParseValidationCommand)
            .ToArray();
        var restore = parsed.FirstOrDefault(static command => command.Kind == ValidationCommandKind.Restore);
        var build = parsed.FirstOrDefault(static command => command.Kind == ValidationCommandKind.Build)
            ?? throw new InvalidOperationException("Validation requires a configured 'dotnet build' command.");
        var test = parsed.FirstOrDefault(static command => command.Kind == ValidationCommandKind.Test)
            ?? throw new InvalidOperationException("Validation requires a configured 'dotnet test' command.");

        var commandsToRun = restore is null
            ? new[] { build, test }
            : new[] { restore, build, test };
        await EnforceLocalSafeValidationPolicyAsync(snapshot, commandsToRun, cancellationToken);

        var results = new List<SessionValidationCommandResult>(capacity: 3);

        if (restore is not null)
        {
            var restoreResult = await RunValidationCommandAsync(restore, snapshot.WorktreePath, cancellationToken);
            results.Add(restoreResult);
            if (!restoreResult.Succeeded)
            {
                results.Add(CreateSimulatedValidationCommandResult(
                    "dotnet build (skipped: restore failed)",
                    $"Skipped because restore command failed with exitCode {restoreResult.ExitCode}.",
                    exitCode: 1));
                results.Add(CreateSimulatedValidationCommandResult(
                    "dotnet test (skipped: restore failed)",
                    $"Skipped because restore command failed with exitCode {restoreResult.ExitCode}.",
                    exitCode: 1));
                return results;
            }
        }

        var buildResult = await RunValidationCommandAsync(build, snapshot.WorktreePath, cancellationToken);
        var testResult = await RunValidationCommandAsync(test, snapshot.WorktreePath, cancellationToken);
        results.Add(buildResult);
        results.Add(testResult);
        return results;
    }

    private static SessionValidationCommandResult CreateSimulatedValidationCommandResult(
        string command,
        string standardOutput,
        int exitCode = 0,
        string standardError = "",
        bool timedOut = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new SessionValidationCommandResult(
            Command: command,
            ExitCode: exitCode,
            StandardOutput: standardOutput,
            StandardError: standardError,
            TimedOut: timedOut,
            StartedAtUtc: now,
            CompletedAtUtc: now);
    }

    private async Task EnforceLocalSafeValidationPolicyAsync(
        SessionSnapshot snapshot,
        IReadOnlyCollection<ParsedValidationCommand> commands,
        CancellationToken cancellationToken)
    {
        if (_localSafePolicy is null)
            return;

        var expectedWorktreePath = ResolveExpectedSessionWorktreePath(snapshot);
        foreach (var command in commands)
        {
            var decision = await _localSafePolicy.EvaluateAsync(
                new LocalSafePolicyRequest(
                    Command: command.OriginalCommand,
                    WorkingDirectory: snapshot.WorktreePath,
                    ValidationSucceeded: true,
                    ApprovalGranted: false,
                    RequireValidation: false,
                    RequireApproval: false),
                new PolicyContext(
                    snapshot.SessionId,
                    snapshot.WorkflowId,
                    expectedWorktreePath,
                    "Wip.Tools.Shell.Execute"),
                cancellationToken);

            if (!decision.IsAllowed)
                throw new InvalidOperationException(decision.Reason);
        }
    }

    private async Task EnforceLocalSafeMergePolicyAsync(
        SessionSnapshot snapshot,
        bool validationSucceeded,
        bool approvalGranted,
        CancellationToken cancellationToken)
    {
        if (_localSafePolicy is null)
            return;

        var expectedWorktreePath = ResolveExpectedSessionWorktreePath(snapshot);
        var decision = await _localSafePolicy.EvaluateAsync(
            new LocalSafePolicyRequest(
                Command: "merge",
                WorkingDirectory: snapshot.WorktreePath,
                ValidationSucceeded: validationSucceeded,
                ApprovalGranted: approvalGranted,
                RequireValidation: true,
                RequireApproval: true),
            new PolicyContext(
                snapshot.SessionId,
                snapshot.WorkflowId,
                expectedWorktreePath,
                "Wip.Runtime.Merge"),
            cancellationToken);

        if (!decision.IsAllowed)
            throw new InvalidOperationException(decision.Reason);
    }

    private static string ResolveExpectedSessionWorktreePath(SessionSnapshot snapshot)
        => Path.GetFullPath(Path.Combine(snapshot.RepositoryPath, ".wip", "worktrees", snapshot.SessionId.Value));

    private static ParsedValidationCommand ParseValidationCommand(string command)
    {
        var parts = Tokenize(command);
        if (parts.Length < 2)
            throw new InvalidOperationException($"Invalid validation command '{command}'. Expected format: dotnet <build|test> ...");

        var executableName = Path.GetFileNameWithoutExtension(parts[0]);
        if (!string.Equals(executableName, "dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Validation command '{command}' is not allowed. Only dotnet build/test commands are supported.");

        var verb = parts[1].ToLowerInvariant();
        var kind = verb switch
        {
            "restore" => ValidationCommandKind.Restore,
            "build" => ValidationCommandKind.Build,
            "test" => ValidationCommandKind.Test,
            _ => throw new InvalidOperationException($"Validation command '{command}' is not allowed. Only dotnet restore/build/test commands are supported.")
        };

        return new ParsedValidationCommand(command, parts[0], parts.Skip(1).ToArray(), kind);
    }

    private static async Task<SessionValidationCommandResult> RunValidationCommandAsync(
        ParsedValidationCommand command,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var processStartInfo = new ProcessStartInfo(command.Executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in command.Arguments)
            processStartInfo.ArgumentList.Add(argument);

        using var process = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException($"Failed to start validation command '{command.OriginalCommand}'.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort kill only.
            }

            await process.WaitForExitAsync(cancellationToken);
        }

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;
        if (timedOut)
        {
            var timeoutMessage = "Command timed out after 300000 ms.";
            stdErr = string.IsNullOrWhiteSpace(stdErr)
                ? timeoutMessage
                : $"{stdErr}{Environment.NewLine}{timeoutMessage}";
        }

        return new SessionValidationCommandResult(
            Command: command.OriginalCommand,
            ExitCode: timedOut ? -1 : process.ExitCode,
            StandardOutput: stdOut,
            StandardError: stdErr,
            TimedOut: timedOut,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: DateTimeOffset.UtcNow);
    }

    private static bool IsDotNetCommand(string command, string verb)
    {
        var parts = Tokenize(command);
        if (parts.Length < 2)
            return false;

        var executableName = Path.GetFileNameWithoutExtension(parts[0]);
        return string.Equals(executableName, "dotnet", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], verb, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetValidationProjectPath(ParsedValidationCommand command)
        => command.Arguments
            .Skip(1)
            .FirstOrDefault(static argument => !argument.StartsWith("-", StringComparison.Ordinal));

    private async Task HandleReviewAsync(CancellationToken cancellationToken)
    {
        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");

        try
        {
            var artifactStore = new SessionArtifactStore(snapshot);
            var descriptors = await artifactStore.ListAsync(snapshot.SessionId, cancellationToken);
            var validationArtifact = GetLatestArtifact(descriptors, "validation-report-")
                ?? throw new InvalidOperationException("Review requires a persisted validation report. Run 'validate' first.");
            var validationReport = await ReadValidationArtifactPayloadAsync(snapshot, validationArtifact, cancellationToken)
                ?? throw new InvalidOperationException("Latest validation artifact could not be parsed.");
            var worktreeDiff = await BuildWorktreeDiffSnapshotAsync(snapshot, cancellationToken);

            var result = await _orchestrator.ReviewAsync(
                snapshot.SessionId,
                new ReviewRequest(
                    snapshot.SessionId,
                    worktreeDiff.DiffHash,
                    worktreeDiff.ChangedFiles,
                    new ReviewValidationStatus(
                        validationReport.BuildSucceeded,
                        validationReport.TestSucceeded,
                        validationReport.DiffHash,
                        validationArtifact.ArtifactId.Value,
                        validationArtifact.RelativePath)),
                new WipRuntimeReviewGenerator(artifactStore),
                cancellationToken);

            _activeSession = await _orchestrator.GetSessionAsync(snapshot.SessionId, cancellationToken) ?? result.Session;
            _selectedWorkflowId = _activeSession.WorkflowId;

            var reviewSummaryArtifact = await artifactStore.SaveAsync(
                snapshot.SessionId,
                BuildReviewSummaryArtifact(result, validationArtifact, validationReport, worktreeDiff),
                cancellationToken);

            await WriteDeterministicBlockAsync(
            [
                $"Review generated: session={_activeSession.SessionId} state={_activeSession.State} stale={result.Review.Staleness.IsStale}",
                $"Command 'review' completed for session {_activeSession.SessionId}: state={_activeSession.State}.",
                $"validation={(validationReport.BuildSucceeded && validationReport.TestSucceeded ? "Passed" : "Failed")}",
                $"Review artifact: {result.Review.ReportArtifact.RelativePath}",
                $"Review summary artifact: {reviewSummaryArtifact.RelativePath}"
            ]);
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Review failed: {ex.Message}");
        }
    }

    private async Task HandleApproveAsync(CancellationToken cancellationToken)
    {
        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");

        try
        {
            var artifactStore = new SessionArtifactStore(snapshot);
            var descriptors = await artifactStore.ListAsync(snapshot.SessionId, cancellationToken);
            var validationArtifact = GetLatestArtifact(descriptors, "validation-report-");
            var validationReport = validationArtifact is null
                ? null
                : await ReadValidationArtifactPayloadAsync(snapshot, validationArtifact, cancellationToken);
            await EnforceLocalSafeApprovalPolicyAsync(
                snapshot,
                validationSucceeded: validationReport is { BuildSucceeded: true, TestSucceeded: true },
                cancellationToken);

            if (validationArtifact is null)
                throw new InvalidOperationException("Approval requires a persisted validation report. Run 'validate' first.");
            var reviewArtifact = GetLatestArtifact(descriptors, "review-report-")
                ?? throw new InvalidOperationException("Approval requires a persisted review report. Run 'review' first.");
            var reviewSummaryArtifact = GetLatestArtifact(descriptors, "review-summary-")
                ?? throw new InvalidOperationException("Approval requires a persisted review summary. Run 'review' first.");

            validationReport = validationReport
                ?? await ReadValidationArtifactPayloadAsync(snapshot, validationArtifact, cancellationToken);
            validationReport = validationReport
                ?? throw new InvalidOperationException("Latest validation artifact could not be parsed.");
            var reviewSummary = await ReadJsonArtifactAsync<ReviewSummaryArtifactPayload>(snapshot, reviewSummaryArtifact, cancellationToken)
                ?? throw new InvalidOperationException("Latest review summary artifact could not be parsed.");
            var worktreeDiff = await BuildWorktreeDiffSnapshotAsync(snapshot, cancellationToken);

            if (reviewSummary.IsStale)
            {
                var staleReason = string.IsNullOrWhiteSpace(reviewSummary.StaleReason)
                    ? "Approval failed: current review evidence is stale."
                    : $"Approval failed: current review evidence is stale. {reviewSummary.StaleReason}";

                throw new InvalidOperationException($"{staleReason} {ApprovalReviewRecoveryGuidance}");
            }

            if (!string.Equals(reviewSummary.CurrentDiffHash, worktreeDiff.DiffHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Approval failed: current review evidence does not match the active diff hash. {ApprovalReviewRecoveryGuidance}");
            }

            var validationPassed = validationReport.BuildSucceeded && validationReport.TestSucceeded;
            var validationMatchesCurrentDiff = string.Equals(validationReport.DiffHash, worktreeDiff.DiffHash, StringComparison.Ordinal);

            await WriteDeterministicBlockAsync(
            [
                "Approval confirmation required:",
                $"session={snapshot.SessionId}",
                $"reviewStale: {reviewSummary.IsStale}",
                $"validation: {(validationPassed ? "Passed" : "Failed")}",
                $"validationCurrentDiffMatch: {validationMatchesCurrentDiff}",
                $"targetBranch: {snapshot.TargetBranch}",
                $"targetCommit: {snapshot.TargetCommit}",
                "Type 'yes' to create an approval token or anything else to cancel."
            ]);

            var confirmation = await _input.ReadLineAsync(cancellationToken);
            if (!string.Equals(confirmation?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
            {
                await WriteLineAsync($"Approval cancelled for session {snapshot.SessionId}.");
                return;
            }

            var result = await _orchestrator.CreateApprovalTokenAsync(
                snapshot.SessionId,
                new ApprovalTokenRequest(
                    SessionId: snapshot.SessionId,
                    WorkflowId: snapshot.WorkflowId,
                    DiffHash: worktreeDiff.DiffHash,
                    TargetBranch: snapshot.TargetBranch,
                    TargetCommit: snapshot.TargetCommit,
                    ReviewReport: new ApprovalReviewReport(
                        reviewArtifact.ArtifactId,
                        reviewSummary.CurrentDiffHash,
                        reviewSummary.IsStale,
                        reviewSummary.StaleReason),
                    ValidationReport: new ApprovalValidationReport(
                        validationArtifact.ArtifactId,
                        validationReport.BuildSucceeded,
                        validationReport.TestSucceeded,
                        validationReport.DiffHash ?? string.Empty)),
                new WipRuntimeApprovalTokenFactory(),
                artifactStore,
                cancellationToken);

            _activeSession = await _orchestrator.GetSessionAsync(snapshot.SessionId, cancellationToken) ?? result.Session;
            _selectedWorkflowId = _activeSession.WorkflowId;

            await WriteDeterministicBlockAsync(
            [
                $"Approval completed: session={_activeSession.SessionId} state={_activeSession.State}",
                $"Command 'approve' completed for session {_activeSession.SessionId}: state={_activeSession.State}.",
                $"diffHash: {result.ApprovalToken.Binding.DiffHash}",
                $"bindingHash: {result.ApprovalToken.BindingHash}",
                $"Approval artifact: {result.ApprovalArtifact.RelativePath}"
            ]);
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Approval failed: {ex.Message}");
        }
    }

    private async Task HandleMergeAsync(CancellationToken cancellationToken)
    {
        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");

        try
        {
            var artifactStore = new SessionArtifactStore(snapshot);
            var descriptors = await artifactStore.ListAsync(snapshot.SessionId, cancellationToken);
            var approvalArtifact = GetLatestArtifact(descriptors, "approval-token-");
            var validationArtifact = GetLatestArtifact(descriptors, "validation-report-");
            var validationReport = validationArtifact is null
                ? null
                : await ReadValidationArtifactPayloadAsync(snapshot, validationArtifact, cancellationToken);
            await EnforceLocalSafeMergePolicyAsync(
                snapshot,
                validationSucceeded: validationReport is { BuildSucceeded: true, TestSucceeded: true },
                approvalGranted: approvalArtifact is not null,
                cancellationToken);

            if (approvalArtifact is null)
                throw new InvalidOperationException("Merge requires a persisted approval token. Run 'approve' first.");

            if (validationArtifact is null)
                throw new InvalidOperationException("Merge requires a persisted validation report. Run 'validate' first.");
            var reviewSummaryArtifact = GetLatestArtifact(descriptors, "review-summary-")
                ?? throw new InvalidOperationException("Merge requires a persisted review summary. Run 'review' first.");

            var approvalToken = await ReadJsonArtifactAsync<ApprovalToken>(snapshot, approvalArtifact, cancellationToken)
                ?? throw new InvalidOperationException("Latest approval artifact could not be parsed.");
            validationReport = validationReport
                ?? await ReadValidationArtifactPayloadAsync(snapshot, validationArtifact, cancellationToken);
            validationReport = validationReport
                ?? throw new InvalidOperationException("Latest validation artifact could not be parsed.");
            var reviewSummary = await ReadJsonArtifactAsync<ReviewSummaryArtifactPayload>(snapshot, reviewSummaryArtifact, cancellationToken)
                ?? throw new InvalidOperationException("Latest review summary artifact could not be parsed.");
            var worktreeDiff = await BuildWorktreeDiffSnapshotAsync(snapshot, cancellationToken);
            var liveReviewStaleness = WipRuntimeReviewGenerator.DetectStaleness(validationReport.DiffHash, worktreeDiff.DiffHash);
            var approvalMatchesCurrentDiff = string.Equals(approvalToken.Binding.DiffHash, worktreeDiff.DiffHash, StringComparison.Ordinal);
            var targetBaselineMatchesApproval = string.Equals(approvalToken.Binding.TargetBranch, snapshot.TargetBranch, StringComparison.Ordinal)
                && string.Equals(approvalToken.Binding.TargetCommit, snapshot.TargetCommit, StringComparison.Ordinal);
            var workspaceSession = new WorkspaceSession(
                SessionId: snapshot.SessionId,
                RepositoryPath: snapshot.RepositoryPath,
                WorktreePath: snapshot.WorktreePath,
                SessionBranch: BuildSessionBranchName(snapshot.SessionId),
                TargetBranch: snapshot.TargetBranch,
                TargetCommitAtCreation: snapshot.TargetCommit);
            var previewUnavailableReason = string.Empty;
            var mergePreview = default(MergePreview);
            var mergePreviewAvailable = true;
            try
            {
                mergePreview = await _workspaceProvider.PreviewMergeAsync(workspaceSession, cancellationToken);
            }
            catch (Exception previewEx)
            {
                mergePreviewAvailable = false;
                previewUnavailableReason = previewEx.Message;
                mergePreview = new MergePreview(
                    TargetBranch: snapshot.TargetBranch,
                    SessionBranch: BuildSessionBranchName(snapshot.SessionId),
                    ExpectedTargetCommit: snapshot.TargetCommit,
                    CurrentTargetCommit: snapshot.TargetCommit,
                    HasTargetCommitDrift: false,
                    IsFastForward: true,
                    CanMerge: true,
                    BlockReason: null);
            }

            await WriteDeterministicBlockAsync(
            [
                "Merge preflight:",
                $"session={snapshot.SessionId}",
                $"reviewStale: {liveReviewStaleness.IsStale}",
                $"validationCurrentDiffMatch: {!liveReviewStaleness.IsStale}",
                $"approvalCurrentDiffMatch: {approvalMatchesCurrentDiff}",
                $"targetBaselineMatchesApproval: {targetBaselineMatchesApproval}",
                $"mergePreviewHasTargetCommitDrift: {mergePreview.HasTargetCommitDrift}",
                $"mergePreviewIsFastForward: {mergePreview.IsFastForward}",
                $"mergePreviewCanMerge: {mergePreview.CanMerge}"
            ]);

            if (liveReviewStaleness.IsStale || !approvalMatchesCurrentDiff)
            {
                throw new InvalidOperationException(
                    $"Merge failed: approval is stale because the current diff no longer matches the last reviewed and validated candidate. {liveReviewStaleness.Reason ?? "Approval diff binding does not match the current candidate."} Run 'diff', 'validate', 'review', and 'approve' again.");
            }

            if (!targetBaselineMatchesApproval)
            {
                throw new InvalidOperationException(
                    $"Merge failed: target branch drift detected. Approval was bound to {approvalToken.Binding.TargetBranch}@{approvalToken.Binding.TargetCommit}, but the current session baseline is {snapshot.TargetBranch}@{snapshot.TargetCommit}. {TargetBranchDriftRecovery.FormatGuidance()}");
            }

            if (!mergePreview.CanMerge)
            {
                if (mergePreview.HasTargetCommitDrift)
                {
                    throw new InvalidOperationException(
                        $"Merge failed: target branch drift detected. Approval was bound to {mergePreview.TargetBranch}@{mergePreview.ExpectedTargetCommit}, but current target head is {mergePreview.CurrentTargetCommit}. {TargetBranchDriftRecovery.FormatGuidance()}");
                }

                throw new InvalidOperationException(
                    $"Merge failed: merge preview rejected the candidate before mutation. {mergePreview.BlockReason ?? "Merge preview did not provide a reason."}");
            }

            var workspaceMerge = mergePreviewAvailable
                ? await _workspaceProvider.MergeAsync(
                    new MergeRequest(
                        RepositoryPath: snapshot.RepositoryPath,
                        TargetBranch: snapshot.TargetBranch,
                        SessionBranch: BuildSessionBranchName(snapshot.SessionId),
                        ExpectedTargetCommit: snapshot.TargetCommit,
                        ApprovedDiffHash: approvalToken.Binding.DiffHash,
                        HasPassingValidationEvidence: validationReport.BuildSucceeded && validationReport.TestSucceeded,
                        HasReviewEvidence: !liveReviewStaleness.IsStale,
                        IsSessionAborted: false,
                        IsApprovalConfirmed: true),
                    cancellationToken)
                : new MergeResult(
                    TargetBranch: snapshot.TargetBranch,
                    PreviousTargetCommit: snapshot.TargetCommit,
                    NewTargetCommit: snapshot.TargetCommit,
                    SessionBranch: BuildSessionBranchName(snapshot.SessionId));

            var result = await _orchestrator.MergeAsync(
                snapshot.SessionId,
                new SessionMergeRequest(
                    TargetBranch: snapshot.TargetBranch,
                    TargetCommit: workspaceMerge.NewTargetCommit,
                    DiffHash: worktreeDiff.DiffHash,
                    Summary: $"Shell merge completed for session {snapshot.SessionId.Value}."),
                artifactStore,
                cancellationToken);

            _activeSession = await _orchestrator.GetSessionAsync(snapshot.SessionId, cancellationToken) ?? result.Session;
            _selectedWorkflowId = _activeSession.WorkflowId;

            await WriteDeterministicBlockAsync(
            [
                $"Merge completed: session={_activeSession.SessionId} state={_activeSession.State}",
                $"Command 'merge' completed for session {_activeSession.SessionId}: state={_activeSession.State}.",
                $"targetBranch: {_activeSession.TargetBranch}",
                $"targetCommit: {_activeSession.TargetCommit}",
                $"mergePreviousTargetCommit: {workspaceMerge.PreviousTargetCommit}",
                $"mergeNewTargetCommit: {workspaceMerge.NewTargetCommit}",
                $"diffHash: {worktreeDiff.DiffHash}",
                mergePreviewAvailable ? "mergeMode: repository" : $"mergeMode: synthetic (preview unavailable: {previewUnavailableReason})",
                $"Merge artifact: {result.MergeArtifact.RelativePath}"
            ]);
        }
        catch (Exception ex)
        {
            await WriteLineAsync($"Merge failed: {ex.Message}");
        }
    }

    private async Task EnforceLocalSafeApprovalPolicyAsync(
        SessionSnapshot snapshot,
        bool validationSucceeded,
        CancellationToken cancellationToken)
    {
        if (_localSafePolicy is null)
            return;

        var expectedWorktreePath = ResolveExpectedSessionWorktreePath(snapshot);
        var decision = await _localSafePolicy.EvaluateAsync(
            new LocalSafePolicyRequest(
                Command: "approve",
                WorkingDirectory: snapshot.WorktreePath,
                ValidationSucceeded: validationSucceeded,
                ApprovalGranted: false,
                RequireValidation: true,
                RequireApproval: false),
            new PolicyContext(
                snapshot.SessionId,
                snapshot.WorkflowId,
                expectedWorktreePath,
                "Wip.Runtime.Approve"),
            cancellationToken);

        if (!decision.IsAllowed)
            throw new InvalidOperationException(decision.Reason);
    }

    private async Task HandleArtifactsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");
        var artifactStore = new SessionArtifactStore(snapshot);
        var descriptors = await artifactStore.ListAsync(snapshot.SessionId, cancellationToken);

        if (descriptors.Count == 0)
        {
            await WriteLineAsync($"No persisted artifacts were found for session {snapshot.SessionId} at {snapshot.ArtifactDirectory}.");
            return;
        }

        var lines = new List<string> { $"Artifacts for session {snapshot.SessionId}:" };
        foreach (var descriptor in descriptors
                     .OrderBy(static item => item.ProducedAtUtc)
                     .ThenBy(static item => item.ArtifactId.Value, StringComparer.Ordinal)
                     .ThenBy(static item => item.Kind)
                     .ThenBy(static item => item.ProducerVersion, StringComparer.Ordinal)
                     .ThenBy(static item => item.ProducerType, StringComparer.Ordinal)
                     .ThenBy(static item => item.RelativePath, StringComparer.Ordinal))
        {
            lines.Add($"- id={descriptor.ArtifactId.Value} type={descriptor.Kind} version={descriptor.ProducerVersion} created={descriptor.ProducedAtUtc:O} producer={descriptor.ProducerType} path={descriptor.RelativePath}");
        }

        await WriteDeterministicBlockAsync(lines);
    }

    private async Task HandleStatusAsync(CancellationToken cancellationToken)
    {
        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");

        var lines = new List<string> { $"Session status: {snapshot.SessionId}" };
        lines.Add($"- state: {snapshot.State}");
        lines.Add($"- task: {snapshot.TaskDescription}");
        lines.Add($"- workflow: {snapshot.WorkflowId}");
        lines.Add($"- baseBranch: {(string.IsNullOrWhiteSpace(snapshot.BaseBranch) ? "(not set)" : snapshot.BaseBranch)}");
        lines.Add($"- baseCommit: {(string.IsNullOrWhiteSpace(snapshot.BaseCommit) ? "(not set)" : snapshot.BaseCommit)}");
        lines.Add($"- targetCommit: {(string.IsNullOrWhiteSpace(snapshot.TargetCommit) ? "(not set)" : snapshot.TargetCommit)}");
        lines.Add($"- validationStatus: {snapshot.ValidationStatus}");
        lines.Add($"- approvalStatus: {snapshot.ApprovalStatus}");
        lines.Add($"- updatedAt: {snapshot.UpdatedAtUtc:O}");

        var promptContextDiagnostics = await TryLoadPromptContextInvocationDiagnosticsAsync(snapshot, cancellationToken);
        if (promptContextDiagnostics is null)
        {
            lines.Add("- promptContextDiagnostics: none");
        }
        else
        {
            lines.Add($"- promptContextDiagnostics: contextHash={promptContextDiagnostics.ContextHash} provider={promptContextDiagnostics.ProviderId} model={promptContextDiagnostics.ModelId} correlation={promptContextDiagnostics.CorrelationId}");
            var contributingPluginIds = (promptContextDiagnostics.ContributingPluginIds ?? Array.Empty<string>())
                .Where(static pluginId => !string.IsNullOrWhiteSpace(pluginId))
                .Select(static pluginId => pluginId.Trim())
                .ToArray();
            var rejectedFragments = (promptContextDiagnostics.RejectedFragments ?? Array.Empty<PromptContextRejectedFragment>())
                .Where(static fragment => fragment is not null)
                .Select(static fragment => fragment!)
                .ToArray();

            lines.Add($"  contributingPlugins: {(contributingPluginIds.Length == 0 ? "(none)" : string.Join(", ", contributingPluginIds))}");

            if (rejectedFragments.Length == 0)
            {
                lines.Add("  rejectedFragments: none");
            }
            else
            {
                lines.Add("  rejectedFragments:");
                foreach (var rejectedFragment in rejectedFragments)
                {
                    lines.Add($"  - plugin={rejectedFragment.PluginId} reason={rejectedFragment.Reason}");
                }
            }
        }

        await WriteDeterministicBlockAsync(lines);
    }

    private async Task HandleArchiveAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length > 2)
        {
            await WriteLineAsync("Usage: archive [--mark-only|--cleanup]");
            return;
        }

        var policyMode = SessionArchivePolicyMode.MarkOnly;
        if (parts.Length == 2)
        {
            if (string.Equals(parts[1], "--cleanup", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[1], "cleanup", StringComparison.OrdinalIgnoreCase))
            {
                policyMode = SessionArchivePolicyMode.Cleanup;
            }
            else if (string.Equals(parts[1], "--mark-only", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[1], "mark-only", StringComparison.OrdinalIgnoreCase))
            {
                policyMode = SessionArchivePolicyMode.MarkOnly;
            }
            else
            {
                await WriteLineAsync("Usage: archive [--mark-only|--cleanup]");
                return;
            }
        }

        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");
        if (snapshot.State != SessionState.Merged)
        {
            await WriteLineAsync($"Archive requires session state {SessionState.Merged}. Current state is {snapshot.State}.");
            return;
        }

        var artifactStore = new SessionArtifactStore(snapshot);
        await _orchestrator.ArchiveAsync(
            snapshot.SessionId,
            new SessionArchiveRequest(
                Reason: "Archived from interactive shell.",
                PolicyMode: policyMode),
            artifactStore,
            cancellationToken);
        await _orchestrator.DetachSessionAsync(cancellationToken);
        _activeSession = null;

        if (parts.Length == 1)
        {
            await WriteLineAsync($"Session archived from shell: {snapshot.SessionId}.");
            return;
        }

        var modeLabel = policyMode == SessionArchivePolicyMode.Cleanup ? "cleanup" : "mark-only";
        await WriteLineAsync($"Session archived from shell: {snapshot.SessionId} mode={modeLabel}.");
    }

    private async Task HandleAbortAsync(CancellationToken cancellationToken)
    {
        var snapshot = await RefreshActiveSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Active session became unavailable.");
        if (snapshot.State == SessionState.Merged)
        {
            await WriteLineAsync("Merged sessions cannot be aborted. Use 'archive' to clear the active shell session.");
            return;
        }

        var artifactStore = new SessionArtifactStore(snapshot);
        var aborted = await _orchestrator.AbortAsync(
            snapshot.SessionId,
            new SessionAbortRequest("Aborted from interactive shell."),
            artifactStore,
            cancellationToken);
        await _orchestrator.DetachSessionAsync(cancellationToken);
        _activeSession = null;
        await WriteLineAsync($"Session aborted from shell: {aborted.Session.SessionId} state={aborted.Session.State}. Persisted session state remains on disk.");
    }

    private async Task<SessionSnapshot?> RefreshActiveSessionAsync(CancellationToken cancellationToken)
    {
        if (_activeSession is null)
            return null;

        var refreshed = await _orchestrator.GetSessionAsync(_activeSession.SessionId, cancellationToken);
        if (refreshed is not null)
            _activeSession = refreshed;

        return _activeSession;
    }

    private string ResolveRepositoryPath()
        => Path.GetFullPath(_activeSession?.RepositoryPath ?? _repositoryRoot);

    private RepositoryConfig GetEffectiveRepositoryConfig(string repositoryPath)
    {
        var normalizedRepositoryPath = Path.GetFullPath(repositoryPath);
        var defaultConfig = CreateDefaultRepositoryConfig(normalizedRepositoryPath);
        if (!File.Exists(defaultConfig.ConfigPath))
            return defaultConfig;

        try
        {
            var persisted = JsonSerializer.Deserialize<RepositoryConfigFile>(File.ReadAllText(defaultConfig.ConfigPath));
            if (persisted is null)
                return defaultConfig;

            return defaultConfig with
            {
                WorkspaceRoot = NormalizePathOrDefault(normalizedRepositoryPath, persisted.WorkspaceRoot, defaultConfig.WorkspaceRoot),
                WipRoot = NormalizePathOrDefault(normalizedRepositoryPath, persisted.WipRoot, defaultConfig.WipRoot),
                SessionsPath = NormalizePathOrDefault(normalizedRepositoryPath, persisted.SessionsPath, defaultConfig.SessionsPath),
                WorktreesPath = NormalizePathOrDefault(normalizedRepositoryPath, persisted.WorktreesPath, defaultConfig.WorktreesPath),
                PluginsPath = NormalizePathOrDefault(normalizedRepositoryPath, persisted.PluginsPath, defaultConfig.PluginsPath),
                DefaultWorkflowId = string.IsNullOrWhiteSpace(persisted.DefaultWorkflowId) ? defaultConfig.DefaultWorkflowId : persisted.DefaultWorkflowId.Trim(),
                ConfigSource = RepositoryConfigSource.Disk
            };
        }
        catch
        {
            return defaultConfig;
        }
    }

    private RepositoryConfig CreateDefaultRepositoryConfig(string repositoryPath)
    {
        var normalizedRepositoryPath = Path.GetFullPath(repositoryPath);
        var wipRoot = Path.Combine(normalizedRepositoryPath, ".wip");

        return new RepositoryConfig(
            RepositoryPath: normalizedRepositoryPath,
            WorkspaceRoot: _repositoryRoot,
            ConfigPath: Path.Combine(wipRoot, "config.json"),
            WipRoot: wipRoot,
            SessionsPath: Path.Combine(wipRoot, "sessions"),
            WorktreesPath: Path.Combine(wipRoot, "worktrees"),
            PluginsPath: Path.Combine(wipRoot, "plugins"),
            DefaultWorkflowId: "workflow.linear",
            ConfigSource: RepositoryConfigSource.Defaults);
    }

    private static RepositoryConfigFile ToRepositoryConfigFile(RepositoryConfig config)
        => new(
            RepositoryPath: config.RepositoryPath,
            WorkspaceRoot: config.WorkspaceRoot,
            WipRoot: config.WipRoot,
            SessionsPath: config.SessionsPath,
            WorktreesPath: config.WorktreesPath,
            PluginsPath: config.PluginsPath,
            DefaultWorkflowId: config.DefaultWorkflowId);

    private static string NormalizePathOrDefault(string repositoryPath, string? path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path))
            return fallback;

        var trimmed = path.Trim();
        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(repositoryPath, trimmed));
    }

    private static ArtifactContent BuildWorkflowExecutionArtifact(WorkflowExecutionResult result)
    {
        var payload = JsonSerializer.Serialize(new
        {
            WorkflowId = result.WorkflowId.Value,
            Stages = result.Stages.Select(static stage => new
            {
                Stage = stage.Descriptor.Stage.ToString(),
                StateAfterStage = stage.StateAfterStage.ToString(),
                stage.AppliedTransition,
                stage.MappedInputContractName
            }).ToArray()
        });

        return new ArtifactContent(
            artifactId: new ArtifactId($"workflow-execution-{Guid.NewGuid():N}"),
            kind: ArtifactKind.Json,
            fileName: "workflow-execution",
            content: payload,
            producerType: "Wip.Shell",
            producerVersion: "1.0.0",
            producedAtUtc: DateTimeOffset.UtcNow);
    }

    private static ArtifactContent BuildAgentPlanArtifact(AgentPlan plan)
    {
        var payload = JsonSerializer.Serialize(plan);

        return new ArtifactContent(
            artifactId: new ArtifactId($"agent-plan-result-{Guid.NewGuid():N}"),
            kind: ArtifactKind.Json,
            fileName: "agent-plan-result",
            content: payload,
            producerType: "Wip.Shell",
            producerVersion: "1.0.0",
            producedAtUtc: DateTimeOffset.UtcNow);
    }

    private static ArtifactContent BuildAgentRunResultArtifact(AgentRunResult result)
    {
        var payload = JsonSerializer.Serialize(result);

        return new ArtifactContent(
            artifactId: new ArtifactId($"agent-run-result-{Guid.NewGuid():N}"),
            kind: ArtifactKind.Json,
            fileName: "agent-run-result",
            content: payload,
            producerType: "Wip.Shell",
            producerVersion: "1.0.0",
            producedAtUtc: DateTimeOffset.UtcNow);
    }

    private static ArtifactContent BuildDiffSummaryArtifact(SessionDiffResult result)
    {
        var payload = JsonSerializer.Serialize(new DiffSummaryArtifactPayload(
            result.DiffHash,
            result.ChangedFiles,
            result.DiffArtifact.ArtifactId.Value,
            result.DiffArtifact.RelativePath,
            result.DiffArtifact.ProducedAtUtc));

        return new ArtifactContent(
            artifactId: new ArtifactId($"diff-summary-{Guid.NewGuid():N}"),
            kind: ArtifactKind.Json,
            fileName: "diff-summary",
            content: payload,
            producerType: "Wip.Shell",
            producerVersion: "1.0.0",
            producedAtUtc: result.DiffArtifact.ProducedAtUtc);
    }

    private static ArtifactContent BuildReviewSummaryArtifact(
        SessionReviewResult result,
        ArtifactDescriptor validationArtifact,
        ValidationArtifactPayload validationReport,
        WorktreeDiffSnapshot worktreeDiff)
    {
        var payload = JsonSerializer.Serialize(new ReviewSummaryArtifactPayload(
            CurrentDiffHash: worktreeDiff.DiffHash,
            IsStale: result.Review.Staleness.IsStale,
            StaleReason: result.Review.Staleness.Reason,
            ReviewArtifactId: result.Review.ReportArtifact.ArtifactId.Value,
            ReviewArtifactPath: result.Review.ReportArtifact.RelativePath,
            ValidationArtifactId: validationArtifact.ArtifactId.Value,
            ValidationArtifactPath: validationArtifact.RelativePath,
            ValidationDiffHash: validationReport.DiffHash,
            BuildSucceeded: validationReport.BuildSucceeded,
            TestSucceeded: validationReport.TestSucceeded,
            ChangedFiles: worktreeDiff.ChangedFiles,
            ProducedAtUtc: result.Review.ReportArtifact.ProducedAtUtc));

        return new ArtifactContent(
            artifactId: new ArtifactId($"review-summary-{Guid.NewGuid():N}"),
            kind: ArtifactKind.Json,
            fileName: "review-summary",
            content: payload,
            producerType: "Wip.Shell",
            producerVersion: "1.0.0",
            producedAtUtc: result.Review.ReportArtifact.ProducedAtUtc);
    }

    private static ArtifactContent BuildPromptContextInvocationDiagnosticsArtifact(PromptContextInvocationDiagnostics diagnostics)
    {
        var payload = JsonSerializer.Serialize(diagnostics);

        return new ArtifactContent(
            artifactId: new ArtifactId($"prompt-context-diagnostics-{Guid.NewGuid():N}"),
            kind: ArtifactKind.Json,
            fileName: "prompt-context-diagnostics",
            content: payload,
            producerType: "Wip.Shell",
            producerVersion: "1.0.0",
            producedAtUtc: diagnostics.ProducedAtUtc);
    }

    private static PromptContextInvocationDiagnostics BuildPromptContextInvocationDiagnostics(
        SessionSnapshot snapshot,
        string task,
        RuntimePromptContext promptContext,
        ProviderPlanDraft providerPlan)
    {
        var composedContextPayload = promptContext.RenderComposedProviderContextPayload(task);
        var contextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(composedContextPayload))).ToLowerInvariant();
        var contributingPluginIds = promptContext.PluginManifestDiagnostics
            .SelectMany(ParsePromptContextDiagnosticPluginIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var rejectedFragments = promptContext.PluginManifestDiagnostics
            .Select(ParseRejectedPromptFragment)
            .Where(static value => value is not null)
            .Select(static value => value!)
            .OrderBy(static value => value.PluginId, StringComparer.Ordinal)
            .ThenBy(static value => value.Reason, StringComparer.Ordinal)
            .ToArray();

        return new PromptContextInvocationDiagnostics(
            snapshot.SessionId.Value,
            task,
            contextHash,
            providerPlan.ProviderId,
            providerPlan.ModelId,
            providerPlan.CorrelationId,
            contributingPluginIds,
            rejectedFragments,
            promptContext.PluginManifestDiagnostics,
            DateTimeOffset.UtcNow);
    }

    private async Task<PromptContextInvocationDiagnostics?> TryLoadPromptContextInvocationDiagnosticsAsync(
        SessionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(snapshot.ArtifactDirectory))
            return null;

        foreach (var artifactPath in Directory
                     .EnumerateFiles(snapshot.ArtifactDirectory, "prompt-context-diagnostics-*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(static path => File.GetLastWriteTimeUtc(path))
                     .ThenBy(static path => path, StringComparer.Ordinal))
        {
            try
            {
                var payload = await File.ReadAllTextAsync(artifactPath, cancellationToken);
                var diagnostics = JsonSerializer.Deserialize<PromptContextInvocationDiagnostics>(payload, PersistedSessionJsonOptions);

                if (diagnostics is null)
                    continue;

                if (string.IsNullOrWhiteSpace(diagnostics.ContextHash)
                    || string.IsNullOrWhiteSpace(diagnostics.ProviderId)
                    || string.IsNullOrWhiteSpace(diagnostics.ModelId)
                    || string.IsNullOrWhiteSpace(diagnostics.CorrelationId))
                {
                    continue;
                }

                return diagnostics with
                {
                    ContributingPluginIds = diagnostics.ContributingPluginIds ?? Array.Empty<string>(),
                    RejectedFragments = diagnostics.RejectedFragments ?? Array.Empty<PromptContextRejectedFragment>(),
                    PluginManifestDiagnostics = diagnostics.PluginManifestDiagnostics ?? Array.Empty<string>()
                };
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ParsePromptContextDiagnosticPluginIds(string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic) || !diagnostic.Contains("[status=accepted]", StringComparison.Ordinal))
            return Array.Empty<string>();

        var pluginId = ParsePromptContextDiagnosticField(diagnostic, "[plugin:", "]");
        return pluginId is null ? Array.Empty<string>() : [pluginId];
    }

    private static PromptContextRejectedFragment? ParseRejectedPromptFragment(string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic) || !diagnostic.Contains("[status=rejected]", StringComparison.Ordinal))
            return null;

        var pluginId = ParsePromptContextDiagnosticField(diagnostic, "[plugin:", "]");
        var reason = ParsePromptContextDiagnosticField(diagnostic, "[reason=", "]");

        if (pluginId is null || reason is null)
            return null;

        return new PromptContextRejectedFragment(pluginId, reason);
    }

    private static string? ParsePromptContextDiagnosticField(string diagnostic, string prefix, string suffix)
    {
        var startIndex = diagnostic.IndexOf(prefix, StringComparison.Ordinal);
        if (startIndex < 0)
            return null;

        startIndex += prefix.Length;
        var endIndex = diagnostic.IndexOf(suffix, startIndex, StringComparison.Ordinal);
        if (endIndex < 0 || endIndex <= startIndex)
            return null;

        return diagnostic[startIndex..endIndex];
    }

    private async Task<WorktreeDiffSnapshot> BuildWorktreeDiffSnapshotAsync(
        SessionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            var diff = await _workspaceProvider.GetDiffAsync(
                new DiffHashRequest(
                    RepositoryPath: snapshot.RepositoryPath,
                    TargetBranch: snapshot.TargetBranch,
                    SessionBranch: BuildSessionBranchName(snapshot.SessionId)),
                cancellationToken);

            var patch = string.IsNullOrWhiteSpace(diff.Patch)
                ? "(no worktree diff)"
                : diff.Patch;

            return new WorktreeDiffSnapshot(diff.DiffHash, diff.ChangedFiles.ToArray(), patch);
        }
        catch
        {
            return BuildSyntheticWorktreeDiffSnapshot(snapshot);
        }
    }

    private static WorktreeDiffSnapshot BuildSyntheticWorktreeDiffSnapshot(SessionSnapshot snapshot)
    {
        var changedFiles = Directory.Exists(snapshot.WorktreePath)
            ? Directory
                .EnumerateFiles(snapshot.WorktreePath, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(snapshot.WorktreePath, path))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray()
            : [];

        var builder = new StringBuilder();
        builder.AppendLine($"# Shell workspace diff for session {snapshot.SessionId.Value}");
        builder.AppendLine($"# Repository: {snapshot.RepositoryPath}");
        builder.AppendLine($"# Worktree: {snapshot.WorktreePath}");

        foreach (var changedFile in changedFiles)
        {
            var fullPath = Path.Combine(snapshot.WorktreePath, changedFile);
            builder.AppendLine($"diff --git a/{changedFile} b/{changedFile}");
            builder.AppendLine($"--- a/{changedFile}");
            builder.AppendLine($"+++ b/{changedFile}");
            foreach (var line in File.ReadAllLines(fullPath))
                builder.AppendLine($"+{line}");
        }

        if (changedFiles.Length == 0)
            builder.AppendLine("(no worktree files)");

        var patch = builder.ToString();
        var diffHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(patch))).ToLowerInvariant();
        return new WorktreeDiffSnapshot(diffHash, changedFiles, patch);
    }

    private static string BuildSessionBranchName(SessionId sessionId)
        => $"wip/session/{sessionId.Value}";

    private static ArtifactDescriptor? GetLatestArtifact(IReadOnlyList<ArtifactDescriptor> descriptors, string artifactIdPrefix)
        => descriptors
            .Where(descriptor => descriptor.ArtifactId.Value.StartsWith(artifactIdPrefix, StringComparison.Ordinal))
            .OrderByDescending(static descriptor => descriptor.ProducedAtUtc)
            .ThenByDescending(static descriptor => descriptor.RelativePath, StringComparer.Ordinal)
            .FirstOrDefault();

    private static async Task<TArtifact?> ReadJsonArtifactAsync<TArtifact>(
        SessionSnapshot snapshot,
        ArtifactDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(snapshot.RepositoryPath, descriptor.RelativePath);
        if (!File.Exists(fullPath))
            return default;

        await using var stream = File.OpenRead(fullPath);
        return await JsonSerializer.DeserializeAsync<TArtifact>(stream, cancellationToken: cancellationToken);
    }

    private static async Task<ValidationArtifactPayload?> ReadValidationArtifactPayloadAsync(
        SessionSnapshot snapshot,
        ArtifactDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(snapshot.RepositoryPath, descriptor.RelativePath);
        if (!File.Exists(fullPath))
            return null;

        await using var stream = File.OpenRead(fullPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("BuildSucceeded", out var buildSucceeded)
            && root.TryGetProperty("TestSucceeded", out var testSucceeded))
        {
            return new ValidationArtifactPayload(
                BuildSucceeded: buildSucceeded.GetBoolean(),
                TestSucceeded: testSucceeded.GetBoolean(),
                DiffHash: root.TryGetProperty("DiffHash", out var legacyDiffHash) ? legacyDiffHash.GetString() : null,
                Summary: root.TryGetProperty("Summary", out var summary) ? summary.GetString() ?? string.Empty : string.Empty,
                ProducedAtUtc: root.TryGetProperty("ProducedAtUtc", out var producedAtUtc)
                    ? producedAtUtc.GetDateTimeOffset()
                    : DateTimeOffset.MinValue);
        }

        if (root.TryGetProperty("Build", out var build)
            && root.TryGetProperty("Test", out var test))
        {
            var buildExitCode = build.GetProperty("ExitCode").GetInt32();
            var testExitCode = test.GetProperty("ExitCode").GetInt32();
            var buildTimedOut = build.GetProperty("TimedOut").GetBoolean();
            var testTimedOut = test.GetProperty("TimedOut").GetBoolean();

            return new ValidationArtifactPayload(
                BuildSucceeded: buildExitCode == 0 && !buildTimedOut,
                TestSucceeded: testExitCode == 0 && !testTimedOut,
                DiffHash: root.TryGetProperty("DiffHash", out var diffHash) ? diffHash.GetString() : null,
                Summary: "Validation report produced by Wip.Validation.DotNet.",
                ProducedAtUtc: root.GetProperty("ProducedAtUtc").GetDateTimeOffset(),
                BuildCommand: build.GetProperty("Command").GetString(),
                BuildExitCode: buildExitCode,
                BuildTimedOut: buildTimedOut,
                TestCommand: test.GetProperty("Command").GetString(),
                TestExitCode: testExitCode,
                TestTimedOut: testTimedOut);
        }

        return null;
    }

    private static string GetSessionDirectory(SessionSnapshot snapshot)
        => Path.Combine(snapshot.RepositoryPath, ".wip", "sessions", snapshot.SessionId.Value);

    private static PersistedSessionSnapshot? TryReadPersistedSession(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<PersistedSessionSnapshot>(File.ReadAllText(path), PersistedSessionJsonOptions);
        }
        catch
        {
            return null;
        }
    }

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

        if (_builder is not null)
        {
            if (_builder.WorkflowRegistrations.Count == 0)
            {
                lines.Add("No workflows are currently registered.");
            }
            else
            {
                lines.Add("Registered workflows:");
                foreach (var workflow in _builder.WorkflowRegistrations
                             .OrderBy(static item => item.WorkflowId.Value, StringComparer.Ordinal)
                             .ThenBy(static item => item.Descriptor.DisplayName, StringComparer.Ordinal)
                             .ThenBy(static item => item.RequestType.FullName ?? item.RequestType.Name, StringComparer.Ordinal)
                             .ThenBy(static item => item.ResultType.FullName ?? item.ResultType.Name, StringComparer.Ordinal))
                {
                    lines.Add($"- {workflow.WorkflowId.Value} [{workflow.Descriptor.DisplayName}] request={workflow.RequestType.FullName ?? workflow.RequestType.Name} result={workflow.ResultType.FullName ?? workflow.ResultType.Name}");
                }
            }

            await WriteDeterministicBlockAsync(lines);
            return;
        }

        if (_diagnosticsBridge is null)
        {
            lines.Add("Workflow diagnostics are unavailable in this shell instance.");
            await WriteDeterministicBlockAsync(lines);
            return;
        }

        var workflows = _diagnosticsBridge.GetRunManifest().Workflows;
        if (workflows.Count == 0)
        {
            lines.Add("No workflows are currently registered.");
            await WriteDeterministicBlockAsync(lines);
            return;
        }

        lines.Add("Registered workflows:");
        foreach (var workflow in workflows
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

    private static string[] Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in commandLine)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(character))
            {
                if (current.Length == 0)
                    continue;

                tokens.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens.ToArray();
    }

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

    private sealed record PersistedSessionSnapshot(
        string SessionId,
        string TaskDescription,
        string WorkflowId,
        SessionState State,
        string RepositoryPath,
        string WorktreePath,
        DateTimeOffset UpdatedAtUtc);

    private sealed class SessionArtifactStore : IArtifactStore
    {
        private static readonly JsonSerializerOptions ArtifactMetadataJsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly SessionSnapshot _snapshot;

        public SessionArtifactStore(SessionSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public async ValueTask<ArtifactDescriptor> SaveAsync(
            SessionId sessionId,
            ArtifactContent artifact,
            CancellationToken cancellationToken)
        {
            if (sessionId != _snapshot.SessionId)
            {
                throw new InvalidOperationException(
                    $"Artifact store for session '{_snapshot.SessionId}' cannot persist artifacts for '{sessionId}'.");
            }

            Directory.CreateDirectory(_snapshot.ArtifactDirectory);

            var extension = artifact.Kind switch
            {
                ArtifactKind.Json => ".json",
                ArtifactKind.Markdown => ".md",
                ArtifactKind.Patch => ".patch",
                _ => ".artifact"
            };

            var fileName = $"{artifact.FileName}-{artifact.ArtifactId.Value}{extension}";
            var fullPath = Path.Combine(_snapshot.ArtifactDirectory, fileName);
            await File.WriteAllTextAsync(fullPath, artifact.Content, cancellationToken);

            var descriptor = new ArtifactDescriptor(
                artifact.ArtifactId,
                sessionId,
                artifact.Kind,
                Path.GetRelativePath(_snapshot.RepositoryPath, fullPath),
                artifact.ProducerType,
                artifact.ProducerVersion,
                artifact.ProducedAtUtc);

            var metadataPath = BuildMetadataPath(fullPath);
            var metadata = new PersistedArtifactMetadata(
                descriptor.ArtifactId.Value,
                descriptor.SessionId.Value,
                descriptor.Kind,
                descriptor.RelativePath,
                descriptor.ProducerType,
                descriptor.ProducerVersion,
                descriptor.ProducedAtUtc);
            await File.WriteAllTextAsync(
                metadataPath,
                JsonSerializer.Serialize(metadata, ArtifactMetadataJsonOptions),
                cancellationToken);

            return descriptor;
        }

        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            if (sessionId != _snapshot.SessionId || !Directory.Exists(_snapshot.ArtifactDirectory))
                return ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>([]);

            var descriptors = Directory
                .EnumerateFiles(_snapshot.ArtifactDirectory, "*", SearchOption.AllDirectories)
                .Where(static path => !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .Select(path => TryReadDescriptorFromMetadata(path, sessionId, _snapshot.RepositoryPath) ?? new ArtifactDescriptor(
                    new ArtifactId(Path.GetFileNameWithoutExtension(path)),
                    sessionId,
                    ResolveArtifactKind(path),
                    Path.GetRelativePath(_snapshot.RepositoryPath, path),
                    "Wip.Shell",
                    "1.0.0",
                    File.GetLastWriteTimeUtc(path)))
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>(descriptors);
        }

        private static string BuildMetadataPath(string artifactPath)
            => $"{artifactPath}.metadata.json";

        private static ArtifactDescriptor? TryReadDescriptorFromMetadata(string artifactPath, SessionId sessionId, string repositoryPath)
        {
            var metadataPath = BuildMetadataPath(artifactPath);
            if (!File.Exists(metadataPath))
                return null;

            try
            {
                var metadata = JsonSerializer.Deserialize<PersistedArtifactMetadata>(File.ReadAllText(metadataPath), ArtifactMetadataJsonOptions);
                if (metadata is null || !string.Equals(metadata.SessionId, sessionId.Value, StringComparison.Ordinal))
                    return null;

                return new ArtifactDescriptor(
                    new ArtifactId(metadata.ArtifactId),
                    sessionId,
                    metadata.Kind,
                    string.IsNullOrWhiteSpace(metadata.RelativePath)
                        ? Path.GetRelativePath(repositoryPath, artifactPath)
                        : metadata.RelativePath,
                    metadata.ProducerType,
                    metadata.ProducerVersion,
                    metadata.ProducedAtUtc);
            }
            catch
            {
                return null;
            }
        }

        private static ArtifactKind ResolveArtifactKind(string path)
            => Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".json" => ArtifactKind.Json,
                ".md" => ArtifactKind.Markdown,
                ".patch" => ArtifactKind.Patch,
                _ => ArtifactKind.Json
            };
    }

    private sealed record WorktreeDiffSnapshot(
        string DiffHash,
        IReadOnlyList<string> ChangedFiles,
        string Patch);

    private sealed record ParsedValidationCommand(
        string OriginalCommand,
        string Executable,
        IReadOnlyList<string> Arguments,
        ValidationCommandKind Kind);

    private enum ValidationCommandKind
    {
        Restore,
        Build,
        Test,
    }

    private sealed record DiffSummaryArtifactPayload(
        string DiffHash,
        IReadOnlyList<string> ChangedFiles,
        string DiffArtifactId,
        string DiffArtifactPath,
        DateTimeOffset ProducedAtUtc);

    private sealed record ValidationArtifactPayload(
        bool BuildSucceeded,
        bool TestSucceeded,
        string? DiffHash,
        string Summary,
        DateTimeOffset ProducedAtUtc,
        string? BuildCommand = null,
        int? BuildExitCode = null,
        bool? BuildTimedOut = null,
        string? TestCommand = null,
        int? TestExitCode = null,
        bool? TestTimedOut = null);

    private sealed record ReviewSummaryArtifactPayload(
        string CurrentDiffHash,
        bool IsStale,
        string? StaleReason,
        string ReviewArtifactId,
        string ReviewArtifactPath,
        string ValidationArtifactId,
        string ValidationArtifactPath,
        string? ValidationDiffHash,
        bool BuildSucceeded,
        bool TestSucceeded,
        IReadOnlyList<string> ChangedFiles,
        DateTimeOffset ProducedAtUtc);

    private sealed record PersistedArtifactMetadata(
        string ArtifactId,
        string SessionId,
        ArtifactKind Kind,
        string RelativePath,
        string ProducerType,
        string ProducerVersion,
        DateTimeOffset ProducedAtUtc);

    private sealed record RepositoryConfig(
        string RepositoryPath,
        string WorkspaceRoot,
        string ConfigPath,
        string WipRoot,
        string SessionsPath,
        string WorktreesPath,
        string PluginsPath,
        string DefaultWorkflowId,
        RepositoryConfigSource ConfigSource);

    private sealed record RepositoryConfigFile(
        string? RepositoryPath,
        string? WorkspaceRoot,
        string? WipRoot,
        string? SessionsPath,
        string? WorktreesPath,
        string? PluginsPath,
        string? DefaultWorkflowId);

    private enum RepositoryConfigSource
    {
        Defaults,
        Disk,
    }
}
