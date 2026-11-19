using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Identifiers;

namespace Wip.Factory.Wip.Governance;

/// <summary>The four independent checks a review must execute (source §9.3, F-N.1, F-N.2).</summary>
public enum ReviewCheckKind
{
    /// <summary>Build and test validation.</summary>
    Validation = 1,

    /// <summary>Requirement-coverage validation.</summary>
    RequirementCoverage = 2,

    /// <summary>Architectural-conformance validation.</summary>
    ArchitecturalConformance = 3,

    /// <summary>Security gate. Present and passing is mandatory (F-N.2).</summary>
    Security = 4
}

/// <summary>Outcome of one check. A check that did not run is absent, never implicitly passing.</summary>
public sealed record ReviewCheckResult(ReviewCheckKind Kind, bool Passed, string Detail);

/// <summary>
/// Executed evidence for one mandatory review check (v0.4 FAC-P0-02). It carries who executed the check, the exact
/// reviewed revision, the policy it applied, the structured outcome, when it ran, and a digest of the artifact it
/// produced. A boolean or a human-readable summary alone is not evidence.
/// </summary>
public sealed record ReviewCheckExecution(
    ReviewCheckKind Kind,
    string CheckId,
    string ExecutedBy,
    string ExecutedVersion,
    string ReviewedSha,
    string PolicyVersion,
    string Outcome,
    string SubjectDigest,
    string ArtifactDigest,
    DateTimeOffset ExecutedAtUtc,
    string Detail)
{
    public const string PassedOutcome = "passed";
    public const string FailedOutcome = "failed";
    public const string SkippedOutcome = "skipped";
    public const string ErrorOutcome = "error";
    public const string NotApplicableOutcome = "not-applicable";

    public bool Passed => string.Equals(Outcome, PassedOutcome, StringComparison.Ordinal);

    /// <summary>A record satisfies the evidence contract only when every field needed to bind it is present.</summary>
    public bool IsComplete
        => !string.IsNullOrWhiteSpace(CheckId)
           && !string.IsNullOrWhiteSpace(ExecutedBy)
           && !string.IsNullOrWhiteSpace(ExecutedVersion)
           && !string.IsNullOrWhiteSpace(ReviewedSha)
           && !string.IsNullOrWhiteSpace(PolicyVersion)
           && !string.IsNullOrWhiteSpace(Outcome)
           && !string.IsNullOrWhiteSpace(SubjectDigest)
           && ArtifactDigest.StartsWith("sha256:", StringComparison.Ordinal)
           && ArtifactDigest.Length > "sha256:".Length
           && ExecutedAtUtc != default;
}

/// <summary>The version baseline an approval is bound to (F-F.4).</summary>
public sealed record ReviewBaseline(
    string HeadCommitSha,
    string BaseCommitSha,
    string TargetBranch,
    string MandatoryPolicyVersion,
    string GuidelinesVersion);

/// <summary>The requirement side of the binding.</summary>
public sealed record ReviewRequirement(
    string ArtifactId,
    string Version,
    IReadOnlyList<string> AcceptanceCriterionIds);

/// <summary>A finding with explicit severity and risk classification.</summary>
public sealed record ReviewFinding(string Id, string Severity, string Detail);

/// <summary>
/// The review artifact. It carries all six elements source §9.3 requires, plus the tool versions that
/// produced them, so a later reader can tell what was actually executed.
/// </summary>
public sealed record FactoryReviewArtifact(
    string ReviewId,
    ReviewRequirement Requirement,
    ReviewBaseline Baseline,
    IReadOnlyDictionary<string, string> ToolVersions,
    IReadOnlyList<ReviewCheckResult> Checks,
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<string> Exclusions,
    string RiskClassification,
    string Disposition,
    AgentProfileVersion ReviewerProfileVersion,
    SessionId ReviewerSessionId,
    string ContentDigest,
    IReadOnlyList<ReviewCheckExecution>? Executions = null);

/// <summary>Result of assembling a review. A rejected review is still an artifact; an incomplete one is not.</summary>
public sealed record ReviewBuildResult(bool Accepted, FactoryReviewArtifact? Artifact, string? Reason)
{
    public static ReviewBuildResult Refused(string reason) => new(false, null, reason);
}

