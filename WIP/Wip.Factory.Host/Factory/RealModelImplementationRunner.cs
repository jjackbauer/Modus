using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Factory.Adaptation;
using Wip.Factory.Agents.Core.Tools;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.Contracts.Telemetry;
using Wip.Factory.Telemetry;
using Wip.Factory.Wip.Fanout;
using Wip.Factory.Wip.Isolation;
using Wip.Factory.Wip.Workspaces;
using Wip.Runtime;
using Wip.Runtime.Runtime;
using Wip.Workspaces.Git;

namespace Wip.Factory.Host.Factory;

/// <summary>
/// Drives one implementation attempt with the real model and the six workspace tools, then verifies what it
/// produced. The verification is deliberately not delegated: a model claiming success is not a diff, and a
/// diff is not a successful build.
/// </summary>
public sealed class RealModelImplementationRunner : IImplementationStageRunner
{
    public static readonly CapabilityId ListFilesId = new("factory.tool.list-files");
    public static readonly CapabilityId ReadFileId = new("factory.tool.read-file");
    public static readonly CapabilityId SearchTextId = new("factory.tool.search-text");
    public static readonly CapabilityId ApplyPatchId = new("factory.tool.apply-patch");
    public static readonly CapabilityId WriteFileId = new("factory.tool.write-file");
    public static readonly CapabilityId RunCommandId = new("factory.tool.run-command");

    private static readonly CapabilityVersion Version = new("1.0.0");
    private static readonly CapabilityPermission WorkspaceWrite = new("workspace.write");

    /// <summary>The closed set of tools the implementation profile may invoke. The security check verifies it.</summary>
    public static IReadOnlyList<CapabilityId> PinnedToolIds { get; } =
        [ListFilesId, ReadFileId, SearchTextId, ApplyPatchId, WriteFileId, RunCommandId];

    /// <summary>Baseline ignore list so generated diffs stay source-only (bin/obj were being committed).</summary>
    public static readonly string DefaultGitIgnore = "bin/" + Environment.NewLine + "obj/" + Environment.NewLine;

    private readonly FactoryControlRoot _root;
    private readonly Uri _baseUrl;
    private readonly string _apiKey;
    private readonly LocalAgentSessionTraceStore _traces;

