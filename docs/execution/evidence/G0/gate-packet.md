# GATE: G0 — baseline packet (pre-gate decision)

**Plan:** WIP-IMPL-001
**Phase:** 0 — Truth and baseline
**Packet date:** 2026-11-02T10:45:00-03:00
**Prepared by:** WIP Program Executor
**Evidence root:** `docs/execution/evidence/`

> **Decision record (2026-11-02T10:58:39-03:00):** the user approved the baseline commit in the existing `Modus` repo ("okay, commit there") and separately requested NuGet packing/publishing for WIP and an update of the Modus packages. The baseline decision is recorded in [decision-2026-11-02.md](decision-2026-11-02.md). This G0 packet remains **NOT READY**; the NuGet request is out-of-plan release work and is blocked on API key, version, and package scope.

## Status

**NOT READY.**

This is a **pre-gate** packet. It does **not** ask to pass G0. G0 cannot pass until WP-0.2–WP-0.6 complete (governance guard, full solution, CI truth, architecture tests, ADRs). What this packet asks is the **one decision that unblocks WP-0.1**: how to freeze the documented 308-path working tree.

The executor has not committed, stashed, tagged, reset, cleaned, or checked out anything. The user's working tree is intact.

## Why the line is stopped here

WP-0.1's first task is "Commit/stash the 308-path working tree after review; tag `wip-baseline-2026-11-02`". The execution contract forbids committing the user's work without explicit approval, and the kickoff says: "Preserve the current working tree; stop at G0 with a baseline packet." The baseline strategy changes repository history and is a human decision.

## Criteria (G0 / Phase 0 exit)

| # | Criterion (plan §3.2) | Status | Evidence / blocker |
|---|---|---|---|
| 1 | Baseline tag exists and working tree is clean except intentional untracked files | **Not met** | No `wip-baseline*` tag; 308 porcelain entries / 522 untracked files. [dirty-tree.md](../WP-0.1/dirty-tree.md) |
| 2 | WP-0.1 inventory reviewed (HEAD, test inventory, known failures, waivers) | **Partial** | HEAD/dirty-tree/solution/test-static inventory captured. Build/test execution and `docs/plan/baseline-test-status.md` are pending the freeze. |
| 3 | Governance guard committed and tested in CI (WP-0.2) | **Not met** | Guard exists at `WipRuntimeOrchestrator.cs:299` in the working tree; absent at HEAD; not in CI. |
| 4 | `Wip.slnx` includes all 37 projects; coverage check fails on orphans (WP-0.3) | **Not met** | 24/37 in `Wip.slnx`; 34/37 in `Modus.slnx`; 3 projects in neither solution. [solution-coverage.txt](../WP-0.1/solution-coverage.txt) |
| 5 | CI runs all offline WIP suites; zero-test guard (WP-0.4) | **Not met** | 1 of 17 WIP test projects; trigger is `main` only; single job. |
| 6 | Architecture fitness tests exist and run (WP-0.5) | **Not met** | No `Wip.Architecture.Tests`; only Modus has one. |
| 7 | ADR-IMPL-01..06 written and accepted (WP-0.6) | **Not met** | No ADR file in the repository. |
| 8 | G0 passed | **Not met** | Criteria 1–7. |

## What is verified (read-only)

- Branch `WIP`; HEAD `d6b895f56af6f633dc3c009cbf93a862a0e920c1`; .NET SDK 10.0.103.
- Working tree matches the plan's documented baseline: **308 porcelain entries = 148 modified + 160 untracked**, diff **+6,758 / −1,427**, **0 deletions**.
- Expanded untracked surface: **522 files / 18.18 MB**; `.tmp/` alone is 291 files / 12.05 MB, with 36,475 files / 578 MB on disk (mostly git-ignored run output).
- Proposed commit set (Option A) is **368 files / 8.14 MB**, excluding scratch and generated run data.
- 37 WIP projects, 17 WIP test projects, 239 test source files, 1,300 `[Fact]`/`[Theory]` declarations.
- Generated harness adapters (35 files in `.cursor`/`.claude`/`.github`) are untracked and not yet checked.
- Secret scan: 3 files match patterns, **0 added diff lines**; all matches are documentation placeholders or a pre-existing negative-test fixture. No values are recorded. See [secret-scan.md](../WP-0.1/secret-scan.md).
- No build or test was run; no green claim is made.

