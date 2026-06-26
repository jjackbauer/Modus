using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Policy.LocalSafe;
using Xunit;

namespace Wip.Policy.LocalSafe.Tests;

public sealed class LocalSafePolicyTests : IDisposable
{
    private const string ChecklistItem = "Implement default local-safe policy profile enforcing workspace boundary, dangerous command deny-list, validation-before-approval, and approval-before-merge [depends on runtime operation policy checks]";
    private const string MergeIsolationChecklistItem = "Preserve privileged merge isolation so generic shell tool paths cannot trigger merge semantics or bypass approval/validation gates [depends on policy/tool hardening]";
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
    [InlineData("erase /f /q *.log")]
    [InlineData("rmdir /s /q temp")]
    [InlineData("rd /s /q cache")]
    [InlineData("Remove-Item -Recurse -Force .\\bin")]
    [InlineData("git push origin main")]
    [InlineData("git clean -fdx")]
    [InlineData("echo hacked > .git/config")]
    [InlineData("git reset --hard HEAD~1")]
    [InlineData("mkfs.ext4 /dev/sda1")]
    [InlineData("format c:")]
    [InlineData("dd if=/dev/zero of=/dev/sda")]
    [InlineData("wipefs -a /dev/sda")]
    [InlineData("diskpart /s wipe.txt")]
    [InlineData(":(){:|:&};:")]
    [InlineData("shutdown /s /t 0")]
    [InlineData("reboot")]
    [InlineData("halt")]
    [InlineData("poweroff")]
    [InlineData("init 0")]
    [InlineData("init 6")]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LocalSafePolicyReadme_GivenMvpDangerousCommandPattern_EvaluateAsyncReturnsDenyDecisionWithDangerReason(string command)
    {
        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest(command, _worktreePath),
            BuildContext(operationName: "Wip.Tools.Shell.Execute"),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
        Assert.Contains("dangerous command rule", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("denied before execution", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("rm -rf .")]
    [InlineData("git push origin main")]
    [InlineData("git reset --hard HEAD~1")]
    [InlineData("echo hacked > .git/config")]
    [InlineData("Remove-Item -Recurse -Force .\\bin")]
    [InlineData("shutdown /s /t 0")]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LocalSafePolicyReadme_GivenDangerousCommandPattern_EvaluateAsyncReturnsDeterministicReason(string command)
    {
        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest(command, _worktreePath),
            BuildContext(operationName: "Wip.Tools.Shell.Execute"),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
        Assert.True(DangerousCommandDenylist.TryMatch(command, out var expectedMatch));

        var payload = AssertPolicyViolationPayload(decision.Reason);
        Assert.Equal(command, payload.BlockedAction);
        Assert.Equal("local-safe", payload.BlockingPolicy);
        Assert.Contains("Remove dangerous command tokens", payload.NextStepGuidance, StringComparison.Ordinal);
        Assert.Equal(DangerousCommandDenylist.BuildPolicyBlockedReason(expectedMatch), payload.Reason);
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
    public async Task LocalSafePolicyReadme_GivenMutationCommandWithOutOfWorktreeTargetPath_EvaluateAsyncReturnsDenyDecision()
    {
        var outsideTargetPath = Path.GetFullPath(Path.Combine(_worktreePath, "..", "outside-target.txt"));
        var command = $"copy README.md \"{outsideTargetPath}\"";

        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest(command, _worktreePath),
            BuildContext(operationName: "Wip.Tools.Shell.Execute"),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);

        var payload = AssertPolicyViolationPayload(decision.Reason);
        Assert.Equal(command, payload.BlockedAction);
        Assert.Equal("local-safe", payload.BlockingPolicy);
        Assert.Contains("inside the active session worktree", payload.NextStepGuidance, StringComparison.Ordinal);
        Assert.Contains("target path", payload.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outside the active worktree boundary", payload.Reason, StringComparison.OrdinalIgnoreCase);
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

    [Theory]
    [InlineData("merge --ff-only")] 
    [InlineData("git merge feature/session")]
    [InlineData("git pull --rebase")]
    [Trait("ChecklistItem", MergeIsolationChecklistItem)]
    public async Task MergeIsolation_GivenGenericShellExecutePathWithMergeSemantics_EvaluateAsyncDeniesWithDeterministicReason(string command)
    {
        var decision = await _policy.EvaluateAsync(
            new LocalSafePolicyRequest(
                Command: command,
                WorkingDirectory: _worktreePath,
                ValidationSucceeded: true,
                ApprovalGranted: true,
                RequireValidation: false,
                RequireApproval: false),
            BuildContext(operationName: "Wip.Tools.Shell.Execute"),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);

        var payload = AssertPolicyViolationPayload(decision.Reason);
        Assert.Equal(command, payload.BlockedAction);
        Assert.Equal("local-safe", payload.BlockingPolicy);
        Assert.Contains("dedicated shell merge flow", payload.NextStepGuidance, StringComparison.Ordinal);
        Assert.Equal(
            "Blocked by local-safe policy: merge semantics are privileged and must use the explicit merge command path with review, validation, and approval evidence.",
            payload.Reason);
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

    private static PolicyViolationPayload AssertPolicyViolationPayload(string payloadText)
    {
        Assert.True(PolicyViolationPayload.TryParse(payloadText, out var payload));
        Assert.NotNull(payload);
        return payload!;
    }
}
