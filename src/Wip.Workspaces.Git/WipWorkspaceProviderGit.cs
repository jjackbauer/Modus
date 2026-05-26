using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Wip.Abstractions.Identifiers;

namespace Wip.Workspaces.Git;

public sealed record CreateWorkspaceRequest(
    string RepositoryPath,
    SessionId SessionId,
    string TargetBranch);

public sealed record WorkspaceSession(
    SessionId SessionId,
    string RepositoryPath,
    string WorktreePath,
    string SessionBranch,
    string TargetBranch,
    string TargetCommitAtCreation);

public sealed record DiffHashRequest(
    string RepositoryPath,
    string TargetBranch,
    string SessionBranch);

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

        var targetCommitAtCreation = await ResolveCommitAsync(
            request.RepositoryPath,
            request.TargetBranch,
            cancellationToken);

        var sessionBranch = BuildSessionBranchName(request.SessionId);
        var worktreePath = Path.Combine(request.RepositoryPath, ".wip", "worktrees", request.SessionId.Value);
        Directory.CreateDirectory(Path.GetDirectoryName(worktreePath) ?? request.RepositoryPath);

        var worktreeAdd = await RunGitAsync(
            request.RepositoryPath,
            allowFailure: true,
            cancellationToken,
            "worktree",
            "add",
            "-b",
            sessionBranch,
            worktreePath,
            request.TargetBranch);

        if (worktreeAdd.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to create worktree for session '{request.SessionId.Value}'. {worktreeAdd.FormatForError()}");
        }

        return new WorkspaceSession(
            request.SessionId,
            request.RepositoryPath,
            worktreePath,
            sessionBranch,
            request.TargetBranch,
            targetCommitAtCreation);
    }

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
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredPath(request.RepositoryPath, nameof(request.RepositoryPath));
        ValidateRequired(request.TargetBranch, nameof(request.TargetBranch));
        ValidateRequired(request.SessionBranch, nameof(request.SessionBranch));

        var diff = await RunGitOrThrowAsync(
            request.RepositoryPath,
            cancellationToken,
            "diff",
            "--no-color",
            "--ignore-cr-at-eol",
            "--no-ext-diff",
            $"{request.TargetBranch}...{request.SessionBranch}");

        var normalized = NormalizeDiff(diff.StdOut);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
            RedirectStandardError = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

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
