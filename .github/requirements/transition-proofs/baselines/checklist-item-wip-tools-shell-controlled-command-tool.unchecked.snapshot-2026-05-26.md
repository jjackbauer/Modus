# Baseline Witness: Checklist Item 13 (Unchecked)

Date (UTC): 2026-05-26T18:49:50.2224948Z

## External Immutable Source Anchor

Source files (outside requirements artifacts):
- tests/Wip.Runtime.Tests/Runtime/RuntimeToolGatewayTests.cs
- source SHA256: b8af85caf53825d18ea14654de2959ab4f96db60cc8d73404b630fba4458ab51
- source LastWriteTimeUtc: 2026-05-26T18:40:17.9453675Z
- src/Wip.Tools.Shell/Shell/ShellCommandTool.cs
- source SHA256: 7583d3a093ea4677713f8a1400df6d337be8fc44f4a8b9378118c25b8d637718
- source LastWriteTimeUtc: 2026-05-26T18:39:27.4292291Z

Anchored source text proving dangerous-pattern deny, worktree boundary deny, and command log artifact behavior:

- tests/Wip.Runtime.Tests/Runtime/RuntimeToolGatewayTests.cs line 46: public async Task InvokeAsync_GivenDangerousCommandPattern_ExpectedPolicyDeniesBeforeExecutionAndLogsReason()
- tests/Wip.Runtime.Tests/Runtime/RuntimeToolGatewayTests.cs line 73: public async Task InvokeAsync_GivenWorkingDirectoryOutsideSessionWorktree_ExpectedPolicyDeniesPathBoundaryViolation()
- tests/Wip.Runtime.Tests/Runtime/RuntimeToolGatewayTests.cs line 101: public async Task InvokeAsync_GivenAllowedCommandInsideWorktree_ExpectedCommandExecutesAndProducesExecutionLogArtifact()
- src/Wip.Tools.Shell/Shell/ShellCommandTool.cs line 67: var policyDecision = await _policy.EvaluateAsync(
- src/Wip.Tools.Shell/Shell/ShellCommandTool.cs line 66: var workingDirectory = ResolveWorkingDirectory(context.WorktreePath, request.RelativeWorkingDirectory);
- src/Wip.Tools.Shell/Shell/ShellCommandTool.cs line 200: private async ValueTask<ArtifactDescriptor> SaveExecutionArtifactAsync(

## Deterministic Unchecked Baseline Line

Normalization rule:
- UTF-8 text, LF line ending, no trailing spaces
- baseline line is formed as: "- [ ] " + checklist item text

Baseline unchecked line:

- [ ] Implement Wip.Tools.Shell controlled command tool constrained to active worktree, denied dangerous patterns, and command execution log artifact production [depends on local-safe policy and runtime tool gateway]

Baseline line SHA256:

124c97e1d13974dc3606e0135b1f56a9a32326dbd2bc46d4adf88e55e17a8b7b

## Why This Is Independent

This witness does not rely on git history of .github/requirements/Wip.Shell-Builder-MVP-Next-Steps.md.
It anchors the baseline to executable behavior-proof source evidence and records a deterministic unchecked checklist line hash.
