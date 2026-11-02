# DEV-001 — NuGet publication requested before the plan's release gate

**Status:** executed — 0.0.2 published on nuget.org
**Date:** 2026-11-02
**Trigger:** user instruction: "I want you to pack and publish wip in nuget as well. and update modus. I think it is outdated in nuget"

## What the plan assumed

- WP-6.2 defines version 1.0, packs NuGet packages/host artifacts, produces release notes, migration guide, support matrix, and checksums.
- G5 approves the WIP 1.0 release after the release checklist, security sign-off, and rollback rehearsal.
- The repository currently has `PackageVersion` **0.0.1** in `Directory.Build.props`; local nupkgs for `Modus.Core.0.0.1` and `Modus.Host.0.0.1` exist under `artifacts/nuget`; the plan's target is 1.0.

## What is true now

- The request arrives at **Phase 0 / G0**, before WP-0.2–WP-0.6, before the sandbox/permissions/gates, and before any release checklist.
- No NuGet API key is present in the process, user, or machine environment (names checked; no values printed).
- No version or package list has been agreed. Publishing `0.0.1` again would collide with the published versions.
- WIP test execution is currently blocked by `BLK-TESTHOST-ENV`; the build/pack evidence is not yet green.
- The package graph is broad: `Modus.Core`, `Modus.Host`, and 20+ `Wip.*` package IDs, including app hosts and a web app that are not obviously library packages.

## Impact

- Scope: adds a release/publication workstream outside WIP-IMPL-001.
- Estimate: a few hours for pack/validation, plus release-engineering time; still gated by tests and version decisions.
- Dependencies: test environment, API key, version, package list, NuGet network access, and (for a real 1.0) G5.
- Public contract/release scope: **yes** — published package versions are immutable; this needs an explicit decision, not a silent pack.

## Smallest reversible correction

Keep WIP 1.0 behind G5. Treat this request as a **pre-release packaging task**:
1. Build the requested projects.
2. Pack locally with a clearly non-release version (for example `0.0.2-local`), without committing a version bump.
3. Validate package contents and installation in a clean temporary project.
4. Publish only after a human supplies a version and API key and accepts the release-gate exception.

## Options and recommendation

| Option | What happens | Assessment |
|---|---|---|
| A — prep only now | Pack/validate locally; publish after tests + explicit version/key; no G5 change | **Recommended**; reversible, protects the public feed |
| B — publish pre-release now | Pick `0.0.2-preview.1` or similar, push immediately with a key | Requires key, green tests, and a release exception; hard to undo |
| C — defer to WP-6.2 | No packages now; deliver at the planned release | Safest for the plan, does not satisfy the immediate request |

**Recommendation:** A. I will prepare local packages and exact push commands; the human decision is the version, package list, API key, and publication timing.

## Execution result 2026-11-02T11:50-03:00

- Version `0.0.2` was packed and published for 18 Modus/WIP packages; `Wip.Bones.Host` and `Wip.Bones.Web` remain application-only.
- Registry shows `0.0.2` for all 18 IDs; a clean public install of `Modus.Core` + `Wip.Modus` built successfully.
- Tests remain unexecuted (`BLK-TESTHOST-ENV`); the release did not pass G5 and does not constitute WIP 1.0.
