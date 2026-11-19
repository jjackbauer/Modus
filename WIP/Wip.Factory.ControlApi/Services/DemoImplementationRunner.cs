using System.Diagnostics;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Host.Factory;
using Wip.Factory.Wip.Workspaces;
using Wip.Workspaces.Git;

namespace Wip.Factory.ControlApi.Services;

/// <summary>
/// A deterministic, non-model implementation runner for offline engineering verification. It is not a simulation:
/// it creates a real worktree, writes a real ASP.NET Core application, runs a real build, commits the result and
/// records a real attempt. The evidence therefore proves the control-plane/factory lifecycle, not model quality.
/// Its identity is reported everywhere as <see cref="RunnerName"/> and it never claims independent acceptance.
/// </summary>
public sealed class DemoImplementationRunner : IImplementationStageRunner
{
    public const string RunnerName = "deterministic-demo";
    public const string SessionPrefix = "session-impl-";
    public const string ProjectFile = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <OutputType>Exe</OutputType>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <AssemblyName>WipFactoryDemoTarget</AssemblyName>
          </PropertyGroup>
        </Project>
        """;

    public const string ProgramFile = """
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.MapGet("/", () => Results.Text("Wip.Factory deterministic demo target"));
        app.MapGet("/health", () => Results.Json(new
        {
            status = "ok",
            service = "wip-factory-demo-target",
            runner = "deterministic-demo"
        }));

        app.Run();
        """;

    private readonly FactoryControlRoot _root;

    public DemoImplementationRunner(FactoryControlRoot root) => _root = root;

    public async ValueTask<ImplementationOutcome> RunAsync(
        ImplementationRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessionId = new SessionId(SessionPrefix + request.WorkItemId.ToLowerInvariant());
        var journal = new ImplementationAttemptJournal(
            ImplementationAttemptJournal.PathFor(_root.Root, request.WorkItemId));
        var startedAt = DateTimeOffset.UtcNow;

        ImplementationOutcome outcome;
        try
        {
            outcome = await RunCoreAsync(request, sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TryRecordAsync(journal, request, sessionId, ImplementationAttemptRecord.CancelledOutcome,
                "implementation-cancelled", "the deterministic attempt was cancelled", startedAt, null, cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            outcome = ImplementationOutcome.Failed("deterministic-runner-error:" + exception.GetType().Name, exception.Message);
            await TryRecordAsync(journal, request, sessionId, ImplementationAttemptRecord.FailedOutcome,
                outcome.FailureCategory, exception.Message, startedAt, null, cancellationToken).ConfigureAwait(false);
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
            outcome,
            cancellationToken).ConfigureAwait(false);

        return outcome;
    }

    private async ValueTask<ImplementationOutcome> RunCoreAsync(
        ImplementationRequest request,
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        await EnsureBaselineCommitAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);

        var workspaceProvider = new WipWorkspaceProviderGit();
        var provisioner = new FactoryWorktreeProvisioner(
            workspaceProvider,
            worktreeRoot: Path.Combine(_root.Root, "worktrees"));

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

        if (lease is null)
        {
            return ImplementationOutcome.Failed("worktree-failed:" + reason);
        }

        var worktree = lease.WorktreePath;
        var baseSha = (await GitAsync(worktree, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

        var attemptFile = Path.Combine(worktree, "factory", "deterministic-attempt.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(attemptFile)!);
        var attemptMarker = string.Join(
            '\n',
            "runner: " + RunnerName,
            "work item: " + request.WorkItemId,
            "attempt at: " + DateTimeOffset.UtcNow.ToString("O"),
            "functional intent: " + FirstLine(request.RequirementText),
            "acceptance expectations: " + request.Expectations.Count,
            "rework context: " + (request.ReworkContext ?? "none"),
            string.Empty);

        await File.WriteAllTextAsync(attemptFile, attemptMarker, cancellationToken).ConfigureAwait(false);
        await WriteIfMissingAsync(Path.Combine(worktree, ".gitignore"), "bin/\nobj/\n", cancellationToken).ConfigureAwait(false);
        await WriteIfMissingAsync(Path.Combine(worktree, "App.csproj"), ProjectFile, cancellationToken).ConfigureAwait(false);
        await WriteIfMissingAsync(Path.Combine(worktree, "Program.cs"), ProgramFile, cancellationToken).ConfigureAwait(false);

        // The rule file is deliberately not executable and carries no secrets; it is evidence of how this candidate
        // was produced, never a claim that a model implemented the requirement.
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
            "commit", "-m", "feat: deterministic implementation for " + request.WorkItemId).ConfigureAwait(false);

        await workspaceProvider
            .RecordSessionHeadAsync(request.RepositoryPath, sessionId, cancellationToken)
            .ConfigureAwait(false);

        var headSha = (await GitAsync(worktree, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var diff = await GitAsync(worktree, cancellationToken, "diff", baseSha + ".." + headSha).ConfigureAwait(false);
        var diffDigest = Digest(diff);

        var project = Directory
            .EnumerateFiles(worktree, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        if (project is null)
        {
            return ImplementationOutcome.Failed(ImplementationOutcome.BuildFailed, "no-project-file");
        }

        var build = await RunBoundedAsync(
            "dotnet",
            ["build", project, "-v", "q", "--nologo"],
            worktree,
            cancellationToken).ConfigureAwait(false);

        if (!build.Ok)
        {
            var detail = build.Output.Length > 4000 ? build.Output[^4000..] : build.Output;
            return ImplementationOutcome.Failed(ImplementationOutcome.BuildFailed, detail);
        }

        return ImplementationOutcome.Ok(baseSha, headSha, changed, diffDigest, worktree);
    }

    private async ValueTask TryRecordAsync(
        ImplementationAttemptJournal journal,
        ImplementationRequest request,
        SessionId sessionId,
        string outcome,
        string? failureCategory,
        string? detail,
        DateTimeOffset startedAt,
        ImplementationOutcome? result,
        CancellationToken cancellationToken)
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
            // Attempt history must not turn a completed implementation into an unhandled failure.
        }
    }

    private static async Task WriteIfMissingAsync(string path, string content, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Digest(string content)
        => "sha256:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static string FirstLine(string text)        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => line.Length > 0) ?? string.Empty;

    private static async Task EnsureBaselineCommitAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var head = await TryGitAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "HEAD").ConfigureAwait(false);
        if (head is not null)
        {
            return;
        }

        var ignore = Path.Combine(repositoryPath, ".gitignore");
        if (!File.Exists(ignore))
        {
            await File.WriteAllTextAsync(ignore, "bin/\nobj/\n", cancellationToken).ConfigureAwait(false);
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
        var result = await RunBoundedAsync("git", arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.Output}");
        }

        return result.Output;
    }

    private static async Task<(bool Ok, string Output)> RunBoundedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(fileName)
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

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start " + fileName + ".");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode == 0, stdout + stderr);
    }
}
