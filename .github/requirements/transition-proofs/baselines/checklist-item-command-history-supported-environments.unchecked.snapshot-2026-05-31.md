# Baseline Witness: Command History Persistence Hardening (Unchecked)

Date (UTC): 2026-05-31T00:00:00Z

## External Immutable Source Anchors

Source files (outside requirements artifacts):
- tests/Wip.Shell.E2E.Tests/E2E/ShellHistoryTests.cs
  - source SHA256: 38caa4efbb8ad539104933567016461c91cf4a39a200434a2f7182dd5fa8a05c
  - anchored proving tests:
    - ShellHistory_GivenTwoSequentialSessions_ExpectedPreviousCommandsRestoredOrDeterministicFallbackExplained
    - ShellHistory_GivenUnavailablePersistenceDirectory_ExpectedFallbackToInMemoryHistory
- src/Wip.Shell/History/PersistentCommandHistory.cs
  - source SHA256: a2cee108940d51f54901b8311aa7824263312330ba23bf736283fde76f33a981
  - anchored implementation: PersistentCommandHistory (persistent storage with deterministic fallback status codes)

## Deterministic Unchecked Baseline Line

Normalization rule:
- UTF-8 text, LF line ending, no trailing spaces
- baseline line is formed as: "- [ ] " + checklist item text

Baseline unchecked line:

- [ ] Complete persistent command history implementation on supported environments while preserving deterministic fallback when persistence is unavailable [depends on SH-007 history persistence hardening]

Baseline line SHA256:

74eefc2dfa740554a0bd1000737dd2b7cdc5bb5918c3ae95e9c79d3ecfc4f84d

## Why This Is Independent

This witness does not require git history of .github/requirements/Wip.Next-Steps.md.
It anchors to executable tests and implementation plus a deterministic unchecked checklist-line hash for independent verifier validation.
