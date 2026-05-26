using Wip.Abstractions.Identifiers;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class WipRuntimeApprovalTokenFactoryTests
{
    [Fact]
    public void Create_GivenValidInputs_BindsTokenToSessionDiffTargetWorkflowAndValidationReport()
    {
        var factory = new WipRuntimeApprovalTokenFactory();
        var producedAtUtc = new DateTimeOffset(2026, 5, 24, 12, 30, 0, TimeSpan.Zero);
        var request = BuildRequest(producedAtUtc: producedAtUtc);

        var token = factory.Create(request);

        Assert.False(string.IsNullOrWhiteSpace(token.Token));
        Assert.Equal(new SessionId("session-001"), token.Binding.SessionId);
        Assert.Equal(new WorkflowId("workflow.linear"), token.Binding.WorkflowId);
        Assert.Equal("diff-123", token.Binding.DiffHash);
        Assert.Equal("main", token.Binding.TargetBranch);
        Assert.Equal("abc123def456", token.Binding.TargetCommit);
        Assert.Equal(new ArtifactId("validation-report-001"), token.Binding.ValidationReportArtifactId);
        Assert.Equal("diff-123", token.Binding.ValidationReportDiffHash);
        Assert.Equal(producedAtUtc, token.ProducedAtUtc);
        Assert.Equal(WipRuntimeApprovalTokenFactory.ComputeBindingHash(token.Binding), token.BindingHash);
    }

    [Fact]
    public void Create_GivenValidationDidNotPass_ThrowsDeterministicError()
    {
        var factory = new WipRuntimeApprovalTokenFactory();
        var request = BuildRequest(validation: new ApprovalValidationReport(
            ReportArtifactId: new ArtifactId("validation-report-001"),
            BuildSucceeded: true,
            TestSucceeded: false,
            DiffHash: "diff-123"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Create(request));

        Assert.Contains("requires a passing validation report", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_GivenValidationDiffHashMismatch_ThrowsDeterministicError()
    {
        var factory = new WipRuntimeApprovalTokenFactory();
        var request = BuildRequest(validation: new ApprovalValidationReport(
            ReportArtifactId: new ArtifactId("validation-report-001"),
            BuildSucceeded: true,
            TestSucceeded: true,
            DiffHash: "different-diff"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Create(request));

        Assert.Contains("validation report diff hash", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_GivenReviewDiffHashMismatch_ThrowsDeterministicError()
    {
        var factory = new WipRuntimeApprovalTokenFactory();
        var request = BuildRequest(review: new ApprovalReviewReport(
            ReportArtifactId: new ArtifactId("review-report-001"),
            ReviewedDiffHash: "different-diff",
            IsStale: false));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Create(request));

        Assert.Contains("reviewed diff hash", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_GivenReviewAlreadyMarkedStale_ThrowsDeterministicError()
    {
        var factory = new WipRuntimeApprovalTokenFactory();
        var request = BuildRequest(review: new ApprovalReviewReport(
            ReportArtifactId: new ArtifactId("review-report-001"),
            ReviewedDiffHash: "diff-123",
            IsStale: true,
            StaleReason: "Validation report is stale."));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Create(request));

        Assert.Contains("review report is stale", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Validation report is stale", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeBindingHash_GivenDifferentBoundCommit_ReturnsDifferentHash()
    {
        var factory = new WipRuntimeApprovalTokenFactory();
        var baseline = factory.Create(BuildRequest(producedAtUtc: new DateTimeOffset(2026, 5, 24, 12, 30, 0, TimeSpan.Zero)));
        var changed = factory.Create(BuildRequest(
            targetCommit: "fff999000111",
            producedAtUtc: new DateTimeOffset(2026, 5, 24, 12, 30, 0, TimeSpan.Zero)));

        Assert.NotEqual(baseline.BindingHash, changed.BindingHash);
    }

    private static ApprovalTokenRequest BuildRequest(
        ApprovalReviewReport? review = null,
        ApprovalValidationReport? validation = null,
        string targetCommit = "abc123def456",
        DateTimeOffset? producedAtUtc = null)
        => new(
            SessionId: new SessionId("session-001"),
            WorkflowId: new WorkflowId("workflow.linear"),
            DiffHash: "diff-123",
            TargetBranch: "main",
            TargetCommit: targetCommit,
            ReviewReport: review ?? new ApprovalReviewReport(
                ReportArtifactId: new ArtifactId("review-report-001"),
                ReviewedDiffHash: "diff-123",
                IsStale: false),
            ValidationReport: validation ?? new ApprovalValidationReport(
                ReportArtifactId: new ArtifactId("validation-report-001"),
                BuildSucceeded: true,
                TestSucceeded: true,
                DiffHash: "diff-123"),
            ProducedAtUtc: producedAtUtc);
}