    public RealModelImplementationRunner(FactoryControlRoot root, Uri baseUrl, string apiKey)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        _apiKey = apiKey ?? throw new ArgumentException("An API key is required.", nameof(apiKey));
        _traces = new LocalAgentSessionTraceStore(Path.Combine(root.Root, "telemetry"));
    }

    public async ValueTask<ImplementationOutcome> RunAsync(
        ImplementationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionId = SessionFor(request.WorkItemId);
        var journal = new ImplementationAttemptJournal(
            ImplementationAttemptJournal.PathFor(_root.Root, request.WorkItemId));
        var startedAt = DateTimeOffset.UtcNow;

        ImplementationOutcome outcome;
        try
        {
            outcome = await RunCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TryRecordAsync(
                journal,
                request,
                sessionId,
                ImplementationAttemptRecord.CancelledOutcome,
                "implementation-cancelled",
                "the implementation attempt was cancelled",
                startedAt,
                cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            var category = "implementation-exception:" + exception.GetType().Name;
            outcome = ImplementationOutcome.Failed(category, exception.Message);
            await TryRecordAsync(
                journal,
                request,
                sessionId,
                ImplementationAttemptRecord.FailedOutcome,
                category,
                exception.Message,
                startedAt,
                cancellationToken).ConfigureAwait(false);
            return outcome;
        }

        await TryRecordAsync(
            journal,
            request,
            sessionId,
            outcome.Succeeded ? ImplementationAttemptRecord.SucceededOutcome : ImplementationAttemptRecord.FailedOutcome,
            outcome.FailureCategory,
            outcome.Detail,
            startedAt,
            cancellationToken,
            outcome).ConfigureAwait(false);

        return outcome;
    }

    private async ValueTask TryRecordAsync(
        ImplementationAttemptJournal journal,
        ImplementationRequest request,
        SessionId sessionId,
        string outcome,
        string? failureCategory,
        string? detail,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken,
        ImplementationOutcome? result = null)
    {
        WorkspaceSessionState? state = null;
        try
        {
            state = await new WipWorkspaceProviderGit()
                .ReadProvisioningStateAsync(request.RepositoryPath, sessionId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The attempt record is still useful without the worktree metadata; never replace the primary outcome.
        }

        try
        {
            await journal.AppendAsync(
                new ImplementationAttemptRecord(
                    AttemptId: Guid.NewGuid().ToString("N"),
                    WorkItemId: request.WorkItemId,
                    AttemptNumber: 0,
                    SessionId: sessionId.Value,
                    Outcome: outcome,
                    FailureCategory: failureCategory,
                    FailureRole: null,
                    Detail: detail,
                    RepositoryPath: request.RepositoryPath,
                    WorktreePath: result?.WorktreePath ?? state?.WorktreePath,
                    TargetCommit: state?.OriginalTargetCommit,
                    BaseSha: result?.BaseSha,
                    HeadSha: result?.HeadSha,
                    PreviousAttemptId: null,
                    StartedAtUtc: startedAt,
                    FinishedAtUtc: DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Durable attempt history must not turn a completed implementation attempt into an unhandled failure.
        }
    }

    private static SessionId SessionFor(string workItemId)
        => new("session-impl-" + workItemId.ToLowerInvariant());

    private async ValueTask<ImplementationOutcome> RunCoreAsync(
        ImplementationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // An empty repository is a legitimate starting point; give it a baseline commit so a
        // worktree can be created, without touching any operator content.
        await EnsureBaselineCommitAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);

        // D-06: session worktrees live under the control root, never inside the operator repository.
        var workspaceProvider = new WipWorkspaceProviderGit();
        var provisioner = new FactoryWorktreeProvisioner(workspaceProvider, worktreeRoot: Path.Combine(_root.Root, "worktrees"));
        var sessionId = SessionFor(request.WorkItemId);
        FactoryWorkspaceLease? lease;
        string? reason;
        try
        {
            (lease, reason) = await provisioner
                .ProvisionAsync(request.RepositoryPath, sessionId, "main", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WorkspaceProvisioningException exception)
        {
            return ImplementationOutcome.Failed("worktree-failed:" + exception.Category, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ImplementationOutcome.Failed("worktree-failed:" + exception.Message);
        }

        if (lease is null)
        {
            return ImplementationOutcome.Failed("worktree-failed:" + reason);
        }

        var worktree = lease.WorktreePath;
        var baseSha = (await GitAsync(worktree, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

        var services = new ServiceCollection();
        services.AddDeepSeekChatModel(new DeepSeekModelProviderOptions(
            _baseUrl, _apiKey, TimeSpan.FromMinutes(10)));

        var builder = new WipBuilder(services);
        builder.AddTool<ListFilesTool, ListFilesRequest, ListFilesResult>(ListFilesId, "List files in the worktree", Version);
        builder.AddTool<ReadFileTool, ReadFileRequest, ReadFileResult>(ReadFileId, "Read a worktree file", Version);
        builder.AddTool<SearchTextTool, SearchTextRequest, SearchTextResult>(SearchTextId, "Search worktree text", Version);
        builder.AddTool<ApplyPatchTool, PatchHunkRequest, PatchHunkResult>(ApplyPatchId, "Apply a unified diff atomically", Version, requiredPermissions: [WorkspaceWrite]);
        builder.AddTool<WriteWorkspaceFileTool, WriteFileRequest, WriteFileResult>(WriteFileId, "Write a worktree file", Version, requiredPermissions: [WorkspaceWrite]);
        builder.AddTool<RunCommandTool, RunCommandRequest, RunCommandResult>(RunCommandId, "Run a bounded dotnet subcommand", Version);

        await using var provider = services.BuildServiceProvider();

        var catalog = new CapabilityCatalog(
        [
            new CapabilityCatalogEntry(ListFilesId, Version),
            new CapabilityCatalogEntry(ReadFileId, Version),
            new CapabilityCatalogEntry(SearchTextId, Version),
            new CapabilityCatalogEntry(ApplyPatchId, Version),
            new CapabilityCatalogEntry(WriteFileId, Version),
            new CapabilityCatalogEntry(RunCommandId, Version)
        ]);

        var registry = new LocalAgentProfileRegistry(
            Path.Combine(_root.Root, "registry"), catalog, FactoryRolePack.Definitions);

        var baseline = FactoryRolePack.Profiles[2]; // implementations analyst
        var profile = baseline with
        {
            Tools =
            [
                new AgentProfileCapabilityRef(ListFilesId, Version),
                new AgentProfileCapabilityRef(ReadFileId, Version),
                new AgentProfileCapabilityRef(SearchTextId, Version),
                new AgentProfileCapabilityRef(ApplyPatchId, Version),
                new AgentProfileCapabilityRef(WriteFileId, Version),
                new AgentProfileCapabilityRef(RunCommandId, Version)
            ],
            ExecutionPolicy = baseline.ExecutionPolicy with { MaxSteps = 40, TimeoutSeconds = 900 }
        };

        var published = await registry.PublishAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!published.Published)
        {
            return ImplementationOutcome.Failed("profile-publish-failed:" + published.Reason);
        }

        var sessions = new SessionStore();
        await sessions.SaveAsync(
            new SessionSnapshot(sessionId, new WorkflowId("workflow.linear"), SessionState.Editing,
                RepositoryPath: request.RepositoryPath, WorktreePath: worktree, UpdatedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        var orchestrator = new WipRuntimeOrchestrator(sessions, new NoOpPublisher());
        var dispatcher = new FactoryAgentDispatcher(
            registry, FactoryRolePack.Definitions, catalog, new AgentProfilePermissionResolver(),
            new WipRuntimeAgentExecutorPort(orchestrator),
            provider.GetRequiredService<IServiceScopeFactory>(), builder);

        var dispatch = await dispatcher.ExecuteAsync(
            new FactoryWorkDispatch(
                sessionId,
                new WorkItemId(request.WorkItemId),
                new StageName("implementation"),
                profile.DefinitionId,
                new AgentProfileVersion(1),
                BuildPrompt(request),
                Tools: ToolDefinitions()),
            cancellationToken).ConfigureAwait(false);

        if (!dispatch.Completed)
        {
            await TryRecordTraceAsync(request, sessionId, dispatch, cancellationToken).ConfigureAwait(false);
            return ImplementationOutcome.Failed("implementation-agent-failed:" + dispatch.FailureCategory);
        }

        await TryRecordTraceAsync(request, sessionId, dispatch, cancellationToken).ConfigureAwait(false);

        // Attribution: the worktree, not the model's prose, decides whether anything changed.
        var status = await GitAsync(worktree, cancellationToken, "status", "--porcelain").ConfigureAwait(false);
        var changed = status
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Count(static line => line.Length > 0);

        if (changed == 0)
        {
            return ImplementationOutcome.Failed(ImplementationOutcome.NoAttributableChange);
        }

        await GitAsync(worktree, cancellationToken, "add", "-A").ConfigureAwait(false);
        await GitAsync(worktree, cancellationToken,
            "-c", "user.email=factory@example.test", "-c", "user.name=Factory",
            "commit", "-m", "feat: implement " + request.WorkItemId).ConfigureAwait(false);

        // Record the new head while the provisioning lease is available. Without this, a later rebase of the session
        // branch would be indistinguishable from legitimate descendant commits.
        await workspaceProvider
            .RecordSessionHeadAsync(request.RepositoryPath, sessionId, cancellationToken)
            .ConfigureAwait(false);

        var headSha = (await GitAsync(worktree, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var diff = await GitAsync(worktree, cancellationToken, "diff", baseSha + ".." + headSha).ConfigureAwait(false);
        var diffDigest = FactoryStageDispatchHandler.Digest(diff);

        // The factory builds it; a green agent turn is not a green build.
        var project = Directory.EnumerateFiles(worktree, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        if (project is null)
        {
            return ImplementationOutcome.Failed(ImplementationOutcome.BuildFailed, "no-project-file");
        }

        var build = await new BoundedProcessRunner().RunAsync(
            "dotnet", ["build", project, "-v", "q", "--nologo"],
            worktree, new ProcessBounds(TimeSpan.FromMinutes(10), 2L * 1024 * 1024 * 1024),
            cancellationToken).ConfigureAwait(false);

        if (!build.Completed)
        {
            var detail = (build.StandardOutput + build.StandardError);
            return ImplementationOutcome.Failed(
                ImplementationOutcome.BuildFailed,
                detail.Length > 4000 ? detail[^4000..] : detail);
        }

        return ImplementationOutcome.Ok(baseSha, headSha, changed, diffDigest, worktree);
    }

    /// <summary>
    /// Persists the sanitized evidence of one agent session. Telemetry is secondary: a trace write failure must not
    /// turn a completed implementation into an error, but it is also never replaced by invented activity.
    /// </summary>
    private async ValueTask TryRecordTraceAsync(
        ImplementationRequest request,
        SessionId sessionId,
        FactoryAgentResult dispatch,
        CancellationToken cancellationToken)
    {
        try
        {
            var redactor = new global::Wip.Factory.Wip.Isolation.SecretRedactor();
            var toolNames = (dispatch.ToolInvocations ?? [])
                .Select(static invocation => invocation.ToolName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            var invocations = (dispatch.ToolInvocations ?? [])
                .Select(invocation =>
                {
                    var arguments = invocation.Arguments ?? string.Empty;
                    var result = invocation.Result ?? string.Empty;
                    return new AgentToolInvocation(
                        invocation.ToolName,
                        FactoryStageDispatchHandler.Digest(arguments),
                        Bound(redactor.Redact(arguments, out _), 600),
                        invocation.IsError,
                        Bound(redactor.Redact(result, out _), 600));
                })
                .ToArray();

            var summary = $"{dispatch.ToolInvocations?.Count ?? 0} tool invocation(s), "
                          + (dispatch.InputTokens + dispatch.OutputTokens) + " token(s), result="
                          + (dispatch.Completed ? "completed" : dispatch.FailureCategory ?? "failed");
            var trace = new AgentSessionTrace(
                sessionId,
                new WorkItemId(request.WorkItemId),
                new AgentDefinitionId("factory.implementations-analyst"),
                dispatch.AgentProfileVersion,
                new ModelId(string.IsNullOrWhiteSpace(dispatch.ModelId) ? "unknown" : dispatch.ModelId),
                dispatch.InputTokens,
                dispatch.OutputTokens,
                dispatch.Completed ? "completed" : "failed",
                dispatch.FailureCategory,
                toolNames,
                Bound(redactor.Redact(summary, out _), 2000),
                DateTimeOffset.UtcNow,
                invocations);

            await _traces.AppendAsync(trace, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Evidence is best-effort at this seam; the attempt journal remains the durable stage record.
        }
    }

    private static string Bound(string value, int max)
        => value.Length <= max ? value : value[..max] + "â€¦";

    private static string BuildPrompt(ImplementationRequest request)    {
        var expectations = string.Join('\n', request.Expectations.Select(static (e, i) => "- AC-" + (i + 1) + ": " + e));

        var prompt = "Implement this requirement in the current repository, which is already checked out in your worktree.\n\n"
            + "REQUIREMENT\n" + request.RequirementText + "\n\n"
            + "ACCEPTANCE EXPECTATIONS\n" + expectations + "\n\n"
            + "RULES\n"
            + "- Use the tools. Write real files; do not describe them.\n"
            + "- The repository may be empty. Create the project (.csproj) and source as needed.\n"
            + "- run_command accepts only: new, restore, build, test, run, publish. Use it to check your work.\n"
            + "- Before you finish, dotnet build must succeed on the project you created.\n"
            + "- Paths are relative to the repository root.\n"
            + "- Reply DONE when the build is green.";

        if (!string.IsNullOrWhiteSpace(request.ReworkContext))
        {
            prompt += "\n\nYOUR PREVIOUS ATTEMPT WAS REJECTED\n" + request.ReworkContext
                + "\n\nA working tree already contains your earlier implementation and it FAILED those scenarios."
                + "\nDo not start over. First inspect what is there: list_files, then read_file or search_text"
                + "\non the endpoint named in the failure, and fix the defect in place. The acceptance contract is not"
                + " negotiable.";
        }

        return prompt;
    }
    private static ToolDefinition[] ToolDefinitions()
    {
        static ToolDefinition Make(string name, string description, string schema)
            => new(name, description, JsonDocument.Parse(schema).RootElement);

        return
        [
            Make(ListFilesId.Value, "List files under a worktree-relative directory.",
                """{"type":"object","properties":{"path":{"type":"string"},"maxEntries":{"type":"integer"}}}"""),
            Make(ReadFileId.Value, "Read a worktree file.",
                """{"type":"object","properties":{"path":{"type":"string"},"maxCharacters":{"type":"integer"}},"required":["path"]}"""),
            Make(SearchTextId.Value, "Search text across worktree files.",
                """{"type":"object","properties":{"pattern":{"type":"string"},"glob":{"type":"string"},"maxMatches":{"type":"integer"},"regex":{"type":"boolean"}},"required":["pattern"]}"""),
            Make(ApplyPatchId.Value, "Apply a unified diff to an existing file atomically.",
                """{"type":"object","properties":{"path":{"type":"string"},"unifiedDiff":{"type":"string"}},"required":["path","unifiedDiff"]}"""),
            Make(WriteFileId.Value, "Write a new file or replace a file's contents.",
                """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}"""),
            Make(RunCommandId.Value, "Run a bounded dotnet subcommand (new, restore, build, test, run, publish).",
                """{"type":"object","properties":{"subcommand":{"type":"string"},"arguments":{"type":"array","items":{"type":"string"}}},"required":["subcommand","arguments"]}""")
        ];
    }

    private static async Task EnsureBaselineCommitAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var head = await TryGitAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "HEAD").ConfigureAwait(false);
        if (head is not null)
        {
            return;
        }

        // Without this, build output lands in the diff and the diff digest covers binaries.
        var ignore = Path.Combine(repositoryPath, ".gitignore");
        if (!File.Exists(ignore))
        {
            await File.WriteAllTextAsync(ignore, DefaultGitIgnore, cancellationToken).ConfigureAwait(false);
            await GitAsync(repositoryPath, cancellationToken, "add", ".gitignore").ConfigureAwait(false);
        }

        await GitAsync(repositoryPath, cancellationToken,
            "-c", "user.email=factory@example.test", "-c", "user.name=Factory",
            "commit", "--allow-empty", "-m", "chore: initialise target repository").ConfigureAwait(false);
    }

    private static async Task<string?> TryGitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        try
        {
            return await GitAsync(workingDirectory, cancellationToken, arguments).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<string> GitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr}");
        }

        return stdout;
    }

    private sealed class SessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionSnapshot> _snapshots = new(StringComparer.Ordinal);

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _snapshots[snapshot.SessionId.Value] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult(_snapshots.TryGetValue(sessionId.Value, out var snapshot) ? snapshot : null);
    }

    private sealed class NoOpPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}
