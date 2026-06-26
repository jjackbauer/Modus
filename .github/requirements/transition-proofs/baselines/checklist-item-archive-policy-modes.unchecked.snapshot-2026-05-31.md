# Baseline Witness: Archive Policy Mark-Only and Cleanup Modes (Unchecked)

Date (UTC): 2026-05-31T00:00:00Z

## External Immutable Source Anchors

Source files (outside requirements artifacts):
- tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs
  - source SHA256: 614aca14249a0c475b601fe46457ca3e35f27cc8093c3f5bebffa4c497f1e235
  - anchored proving test:
    - ArchivePolicy_GivenMarkOnlyAndCleanupModes_PreservesArtifactsAndMaintainsDeterministicSessionTraceability
- src/Wip.Runtime/Runtime/WipRuntimeSessionOperations.cs
  - source SHA256: dbb13db4c7706e84dd9cbdf38ea534768b05be277081121b0c9cde84e4e4db26
  - anchored contract: SessionArchivePolicyMode and SessionArchiveRequest
- src/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs
  - source SHA256: 641a0c55b21f9147ed64c233440f95bffa472eb3bb6ecf8ae7e520cd70998bba
  - anchored runtime behavior: ArchiveAsync mark-only versus cleanup worktree outcomes with persisted session-archive artifact payload
- src/Wip.Shell/Interactive/WipShellCommandLoop.cs
  - source SHA256: e4f618c70bfe9f57065824fb163f1ba3265531a5b0028557901f49a2f74b9d24
  - anchored shell command surface: archive --mark-only and archive --cleanup option handling

## Deterministic Unchecked Baseline Line

Normalization rule:
- UTF-8 text, LF line ending, no trailing spaces
- baseline line is formed as: "- [ ] " + checklist item text

Baseline unchecked line:

- [ ] Add worktree archive policy options and runtime proof for both mark-only and cleanup modes while preserving artifact retention and session traceability [depends on GW-009 archive/cleanup policy behavior]

Baseline line SHA256:

28fbc8ca93d2403531fe87bc897a9d12916f4da01afd10a47a0c2cdb5ed15565

## Why This Is Independent

This witness does not require git history of .github/requirements/Wip.Next-Steps.md.
It anchors to executable tests and implementation plus a deterministic unchecked checklist-line hash for independent verifier validation.
