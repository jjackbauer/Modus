# Baseline Witness: Checklist Item 12 (Unchecked)

Date (UTC): 2026-05-26T18:31:21.6491721Z

## External Immutable Source Anchor

Source files (outside requirements artifacts):
- tests/Wip.Workspaces.Git.Tests/Git/WipWorkspaceProviderGitTests.cs
  - source SHA256: dabc3040b3d2c8aabd31aabaa9bc2b3de82ebc64c8819b52b3e42fb2d460c471
  - source LastWriteTimeUtc: 2026-05-26T18:25:04.1588726Z

Anchored source text proving merge-gate rejection paths:

- line 136: public async Task MergeAsync_GivenApprovedTokenAndTargetBranchDrift_RejectsMergeWithDeterministicReason()
- line 176: public async Task MergeAsync_GivenDiffChangedAfterApproval_RejectsMergeAndMarksApprovalStale()
- line 252: public async Task MergeAsync_GivenMissingValidationEvidence_RejectsMergeWithDeterministicReason()
- line 290: public async Task MergeAsync_GivenMissingReviewEvidence_RejectsMergeWithDeterministicReason()
- line 328: public async Task MergeAsync_GivenAbortedSession_RejectsMergeWithDeterministicReason()
- line 366: public async Task MergeAsync_GivenApprovalNotConfirmed_RejectsMergeWithDeterministicReason()

## Deterministic Unchecked Baseline Line

Normalization rule:
- UTF-8 text, LF line ending, no trailing spaces
- baseline line is formed as: "- [ ] " + checklist item text

Baseline unchecked line:

- [ ] Implement approval-gated merge flow that rejects stale diff, branch drift, missing validation, missing review, aborted session, and non-confirmed approval paths [depends on approval token flow and merge preview]

Baseline line SHA256:

cb75e676539f984c45909139e8d02d799987578eef83f242c7a6558e2b1d89ce

## Why This Is Independent

This witness does not rely on git history of .github/requirements/Wip.Shell-Builder-MVP-Next-Steps.md.
It anchors the baseline to executable behavior-proof source evidence and records a deterministic unchecked checklist line hash.