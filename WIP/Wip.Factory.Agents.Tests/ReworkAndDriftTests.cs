using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Wip.Governance;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class ReworkAndDriftTests
{
    private static readonly ReviewRequirement Requirement = new("Q-87", "2", ["AC-1"]);
    private static readonly ReviewBaseline Baseline = new("head-aaa", "base-bbb", "main", "policy@3", "guidelines@12");
    private static readonly WorkItemId Item = new("W-1042");

    private static FactoryReviewArtifact Review(bool accept, string reviewId)
    {
        var service = new FactoryReviewService();
        var result = service.Evaluate(
            Requirement,
            Baseline,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["dotnet-validation"] = "9.0.0" },
            [
                new ReviewCheckResult(ReviewCheckKind.Validation, accept, accept ? "ok" : "2 tests failed"),
                new ReviewCheckResult(ReviewCheckKind.RequirementCoverage, true, "covered"),
                new ReviewCheckResult(ReviewCheckKind.ArchitecturalConformance, true, "conformant"),
                new ReviewCheckResult(ReviewCheckKind.Security, true, "clean")
            ],
            accept ? [] : [new ReviewFinding("F-9", "medium", "failing test")],
            [],
            accept ? "low" : "medium",
            new AgentProfileVersion(1),
            new SessionId("review-session"));

        Assert.True(result.Accepted, result.Reason);
        var artifact = result.Artifact!;

        // Renaming a review must re-derive its digest; a digest that no longer matches its content would
        // make the approval binding meaningless.
        return artifact with
        {
            ReviewId = reviewId,
            ContentDigest = FactoryReviewService.DigestOf(
                reviewId,
                artifact.Requirement,
                artifact.Baseline,
                artifact.Checks)
        };
    }

    private static WorkItemSnapshot ItemIn(FactoryWorkState state, int version) =>
        new(Item, state, version, "modus", [], DateTimeOffset.UnixEpoch);

    [Fact]
    public void Review_GivenAcceptedReview_RecordsAllSixRequiredElements()
    {
        var review = Review(accept: true, reviewId: "review-1");

        // Source §9.3: requirement version with criterion ids, policy/guidelines version, head and
        // base commits, tool and validator versions with results, findings and disposition, reviewer
        // provenance.
        Assert.Equal("Q-87", review.Requirement.ArtifactId);
        Assert.Equal("2", review.Requirement.Version);
        Assert.Equal(["AC-1"], review.Requirement.AcceptanceCriterionIds);

        Assert.Equal("policy@3", review.Baseline.MandatoryPolicyVersion);
        Assert.Equal("guidelines@12", review.Baseline.GuidelinesVersion);

        Assert.Equal("head-aaa", review.Baseline.HeadCommitSha);
        Assert.Equal("base-bbb", review.Baseline.BaseCommitSha);
        Assert.Equal("main", review.Baseline.TargetBranch);

        Assert.Equal("9.0.0", review.ToolVersions["dotnet-validation"]);
        Assert.Equal(4, review.Checks.Count);
        Assert.All(review.Checks, static check => Assert.True(check.Passed, check.Kind.ToString()));

        Assert.Equal("Accepted", review.Disposition);
        Assert.Equal("low", review.RiskClassification);

        Assert.Equal(1, review.ReviewerProfileVersion.Value);
        Assert.Equal("review-session", review.ReviewerSessionId.Value);
        Assert.StartsWith("sha256:", review.ContentDigest, StringComparison.Ordinal);

        // The digest is a function of the reviewed content, so an identical review hashes identically.
        var same = FactoryReviewService.DigestOf(review.ReviewId, review.Requirement, review.Baseline, review.Checks);
        Assert.Equal(review.ContentDigest, same);
    }

    [Fact]
    public void Rework_GivenRejectedReview_RedispatchesImplementationAndPreservesBothReviews()
    {
        var ledger = new FactoryReviewLedger();
        var policy = new FactoryReworkPolicy();
        var item = ItemIn(FactoryWorkState.InReview, version: 3);

        var rejected = Review(accept: false, reviewId: "review-1");
        ledger.Record(Item, rejected);

        var decision = policy.Decide(rejected, item, ledger, maxAttempts: 3);
        Assert.Equal(FactoryWorkState.NeedsRework, decision.NextState);
        Assert.True(decision.RedispatchImplementation);
        Assert.Equal(FactoryReworkPolicy.ReviewRejectedReason, decision.Reason);

        var (committed, transition) = policy.ApplyRejection(item);
        Assert.True(transition.Succeeded, transition.RejectionReason);
        Assert.NotNull(committed);

        // The item is genuinely back in implementation, not parked in a state nothing dispatches from.
        Assert.Equal(FactoryWorkState.InImplementation, committed!.State);
        Assert.Equal(5, committed.Version);

        // A second cycle produces a second review; the first is preserved, not replaced.
        var accepted = Review(accept: true, reviewId: "review-2");
        ledger.Record(Item, accepted);

        Assert.Equal(2, ledger.ReviewsOf(Item).Count);
        Assert.Contains(ledger.ReviewsOf(Item), static review => review.ReviewId == "review-1");
        Assert.Contains(ledger.ReviewsOf(Item), static review => review.ReviewId == "review-2");
        Assert.Equal(1, ledger.ReworkAttempts(Item));

        var second = policy.Decide(accepted, committed, ledger, maxAttempts: 3);
        Assert.Equal(FactoryWorkState.ReadyForDelivery, second.NextState);
        Assert.False(second.RedispatchImplementation);

        // The attempt bound is enforced rather than looping forever.
        var boundLedger = new FactoryReviewLedger();
        for (var i = 0; i < 3; i++)
        {
            boundLedger.Record(Item, Review(accept: false, reviewId: "reject-" + i));
        }

        var exhausted = policy.Decide(Review(accept: false, reviewId: "reject-final"), item, boundLedger, maxAttempts: 3);
        Assert.Equal(FactoryWorkState.Failed, exhausted.NextState);
        Assert.Equal(FactoryReworkPolicy.ReworkExhaustedReason, exhausted.Reason);
    }

    [Fact]
    public async Task Merge_GivenTargetBranchMovedAfterApproval_InvalidatesEvidenceInsteadOfRetrying()
    {
        var review = Review(accept: true, reviewId: "review-1");
        var approvals = new FactoryApprovalService();
        var gate = new FactoryMergeGate(approvals);
        var (approval, _) = approvals.Create(review, DateTimeOffset.UtcNow);

        var mergeInvocations = 0;
        ValueTask Merge(CancellationToken _)
        {
            mergeInvocations++;
            return ValueTask.CompletedTask;
        }

        // The target branch moves after approval: the evidence is invalidated, not silently retried.
        var movedBaseline = Baseline with { BaseCommitSha = "base-ccc" };

        var (firstMerged, firstReason) = await gate.MergeAsync(approval, review, movedBaseline, Requirement, Merge);
        Assert.False(firstMerged);
        Assert.Contains("base-commit-changed", firstReason, StringComparison.Ordinal);
        Assert.Equal(0, mergeInvocations);

        // A second attempt with the same stale approval refuses again: there is no automatic re-approval
        // and no retry loop that could eventually slip the change through.
        var (secondMerged, secondReason) = await gate.MergeAsync(approval, review, movedBaseline, Requirement, Merge);
        Assert.False(secondMerged);
        Assert.Contains("base-commit-changed", secondReason, StringComparison.Ordinal);
        Assert.Equal(0, mergeInvocations);

        // Fresh verification (a new review bound to the moved baseline plus a new approval) is required.
        var revalidated = Review(accept: true, reviewId: "review-2") with
        {
            Baseline = movedBaseline
        };
        var (freshApproval, _) = approvals.Create(revalidated, DateTimeOffset.UtcNow);

        var (finallyMerged, finallyReason) = await gate.MergeAsync(freshApproval, revalidated, movedBaseline, Requirement, Merge);
        Assert.True(finallyMerged, finallyReason);
        Assert.Equal(1, mergeInvocations);
    }
}
