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
    string ContentDigest);

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
