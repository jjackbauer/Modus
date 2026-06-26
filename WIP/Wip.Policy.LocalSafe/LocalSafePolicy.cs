using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using System.Text.RegularExpressions;

namespace Wip.Policy.LocalSafe;

public sealed record LocalSafePolicyRequest(
    string Command,
    string WorkingDirectory,
    bool ValidationSucceeded = false,
    bool ApprovalGranted = false,
    bool? RequireValidation = null,
    bool? RequireApproval = null);

public sealed class LocalSafePolicy : IPolicy<LocalSafePolicyRequest>
{
    private const string LocalSafePolicyName = "local-safe";
    private static readonly Regex GitInlineAliasPattern = new(
        "(?:^|\\s)-c\\s+alias\\.[A-Za-z0-9._-]+=(?<aliasValue>\"[^\"]+\"|'[^']+'|[^\\s&|;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GitConfigAliasPattern = new(
        "git\\s+config\\s+alias\\.[A-Za-z0-9._-]+\\s+(?<aliasValue>\"[^\"]+\"|'[^']+'|[^\\s&|;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public PolicyId PolicyId => new(LocalSafePolicyName);

    public ValueTask<PolicyDecision> EvaluateAsync(
        LocalSafePolicyRequest request,
        PolicyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Command))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(request.Command));

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(request.WorkingDirectory));

        if (!IsWithinWorktree(context.WorktreePath, request.WorkingDirectory))
        {
            return ValueTask.FromResult(DenyWithPayload(
                blockedAction: request.Command,
                reason: "Blocked by local-safe policy: command working directory resolves outside the active worktree boundary.",
                nextStepGuidance: "Use a working directory inside the active session worktree and retry the command."));
        }

        if (DangerousCommandDenylist.TryMatch(request.Command, out var dangerousMatch))
        {
            return ValueTask.FromResult(DenyWithPayload(
                blockedAction: request.Command,
                reason: DangerousCommandDenylist.BuildPolicyBlockedReason(dangerousMatch),
                nextStepGuidance: "Remove dangerous command tokens from the requested action, then retry with a safe command."));
        }

        if (TryResolveOutOfWorktreeTargetPath(request.Command, context.WorktreePath, request.WorkingDirectory, out var outOfWorktreeTargetPath))
        {
            return ValueTask.FromResult(DenyWithPayload(
                blockedAction: request.Command,
                reason: $"Blocked by local-safe policy: command target path '{outOfWorktreeTargetPath}' resolves outside the active worktree boundary and was denied before execution.",
                nextStepGuidance: "Use only target paths inside the active session worktree and retry the command."));
        }

        if (IsPrivilegedMergeCommandOnGenericToolPath(context.OperationName, request.Command))
        {
            return ValueTask.FromResult(DenyWithPayload(
                blockedAction: request.Command,
                reason: "Blocked by local-safe policy: merge semantics are privileged and must use the explicit merge command path with review, validation, and approval evidence.",
                nextStepGuidance: "Use the dedicated shell merge flow after running diff, validate, review, and approve."));
        }

        var requiresValidation = request.RequireValidation ?? RequiresValidationGate(context.OperationName);
        if (requiresValidation && !request.ValidationSucceeded)
        {
            return ValueTask.FromResult(DenyWithPayload(
                blockedAction: request.Command,
                reason: "Blocked by local-safe policy: passing validation evidence is required before this operation.",
                nextStepGuidance: "Run 'validate' and obtain passing evidence before retrying this operation."));
        }

        var requiresApproval = request.RequireApproval ?? RequiresApprovalGate(context.OperationName);
        if (requiresApproval && !request.ApprovalGranted)
        {
            return ValueTask.FromResult(DenyWithPayload(
                blockedAction: request.Command,
                reason: "Blocked by local-safe policy: explicit approval evidence is required before merge operations.",
                nextStepGuidance: "Run 'approve' and confirm the prompt before retrying merge."));
        }

        return ValueTask.FromResult(PolicyDecision.Allow());
    }
    private static bool RequiresValidationGate(string operationName)
        => operationName.Contains("Approve", StringComparison.OrdinalIgnoreCase)
            || operationName.Contains("Merge", StringComparison.OrdinalIgnoreCase)
            || operationName.Contains("RequireApproval", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresApprovalGate(string operationName)
        => operationName.Contains("Merge", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrivilegedMergeCommandOnGenericToolPath(string operationName, string command)
    {
        if (!operationName.Contains("Tools.Shell.Execute", StringComparison.OrdinalIgnoreCase))
            return false;

        var normalized = command.Trim();
        if (normalized.StartsWith("merge", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("git merge", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("git pull", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("git rebase", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("gh pr merge", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasPrivilegedMergeAliasDefinition(normalized);
    }

    private static bool HasPrivilegedMergeAliasDefinition(string command)
    {
        foreach (Match match in GitInlineAliasPattern.Matches(command))
        {
            if (AliasValueInvokesPrivilegedMergeSemantics(match.Groups["aliasValue"].Value))
                return true;
        }

        foreach (Match match in GitConfigAliasPattern.Matches(command))
        {
            if (AliasValueInvokesPrivilegedMergeSemantics(match.Groups["aliasValue"].Value))
                return true;
        }

        return false;
    }

    private static bool AliasValueInvokesPrivilegedMergeSemantics(string aliasValue)
    {
        var normalized = aliasValue.Trim().Trim('"', '\'').Trim();
        if (normalized.Length == 0)
            return false;

        if (normalized.StartsWith("!", StringComparison.Ordinal))
            normalized = normalized[1..].TrimStart();

        if (normalized.StartsWith("git ", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[4..].TrimStart();

        return normalized.StartsWith("merge", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("pull", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("rebase", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("gh pr merge", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithinWorktree(string worktreePath, string candidatePath)
    {
        var normalizedWorktree = EnsureTrailingSeparator(Path.GetFullPath(worktreePath));
        var normalizedCandidate = Path.GetFullPath(candidatePath);

        return IsWithinNormalizedWorktree(normalizedWorktree, normalizedCandidate);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
            return path;

        return path + Path.DirectorySeparatorChar;
    }

    private static bool TryResolveOutOfWorktreeTargetPath(
        string command,
        string worktreePath,
        string workingDirectory,
        out string outOfWorktreeTargetPath)
    {
        outOfWorktreeTargetPath = string.Empty;

        if (!LooksLikeMutationCommand(command))
            return false;

        var normalizedWorktree = EnsureTrailingSeparator(Path.GetFullPath(worktreePath));

        foreach (var token in TokenizeCommand(command))
        {
            if (!TryResolveCommandPathToken(token, workingDirectory, out var resolvedPath))
                continue;

            if (IsWithinNormalizedWorktree(normalizedWorktree, resolvedPath))
                continue;

            outOfWorktreeTargetPath = resolvedPath;
            return true;
        }

        return false;
    }

    private static bool LooksLikeMutationCommand(string command)
    {
        ReadOnlySpan<string> mutationTokens =
        [
            "copy ",
            "cp ",
            "move ",
            "mv ",
            "remove-item",
            "rm ",
            "del ",
            "erase ",
            "rmdir ",
            "rd ",
            "set-content",
            "add-content",
            "out-file",
            "git clean",
            "git checkout --",
            "git restore",
            "git update-ref",
            "git push",
            ">",
            ">>"
        ];

        foreach (var token in mutationTokens)
        {
            if (command.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> TokenizeCommand(string command)
    {
        foreach (Match match in Regex.Matches(command, "\"([^\"]*)\"|'([^']*)'|\\S+"))
        {
            var value = match.Value.Trim();
            if (value.Length == 0)
                continue;

            yield return value;
        }
    }

    private static bool TryResolveCommandPathToken(string token, string workingDirectory, out string resolvedPath)
    {
        resolvedPath = string.Empty;

        var normalizedToken = token.Trim().Trim('"', '\'').TrimEnd(',', ';');
        if (normalizedToken.Length == 0)
            return false;

        if (normalizedToken.Equals("&&", StringComparison.Ordinal)
            || normalizedToken.Equals("||", StringComparison.Ordinal)
            || normalizedToken.Equals("|", StringComparison.Ordinal)
            || normalizedToken.Equals(">", StringComparison.Ordinal)
            || normalizedToken.Equals(">>", StringComparison.Ordinal)
            || normalizedToken.Equals("1>", StringComparison.Ordinal)
            || normalizedToken.Equals("2>", StringComparison.Ordinal)
            || normalizedToken.StartsWith("-", StringComparison.Ordinal)
            || normalizedToken.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        var hasPathHints = Path.IsPathRooted(normalizedToken)
            || normalizedToken.StartsWith("..", StringComparison.Ordinal)
            || normalizedToken.Contains('\\', StringComparison.Ordinal)
            || normalizedToken.Contains('/', StringComparison.Ordinal);

        if (!hasPathHints)
            return false;

        resolvedPath = Path.IsPathRooted(normalizedToken)
            ? Path.GetFullPath(normalizedToken)
            : Path.GetFullPath(Path.Combine(workingDirectory, normalizedToken));

        return true;
    }

    private static bool IsWithinNormalizedWorktree(string normalizedWorktree, string normalizedCandidate)
    {
        if (string.Equals(
                normalizedWorktree.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                normalizedCandidate,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(normalizedWorktree, StringComparison.OrdinalIgnoreCase);
    }

    private static PolicyDecision DenyWithPayload(string blockedAction, string reason, string nextStepGuidance)
    {
        return PolicyDecision.Deny(
            PolicyViolationPayload.Create(
                    blockedAction,
                    LocalSafePolicyName,
                    nextStepGuidance,
                    reason)
                .ToDeterministicPayload());
    }
}
