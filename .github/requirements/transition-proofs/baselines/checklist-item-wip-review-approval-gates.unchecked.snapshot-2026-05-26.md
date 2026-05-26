# Baseline Witness: Checklist Item 11 (Unchecked)

Date (UTC): 2026-05-26T18:19:00.0000000Z

## External Immutable Source Anchor

Source files (outside requirements artifacts):
- tests/Wip.Runtime.Tests/Runtime/WipRuntimeReviewGeneratorTests.cs
  - source SHA256: b686b37ff3a00cf6ea162bf5914e8fc976af5d252cf854a4ebe3b95e159d8b6f
  - source LastWriteTimeUtc: 2026-05-26T18:13:31.1606580Z
- tests/Wip.Runtime.Tests/Runtime/WipRuntimeApprovalTokenFactoryTests.cs
  - source SHA256: d5069dfb51a4209e148032d9263b3bbdd69d04fd934dc22ebbb5122ea959abf0
  - source LastWriteTimeUtc: 2026-05-26T18:13:25.0611317Z

Anchored source text proving staleness detection and token binding behavior:

- line 36: public async Task ReviewAsync_GivenValidationDiffHashMismatch_MarksReviewAsStaleWithDeterministicReason()
- line 22: Assert.Equal("main", token.Binding.TargetBranch);
- line 23: Assert.Equal("abc123def456", token.Binding.TargetCommit);

## Deterministic Unchecked Baseline Line

Normalization rule:
- UTF-8 text, LF line ending, no trailing spaces
- baseline line is formed as: "- [ ] " + checklist item text

Baseline unchecked line:

- [ ] Implement review and approval gates: review report generation with staleness detection and approval token generation bound to diff hash, target branch, and target commit [depends on diff hash and validation report availability]

Baseline line SHA256:

8d40421e60ae10e047645e8924d8109ffa1ea1eb475ec2770a232ae1194b72d9

## Why This Is Independent

This witness does not rely on git history of .github/requirements/Wip.Shell-Builder-MVP-Next-Steps.md.
It anchors the baseline to executable behavior-proof source evidence and records a deterministic unchecked checklist line hash.