## Baseline strategy options

| Option | What happens | Assessment |
|---|---|---|
| **A — Curated baseline commit + annotated tag (recommended)** | Stage 148 modified + 220 intentional untracked files; exclude `.tmp/` (291 files) and 11 scratch/runtime files; add `.tmp/` to `.gitignore` without deleting files; commit on `WIP`; tag `wip-baseline-2026-11-02`; no push. | Matches WP-0.1, yields a clean tree and a reviewable checkpoint, loses nothing (excluded files stay on disk). 368 files / 8.14 MB plus evidence. |
| B — Commit everything as-is | Include `.tmp/`, scratch scripts, and the pool snapshot. | **Rejected.** Adds 291 scratch files / 12 MB of run residue; history noise with no product value. |
| C — Stash the tree (`git stash push -u`) | Remove tracked and untracked changes from the working tree, restore later. | **Rejected.** 522 untracked files (including 578 MB of run output) are a poor stash candidate; conflicts and data-loss risk; does not produce the plan's baseline tag. |
| D — Snapshot on a separate branch | Commit the tree on `baseline/wip-2026-11-02`, leave `WIP` at `d6b895f`. | Does not satisfy "baseline on `WIP`"; useful only if the team wants `WIP` pristine. Can be combined with A later. |
| E — Do not freeze yet | Keep working; re-baseline later. | WP-0.1 and G0 remain blocked; every later work package builds on an unfrozen tree. Only if the owner wants more implementation before the review. |

## Proposed Option A manifest

**Include (368 files / 8,136,449 bytes at snapshot; WP-0.1 evidence is added on top):**

| Group | Files | Notes |
|---|---:|---|
| Modified tracked files | 148 | All changes; no deletions; includes the governance guard, Bones work, docs, CI, tooling. |
| Untracked WIP source/tests | 103 | `Wip.Shell.E2E`, `Wip.Bones.Benchmark`, `BonesLibraryRecovery`, pool/champion/benchmark code, viewer assets, requirements docs. |
| Untracked `docs/` | 51 | 5 plan/report/prompt + PDFs; 46 `goal-bones-*` session records. |
| `harness/` | 23 | Requirements, rules, skills, workflows. |
| Generated adapters | 35 | `.cursor`/`.claude`/`.github` stubs — **only after HarnessSync regenerates and checks them.** |
| `tools/` | 5 | Encoding helpers + HarnessSync UTF-8 validator/tests (excludes `_write_bones_e2e_files.py`). |
| Root config | 3 | `.editorconfig`, `bones.ps1`, `.githooks/pre-commit`. |
| WP-0.1 evidence (added at commit) | 16 at this packet | `docs/execution/state.json`, `journal.md`, `evidence/WP-0.1/*`, this packet — updated to record the approval before staging. |

**Exclude (stay on disk, not committed):**

| Path | Files | Reason |
|---|---:|---|
| `.tmp/**` | 291 | Scratch runs; add `.tmp/` to `.gitignore` |
| `host-stdout.txt`, `host-stderr.txt`, `test_status_simple.cs`, `tmp-facing-repro.csx` | 4 | Root scratch |
| `WIP/_*.csx` (4) and `WIP/BonesLibraryRecovery/debug.csx` | 5 | Probe scripts |
| `tools/_write_bones_e2e_files.py` | 1 | One-off generator |
| `WIP/Wip.Bones.Host/bones-data/.bones/pool/pool-snapshot.json` | 1 | Runtime state, not source |
| `bin/`, `obj/`, `artifacts/`, `TestResults/`, `*.log` | — | Already git-ignored (no deletion) |

**Open sub-decisions for the approver:**

1. Include or exclude the 46 `goal-bones-*` session records (0.29 MB) and the two PDFs (4.1 MB)? Recommended: include both to preserve provenance (the goal records are small; the PDFs are a one-time 4.1 MB addition).
2. Include or exclude the four modified tracked `strategy-library/seat-*.json`? They are already tracked and modified; excluding them would leave the tree dirty. Recommended: include as part of the current state.
3. The untracked pool snapshot is excluded by default; confirm it should remain untracked.