/// <summary>
/// Assembles a review from executed checks. It refuses to produce an acceptable review when any of the
/// three independent checks is missing, or when the security gate did not run or failed (F-N.1, F-N.2).
/// </summary>
public sealed class FactoryReviewService
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public const string MissingValidationCategory = "review-incomplete:validation";
    public const string MissingCoverageCategory = "review-incomplete:requirement-coverage";
    public const string MissingConformanceCategory = "review-incomplete:architectural-conformance";
    public const string SecurityGateFailedCategory = "security-gate-failed";
    public const string SecurityGateMissingCategory = "security-gate-missing";

    /// <summary>An executed-evidence record is absent, incomplete, stale or failed (v0.4 FAC-P0-02).</summary>
    public const string EvidenceMissingCategory = "review-evidence-missing";
    public const string EvidenceIncompleteCategory = "review-evidence-incomplete";
    public const string EvidenceStaleCategory = "review-evidence-stale";
    public const string EvidenceDuplicateCategory = "review-evidence-duplicate";
    public const string EvidenceContractMismatchCategory = "review-evidence-contract-mismatch";
    public const string EvidenceCheckFailedCategory = "review-check-failed";

    public ReviewBuildResult Evaluate(
        ReviewRequirement requirement,
        ReviewBaseline baseline,
        IReadOnlyDictionary<string, string> toolVersions,
        IReadOnlyList<ReviewCheckResult> checks,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<string> exclusions,
        string riskClassification,
        AgentProfileVersion reviewerProfileVersion,
        SessionId reviewerSessionId)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(findings);

        var byKind = checks.ToDictionary(static check => check.Kind);

        if (!byKind.ContainsKey(ReviewCheckKind.Validation))
        {
            return ReviewBuildResult.Refused(MissingValidationCategory);
        }

        if (!byKind.ContainsKey(ReviewCheckKind.RequirementCoverage))
        {
            return ReviewBuildResult.Refused(MissingCoverageCategory);
        }

        if (!byKind.ContainsKey(ReviewCheckKind.ArchitecturalConformance))
        {
            return ReviewBuildResult.Refused(MissingConformanceCategory);
        }

        if (!byKind.TryGetValue(ReviewCheckKind.Security, out var security))
        {
            return ReviewBuildResult.Refused(SecurityGateMissingCategory);
        }

        if (!security.Passed)
        {
            return ReviewBuildResult.Refused(SecurityGateFailedCategory + ":" + security.Detail);
        }

        var disposition = byKind.Values.All(static check => check.Passed) ? "Accepted" : "Rejected";
        var reviewId = "review-" + Guid.NewGuid().ToString("N");

        var artifact = new FactoryReviewArtifact(
            reviewId,
            requirement,
            baseline,
            toolVersions,
            checks,
            findings,
            exclusions,
            riskClassification,
            disposition,
            reviewerProfileVersion,
            reviewerSessionId,
            DigestOf(reviewId, requirement, baseline, checks));

        return new ReviewBuildResult(true, artifact, null);
    }

    /// <summary>
    /// Assembles a review from <b>executed evidence</b>. Every mandatory check must be present exactly once, complete,
    /// bound to the exact reviewed revision, consistent with the frozen contract, and genuinely passing. A skipped,
    /// thrown, timed-out or differently-revisioned check is missing or failed — never passed (v0.4 FAC-P0-02).
    /// </summary>
    public ReviewBuildResult EvaluateWithEvidence(
        ReviewRequirement requirement,
        ReviewBaseline baseline,
        IReadOnlyDictionary<string, string> toolVersions,
        IReadOnlyList<ReviewCheckExecution> executions,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<string> exclusions,
        string riskClassification,
        AgentProfileVersion reviewerProfileVersion,
        SessionId reviewerSessionId,
        string requiredHeadSha,
        string requiredContractDigest)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(executions);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredHeadSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredContractDigest);

        var byKind = new Dictionary<ReviewCheckKind, ReviewCheckExecution>();
        foreach (var execution in executions)
        {
            if (!byKind.TryAdd(execution.Kind, execution))
            {
                return ReviewBuildResult.Refused(EvidenceDuplicateCategory + ":" + execution.Kind);
            }
        }

        foreach (var kind in Enum.GetValues<ReviewCheckKind>())
        {
            if (!byKind.TryGetValue(kind, out var execution))
            {
                return ReviewBuildResult.Refused(EvidenceMissingCategory + ":" + KindName(kind));
            }

            if (!execution.IsComplete)
            {
                return ReviewBuildResult.Refused(EvidenceIncompleteCategory + ":" + KindName(kind));
            }

            if (!string.Equals(execution.ReviewedSha, requiredHeadSha, StringComparison.Ordinal))
            {
                return ReviewBuildResult.Refused(EvidenceStaleCategory + ":" + KindName(kind));
            }

            if (kind == ReviewCheckKind.RequirementCoverage
                && !string.Equals(execution.SubjectDigest, requiredContractDigest, StringComparison.Ordinal))
            {
                return ReviewBuildResult.Refused(EvidenceContractMismatchCategory + ":" + KindName(kind));
            }

            if (!execution.Passed)
            {
                var category = kind == ReviewCheckKind.Security
                    ? SecurityGateFailedCategory
                    : EvidenceCheckFailedCategory + ":" + KindName(kind);
                return ReviewBuildResult.Refused(category + ":" + execution.Detail);
            }
        }

        var checks = executions
            .OrderBy(static execution => execution.Kind)
            .Select(static execution => new ReviewCheckResult(execution.Kind, execution.Passed, execution.Detail))
            .ToArray();

        var reviewId = "review-" + Guid.NewGuid().ToString("N");
        var ordered = executions.OrderBy(static execution => execution.Kind).ToArray();

        var artifact = new FactoryReviewArtifact(
            reviewId,
            requirement,
            baseline,
            toolVersions,
            checks,
            findings,
            exclusions,
            riskClassification,
            "Accepted",
            reviewerProfileVersion,
            reviewerSessionId,
            DigestOfEvidence(reviewId, requirement, baseline, ordered),
            ordered);

        return new ReviewBuildResult(true, artifact, null);
    }

    private static string KindName(ReviewCheckKind kind) => kind switch
    {
        ReviewCheckKind.Validation => "validation",
        ReviewCheckKind.RequirementCoverage => "requirement-coverage",
        ReviewCheckKind.ArchitecturalConformance => "architectural-conformance",
        ReviewCheckKind.Security => "security",
        _ => kind.ToString()
    };

    /// <summary>The digest that binds a merge approval to the exact executed evidence.</summary>
    public static string DigestOfEvidence(
        string reviewId,
        ReviewRequirement requirement,
        ReviewBaseline baseline,
        IReadOnlyList<ReviewCheckExecution> executions)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                reviewId,
                requirement = new { requirement.ArtifactId, requirement.Version, requirement.AcceptanceCriterionIds },
                baseline,
                executions = executions
                    .OrderBy(static execution => execution.Kind)
                    .Select(static execution => new
                    {
                        kind = execution.Kind.ToString(),
                        execution.CheckId,
                        execution.ExecutedBy,
                        execution.ExecutedVersion,
                        execution.ReviewedSha,
                        execution.PolicyVersion,
                        execution.Outcome,
                        execution.SubjectDigest,
                        execution.ArtifactDigest,
                        executedAtUtc = execution.ExecutedAtUtc.ToString("O")
                    })
                    .ToArray()
            },
            CanonicalOptions);

        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    /// <summary>The digest that binds an approval to this exact review content.</summary>
    public static string DigestOf(
        string reviewId,
        ReviewRequirement requirement,
        ReviewBaseline baseline,
        IReadOnlyList<ReviewCheckResult> checks)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                reviewId,
                requirement = new { requirement.ArtifactId, requirement.Version, requirement.AcceptanceCriterionIds },
                baseline,
                checks = checks
                    .OrderBy(static check => check.Kind)
                    .Select(static check => new { kind = check.Kind.ToString(), check.Passed })
                    .ToArray()
            },
            CanonicalOptions);

        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}

