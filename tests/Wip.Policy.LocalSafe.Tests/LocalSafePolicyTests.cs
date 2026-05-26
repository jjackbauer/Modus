using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Policy.LocalSafe;
using Xunit;

namespace Wip.Policy.LocalSafe.Tests;

public sealed class LocalSafePolicyTests : IDisposable
{
    private const string ChecklistItem = "Implement default local-safe policy profile enforcing workspace boundary, dangerous command deny-list, validation-before-approval, and approval-before-merge [depends on runtime operation policy checks]";
    private readonly string _worktreePath;
    private readonly LocalSafePolicy _policy = new();

    public LocalSafePolicyTests()
    {
        _worktreePath = Path.Combine(Path.GetTempPath(), $"modus-wip-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_worktreePath);
    }

    [Theory]
    [InlineData("rm -rf .")]
    [InlineData("del /f /q *.dll")]
    [InlineData("git clean -fdx")]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LocalSafePolicyReadme_GivenDangerousCommandPattern_EvaluateAsyncReturnsDenyDecisionWithDangerReason(string command)
    {
        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest(command, _worktreePath),
            BuildContext(operationName: "Wip.Tools.Shell.Execute"),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
        Assert.Contains("dangerous command pattern", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LocalSafePolicyReadme_GivenSafeCommandInsideWorktree_EvaluateAsyncReturnsAllowDecision()
    {
        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest("echo cleanup", _worktreePath),
            BuildContext(operationName: "Wip.Tools.Shell.Execute"),
            CancellationToken.None);

        Assert.True(decision.IsAllowed);
        Assert.Equal(string.Empty, decision.Reason);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LocalSafePolicyReadme_GivenWorkingDirectoryOutsideWorktree_EvaluateAsyncReturnsDenyDecision()
    {
        var outsidePath = Path.GetFullPath(Path.Combine(_worktreePath, ".."));

        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest("echo safe", outsidePath),
            BuildContext(operationName: "Wip.Tools.Shell.Execute"),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
        Assert.Contains("outside the active worktree boundary", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LocalSafePolicyReadme_GivenWorkingDirectoryInsideWorktree_EvaluateAsyncReturnsAllowDecision()
    {
        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest("echo safe", _worktreePath),
            BuildContext(operationName: "Wip.Tools.Shell.Execute"),
            CancellationToken.None);

        Assert.True(decision.IsAllowed);
        Assert.Equal(string.Empty, decision.Reason);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LocalSafePolicyReadme_GivenApproveOperationWithoutValidation_EvaluateAsyncDeniesUntilValidationEvidenceProvided()
    {
        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest(
                Command: "echo approve",
                WorkingDirectory: _worktreePath,
                ValidationSucceeded: false,
                ApprovalGranted: false),
            BuildContext(operationName: "Wip.Runtime.Approve"),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
        Assert.Contains("passing validation evidence", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LocalSafePolicyReadme_GivenApproveOperationWithValidation_EvaluateAsyncAllows()
    {
        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest(
                Command: "echo approve",
                WorkingDirectory: _worktreePath,
                ValidationSucceeded: true,
                ApprovalGranted: false),
            BuildContext(operationName: "Wip.Runtime.Approve"),
            CancellationToken.None);

        Assert.True(decision.IsAllowed);
        Assert.Equal(string.Empty, decision.Reason);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LocalSafePolicyReadme_GivenMergeOperationWithoutApproval_EvaluateAsyncDeniesUntilApprovalEvidenceProvided()
    {
        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest(
                Command: "echo merge",
                WorkingDirectory: _worktreePath,
                ValidationSucceeded: true,
                ApprovalGranted: false),
            BuildContext(operationName: "Wip.Runtime.Merge"),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
        Assert.Contains("explicit approval evidence", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LocalSafePolicyReadme_GivenMergeOperationWithoutValidation_EvaluateAsyncDeniesUntilValidationEvidenceProvided()
    {
        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest(
                Command: "echo merge",
                WorkingDirectory: _worktreePath,
                ValidationSucceeded: false,
                ApprovalGranted: true),
            BuildContext(operationName: "Wip.Runtime.Merge"),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
        Assert.Contains("passing validation evidence", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LocalSafePolicyReadme_GivenMergeOperationWithApprovalAndValidation_EvaluateAsyncAllows()
    {
        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest(
                Command: "echo safe",
                WorkingDirectory: _worktreePath,
                ValidationSucceeded: true,
                ApprovalGranted: true),
            BuildContext(operationName: "Wip.Runtime.Merge"),
            CancellationToken.None);

        Assert.True(decision.IsAllowed);
        Assert.Equal(string.Empty, decision.Reason);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_worktreePath))
                Directory.Delete(_worktreePath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private PolicyContext BuildContext(string operationName)
        => new(
            SessionId: new SessionId("session-001"),
            WorkflowId: new WorkflowId("workflow.linear"),
            WorktreePath: _worktreePath,
            OperationName: operationName);
}
