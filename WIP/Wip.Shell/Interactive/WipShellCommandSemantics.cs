using Wip.Abstractions.Sessions;

namespace Wip.Shell.CommandSemantics;

public static class WipShellCommandSemantics
{
    public const string RunExecutionBoundaryPhrase = "Plan and Run stages only (execution-only)";
    public const string RunDoesNotReplaceGovernancePhrase = "does not replace validate, review, approve, or merge";
    public const string GovernanceExplicitCommandsPhrase = "Governance lifecycle requires explicit validate, review, approve, and merge commands";
    public const string GovernanceEvidenceGapPhrase = "governanceEvidenceGap:";
    public const string TransitionNotShellCommandPhrase = "transition is a runtime API, not an interactive shell command";

    public static IReadOnlyDictionary<string, string> LifecycleCommandDescriptions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["plan"] = "Prepares the active session for execution by advancing Created to Editing. Does not execute workflow stages or complete governance.",
            ["run"] = $"Executes registered workflow {RunExecutionBoundaryPhrase}. {CapitalizeFirst(RunDoesNotReplaceGovernancePhrase)} and does not complete the governance lifecycle.",
            ["agent"] = "Runs the streaming agent loop for the active session: model deltas are printed as they arrive, tool calls and results are shown, and the final answer is recorded in session memory. It does not replace validate, review, approve, or merge.",
            ["validate"] = "Governance command: records validation evidence, updates ValidationStatus, and advances session state to Validating when prerequisites are met.",
            ["review"] = "Governance command: records review evidence for the current diff and advances session state toward approval.",
            ["approve"] = "Governance command: creates an approval-token artifact and sets ApprovalStatus when confirmation succeeds.",
            ["merge"] = "Governance command: merges approved work into the target branch and advances session state to Merged when required artifacts exist.",
            ["transition"] = "Runtime-only state transition API (TransitionAsync). Not exposed as a shell command; use plan, validate, review, approve, and merge instead."
        };

    public static string RenderHelpText()
    {
        var lines = new List<string>
        {
            "Available commands: help, init, repo, config, sessions, session start \"<task>\", session attach <session-id>, use workflow <id>, plan, run, agent \"<task>\", diff, checkpoint, validate, review, approve, merge, artifacts, status, detach, archive, abort, workflows, plugins [load|unload], debug-logs, exit, dotnet <restore|build|test|validate>",
            string.Empty,
            "Command semantics (authoritative):",
            $"- plan: {LifecycleCommandDescriptions["plan"]}",
            $"- run: {LifecycleCommandDescriptions["run"]}",
            $"- agent: {LifecycleCommandDescriptions["agent"]}",
            $"- validate: {LifecycleCommandDescriptions["validate"]}",
            $"- review: {LifecycleCommandDescriptions["review"]}",
            $"- approve: {LifecycleCommandDescriptions["approve"]}",
            $"- merge: {LifecycleCommandDescriptions["merge"]}",
            $"- transition: {LifecycleCommandDescriptions["transition"]}",
            string.Empty,
            $"{GovernanceExplicitCommandsPhrase}; run is execution-only and does not substitute for them."
        };

        return string.Join(Environment.NewLine, lines);
    }

    public static IReadOnlyList<string> BuildPostExecutionGovernanceGuidance(
        bool hasValidationArtifact,
        bool hasReviewArtifact,
        bool hasApprovalArtifact)
    {
        if (hasValidationArtifact && hasReviewArtifact && hasApprovalArtifact)
            return Array.Empty<string>();

        var lines = new List<string>
        {
            "lifecycleGuidance: run completed execution stages only; governance remains open.",
            GovernanceExplicitCommandsPhrase + "."
        };

        if (!hasValidationArtifact)
            lines.Add("next: run 'validate' to record validation evidence.");

        if (!hasReviewArtifact)
            lines.Add("next: run 'review' to record review evidence.");

        if (!hasApprovalArtifact)
            lines.Add("next: run 'approve' to create an approval token.");
        else
            lines.Add("next: run 'merge' when approval artifacts are current.");

        return lines;
    }

    public static bool RequiresGovernanceLifecycleGuidance(
        SessionValidationStatus validationStatus,
        SessionApprovalStatus approvalStatus,
        bool hasValidationArtifact,
        bool hasReviewArtifact,
        bool hasApprovalArtifact)
    {
        if (!hasValidationArtifact || !hasReviewArtifact || !hasApprovalArtifact)
            return true;

        return validationStatus != SessionValidationStatus.Passed
            || approvalStatus != SessionApprovalStatus.Approved;
    }

    public static IReadOnlyList<string> BuildGovernanceEvidenceGapLines(
        SessionState state,
        SessionValidationStatus validationStatus,
        SessionApprovalStatus approvalStatus,
        bool hasValidationArtifact,
        bool hasReviewSummaryArtifact,
        bool hasApprovalArtifact,
        bool hasMergeArtifact)
    {
        var lines = new List<string>();

        if (validationStatus == SessionValidationStatus.Passed && !hasValidationArtifact)
        {
            lines.Add($"{GovernanceEvidenceGapPhrase} validationStatus=Passed but validation-report artifact is missing.");
            lines.Add("recovery: run 'validate' to record validation evidence.");
        }

        if (approvalStatus == SessionApprovalStatus.Approved && !hasApprovalArtifact)
        {
            lines.Add($"{GovernanceEvidenceGapPhrase} approvalStatus=Approved but approval-token artifact is missing.");
            lines.Add("recovery: run 'approve' to create an approval token.");
        }

        if (ImpliesReviewSummaryEvidence(state, validationStatus) && !hasReviewSummaryArtifact)
        {
            lines.Add($"{GovernanceEvidenceGapPhrase} session state {state} implies review evidence but review-summary artifact is missing.");
            lines.Add("recovery: run 'review' to record review evidence.");
        }

        if (state == SessionState.Merged && !hasMergeArtifact)
        {
            lines.Add($"{GovernanceEvidenceGapPhrase} state=Merged but merge-result artifact is missing.");
            lines.Add("recovery: run 'merge' when governance prerequisites are satisfied.");
        }

        return lines;
    }

    private static bool ImpliesReviewSummaryEvidence(SessionState state, SessionValidationStatus validationStatus)
        => state is SessionState.Validating
            or SessionState.AwaitingApproval
            or SessionState.Approved
            or SessionState.Merged
            || validationStatus == SessionValidationStatus.Passed;

    private static string CapitalizeFirst(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (value.Length == 1)
            return value.ToUpperInvariant();

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}