/// <summary>Why an approval is not currently valid.</summary>
public enum ApprovalStatus
{
    Valid = 1,
    Missing = 2,

    /// <summary>A bound field changed after the approval was issued (F-F.4).</summary>
    Stale = 3,

    /// <summary>The review did not pass its checks, so no approval may exist for it.</summary>
    ReviewNotAccepted = 4
}

/// <summary>A durable approval bound to a review and its baseline.</summary>
public sealed record FactoryApproval(
    string ApprovalId,
    string ReviewId,
    string ReviewDigest,
    ReviewBaseline Baseline,
    ReviewRequirement Requirement,
    DateTimeOffset IssuedAtUtc);

/// <summary>Result of validating an approval against the current world.</summary>
public sealed record ApprovalValidation(ApprovalStatus Status, string? Reason)
{
    public bool IsValid => Status == ApprovalStatus.Valid;
}

/// <summary>
/// Issues and validates approvals. An approval binds to the review digest, head and base commits, the
/// requirements version, the mandatory policy version and the guidelines version; any change to a bound
/// field invalidates it deterministically (F-F.4).
/// </summary>
public sealed class FactoryApprovalService
{
    public const string DispositionNotAccepted = "review-disposition-not-accepted";

    public (FactoryApproval? Approval, string? Reason) Create(
        FactoryReviewArtifact review,
        DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (!string.Equals(review.Disposition, "Accepted", StringComparison.Ordinal))
        {
            return (null, DispositionNotAccepted + ":" + review.Disposition);
        }

        return (new FactoryApproval(
            "approval-" + Guid.NewGuid().ToString("N"),
            review.ReviewId,
            review.ContentDigest,
            review.Baseline,
            review.Requirement,
            issuedAtUtc), null);
    }

