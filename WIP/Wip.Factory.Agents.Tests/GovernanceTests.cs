using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Wip.Governance;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class GovernanceTests
{
    private static readonly ReviewRequirement Requirement = new("Q-87", "2", ["AC-1", "AC-2"]);
    private static readonly ReviewBaseline Baseline = new(
        HeadCommitSha: "head-aaa",
        BaseCommitSha: "base-bbb",
        TargetBranch: "main",
        MandatoryPolicyVersion: "policy://factory/mandatory@3",
        GuidelinesVersion: "guidelines/system@12");

    private static readonly IReadOnlyDictionary<string, string> ToolVersions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dotnet-validation"] = "9.0.0",
            ["requirements-coverage"] = "1.0.0",
            ["architecture-conformance"] = "1.0.0",
            ["security"] = "1.0.0"
        };

    private static ReviewCheckResult Check(ReviewCheckKind kind, bool passed = true) =>
        new(kind, passed, passed ? "ok" : "failed");

    private static FactoryReviewArtifact AcceptedReview(bool securityPasses = true)
    {
        var service = new FactoryReviewService();
        var result = service.Evaluate(
            Requirement,
            Baseline,
            ToolVersions,
            [
                Check(ReviewCheckKind.Validation),
                Check(ReviewCheckKind.RequirementCoverage),
                Check(ReviewCheckKind.ArchitecturalConformance),
                Check(ReviewCheckKind.Security, securityPasses)
            ],
            [new ReviewFinding("F-1", "low", "naming")],
            [],
            "low",
            new AgentProfileVersion(1),
            new SessionId("review-session"));

        Assert.True(result.Accepted, result.Reason);
        return result.Artifact!;
    }

    [Fact]
    public void Review_GivenMissingCoverageOrConformanceResult_CannotBeAccepted()
    {
        var service = new FactoryReviewService();

        var missingCoverage = service.Evaluate(
            Requirement, Baseline, ToolVersions,
            [
                Check(ReviewCheckKind.Validation),
                Check(ReviewCheckKind.ArchitecturalConformance),
                Check(ReviewCheckKind.Security)
            ],
            [], [], "low", new AgentProfileVersion(1), new SessionId("s"));

        Assert.False(missingCoverage.Accepted);
        Assert.Equal(FactoryReviewService.MissingCoverageCategory, missingCoverage.Reason);
        Assert.Null(missingCoverage.Artifact);

        var missingConformance = service.Evaluate(
            Requirement, Baseline, ToolVersions,
            [
                Check(ReviewCheckKind.Validation),
                Check(ReviewCheckKind.RequirementCoverage),
                Check(ReviewCheckKind.Security)
            ],
            [], [], "low", new AgentProfileVersion(1), new SessionId("s"));

        Assert.False(missingConformance.Accepted);
        Assert.Equal(FactoryReviewService.MissingConformanceCategory, missingConformance.Reason);

        // Build and test alone is not a review.
        var validationOnly = service.Evaluate(
            Requirement, Baseline, ToolVersions,
            [Check(ReviewCheckKind.Validation), Check(ReviewCheckKind.Security)],
            [], [], "low", new AgentProfileVersion(1), new SessionId("s"));

        Assert.False(validationOnly.Accepted);
        Assert.Equal(FactoryReviewService.MissingCoverageCategory, validationOnly.Reason);

        // The complete set is accepted and reports a disposition.
        var complete = AcceptedReview();
        Assert.Equal("Accepted", complete.Disposition);
        Assert.StartsWith("sha256:", complete.ContentDigest, StringComparison.Ordinal);
        Assert.Equal(4, complete.Checks.Count);
        Assert.Equal(Requirement, complete.Requirement);
        Assert.Equal(Baseline.HeadCommitSha, complete.Baseline.HeadCommitSha);
        Assert.NotEmpty(complete.ToolVersions);
    }

    [Fact]
    public void Security_GivenFailingSecurityGate_BlocksApproval()
    {
        var service = new FactoryReviewService();

        var failing = service.Evaluate(
            Requirement, Baseline, ToolVersions,
            [
                Check(ReviewCheckKind.Validation),
                Check(ReviewCheckKind.RequirementCoverage),
                Check(ReviewCheckKind.ArchitecturalConformance),
                Check(ReviewCheckKind.Security, passed: false)
            ],
            [], [], "high", new AgentProfileVersion(1), new SessionId("s"));

        Assert.False(failing.Accepted);
        Assert.StartsWith(FactoryReviewService.SecurityGateFailedCategory, failing.Reason, StringComparison.Ordinal);
        Assert.Null(failing.Artifact);

        // A review with no security check at all is equally unacceptable.
        var missing = service.Evaluate(
            Requirement, Baseline, ToolVersions,
            [
                Check(ReviewCheckKind.Validation),
                Check(ReviewCheckKind.RequirementCoverage),
                Check(ReviewCheckKind.ArchitecturalConformance)
            ],
            [], [], "low", new AgentProfileVersion(1), new SessionId("s"));

        Assert.False(missing.Accepted);
        Assert.Equal(FactoryReviewService.SecurityGateMissingCategory, missing.Reason);

        // No approval can be created for a review that was never accepted.
        var approvals = new FactoryApprovalService();
        var (approval, reason) = approvals.Create(
            AcceptedReview() with { Disposition = "Rejected" },
            DateTimeOffset.UtcNow);

        Assert.Null(approval);
        Assert.Contains("review-disposition-not-accepted", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Approval_GivenAnyBoundFieldChanged_InvalidatesApproval()
    {
        var review = AcceptedReview();
        var approvals = new FactoryApprovalService();
        var (approval, reason) = approvals.Create(review, DateTimeOffset.UtcNow);

        Assert.NotNull(approval);
        Assert.Null(reason);
        Assert.Equal(review.ContentDigest, approval!.ReviewDigest);

        // Unchanged world: valid.
        Assert.True(approvals.Validate(approval, review, Baseline, Requirement).IsValid);

        // Each bound field, changed independently, must invalidate with its own reason.
        var mutations = new (string ExpectedReason, ReviewBaseline Baseline, ReviewRequirement Requirement, FactoryReviewArtifact Review)[]
        {
            ("head-commit-changed", Baseline with { HeadCommitSha = "head-zzz" }, Requirement, review),
            ("base-commit-changed", Baseline with { BaseCommitSha = "base-zzz" }, Requirement, review),
            ("requirements-version-changed", Baseline, Requirement with { Version = "3" }, review),
            ("mandatory-policy-version-changed", Baseline with { MandatoryPolicyVersion = "policy://factory/mandatory@4" }, Requirement, review),
            ("guidelines-version-changed", Baseline with { GuidelinesVersion = "guidelines/system@13" }, Requirement, review),
            ("target-branch-changed", Baseline with { TargetBranch = "release" }, Requirement, review),
            ("review-content-changed", Baseline, Requirement, review with { ContentDigest = "sha256:other" })
        };

        foreach (var mutation in mutations)
        {
            var validation = approvals.Validate(approval, mutation.Review, mutation.Baseline, mutation.Requirement);

            Assert.Equal(ApprovalStatus.Stale, validation.Status);
            Assert.Contains(mutation.ExpectedReason, validation.Reason, StringComparison.Ordinal);
        }

        // A missing approval is distinguished from a stale one.
        Assert.Equal(ApprovalStatus.Missing, approvals.Validate(null, review, Baseline, Requirement).Status);
    }

    [Fact]
    public async Task Merge_GivenNoOperatorApproval_IsUnreachable()
    {
        var review = AcceptedReview();
        var approvals = new FactoryApprovalService();
        var gate = new FactoryMergeGate(approvals);

        var mergeInvocations = 0;
        ValueTask Merge(CancellationToken _)
        {
            mergeInvocations++;
            return ValueTask.CompletedTask;
        }

        var (noApprovalMerged, noApprovalReason) = await gate.MergeAsync(
            null, review, Baseline, Requirement, Merge);
        Assert.False(noApprovalMerged);
        Assert.StartsWith(nameof(ApprovalStatus.Missing), noApprovalReason, StringComparison.Ordinal);

        // A stale approval must not merge either (target branch moved after approval).
        var (approval, _) = approvals.Create(review, DateTimeOffset.UtcNow);
        var (staleMerged, staleReason) = await gate.MergeAsync(
            approval, review, Baseline with { TargetBranch = "main-moved" }, Requirement, Merge);
        Assert.False(staleMerged);
        Assert.Contains("target-branch-changed", staleReason, StringComparison.Ordinal);

        Assert.Equal(0, mergeInvocations);

        // Only a valid approval reaches the merge action, and it does so exactly once.
        var (merged, mergeReason) = await gate.MergeAsync(approval, review, Baseline, Requirement, Merge);
        Assert.True(merged, mergeReason);
        Assert.Equal(1, mergeInvocations);
    }
}
