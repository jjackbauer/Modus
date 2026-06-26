# Baseline Witness: Startup Banner Contract (Unchecked)

Date (UTC): 2026-05-30T00:00:00Z

## External Immutable Source Anchor

Source files (outside requirements artifacts):
- tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs
  - source SHA256: bf78e7387dc5691ff7a92bcc10f9f7ee1f560b30f3e72fb6ea72b7c98c010de1
  - anchored proving test: ShellProcess_GivenFreshLaunch_StartupBannerIncludesVersionRepoPluginCountPolicyAndHelpHint
- src/Wip.Shell/Interactive/WipShellCommandLoop.cs
  - source SHA256: 062b344ca9aa28be8b72d3d3142dbe1a66abbace303005c20d67d642a666fbbc
  - anchored banner writer: WriteStartupBannerAsync

Anchored source text proving the required startup banner fields are executable runtime behavior:

- test assertion pattern: Product + Version banner line is emitted
- test assertion pattern: Repository path line is emitted
- test assertion pattern: Loaded plugin count line is emitted
- test assertion pattern: Active policy profile line is emitted
- test assertion pattern: help hint line is emitted

## Deterministic Unchecked Baseline Line

Normalization rule:
- UTF-8 text, LF line ending, no trailing spaces
- baseline line is formed as: "- [ ] " + checklist item text

Baseline unchecked line:

- [ ] Implement startup banner contract to include product/version, detected repository path, loaded plugin count, active policy profile, and help hint on shell launch [depends on SH-001 startup compliance]

Baseline line SHA256:

a4c429c6ace22dba8a16e8a9e47463e9a5ef3ca76f89bca7c7fe2251a3ed775f

## Why This Is Independent

This witness does not rely on git history of .github/requirements/Wip.Next-Steps.md.
It anchors to executable behavior-proof source files and records a deterministic unchecked checklist-line hash for independent verifier checks.