    public ApprovalValidation Validate(
        FactoryApproval? approval,
        FactoryReviewArtifact? currentReview,
        ReviewBaseline currentBaseline,
        ReviewRequirement currentRequirement)
    {
        if (approval is null)
        {
            return new ApprovalValidation(ApprovalStatus.Missing, "no approval was issued");
        }

        if (currentReview is null)
        {
            return new ApprovalValidation(ApprovalStatus.Stale, "the reviewed artifact is no longer present");
        }

        // Each bound field is checked independently so the failure names the field that moved.
        if (!string.Equals(approval.Baseline.HeadCommitSha, currentBaseline.HeadCommitSha, StringComparison.Ordinal))
        {
            return new ApprovalValidation(ApprovalStatus.Stale, "head-commit-changed");
        }

        if (!string.Equals(approval.Baseline.BaseCommitSha, currentBaseline.BaseCommitSha, StringComparison.Ordinal))
        {
            return new ApprovalValidation(ApprovalStatus.Stale, "base-commit-changed");
        }

        if (!string.Equals(approval.Requirement.Version, currentRequirement.Version, StringComparison.Ordinal))
        {
            return new ApprovalValidation(ApprovalStatus.Stale, "requirements-version-changed");
        }

        if (!string.Equals(approval.Baseline.MandatoryPolicyVersion, currentBaseline.MandatoryPolicyVersion, StringComparison.Ordinal))
        {
            return new ApprovalValidation(ApprovalStatus.Stale, "mandatory-policy-version-changed");
        }

        if (!string.Equals(approval.Baseline.GuidelinesVersion, currentBaseline.GuidelinesVersion, StringComparison.Ordinal))
        {
            return new ApprovalValidation(ApprovalStatus.Stale, "guidelines-version-changed");
        }

        if (!string.Equals(approval.Baseline.TargetBranch, currentBaseline.TargetBranch, StringComparison.Ordinal))
        {
            return new ApprovalValidation(ApprovalStatus.Stale, "target-branch-changed");
        }

        if (!string.Equals(approval.ReviewDigest, currentReview.ContentDigest, StringComparison.Ordinal))
        {
            return new ApprovalValidation(ApprovalStatus.Stale, "review-content-changed");
        }

        return new ApprovalValidation(ApprovalStatus.Valid, null);
    }
}

/// <summary>
/// The merge gate. It refuses before the merge action runs, so an unauthorized merge has no side effects
/// (F-F.5). In the alpha the operator approval is mandatory and no policy may bypass it.
/// </summary>
public sealed class FactoryMergeGate
{
    public const string AlreadyAppliedCategory = "approval-already-applied";

    private readonly FactoryApprovalService _approvals;
    private readonly HashSet<string> _appliedApprovals = new(StringComparer.Ordinal);

    public FactoryMergeGate(FactoryApprovalService approvals)
        => _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));

    public async ValueTask<(bool Merged, string? Reason)> MergeAsync(
        FactoryApproval? approval,
        FactoryReviewArtifact? currentReview,
        ReviewBaseline currentBaseline,
        ReviewRequirement currentRequirement,
        Func<CancellationToken, ValueTask> mergeAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mergeAction);

        var validation = _approvals.Validate(approval, currentReview, currentBaseline, currentRequirement);
        if (!validation.IsValid)
        {
            return (false, validation.Status + ":" + validation.Reason);
        }

        // An approval authorizes exactly one merge. A duplicate event carrying the same approval must
        // not produce a second merge, release or promotion (F-N.4).
        if (!_appliedApprovals.Add(approval!.ApprovalId))
        {
            return (false, AlreadyAppliedCategory + ":" + approval.ApprovalId);
        }

        await mergeAction(cancellationToken).ConfigureAwait(false);
        return (true, null);
    }
}
