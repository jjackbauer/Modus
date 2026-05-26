using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;

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
    private static readonly string[] DangerousCommandPatterns =
    [
        "rm -rf",
        "del /f /q",
        "rmdir /s /q",
        "git clean -fdx",
        "mkfs",
        "format ",
        "dd if=",
        "shutdown",
        "reboot",
        "halt",
        ":(){:|:&};:"
    ];

    public PolicyId PolicyId => new("local-safe");

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
            return ValueTask.FromResult(PolicyDecision.Deny(
                "Blocked by local-safe policy: command working directory resolves outside the active worktree boundary."));
        }

        if (ContainsDangerousCommandPattern(request.Command))
        {
            return ValueTask.FromResult(PolicyDecision.Deny(
                "Blocked by local-safe policy: dangerous command pattern detected and denied."));
        }

        var requiresValidation = request.RequireValidation ?? RequiresValidationGate(context.OperationName);
        if (requiresValidation && !request.ValidationSucceeded)
        {
            return ValueTask.FromResult(PolicyDecision.Deny(
                "Blocked by local-safe policy: passing validation evidence is required before this operation."));
        }

        var requiresApproval = request.RequireApproval ?? RequiresApprovalGate(context.OperationName);
        if (requiresApproval && !request.ApprovalGranted)
        {
            return ValueTask.FromResult(PolicyDecision.Deny(
                "Blocked by local-safe policy: explicit approval evidence is required before merge operations."));
        }

        return ValueTask.FromResult(PolicyDecision.Allow());
    }

    private static bool ContainsDangerousCommandPattern(string command)
        => DangerousCommandPatterns.Any(pattern =>
            command.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static bool RequiresValidationGate(string operationName)
        => operationName.Contains("Approve", StringComparison.OrdinalIgnoreCase)
            || operationName.Contains("Merge", StringComparison.OrdinalIgnoreCase)
            || operationName.Contains("RequireApproval", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresApprovalGate(string operationName)
        => operationName.Contains("Merge", StringComparison.OrdinalIgnoreCase);

    private static bool IsWithinWorktree(string worktreePath, string candidatePath)
    {
        var normalizedWorktree = EnsureTrailingSeparator(Path.GetFullPath(worktreePath));
        var normalizedCandidate = Path.GetFullPath(candidatePath);

        if (string.Equals(
                normalizedWorktree.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                normalizedCandidate,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(normalizedWorktree, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
            return path;

        return path + Path.DirectorySeparatorChar;
    }
}
