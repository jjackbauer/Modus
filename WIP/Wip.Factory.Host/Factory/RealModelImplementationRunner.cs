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
using Wip.Factory.Wip.Fanout;
using Wip.Factory.Wip.Isolation;
using Wip.Factory.Wip.Workspaces;
using Wip.Runtime;
using Wip.Runtime.Runtime;

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

    /// <summary>Baseline ignore list so generated diffs stay source-only (bin/obj were being committed).</summary>
    public static readonly string DefaultGitIgnore = "bin/" + Environment.NewLine + "obj/" + Environment.NewLine;

    private readonly FactoryControlRoot _root;
    private readonly Uri _baseUrl;
    private readonly string _apiKey;

    public RealModelImplementationRunner(FactoryControlRoot root, Uri baseUrl, string apiKey)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        _apiKey = apiKey ?? throw new ArgumentException("An API key is required.", nameof(apiKey));
    }

    public async ValueTask<ImplementationOutcome> RunAsync(
        ImplementationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // An empty repository is a legitimate starting point; give it a baseline commit so a
        // worktree can be created, without touching any operator content.
        await EnsureBaselineCommitAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);

        var provisioner = new FactoryWorktreeProvisioner();
        var sessionId = new SessionId("session-impl-" + request.WorkItemId.ToLowerInvariant());
        FactoryWorkspaceLease? lease;
        string? reason;
        try
        {
            (lease, reason) = await provisioner
                .ProvisionAsync(request.RepositoryPath, sessionId, "main", cancellationToken)
                .ConfigureAwait(false);
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
            return ImplementationOutcome.Failed("implementation-agent-failed:" + dispatch.FailureCategory);
        }

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

    private static string BuildPrompt(ImplementationRequest request)
    {
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