## Required pre-commit checks (only after approval)

1. `dotnet run --project tools/HarnessSync -- generate` then `--check`; commit the generated adapters only if the check passes.
2. `powershell -NoProfile -ExecutionPolicy Bypass -File tools/encoding/Test-StagedTextFileEncoding.ps1` (or force-add evidence logs; `*.log` is ignored).
3. Re-run the secret scan over the staged set; human confirms the three known files.
4. `git diff --cached --stat` and a scope review: no `.tmp/`, no `bin/obj`, no unexpected binaries, no test weakening, no secrets.
5. Stage, commit `chore(wip): freeze baseline at d6b895f + 308-path working tree`, annotate tag `wip-baseline-2026-11-02` with the plan ID; **no push**.
6. Run the WP-0.1 build and test inventory; write `docs/plan/baseline-test-status.md` with every failure, owner, classification, and waiver expiry. A failing baseline is recorded, not hidden.

## Risks

- **Committed in-flight work is a checkpoint, not a code review.** The 148 modified files include substantial Bones/runtime behavior; the baseline freezes it, and later WPs may need to repair it. This is the plan's intent (freeze truth first), but it means the tag should be treated as "current state", not "known good".
- **The baseline may be red.** Compliance/E2E suites are not in CI today and the report flags unregistered docs and missing gates. WP-0.1 must record failures with owners/expiry; a waiver is not a skip.
- **Large-ish commit:** 8.14 MB, including 4.1 MB PDFs and 2.3 MB of modified tracked files. History size is acceptable but avoid repeating PDF commits.
- **Line-ending normalization** (`core.autocrlf=true`, no `.gitattributes`) will rewrite many files to CRLF in the index; review the staged diff for accidental content changes.
- **CI does not run on `WIP`** and covers 1 of 17 WIP suites until WP-0.4. A green CI will not prove this baseline.
- **Secret fixture:** the negative-test key at `BonesViewerLearningLoopStatusScriptTests.cs:519` is pre-existing at HEAD and synthetic in use; confirm provenance before any external publication.
- **`.tmp/` retention:** 578 MB stays on disk. No deletion is authorized; storage cleanup is a separate, explicit decision.

## Recommendation

**Approve Option A** with the proposed exclusions, including the three sub-decisions as recommended. Then execute WP-0.1's freeze, run the build/test inventory, and continue to WP-0.2. Do **not** pass G0 now.

## Decision required

1. Approve Option A (curated baseline commit on `WIP` + annotated tag `wip-baseline-2026-11-02`, no push), or select B/C/D/E.
2. Confirm the exclusion list and the three sub-decisions (goal records, PDFs, pool snapshot, seat JSONs).
3. Authorize exactly one baseline commit and one annotated tag.

## If approved, next action

Execute the WP-0.1 freeze exactly as scoped above, capture the commit/tag SHAs and verification logs, write `docs/plan/baseline-test-status.md`, update `state.json`/`journal.md`, and proceed to WP-0.2 (governance guard) without pausing at another gate. G0 remains scheduled for after WP-0.6.

## Evidence index

- [dirty-tree.md](../WP-0.1/dirty-tree.md) — full dirty-tree inventory
- [baseline-facts.md](../WP-0.1/baseline-facts.md) — verified facts vs plan
- [solution-coverage.txt](../WP-0.1/solution-coverage.txt) — 37/24/34/3 project math
- [secret-scan.md](../WP-0.1/secret-scan.md) — redacted scan
- [baseline-manifest.txt](../WP-0.1/baseline-manifest.txt) — include/exclude groups
- [notes.md](../WP-0.1/notes.md) — implementation note and residual risks
- [command-exit-codes.txt](../WP-0.1/command-exit-codes.txt), [preflight.txt](../WP-0.1/preflight.txt)
- Raw snapshots: [git-status-porcelain.txt](../WP-0.1/git-status-porcelain.txt), [git-status-porcelain-uall.txt](../WP-0.1/git-status-porcelain-uall.txt), [git-diff-stat.txt](../WP-0.1/git-diff-stat.txt)
