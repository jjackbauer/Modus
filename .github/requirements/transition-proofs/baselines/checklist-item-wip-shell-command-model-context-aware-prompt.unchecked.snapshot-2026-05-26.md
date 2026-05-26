# Baseline Witness: Checklist Item 4 (Unchecked)

Date (UTC): 2026-05-26T14:47:00Z

## External Immutable Source Anchor

Source file (outside requirements artifacts):
- tests/Wip.Shell.Tests/Interactive/WipShellCommandLoopTests.cs
- line: 13
- source SHA256: fcedb843f88a7e948250de30658ce3a3c1f40b16bb33aaab6adce56798e4ea21
- source LastWriteTimeUtc: 2026-05-26T14:45:57.5321364Z

Anchored source text:

Implement interactive Wip.Shell command model and context-aware prompt transitions for global and session commands defined in MVP scope [depends on runtime orchestration commands]

## Deterministic Unchecked Baseline Line

Normalization rule:
- UTF-8 text, LF line ending, no trailing spaces
- baseline line is formed as: "- [ ] " + anchored source text

Baseline unchecked line:

- [ ] Implement interactive Wip.Shell command model and context-aware prompt transitions for global and session commands defined in MVP scope [depends on runtime orchestration commands]

Baseline line SHA256:

88c817c6c0a16f558be65c2c6d7addaa206cf46f5fb524b3dcfbfaef84ba47dc

## Why This Is Independent

This witness does not rely on git history of .github/requirements/Wip.Shell-Builder-MVP-Next-Steps.md.
It anchors the baseline item text to an external file (test trait constant), then computes the unchecked baseline deterministically with a reproducible hash.