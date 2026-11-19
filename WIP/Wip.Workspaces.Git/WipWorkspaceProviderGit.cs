using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Wip.Abstractions.Identifiers;

namespace Wip.Workspaces.Git;

public sealed record CreateWorkspaceRequest(
    string RepositoryPath,
    SessionId SessionId,
    string TargetBranch,
    string? WorktreeRoot = null);

public sealed record WorkspaceSession(
    SessionId SessionId,
    string RepositoryPath,
    string WorktreePath,
    string SessionBranch,
    string TargetBranch,
    string TargetCommitAtCreation,
    string ObservedTargetCommit = "",
    bool TargetHasAdvanced = false,
    string BaselineSource = WorkspaceSessionState.ResolvedAtCreate,
    int ProvisioningAttempt = 1);

public sealed record DiffHashRequest(
    string RepositoryPath,
    string TargetBranch,
    string SessionBranch);

public sealed record WorkspaceDiff(
    string DiffHash,
    IReadOnlyList<string> ChangedFiles,
    string Patch);

public sealed record TargetCommitDrift(
    string ExpectedTargetCommit,
    string CurrentTargetCommit,
    bool HasDrifted);

public sealed record MergePreview(
    string TargetBranch,
    string SessionBranch,
    string ExpectedTargetCommit,
    string CurrentTargetCommit,
    bool HasTargetCommitDrift,
    bool IsFastForward,
    bool CanMerge,
    string? BlockReason);

public sealed record MergeRequest(
    string RepositoryPath,
    string TargetBranch,
    string SessionBranch,
    string ExpectedTargetCommit,
    string ApprovedDiffHash,
    bool HasPassingValidationEvidence,
    bool HasReviewEvidence,
    bool IsSessionAborted,
    bool IsApprovalConfirmed);

public sealed record MergeResult(
    string TargetBranch,
    string PreviousTargetCommit,
    string NewTargetCommit,
    string SessionBranch);

public sealed class WipWorkspaceProviderGit
{
    public async ValueTask<WorkspaceSession> CreateAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredPath(request.RepositoryPath, nameof(request.RepositoryPath));
        ValidateRequired(request.TargetBranch, nameof(request.TargetBranch));

        var repositoryPath = Path.GetFullPath(request.RepositoryPath);
        var sessionBranch = BuildSessionBranchName(request.SessionId);
        var worktreeRoot = string.IsNullOrWhiteSpace(request.WorktreeRoot)
            ? Path.Combine(repositoryPath, ".wip", "worktrees")
            : Path.GetFullPath(request.WorktreeRoot!);
        var worktreePath = Path.GetFullPath(Path.Combine(worktreeRoot, request.SessionId.Value));

        var gitCommonDirectory = await ResolveGitCommonDirectoryAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        var statePath = WorkspaceStateStore.StatePath(gitCommonDirectory, request.SessionId);
        var lockPath = WorkspaceStateStore.LockPath(gitCommonDirectory, request.SessionId);

        var existingState = WorkspaceStateStore.Read(statePath);
        if (existingState is not null)
        {
            ValidateStateIdentity(existingState, repositoryPath, worktreePath, sessionBranch, request.TargetBranch, request.SessionId);
        }

        Directory.CreateDirectory(worktreeRoot);

        await using var lease = await WorkspaceProvisioningLease
            .AcquireAsync(lockPath, TimeSpan.FromSeconds(60), cancellationToken)
            .ConfigureAwait(false);

        // Re-read after acquiring the lease: another process may have completed this session while we waited.
        existingState = WorkspaceStateStore.Read(statePath);
        if (existingState is not null)
        {
            ValidateStateIdentity(existingState, repositoryPath, worktreePath, sessionBranch, request.TargetBranch, request.SessionId);
        }

        var currentTarget = await ResolveCommitAsync(repositoryPath, request.TargetBranch, cancellationToken).ConfigureAwait(false);
        var originalTarget = existingState?.OriginalTargetCommit ?? currentTarget;
        var baselineSource = existingState?.OriginalTargetSource ?? WorkspaceSessionState.ResolvedAtCreate;
        var attempts = existingState is null
            ? []
            : new List<WorkspaceProvisioningAttempt>(existingState.Attempts);
        var attemptNumber = attempts.Count + 1;

