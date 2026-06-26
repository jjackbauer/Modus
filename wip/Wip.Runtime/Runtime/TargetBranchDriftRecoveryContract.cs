using System.Text;
using Wip.Abstractions.Sessions;

namespace Wip.Runtime.Runtime;

public sealed record TargetBranchDriftRecoveryStep(
    string Command,
    SessionState? SessionStateAfterStep,
    string Description);

public sealed record TargetBranchDriftRecoveryContract(
    IReadOnlyList<TargetBranchDriftRecoveryStep> Steps,
    IReadOnlyList<SessionState> ObservableSessionStatesBeforeRetry)
{
    public string FormatGuidance()
    {
        var builder = new StringBuilder();
        builder.Append("Target branch drift recovery requires rebase, revalidate, and refresh before retrying merge.");
        builder.Append(' ');
        builder.Append("Required steps: ");
        builder.Append(string.Join(
            " -> ",
            Steps.Select(step => $"{step.Command} ({step.Description})")));
        builder.Append('.');
        builder.Append(' ');
        builder.Append("Observable session states before retry: ");
        builder.Append(string.Join(" -> ", ObservableSessionStatesBeforeRetry));
        builder.Append('.');
        return builder.ToString();
    }
}

public static class TargetBranchDriftRecovery
{
    private static readonly TargetBranchDriftRecoveryContract Contract = new(
        Steps:
        [
            new TargetBranchDriftRecoveryStep(
                Command: "rebase",
                SessionStateAfterStep: SessionState.Editing,
                Description: "Rebase the session worktree onto the refreshed target baseline."),
            new TargetBranchDriftRecoveryStep(
                Command: "diff",
                SessionStateAfterStep: SessionState.Editing,
                Description: "Refresh candidate diff evidence against the updated target head."),
            new TargetBranchDriftRecoveryStep(
                Command: "validate",
                SessionStateAfterStep: SessionState.Validating,
                Description: "Revalidate the refreshed candidate before requesting review."),
            new TargetBranchDriftRecoveryStep(
                Command: "review",
                SessionStateAfterStep: SessionState.AwaitingApproval,
                Description: "Refresh review evidence for the current diff hash."),
            new TargetBranchDriftRecoveryStep(
                Command: "approve",
                SessionStateAfterStep: SessionState.Approved,
                Description: "Refresh approval evidence bound to the refreshed target baseline."),
            new TargetBranchDriftRecoveryStep(
                Command: "merge",
                SessionStateAfterStep: SessionState.Merged,
                Description: "Retry merge only after the refreshed evidence is current.")
        ],
        ObservableSessionStatesBeforeRetry:
        [
            SessionState.Editing,
            SessionState.Validating,
            SessionState.AwaitingApproval,
            SessionState.Approved
        ]);

    public static TargetBranchDriftRecoveryContract Current => Contract;

    public static string FormatGuidance() => Contract.FormatGuidance();
}