# Checklist Transition Proof: LocalSafe Policy Rules

Date: 2026-05-24
Checklist item:

Document LocalSafe policy rules with explicit allow/deny examples for dangerous commands, worktree escape, validation gate, and approval gate [depends on Shell command documentation]

## Baseline Unchecked Source Text Evidence

Exact unchecked checklist text captured in-workspace:

- [ ] Document LocalSafe policy rules with explicit allow/deny examples for dangerous commands, worktree escape, validation gate, and approval gate [depends on Shell command documentation]

Deterministic source reference:

- .github/requirements/transition-proofs/baselines/checklist-item-document-localsafe-policy-rules.unchecked.txt:1

Deterministic fingerprint:

- Baseline unchecked text SHA256: B03F90C8695911CD44C34E82ADCAE2C52B9D089EF5BBD681B86BDBA781C8FABE

## Checked Completion Evidence

The checked checklist text for this exact item is:

- [x] Document LocalSafe policy rules with explicit allow/deny examples for dangerous commands, worktree escape, validation gate, and approval gate [depends on Shell command documentation] [transition-proof: .github/requirements/transition-proofs/checklist-item-document-localsafe-policy-rules-transition-proof-2026-05-24.md]

Deterministic workspace evidence:

- Requirements file checked line: .github/requirements/WIP.Contributor-Readmes.md:87
- Requirements line capture: - [x] Document LocalSafe policy rules with explicit allow/deny examples for dangerous commands, worktree escape, validation gate, and approval gate [depends on Shell command documentation] [transition-proof: .github/requirements/transition-proofs/checklist-item-document-localsafe-policy-rules-transition-proof-2026-05-24.md]
- Requirements SHA256: 1266E08D89D756FDD431A374E9961CB98DAFCFB5C329309FD8840CB57BDBF517

Supporting implementation and tests for this item:

- src/WIP.Contributor-Architecture.README.md documents explicit allow/deny examples for dangerous-command, worktree boundary, validation, and approval gates.
- tests/Wip.Policy.LocalSafe.Tests/LocalSafePolicyTests.cs contains behavior-proof tests:
  - LocalSafePolicyReadme_GivenDangerousCommandPattern_EvaluateAsyncReturnsDenyDecisionWithDangerReason
  - LocalSafePolicyReadme_GivenWorkingDirectoryOutsideWorktree_EvaluateAsyncReturnsDenyDecision
  - LocalSafePolicyReadme_GivenMergeOperationWithoutApproval_EvaluateAsyncDeniesUntilApprovalEvidenceProvided
  - LocalSafePolicyReadme_GivenApproveOperationWithoutValidation_EvaluateAsyncDeniesUntilValidationEvidenceProvided
  - LocalSafePolicyReadme_GivenSafeCommandInsideWorktree_EvaluateAsyncReturnsAllowDecision
  - LocalSafePolicyReadme_GivenWorkingDirectoryInsideWorktree_EvaluateAsyncReturnsAllowDecision
  - LocalSafePolicyReadme_GivenApproveOperationWithValidation_EvaluateAsyncAllows
  - LocalSafePolicyReadme_GivenMergeOperationWithApprovalAndValidation_EvaluateAsyncAllows