        try
        {
            var provisioned = await ProvisionLockedAsync(
                repositoryPath,
                worktreePath,
                sessionBranch,
                request.TargetBranch,
                originalTarget,
                existingState,
                cancellationToken).ConfigureAwait(false);

            originalTarget = provisioned.OriginalTargetCommit;
            baselineSource = provisioned.BaselineSource;
            attempts.Add(new WorkspaceProvisioningAttempt(
                attemptNumber,
                provisioned.Operation,
                "succeeded",
                Category: null,
                provisioned.Detail,
                originalTarget,
                provisioned.HeadSha,
                DateTimeOffset.UtcNow));

            var now = DateTimeOffset.UtcNow;
            WorkspaceStateStore.Write(statePath, new WorkspaceSessionState(
                WorkspaceSessionState.CurrentVersion,
                request.SessionId.Value,
                repositoryPath,
                worktreePath,
                sessionBranch,
                request.TargetBranch,
                originalTarget,
                baselineSource,
                provisioned.HeadSha,
                attempts,
                existingState?.CreatedAtUtc ?? now,
                now));

            return new WorkspaceSession(
                request.SessionId,
                repositoryPath,
                worktreePath,
                sessionBranch,
                request.TargetBranch,
                originalTarget,
                currentTarget,
                !string.Equals(currentTarget, originalTarget, StringComparison.Ordinal),
                baselineSource,
                attemptNumber);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failure = exception as WorkspaceProvisioningException
                ?? new WorkspaceProvisioningException(
                    WorkspaceFailureCategories.ProvisionFailed,
                    exception.Message);

            attempts.Add(new WorkspaceProvisioningAttempt(
                attemptNumber,
                "provision",
                "failed",
                failure.Category,
                failure.Message,
                originalTarget,
                WorktreeHead: null,
                DateTimeOffset.UtcNow));

            var now = DateTimeOffset.UtcNow;
            try
            {
                WorkspaceStateStore.Write(statePath, new WorkspaceSessionState(
                    WorkspaceSessionState.CurrentVersion,
                    request.SessionId.Value,
                    repositoryPath,
                    worktreePath,
                    sessionBranch,
                    request.TargetBranch,
                    originalTarget,
                    baselineSource,
                    existingState?.LastKnownSessionHead ?? string.Empty,
                    attempts,
                    existingState?.CreatedAtUtc ?? now,
                    now));
            }
            catch (Exception stateException) when (stateException is IOException or UnauthorizedAccessException)
            {
                // The provisioning refusal is the primary fact. A secondary state-write failure must not replace it.
            }

            if (ReferenceEquals(failure, exception))
            {
                throw;
            }

            throw failure;
        }
    }

    /// <summary>
    /// Records the session branch head observed after an implementation commit, so a later retry can distinguish a
    /// legitimate descendant from a silent history rewrite. The caller holds no lease; this method takes it.
    /// </summary>
    public async ValueTask<WorkspaceSessionState> RecordSessionHeadAsync(
        string repositoryPath,
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        var repository = Path.GetFullPath(repositoryPath);
        var gitCommonDirectory = await ResolveGitCommonDirectoryAsync(repository, cancellationToken).ConfigureAwait(false);
        var statePath = WorkspaceStateStore.StatePath(gitCommonDirectory, sessionId);
        var lockPath = WorkspaceStateStore.LockPath(gitCommonDirectory, sessionId);

        await using var lease = await WorkspaceProvisioningLease
            .AcquireAsync(lockPath, TimeSpan.FromSeconds(60), cancellationToken)
            .ConfigureAwait(false);

        var state = WorkspaceStateStore.Read(statePath)
            ?? throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.StateCorrupt,
                $"no durable provisioning state exists for session '{sessionId.Value}'");

        if (!string.Equals(state.SessionId, sessionId.Value, StringComparison.Ordinal)
            || !PathsEqual(state.RepositoryPath, repository))
        {
            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.Mismatch,
                $"durable state for session '{sessionId.Value}' identifies repository '{state.RepositoryPath}'");
        }

        var head = await VerifyWorktreeAsync(
            state.WorktreePath,
            state.SessionBranch,
            state.OriginalTargetCommit,
            state,
            cancellationToken).ConfigureAwait(false);

        var updated = state with
        {
            LastKnownSessionHead = head,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        WorkspaceStateStore.Write(statePath, updated);
        return updated;
    }

    /// <summary>Loads the durable provisioning record for diagnostics and recovery tests; null when none exists.</summary>
    public async ValueTask<WorkspaceSessionState?> ReadProvisioningStateAsync(
        string repositoryPath,
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        var repository = Path.GetFullPath(repositoryPath);
        var gitCommonDirectory = await ResolveGitCommonDirectoryAsync(repository, cancellationToken).ConfigureAwait(false);
        return WorkspaceStateStore.Read(WorkspaceStateStore.StatePath(gitCommonDirectory, sessionId));
    }

    private static async ValueTask<ProvisionedWorkspace> ProvisionLockedAsync(
        string repositoryPath,
        string worktreePath,
        string sessionBranch,
        string targetBranch,
        string originalTarget,
        WorkspaceSessionState? state,
        CancellationToken cancellationToken)
    {
        var targetPath = Path.GetFullPath(worktreePath);
        var entries = await ReadWorktreeEntriesAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        var entry = entries.FirstOrDefault(candidate => PathsEqual(candidate.Path, targetPath));

        if (entry is { Prunable: true })
        {
            await RunGitAsync(repositoryPath, allowFailure: true, cancellationToken, "worktree", "prune").ConfigureAwait(false);
            entries = await ReadWorktreeEntriesAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
            entry = entries.FirstOrDefault(candidate => PathsEqual(candidate.Path, targetPath));
        }

        if (entry is not null && !Directory.Exists(worktreePath))
        {
            // The directory disappeared under a registered worktree: prune the stale registration, then attach.
            await RunGitAsync(repositoryPath, allowFailure: true, cancellationToken, "worktree", "prune").ConfigureAwait(false);
            entry = null;
        }

        if (entry is not null)
        {
            if (!string.Equals(entry.Branch, "refs/heads/" + sessionBranch, StringComparison.Ordinal))
            {
                throw new WorkspaceProvisioningException(
                    WorkspaceFailureCategories.Mismatch,
                    $"registered worktree '{worktreePath}' is on '{entry.Branch ?? "detached HEAD"}', not '{sessionBranch}'");
            }

            var reusedHead = await VerifyWorktreeAsync(worktreePath, sessionBranch, originalTarget, state, cancellationToken)
                .ConfigureAwait(false);
            return new ProvisionedWorkspace(
                "reused",
                "existing worktree reused without reset, clean or rebase",
                reusedHead,
                originalTarget,
                state?.OriginalTargetSource ?? WorkspaceSessionState.ResolvedAtCreate);
        }

        if (Directory.Exists(worktreePath))
        {
            // Git does not register this path. Try a non-destructive repair first; never delete operator/model work.
            var repair = await RunGitAsync(
                repositoryPath,
                allowFailure: true,
                cancellationToken,
                "worktree",
                "repair",
                worktreePath).ConfigureAwait(false);

            if (repair.ExitCode == 0)
            {
                entries = await ReadWorktreeEntriesAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
                var repaired = entries.FirstOrDefault(candidate => PathsEqual(candidate.Path, targetPath));
                if (repaired is not null && Directory.Exists(worktreePath))
                {
                    if (!string.Equals(repaired.Branch, "refs/heads/" + sessionBranch, StringComparison.Ordinal))
                    {
                        throw new WorkspaceProvisioningException(
                            WorkspaceFailureCategories.Mismatch,
                            $"repaired worktree '{worktreePath}' is on '{repaired.Branch ?? "detached HEAD"}', not '{sessionBranch}'");
                    }

                    var repairedHead = await VerifyWorktreeAsync(
                        worktreePath,
                        sessionBranch,
                        originalTarget,
                        state,
                        cancellationToken).ConfigureAwait(false);
                    return new ProvisionedWorkspace(
                        "repaired",
                        "partial worktree repaired without discarding files",
                        repairedHead,
                        originalTarget,
                        state?.OriginalTargetSource ?? WorkspaceSessionState.ResolvedAtCreate);
                }
            }

            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.Partial,
                $"directory '{worktreePath}' exists but is not a registered Git worktree; it was preserved unchanged",
                preservationPath: worktreePath);
        }

        var branchRef = "refs/heads/" + sessionBranch;
        var branch = await RunGitAsync(
            repositoryPath,
            allowFailure: true,
            cancellationToken,
            "rev-parse",
            "--verify",
            "--quiet",
            branchRef).ConfigureAwait(false);

        var baselineSource = state?.OriginalTargetSource ?? WorkspaceSessionState.ResolvedAtCreate;
        if (branch.ExitCode == 0)
        {
            var branchHead = (await RunGitOrThrowAsync(
                repositoryPath,
                cancellationToken,
                "rev-parse",
                "--verify",
                branchRef).ConfigureAwait(false)).StdOut;

            if (state is null)
            {
                // Reconstruct the original baseline rather than substituting the current target branch.
                var mergeBase = await RunGitOrThrowAsync(
                    repositoryPath,
                    cancellationToken,
                    "merge-base",
                    targetBranch,
                    branchRef).ConfigureAwait(false);
                originalTarget = mergeBase.StdOut;
                baselineSource = WorkspaceSessionState.InferredMergeBase;
            }

            if (!await IsAncestorAsync(repositoryPath, originalTarget, branchHead, cancellationToken).ConfigureAwait(false))
            {
                throw new WorkspaceProvisioningException(
                    WorkspaceFailureCategories.BaselineRewritten,
                    $"session branch '{sessionBranch}' no longer descends from original target '{originalTarget}'");
            }

            if (state is not null
                && !string.IsNullOrWhiteSpace(state.LastKnownSessionHead)
                && !string.Equals(state.LastKnownSessionHead, branchHead, StringComparison.Ordinal)
                && !await IsAncestorAsync(repositoryPath, state.LastKnownSessionHead, branchHead, cancellationToken).ConfigureAwait(false))
            {
                throw new WorkspaceProvisioningException(
                    WorkspaceFailureCategories.HistoryRewritten,
                    $"session branch '{sessionBranch}' was rewritten: prior head '{state.LastKnownSessionHead}' is not an ancestor of '{branchHead}'");
            }

            var attach = await RunGitAsync(
                repositoryPath,
                allowFailure: true,
                cancellationToken,
                "worktree",
                "add",
                worktreePath,
                sessionBranch).ConfigureAwait(false);

            if (attach.ExitCode != 0)
            {
                throw new WorkspaceProvisioningException(
                    WorkspaceFailureCategories.ProvisionFailed,
                    $"failed to attach existing session branch '{sessionBranch}': {attach.FormatForError()}");
            }

            var attachedHead = await VerifyWorktreeAsync(
                worktreePath,
                sessionBranch,
                originalTarget,
                state,
                cancellationToken).ConfigureAwait(false);
            return new ProvisionedWorkspace(
                "attached",
                "existing session branch attached to a worktree",
                attachedHead,
                originalTarget,
                baselineSource);
        }

        // Create from the persisted original target, never from a target branch that may have advanced.
        var create = await RunGitAsync(
            repositoryPath,
            allowFailure: true,
            cancellationToken,
            "worktree",
            "add",
            "-b",
            sessionBranch,
            worktreePath,
            originalTarget).ConfigureAwait(false);

        if (create.ExitCode != 0)
        {
            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.ProvisionFailed,
                $"failed to create worktree for session '{sessionBranch}' at '{originalTarget}': {create.FormatForError()}");
        }

        var createdHead = await VerifyWorktreeAsync(
            worktreePath,
            sessionBranch,
            originalTarget,
            state,
            cancellationToken).ConfigureAwait(false);
        return new ProvisionedWorkspace(
            "created",
            "worktree created at the persisted original target",
            createdHead,
            originalTarget,
            baselineSource);
    }

    private static async ValueTask<string> VerifyWorktreeAsync(
        string worktreePath,
        string sessionBranch,
        string originalTarget,
        WorkspaceSessionState? state,
        CancellationToken cancellationToken)
    {
        var currentBranch = await RunGitAsync(
            worktreePath,
            allowFailure: true,
            cancellationToken,
            "rev-parse",
            "--abbrev-ref",
            "HEAD").ConfigureAwait(false);

        if (currentBranch.ExitCode != 0)
        {
            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.Corrupt,
                $"worktree '{worktreePath}' cannot resolve HEAD: {currentBranch.FormatForError()}",
                preservationPath: worktreePath);
        }

        if (!string.Equals(currentBranch.StdOut, sessionBranch, StringComparison.Ordinal))
        {
            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.Mismatch,
                $"worktree '{worktreePath}' is on branch '{currentBranch.StdOut}', not '{sessionBranch}'" );
        }

        var head = (await RunGitOrThrowAsync(worktreePath, cancellationToken, "rev-parse", "--verify", "HEAD").ConfigureAwait(false)).StdOut;

        if (!await IsAncestorAsync(worktreePath, originalTarget, head, cancellationToken).ConfigureAwait(false))
        {
            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.BaselineRewritten,
                $"worktree HEAD '{head}' no longer descends from original target '{originalTarget}'");
        }

        if (state is not null
            && !string.IsNullOrWhiteSpace(state.LastKnownSessionHead)
            && !string.Equals(state.LastKnownSessionHead, head, StringComparison.Ordinal)
            && !await IsAncestorAsync(worktreePath, state.LastKnownSessionHead, head, cancellationToken).ConfigureAwait(false))
        {
            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.HistoryRewritten,
                $"worktree HEAD '{head}' was rewritten: prior head '{state.LastKnownSessionHead}' is not an ancestor");
        }

        return head;
    }

    private static void ValidateStateIdentity(
        WorkspaceSessionState state,
        string repositoryPath,
        string worktreePath,
        string sessionBranch,
        string targetBranch,
        SessionId sessionId)
    {
        if (!string.Equals(state.SessionId, sessionId.Value, StringComparison.Ordinal)
            || !PathsEqual(state.RepositoryPath, repositoryPath)
            || !PathsEqual(state.WorktreePath, worktreePath)
            || !string.Equals(state.SessionBranch, sessionBranch, StringComparison.Ordinal)
            || !string.Equals(state.TargetBranch, targetBranch, StringComparison.Ordinal))
        {
            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.Mismatch,
                $"durable state identifies a different session/worktree: state session='{state.SessionId}', branch='{state.SessionBranch}', worktree='{state.WorktreePath}'");
        }
    }

    private static async ValueTask<string> ResolveGitCommonDirectoryAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var result = await RunGitOrThrowAsync(
            repositoryPath,
            cancellationToken,
            "rev-parse",
            "--git-common-dir").ConfigureAwait(false);

        var value = result.StdOut;
        var fullPath = Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(repositoryPath, value));

        if (!Directory.Exists(fullPath))
        {
            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.Corrupt,
                $"Git common directory '{fullPath}' does not exist");
        }

        return fullPath;
    }

    private static async ValueTask<IReadOnlyList<WorktreeEntry>> ReadWorktreeEntriesAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var result = await RunGitOrThrowAsync(
            repositoryPath,
            cancellationToken,
            "worktree",
            "list",
            "--porcelain").ConfigureAwait(false);

        var entries = new List<WorktreeEntry>();
        string? path = null;
        var head = string.Empty;
        string? branch = null;
        var prunable = false;

        foreach (var rawLine in result.StdOut.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                AddEntry();
                path = line["worktree ".Length..];
                head = string.Empty;
                branch = null;
                prunable = false;
            }
            else if (path is not null && line.StartsWith("HEAD ", StringComparison.Ordinal))
            {
                head = line["HEAD ".Length..];
            }
            else if (path is not null && line.StartsWith("branch ", StringComparison.Ordinal))
            {
                branch = line["branch ".Length..];
            }
            else if (path is not null && string.Equals(line, "prunable", StringComparison.Ordinal))
            {
                prunable = true;
            }
        }

        AddEntry();
        return entries;

        void AddEntry()
        {
            if (path is not null)
            {
                entries.Add(new WorktreeEntry(path, head, branch, prunable));
            }
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private sealed record WorktreeEntry(string Path, string Head, string? Branch, bool Prunable);

    private sealed record ProvisionedWorkspace(
        string Operation,
        string Detail,
        string HeadSha,
        string OriginalTargetCommit,
        string BaselineSource);

    public string ResolveWritePath(WorkspaceSession session, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateRequiredPath(session.WorktreePath, nameof(session.WorktreePath));
        ValidateRequired(relativePath, nameof(relativePath));

        var normalizedRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(session.WorktreePath));
        var candidatePath = Path.IsPathRooted(relativePath)
            ? Path.GetFullPath(relativePath)
            : Path.GetFullPath(Path.Combine(session.WorktreePath, relativePath));

        if (!candidatePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Write rejected: resolved path '{candidatePath}' escapes worktree boundary '{session.WorktreePath}'.");
        }

        return candidatePath;
    }

    public async ValueTask<string> WriteTextAsync(
        WorkspaceSession session,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        var targetPath = ResolveWritePath(session, relativePath);
        var parentDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        await File.WriteAllTextAsync(targetPath, content, cancellationToken);
        return targetPath;
    }

    public async ValueTask<string> ComputeNormalizedDiffHashAsync(DiffHashRequest request, CancellationToken cancellationToken)
    {
        var diff = await GetDiffAsync(request, cancellationToken);
        return diff.DiffHash;
    }

    public async ValueTask<WorkspaceDiff> GetDiffAsync(DiffHashRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredPath(request.RepositoryPath, nameof(request.RepositoryPath));
        ValidateRequired(request.TargetBranch, nameof(request.TargetBranch));
        ValidateRequired(request.SessionBranch, nameof(request.SessionBranch));

        var range = $"{request.TargetBranch}...{request.SessionBranch}";
        var diff = await RunGitOrThrowAsync(
            request.RepositoryPath,
            cancellationToken,
            "diff",
            "--no-color",
            "--ignore-cr-at-eol",
            "--no-ext-diff",
            range);

        var changedFilesResult = await RunGitOrThrowAsync(
            request.RepositoryPath,
            cancellationToken,
            "diff",
            "--name-only",
            "--ignore-cr-at-eol",
            "--no-ext-diff",
            range);

        var normalized = NormalizeDiff(diff.StdOut);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant();
        var changedFiles = changedFilesResult.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        return new WorkspaceDiff(hash, changedFiles, diff.StdOut);
    }

    public async ValueTask<TargetCommitDrift> DetectTargetCommitDriftAsync(
        string repositoryPath,
        string targetBranch,
        string expectedTargetCommit,
        CancellationToken cancellationToken)
    {
        ValidateRequiredPath(repositoryPath, nameof(repositoryPath));
        ValidateRequired(targetBranch, nameof(targetBranch));
        ValidateRequired(expectedTargetCommit, nameof(expectedTargetCommit));

        var currentTargetCommit = await ResolveCommitAsync(repositoryPath, targetBranch, cancellationToken);
        var hasDrifted = !string.Equals(expectedTargetCommit, currentTargetCommit, StringComparison.Ordinal);
        return new TargetCommitDrift(expectedTargetCommit, currentTargetCommit, hasDrifted);
    }

    public async ValueTask<MergePreview> PreviewMergeAsync(WorkspaceSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var drift = await DetectTargetCommitDriftAsync(
            session.RepositoryPath,
            session.TargetBranch,
            session.TargetCommitAtCreation,
            cancellationToken);

        var isFastForward = await IsAncestorAsync(
            session.RepositoryPath,
            session.TargetBranch,
            session.SessionBranch,
            cancellationToken);

        var canMerge = !drift.HasDrifted && isFastForward;
        var reason = canMerge
            ? null
            : drift.HasDrifted
                ? "Merge blocked: target branch has drifted since session approval baseline."
                : "Merge blocked: session branch cannot be merged as fast-forward into target branch.";

        return new MergePreview(
            session.TargetBranch,
            session.SessionBranch,
            session.TargetCommitAtCreation,
            drift.CurrentTargetCommit,
            drift.HasDrifted,
            isFastForward,
            canMerge,
            reason);
    }

    public async ValueTask<MergeResult> MergeAsync(MergeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredPath(request.RepositoryPath, nameof(request.RepositoryPath));
        ValidateRequired(request.TargetBranch, nameof(request.TargetBranch));
        ValidateRequired(request.SessionBranch, nameof(request.SessionBranch));
        ValidateRequired(request.ExpectedTargetCommit, nameof(request.ExpectedTargetCommit));

        if (request.IsSessionAborted)
        {
            throw new InvalidOperationException(
                "Merge rejected: session is aborted and cannot be merged.");
        }

        if (!request.HasPassingValidationEvidence)
        {
            throw new InvalidOperationException(
                "Merge rejected: missing passing validation evidence for merge candidate.");
        }

        if (!request.HasReviewEvidence)
        {
            throw new InvalidOperationException(
                "Merge rejected: missing review evidence for merge candidate.");
        }

        if (!request.IsApprovalConfirmed)
        {
            throw new InvalidOperationException(
                "Merge rejected: approval confirmation was not completed.");
        }

        if (string.IsNullOrWhiteSpace(request.ApprovedDiffHash))
        {
            throw new InvalidOperationException(
                "Merge rejected: missing approval evidence for candidate diff hash.");
        }

        var drift = await DetectTargetCommitDriftAsync(
            request.RepositoryPath,
            request.TargetBranch,
            request.ExpectedTargetCommit,
            cancellationToken);

        if (drift.HasDrifted)
        {
            throw new InvalidOperationException(
                $"Merge rejected due to target branch drift. Expected '{request.ExpectedTargetCommit}', current '{drift.CurrentTargetCommit}'.");
        }

        var currentDiffHash = await ComputeNormalizedDiffHashAsync(
            new DiffHashRequest(request.RepositoryPath, request.TargetBranch, request.SessionBranch),
            cancellationToken);

        if (!string.Equals(request.ApprovedDiffHash, currentDiffHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Merge rejected because approval is stale: approved diff hash does not match current candidate diff.");
        }

        var isFastForward = await IsAncestorAsync(
            request.RepositoryPath,
            request.TargetBranch,
            request.SessionBranch,
            cancellationToken);

        if (!isFastForward)
        {
            throw new InvalidOperationException(
                $"Merge rejected because branch '{request.SessionBranch}' is not a fast-forward of '{request.TargetBranch}'.");
        }

        var sessionCommit = await ResolveCommitAsync(request.RepositoryPath, request.SessionBranch, cancellationToken);

        var updateRef = await RunGitAsync(
            request.RepositoryPath,
            allowFailure: true,
            cancellationToken,
            "update-ref",
            $"refs/heads/{request.TargetBranch}",
            sessionCommit,
            request.ExpectedTargetCommit);

        if (updateRef.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Merge rejected while updating target branch atomically. {updateRef.FormatForError()}");
        }

        return new MergeResult(
            request.TargetBranch,
            request.ExpectedTargetCommit,
            sessionCommit,
            request.SessionBranch);
    }

    private static string BuildSessionBranchName(SessionId sessionId)
        => $"wip/session/{sessionId.Value}";

    private static async ValueTask<string> ResolveCommitAsync(string repositoryPath, string reference, CancellationToken cancellationToken)
    {
        var result = await RunGitOrThrowAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", reference);
        return result.StdOut;
    }

    private static async ValueTask<bool> IsAncestorAsync(
        string repositoryPath,
        string ancestorReference,
        string descendantReference,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            repositoryPath,
            allowFailure: true,
            cancellationToken,
            "merge-base",
            "--is-ancestor",
            ancestorReference,
            descendantReference);

        return result.ExitCode == 0;
    }

    private static string NormalizeDiff(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var normalizedLineEndings = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalizedLineEndings.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd(' ', '\t');
        }

        return string.Join("\n", lines).Trim();
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
            return path;

        return path + Path.DirectorySeparatorChar;
    }

    private static async ValueTask<GitCommandResult> RunGitOrThrowAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] args)
    {
        var result = await RunGitAsync(workingDirectory, allowFailure: true, cancellationToken, args);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git command failed. {result.FormatForError()}");
        }

        return result;
    }

    private static async ValueTask<GitCommandResult> RunGitAsync(
        string workingDirectory,
        bool allowFailure,
        CancellationToken cancellationToken,
        params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process already exited between cancellation and the kill.
            }

            throw;
        }

        var result = new GitCommandResult(
            process.ExitCode,
            (await stdOutTask).Trim(),
            (await stdErrTask).Trim(),
            args);

        if (!allowFailure && result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git command failed. {result.FormatForError()}");
        }

        return result;
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
    }

    private static void ValidateRequiredPath(string value, string parameterName)
    {
        ValidateRequired(value, parameterName);
        if (!Directory.Exists(value))
            throw new DirectoryNotFoundException($"Directory '{value}' was not found.");
    }

    private sealed record GitCommandResult(int ExitCode, string StdOut, string StdErr, IReadOnlyList<string> Args)
    {
        public string FormatForError()
            => $"Args='git {string.Join(" ", Args)}', ExitCode={ExitCode}, StdErr='{StdErr}', StdOut='{StdOut}'.";
    }
}
