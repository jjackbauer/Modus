using Wip.Abstractions.Identifiers;
using Wip.Workspaces.Git;
using Xunit;

namespace Wip.Workspaces.Git.Tests.Git;

/// <summary>
/// Executable recovery and concurrency proof for session worktree provisioning: original-baseline preservation,
/// idempotent retries, uncommitted-work preservation, prune/attach recovery and named refusals for corrupt,
/// mismatched or rewritten worktrees.
/// </summary>
public sealed class WorktreeRecoveryTests
{
    [Fact]
    public async Task CreateAsync_GivenTargetAdvancedAfterFirstCreate_PreservesOriginalTarget()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var request = new CreateWorkspaceRequest(repository.Path, new SessionId("session-baseline"), "main");

        var first = await provider.CreateAsync(request, CancellationToken.None);
        await repository.AdvanceMainAsync("target-advance.txt");

        var second = await provider.CreateAsync(request, CancellationToken.None);
        var state = await provider.ReadProvisioningStateAsync(repository.Path, request.SessionId, CancellationToken.None);

        Assert.Equal(first.TargetCommitAtCreation, second.TargetCommitAtCreation);
        Assert.NotEqual(first.TargetCommitAtCreation, second.ObservedTargetCommit);
        Assert.True(second.TargetHasAdvanced);
        Assert.Equal(2, second.ProvisioningAttempt);
        Assert.NotNull(state);
        Assert.Equal(first.TargetCommitAtCreation, state!.OriginalTargetCommit);
        Assert.Equal(2, state.Attempts.Count);
        Assert.All(state.Attempts, static attempt => Assert.Equal("succeeded", attempt.Outcome));
    }

    [Fact]
    public async Task CreateAsync_GivenRetryWithUncommittedWork_PreservesWorkingTree()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var request = new CreateWorkspaceRequest(repository.Path, new SessionId("session-uncommitted"), "main");

        var first = await provider.CreateAsync(request, CancellationToken.None);
        var tracked = Path.Combine(first.WorktreePath, "readme.txt");
        var untracked = Path.Combine(first.WorktreePath, "implementation", "draft.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(untracked)!);
        await File.WriteAllTextAsync(tracked, "modified but uncommitted", CancellationToken.None);
        await File.WriteAllTextAsync(untracked, "class Draft { }", CancellationToken.None);

        var second = await provider.CreateAsync(request, CancellationToken.None);

        Assert.Equal("modified but uncommitted", await File.ReadAllTextAsync(Path.Combine(second.WorktreePath, "readme.txt"), CancellationToken.None));
        Assert.Equal("class Draft { }", await File.ReadAllTextAsync(Path.Combine(second.WorktreePath, "implementation", "draft.cs"), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_GivenConcurrentRetries_ProducesOneConsistentWorktree()
    {
        await using var repository = await TempRepository.CreateAsync();
        var request = new CreateWorkspaceRequest(repository.Path, new SessionId("session-concurrent"), "main");

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () =>
            {
                var provider = new WipWorkspaceProviderGit();
                return await provider.CreateAsync(request, CancellationToken.None);
            }))
            .ToArray();

        var sessions = await Task.WhenAll(tasks);
        var branches = await repository.RunGitAsync("branch", "--list", "wip/session/session-concurrent");
        var worktrees = await repository.RunGitAsync("worktree", "list", "--porcelain");

        Assert.All(sessions, session =>
        {
            Assert.Equal(sessions[0].WorktreePath, session.WorktreePath);
            Assert.Equal(sessions[0].TargetCommitAtCreation, session.TargetCommitAtCreation);
            Assert.Equal(sessions[0].SessionBranch, session.SessionBranch);
        });
        Assert.Single(branches.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(
            sessions[0].WorktreePath.Replace('\\', '/'),
            worktrees.StdOut,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_GivenBranchWithoutWorktree_ReattachesAtOriginalBaseline()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var request = new CreateWorkspaceRequest(repository.Path, new SessionId("session-attach-after-remove"), "main");

        var first = await provider.CreateAsync(request, CancellationToken.None);
        await File.AppendAllTextAsync(Path.Combine(first.WorktreePath, "work.txt"), "committed\n", CancellationToken.None);
        await repository.RunGitAsyncAt(first.WorktreePath, "add", ".");
        await repository.RunGitAsyncAt(first.WorktreePath, "commit", "-m", "session work");
        var sessionHead = (await repository.RunGitAsyncAt(first.WorktreePath, "rev-parse", "HEAD")).StdOut;

        await repository.RunGitAsync("worktree", "remove", "--force", first.WorktreePath);
        await repository.AdvanceMainAsync("advanced-after-remove.txt");

        var second = await provider.CreateAsync(request, CancellationToken.None);

        Assert.Equal(first.TargetCommitAtCreation, second.TargetCommitAtCreation);
        Assert.True(second.TargetHasAdvanced);
        Assert.Equal(sessionHead, (await repository.RunGitAsyncAt(second.WorktreePath, "rev-parse", "HEAD")).StdOut);
        Assert.True(File.Exists(Path.Combine(second.WorktreePath, "work.txt")));
    }

    [Fact]
    public async Task CreateAsync_GivenRegisteredWorktreeDirectoryRemoved_PrunesAndRecovers()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var request = new CreateWorkspaceRequest(repository.Path, new SessionId("session-prune"), "main");

        var first = await provider.CreateAsync(request, CancellationToken.None);
        Directory.Delete(first.WorktreePath, recursive: true);

        var second = await provider.CreateAsync(request, CancellationToken.None);
        var state = await provider.ReadProvisioningStateAsync(repository.Path, request.SessionId, CancellationToken.None);

        Assert.True(Directory.Exists(second.WorktreePath));
        Assert.Equal(first.TargetCommitAtCreation, second.TargetCommitAtCreation);
        Assert.NotNull(state);
        Assert.Equal(2, state!.Attempts.Count);
        Assert.Contains(state.Attempts, static attempt => attempt.Operation is "attached" or "created");
    }

    [Fact]
    public async Task CreateAsync_GivenUnregisteredDirectoryThatIsNotAWorktree_RefusesAndPreserves()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var session = new SessionId("session-partial");
        var worktreePath = Path.Combine(repository.Path, ".wip", "worktrees", session.Value);
        Directory.CreateDirectory(worktreePath);
        var preserved = Path.Combine(worktreePath, "partial-draft.txt");
        await File.WriteAllTextAsync(preserved, "do not delete", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<WorkspaceProvisioningException>(async () =>
            await provider.CreateAsync(
                new CreateWorkspaceRequest(repository.Path, session, "main"),
                CancellationToken.None));

        Assert.Equal(WorkspaceFailureCategories.Partial, exception.Category);
        Assert.True(File.Exists(preserved));
        Assert.Equal("do not delete", await File.ReadAllTextAsync(preserved, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_GivenWorktreeOnDifferentBranch_RefusesMismatchAndPreservesWorktree()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var request = new CreateWorkspaceRequest(repository.Path, new SessionId("session-wrong-branch"), "main");

        var first = await provider.CreateAsync(request, CancellationToken.None);
        await repository.RunGitAsyncAt(first.WorktreePath, "checkout", "-b", "operator/other-work");

        var exception = await Assert.ThrowsAsync<WorkspaceProvisioningException>(async () =>
            await provider.CreateAsync(request, CancellationToken.None));

        Assert.Equal(WorkspaceFailureCategories.Mismatch, exception.Category);
        Assert.True(Directory.Exists(first.WorktreePath));
        Assert.True(File.Exists(Path.Combine(first.WorktreePath, "readme.txt")));
    }

    [Fact]
    public async Task CreateAsync_GivenSessionHistoryRewritten_RefusesHistoryRewritten()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var request = new CreateWorkspaceRequest(repository.Path, new SessionId("session-rewrite"), "main");

        var first = await provider.CreateAsync(request, CancellationToken.None);
        await repository.RunGitAsyncAt(first.WorktreePath, "-c", "user.email=t@example.test", "-c", "user.name=T", "commit", "--allow-empty", "-m", "attempt-one");
        await provider.RecordSessionHeadAsync(repository.Path, request.SessionId, CancellationToken.None);

        await repository.RunGitAsyncAt(first.WorktreePath, "reset", "--hard", first.TargetCommitAtCreation);
        await repository.RunGitAsyncAt(first.WorktreePath, "-c", "user.email=t@example.test", "-c", "user.name=T", "commit", "--allow-empty", "-m", "attempt-two-rewritten");

        var exception = await Assert.ThrowsAsync<WorkspaceProvisioningException>(async () =>
            await provider.CreateAsync(request, CancellationToken.None));

        Assert.Equal(WorkspaceFailureCategories.HistoryRewritten, exception.Category);
    }

    [Fact]
    public async Task CreateAsync_GivenOrphanBranchWithoutState_InfersMergeBaseInsteadOfCurrentTarget()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var request = new CreateWorkspaceRequest(repository.Path, new SessionId("session-orphan"), "main");

        var first = await provider.CreateAsync(request, CancellationToken.None);
        await File.AppendAllTextAsync(Path.Combine(first.WorktreePath, "orphan.txt"), "branch work\n", CancellationToken.None);
        await repository.RunGitAsyncAt(first.WorktreePath, "add", ".");
        await repository.RunGitAsyncAt(first.WorktreePath, "commit", "-m", "orphan branch work");
        var sessionHead = (await repository.RunGitAsyncAt(first.WorktreePath, "rev-parse", "HEAD")).StdOut;

        await repository.RunGitAsync("worktree", "remove", "--force", first.WorktreePath);
        await repository.AdvanceMainAsync("advanced-after-orphan.txt");
        await repository.DeleteProvisioningStateAsync();

        var second = await provider.CreateAsync(request, CancellationToken.None);

        Assert.Equal(first.TargetCommitAtCreation, second.TargetCommitAtCreation);
        Assert.Equal(WorkspaceSessionState.InferredMergeBase, second.BaselineSource);
        Assert.True(second.TargetHasAdvanced);
        Assert.Equal(sessionHead, (await repository.RunGitAsyncAt(second.WorktreePath, "rev-parse", "HEAD")).StdOut);
    }

    [Fact]
    public async Task CreateAsync_GivenCorruptStateFile_RefusesStateCorruptWithoutDeletingWorktree()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var request = new CreateWorkspaceRequest(repository.Path, new SessionId("session-corrupt-state"), "main");

        var first = await provider.CreateAsync(request, CancellationToken.None);
        await repository.CorruptProvisioningStateAsync();

        var exception = await Assert.ThrowsAsync<WorkspaceProvisioningException>(async () =>
            await provider.CreateAsync(request, CancellationToken.None));

        Assert.Equal(WorkspaceFailureCategories.StateCorrupt, exception.Category);
        Assert.True(Directory.Exists(first.WorktreePath));
        Assert.True(File.Exists(Path.Combine(first.WorktreePath, "readme.txt")));
    }

    [Fact]
    public async Task CreateAsync_GivenStateFromDifferentWorktreeRoot_RefusesMismatch()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var session = new SessionId("session-root-change");
        var first = await provider.CreateAsync(
            new CreateWorkspaceRequest(repository.Path, session, "main"),
            CancellationToken.None);

        var otherRoot = Path.Combine(Path.GetTempPath(), "modus-wip-other-" + Guid.NewGuid().ToString("N"));
        try
        {
            var exception = await Assert.ThrowsAsync<WorkspaceProvisioningException>(async () =>
                await provider.CreateAsync(
                    new CreateWorkspaceRequest(repository.Path, session, "main", otherRoot),
                    CancellationToken.None));

            Assert.Equal(WorkspaceFailureCategories.Mismatch, exception.Category);
            Assert.True(Directory.Exists(first.WorktreePath));
        }
        finally
        {
            if (Directory.Exists(otherRoot))
            {
                Directory.Delete(otherRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateAsync_GivenCommitAfterFirstCreate_UpdatesLastKnownHeadWithoutRewritingBaseline()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var request = new CreateWorkspaceRequest(repository.Path, new SessionId("session-head-update"), "main");

        var first = await provider.CreateAsync(request, CancellationToken.None);
        await repository.RunGitAsyncAt(first.WorktreePath, "-c", "user.email=t@example.test", "-c", "user.name=T", "commit", "--allow-empty", "-m", "session commit");
        var head = (await repository.RunGitAsyncAt(first.WorktreePath, "rev-parse", "HEAD")).StdOut;

        var second = await provider.CreateAsync(request, CancellationToken.None);
        var state = await provider.ReadProvisioningStateAsync(repository.Path, request.SessionId, CancellationToken.None);

        Assert.Equal(first.TargetCommitAtCreation, second.TargetCommitAtCreation);
        Assert.NotNull(state);
        Assert.Equal(head, state!.LastKnownSessionHead);
        Assert.Equal(2, state.Attempts.Count);
    }

    [Fact]
    public async Task CreateAsync_GivenFailedAttemptThenRecovery_PreservesOriginalFailureSeparately()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var session = new SessionId("session-failure-recovery");
        var request = new CreateWorkspaceRequest(repository.Path, session, "main");
        var worktreePath = Path.Combine(repository.Path, ".wip", "worktrees", session.Value);
        Directory.CreateDirectory(worktreePath);
        await File.WriteAllTextAsync(Path.Combine(worktreePath, "preserved.txt"), "keep", CancellationToken.None);

        var failure = await Assert.ThrowsAsync<WorkspaceProvisioningException>(async () =>
            await provider.CreateAsync(request, CancellationToken.None));
        Assert.Equal(WorkspaceFailureCategories.Partial, failure.Category);

        Directory.Delete(worktreePath, recursive: true);
        var recovered = await provider.CreateAsync(request, CancellationToken.None);
        var state = await provider.ReadProvisioningStateAsync(repository.Path, session, CancellationToken.None);

        Assert.True(Directory.Exists(recovered.WorktreePath));
        Assert.NotNull(state);
        Assert.Equal(2, state!.Attempts.Count);
        Assert.Equal(1, state.OriginalFailure!.Attempt);
        Assert.Equal(WorkspaceFailureCategories.Partial, state.OriginalFailure.Category);
        Assert.Equal(1, state.LatestFailure!.Attempt);
        Assert.Equal("succeeded", state.Attempts[1].Outcome);
    }

    [Fact]
    public async Task CreateAsync_GivenMultipleSessions_EachKeepsItsOwnBaseline()
    {
        await using var repository = await TempRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();
        var sessionA = new SessionId("session-a");
        var sessionB = new SessionId("session-b");

        var a = await provider.CreateAsync(new CreateWorkspaceRequest(repository.Path, sessionA, "main"), CancellationToken.None);
        await repository.AdvanceMainAsync("advance-for-b.txt");
        var b = await provider.CreateAsync(new CreateWorkspaceRequest(repository.Path, sessionB, "main"), CancellationToken.None);

        Assert.NotEqual(a.TargetCommitAtCreation, b.TargetCommitAtCreation);
        Assert.False(a.TargetHasAdvanced);
        Assert.False(b.TargetHasAdvanced);
        Assert.NotEqual(a.WorktreePath, b.WorktreePath);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class TempRepository : IAsyncDisposable
    {
        private TempRepository(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static async ValueTask<TempRepository> CreateAsync()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "modus-worktree-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);

            var repository = new TempRepository(path);
            await repository.RunGitAsync("init", "--initial-branch=main", ".");
            await repository.RunGitAsync("config", "user.email", "worktree-tests@example.test");
            await repository.RunGitAsync("config", "user.name", "Worktree Tests");
            await File.WriteAllTextAsync(System.IO.Path.Combine(path, "readme.txt"), "base\n", CancellationToken.None);
            await repository.RunGitAsync("add", ".");
            await repository.RunGitAsync("commit", "-m", "initial commit");
            return repository;
        }

        public async Task AdvanceMainAsync(string fileName)
        {
            await File.WriteAllTextAsync(System.IO.Path.Combine(Path, fileName), "advanced\n", CancellationToken.None);
            await RunGitAsync("add", ".");
            await RunGitAsync("commit", "-m", "advance main");
        }

        public async Task CorruptProvisioningStateAsync()
        {
            var stateDirectory = System.IO.Path.Combine(Path, ".git", "wip", "sessions");
            var stateFile = Directory.EnumerateFiles(stateDirectory, "*.json").Single();
            await File.WriteAllTextAsync(stateFile, "{ not valid json", CancellationToken.None);
        }

        public Task DeleteProvisioningStateAsync()
        {
            var stateDirectory = System.IO.Path.Combine(Path, ".git", "wip", "sessions");
            foreach (var stateFile in Directory.EnumerateFiles(stateDirectory, "*.json"))
            {
                File.Delete(stateFile);
            }

            return Task.CompletedTask;
        }

        public Task<GitResult> RunGitAsync(params string[] args)
            => RunGitAsyncAt(Path, args);

        public async Task<GitResult> RunGitAsyncAt(string workingDirectory, params string[] args)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

            foreach (var argument in args)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start git.");
            var stdout = await process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(CancellationToken.None);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
            }

            return new GitResult(stdout.Trim(), stderr.Trim());
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed record GitResult(string StdOut, string StdErr);
}
