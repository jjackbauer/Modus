using Wip.Abstractions.Identifiers;
using Wip.Workspaces.Git;
using Xunit;

namespace Wip.Workspaces.Git.Tests.Git;

public sealed class WipWorkspaceProviderGitTests
{
    [Fact]
    public async Task CreateAsync_GivenSessionStart_CreatesIsolatedGitWorktreeAndSessionBranch()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-create"),
                TargetBranch: "main"),
            CancellationToken.None);

        var branchList = await fixture.RunGitAsync("branch", "--list", workspace.SessionBranch);
        var expectedWorktree = Path.Combine(fixture.RepositoryPath, ".wip", "worktrees", "session-create");

        Assert.Equal("main", workspace.TargetBranch);
        Assert.Equal(Path.GetFullPath(expectedWorktree), Path.GetFullPath(workspace.WorktreePath));
        Assert.True(Directory.Exists(workspace.WorktreePath));
        Assert.Contains(workspace.SessionBranch, branchList.StdOut, StringComparison.Ordinal);

        var sessionFile = Path.Combine(workspace.WorktreePath, "readme.txt");
        await File.WriteAllTextAsync(sessionFile, "session-only-change\n", CancellationToken.None);

        var mainFile = Path.Combine(fixture.RepositoryPath, "readme.txt");
        var primaryWorktreeContent = await File.ReadAllTextAsync(mainFile, CancellationToken.None);
        Assert.Equal("base\n", primaryWorktreeContent);
    }

    [Fact]
    public async Task ComputeDiffHashAsync_GivenEquivalentChangesWithLineEndingNoise_ReturnsStableNormalizedHash()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-diff"),
                TargetBranch: "main"),
            CancellationToken.None);

        var trackedFile = Path.Combine(workspace.WorktreePath, "readme.txt");

        await File.WriteAllTextAsync(trackedFile, "base\r\nfeature\r\n", CancellationToken.None);
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "add", ".");
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "commit", "-m", "feature with crlf");

        var firstHash = await provider.ComputeNormalizedDiffHashAsync(
            new DiffHashRequest(
                RepositoryPath: fixture.RepositoryPath,
                TargetBranch: workspace.TargetBranch,
                SessionBranch: workspace.SessionBranch),
            CancellationToken.None);

        await File.WriteAllTextAsync(trackedFile, "base\nfeature\n", CancellationToken.None);
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "add", ".");
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "commit", "--amend", "--no-edit");

        var secondHash = await provider.ComputeNormalizedDiffHashAsync(
            new DiffHashRequest(
                RepositoryPath: fixture.RepositoryPath,
                TargetBranch: workspace.TargetBranch,
                SessionBranch: workspace.SessionBranch),
            CancellationToken.None);

        Assert.Equal(firstHash, secondHash);
    }

    [Fact]
    public async Task MergePreviewAsync_GivenTargetBranchDrift_ReturnsBlockedPreviewAndDriftSignal()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-preview"),
                TargetBranch: "main"),
            CancellationToken.None);

        await fixture.AppendAndCommitAsync("readme.txt", "target-drift\n", "advance target");

        var preview = await provider.PreviewMergeAsync(workspace, CancellationToken.None);
        var drift = await provider.DetectTargetCommitDriftAsync(
            fixture.RepositoryPath,
            workspace.TargetBranch,
            workspace.TargetCommitAtCreation,
            CancellationToken.None);

        Assert.True(drift.HasDrifted);
        Assert.True(preview.HasTargetCommitDrift);
        Assert.False(preview.CanMerge);
        Assert.Contains("drift", preview.BlockReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteGuard_GivenPathEscapeAttempt_ExpectedOperationBlockedAndNoExternalMutation()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-guard"),
                TargetBranch: "main"),
            CancellationToken.None);

        var outsideFile = Path.Combine(fixture.RepositoryPath, "outside-guard.txt");
        await File.WriteAllTextAsync(outsideFile, "outside-initial", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.WriteTextAsync(
                workspace,
                Path.Combine("..", "..", "outside-guard.txt"),
                "malicious-overwrite",
                CancellationToken.None));

        var outsideContent = await File.ReadAllTextAsync(outsideFile, CancellationToken.None);

        Assert.Contains("escapes worktree boundary", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("outside-initial", outsideContent);
    }

    [Fact]
    public async Task MergeAsync_GivenApprovedTokenAndTargetBranchDrift_RejectsMergeWithDeterministicReason()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-drift-reject"),
                TargetBranch: "main"),
            CancellationToken.None);

        await File.AppendAllTextAsync(Path.Combine(workspace.WorktreePath, "readme.txt"), "session-change\n", CancellationToken.None);
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "add", ".");
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "commit", "-m", "session commit");

        var approvedDiffHash = await provider.ComputeNormalizedDiffHashAsync(
            new DiffHashRequest(fixture.RepositoryPath, workspace.TargetBranch, workspace.SessionBranch),
            CancellationToken.None);

        await fixture.AppendAndCommitAsync("readme.txt", "target-drift\n", "advance target");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.MergeAsync(
                new MergeRequest(
                    RepositoryPath: fixture.RepositoryPath,
                    TargetBranch: workspace.TargetBranch,
                    SessionBranch: workspace.SessionBranch,
                    ExpectedTargetCommit: workspace.TargetCommitAtCreation,
                    ApprovedDiffHash: approvedDiffHash,
                    HasPassingValidationEvidence: true,
                    HasReviewEvidence: true,
                    IsSessionAborted: false,
                    IsApprovalConfirmed: true),
                CancellationToken.None));

        Assert.Contains("drift", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MergeAsync_GivenDiffChangedAfterApproval_RejectsMergeAndMarksApprovalStale()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-stale"),
                TargetBranch: "main"),
            CancellationToken.None);

        await File.AppendAllTextAsync(Path.Combine(workspace.WorktreePath, "readme.txt"), "feature-v1\n", CancellationToken.None);
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "add", ".");
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "commit", "-m", "feature v1");

        var approvedHash = await provider.ComputeNormalizedDiffHashAsync(
            new DiffHashRequest(fixture.RepositoryPath, workspace.TargetBranch, workspace.SessionBranch),
            CancellationToken.None);

        await File.AppendAllTextAsync(Path.Combine(workspace.WorktreePath, "readme.txt"), "feature-v2\n", CancellationToken.None);
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "add", ".");
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "commit", "-m", "feature v2");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.MergeAsync(
                new MergeRequest(
                    RepositoryPath: fixture.RepositoryPath,
                    TargetBranch: workspace.TargetBranch,
                    SessionBranch: workspace.SessionBranch,
                    ExpectedTargetCommit: workspace.TargetCommitAtCreation,
                    ApprovedDiffHash: approvedHash,
                    HasPassingValidationEvidence: true,
                    HasReviewEvidence: true,
                    IsSessionAborted: false,
                    IsApprovalConfirmed: true),
                CancellationToken.None));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MergeAsync_GivenMissingApprovalEvidence_RejectsMergeWithDeterministicReason()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-missing-approval"),
                TargetBranch: "main"),
            CancellationToken.None);

        await File.AppendAllTextAsync(Path.Combine(workspace.WorktreePath, "readme.txt"), "feature-v1\n", CancellationToken.None);
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "add", ".");
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "commit", "-m", "feature v1");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.MergeAsync(
                new MergeRequest(
                    RepositoryPath: fixture.RepositoryPath,
                    TargetBranch: workspace.TargetBranch,
                    SessionBranch: workspace.SessionBranch,
                    ExpectedTargetCommit: workspace.TargetCommitAtCreation,
                    ApprovedDiffHash: string.Empty,
                    HasPassingValidationEvidence: true,
                    HasReviewEvidence: true,
                    IsSessionAborted: false,
                    IsApprovalConfirmed: true),
                CancellationToken.None));

        Assert.Contains("missing approval evidence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MergeAsync_GivenMissingValidationEvidence_RejectsMergeWithDeterministicReason()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-missing-validation"),
                TargetBranch: "main"),
            CancellationToken.None);

        await File.AppendAllTextAsync(Path.Combine(workspace.WorktreePath, "readme.txt"), "feature-v1\n", CancellationToken.None);
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "add", ".");
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "commit", "-m", "feature v1");

        var approvedDiffHash = await provider.ComputeNormalizedDiffHashAsync(
            new DiffHashRequest(fixture.RepositoryPath, workspace.TargetBranch, workspace.SessionBranch),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.MergeAsync(
                new MergeRequest(
                    RepositoryPath: fixture.RepositoryPath,
                    TargetBranch: workspace.TargetBranch,
                    SessionBranch: workspace.SessionBranch,
                    ExpectedTargetCommit: workspace.TargetCommitAtCreation,
                    ApprovedDiffHash: approvedDiffHash,
                    HasPassingValidationEvidence: false,
                    HasReviewEvidence: true,
                    IsSessionAborted: false,
                    IsApprovalConfirmed: true),
                CancellationToken.None));

        Assert.Contains("missing passing validation evidence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MergeAsync_GivenMissingReviewEvidence_RejectsMergeWithDeterministicReason()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-missing-review"),
                TargetBranch: "main"),
            CancellationToken.None);

        await File.AppendAllTextAsync(Path.Combine(workspace.WorktreePath, "readme.txt"), "feature-v1\n", CancellationToken.None);
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "add", ".");
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "commit", "-m", "feature v1");

        var approvedDiffHash = await provider.ComputeNormalizedDiffHashAsync(
            new DiffHashRequest(fixture.RepositoryPath, workspace.TargetBranch, workspace.SessionBranch),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.MergeAsync(
                new MergeRequest(
                    RepositoryPath: fixture.RepositoryPath,
                    TargetBranch: workspace.TargetBranch,
                    SessionBranch: workspace.SessionBranch,
                    ExpectedTargetCommit: workspace.TargetCommitAtCreation,
                    ApprovedDiffHash: approvedDiffHash,
                    HasPassingValidationEvidence: true,
                    HasReviewEvidence: false,
                    IsSessionAborted: false,
                    IsApprovalConfirmed: true),
                CancellationToken.None));

        Assert.Contains("missing review evidence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MergeAsync_GivenAbortedSession_RejectsMergeWithDeterministicReason()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-aborted"),
                TargetBranch: "main"),
            CancellationToken.None);

        await File.AppendAllTextAsync(Path.Combine(workspace.WorktreePath, "readme.txt"), "feature-v1\n", CancellationToken.None);
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "add", ".");
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "commit", "-m", "feature v1");

        var approvedDiffHash = await provider.ComputeNormalizedDiffHashAsync(
            new DiffHashRequest(fixture.RepositoryPath, workspace.TargetBranch, workspace.SessionBranch),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.MergeAsync(
                new MergeRequest(
                    RepositoryPath: fixture.RepositoryPath,
                    TargetBranch: workspace.TargetBranch,
                    SessionBranch: workspace.SessionBranch,
                    ExpectedTargetCommit: workspace.TargetCommitAtCreation,
                    ApprovedDiffHash: approvedDiffHash,
                    HasPassingValidationEvidence: true,
                    HasReviewEvidence: true,
                    IsSessionAborted: true,
                    IsApprovalConfirmed: true),
                CancellationToken.None));

        Assert.Contains("session is aborted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MergeAsync_GivenApprovalNotConfirmed_RejectsMergeWithDeterministicReason()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-non-confirmed"),
                TargetBranch: "main"),
            CancellationToken.None);

        await File.AppendAllTextAsync(Path.Combine(workspace.WorktreePath, "readme.txt"), "feature-v1\n", CancellationToken.None);
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "add", ".");
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "commit", "-m", "feature v1");

        var approvedDiffHash = await provider.ComputeNormalizedDiffHashAsync(
            new DiffHashRequest(fixture.RepositoryPath, workspace.TargetBranch, workspace.SessionBranch),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.MergeAsync(
                new MergeRequest(
                    RepositoryPath: fixture.RepositoryPath,
                    TargetBranch: workspace.TargetBranch,
                    SessionBranch: workspace.SessionBranch,
                    ExpectedTargetCommit: workspace.TargetCommitAtCreation,
                    ApprovedDiffHash: approvedDiffHash,
                    HasPassingValidationEvidence: true,
                    HasReviewEvidence: true,
                    IsSessionAborted: false,
                    IsApprovalConfirmed: false),
                CancellationToken.None));

        Assert.Contains("approval confirmation was not completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MergeAsync_GivenValidatedReviewedConfirmedAndCurrentApproval_MergesFastForwardWithoutDrift()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var provider = new WipWorkspaceProviderGit();

        var workspace = await provider.CreateAsync(
            new CreateWorkspaceRequest(
                RepositoryPath: fixture.RepositoryPath,
                SessionId: new SessionId("session-merge-success"),
                TargetBranch: "main"),
            CancellationToken.None);

        await File.AppendAllTextAsync(Path.Combine(workspace.WorktreePath, "readme.txt"), "feature-v1\n", CancellationToken.None);
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "add", ".");
        await fixture.RunGitAsync("-C", workspace.WorktreePath, "commit", "-m", "feature v1");

        var approvedDiffHash = await provider.ComputeNormalizedDiffHashAsync(
            new DiffHashRequest(fixture.RepositoryPath, workspace.TargetBranch, workspace.SessionBranch),
            CancellationToken.None);

        var result = await provider.MergeAsync(
            new MergeRequest(
                RepositoryPath: fixture.RepositoryPath,
                TargetBranch: workspace.TargetBranch,
                SessionBranch: workspace.SessionBranch,
                ExpectedTargetCommit: workspace.TargetCommitAtCreation,
                ApprovedDiffHash: approvedDiffHash,
                HasPassingValidationEvidence: true,
                HasReviewEvidence: true,
                IsSessionAborted: false,
                IsApprovalConfirmed: true),
            CancellationToken.None);

        var currentMainCommit = (await fixture.RunGitAsync("rev-parse", "--verify", workspace.TargetBranch)).StdOut;

        Assert.Equal(workspace.TargetBranch, result.TargetBranch);
        Assert.Equal(workspace.SessionBranch, result.SessionBranch);
        Assert.Equal(workspace.TargetCommitAtCreation, result.PreviousTargetCommit);
        Assert.Equal(currentMainCommit, result.NewTargetCommit);
    }

    private sealed class TempGitRepository : IAsyncDisposable
    {
        private TempGitRepository(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async ValueTask<TempGitRepository> CreateAsync()
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-git-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var fixture = new TempGitRepository(repositoryPath);
            await fixture.RunGitAsync("init", "--initial-branch=main", ".");
            await fixture.RunGitAsync("config", "user.email", "wip-tests@example.test");
            await fixture.RunGitAsync("config", "user.name", "Wip Tests");

            await File.WriteAllTextAsync(Path.Combine(repositoryPath, "readme.txt"), "base\n", CancellationToken.None);
            await fixture.RunGitAsync("add", ".");
            await fixture.RunGitAsync("commit", "-m", "initial commit");

            return fixture;
        }

        public async ValueTask<GitCommandResult> RunGitAsync(params string[] args)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start git process.");

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(CancellationToken.None);

            var result = new GitCommandResult(
                process.ExitCode,
                (await stdOutTask).Trim(),
                (await stdErrTask).Trim());

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Git command failed ({string.Join(" ", args)}). ExitCode={result.ExitCode}. StdErr={result.StdErr}");
            }

            return result;
        }

        public async ValueTask AppendAndCommitAsync(string fileName, string content, string message)
        {
            var path = Path.Combine(RepositoryPath, fileName);
            await File.AppendAllTextAsync(path, content, CancellationToken.None);
            await RunGitAsync("add", ".");
            await RunGitAsync("commit", "-m", message);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                var listed = RunGitAllowFailureAsync("worktree", "list", "--porcelain")
                    .GetAwaiter()
                    .GetResult();

                if (listed.ExitCode == 0 && !string.IsNullOrWhiteSpace(listed.StdOut))
                {
                    var worktreePaths = listed.StdOut
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Where(line => line.StartsWith("worktree ", StringComparison.Ordinal))
                        .Select(line => line.Substring("worktree ".Length).Trim())
                        .Where(path => !string.Equals(
                            Path.GetFullPath(path),
                            Path.GetFullPath(RepositoryPath),
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    foreach (var worktreePath in worktreePaths)
                    {
                        RunGitAllowFailureAsync("worktree", "remove", "--force", worktreePath)
                            .GetAwaiter()
                            .GetResult();
                    }
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }

            try
            {
                if (Directory.Exists(RepositoryPath))
                    Directory.Delete(RepositoryPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            return ValueTask.CompletedTask;
        }

        private async ValueTask<GitCommandResult> RunGitAllowFailureAsync(params string[] args)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start git process.");

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(CancellationToken.None);

            return new GitCommandResult(
                process.ExitCode,
                (await stdOutTask).Trim(),
                (await stdErrTask).Trim());
        }
    }

    private sealed record GitCommandResult(int ExitCode, string StdOut, string StdErr);
}
