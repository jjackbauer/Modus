# WP-0.1 — Dirty working-tree inventory

**Captured:** 2026-11-02T10:41:40-03:00 (America/Sao_Paulo)
**Repository:** `C:\Users\ricar\dev\Modus`
**Branch:** `WIP` (upstream `origin/WIP`)
**HEAD:** `d6b895f56af6f633dc3c009cbf93a862a0e920c1` — `feat(bones): implement learning-loop enhancements (T1.1-T3.5, M1)`
**SDK:** .NET `10.0.103`
**Plan baseline:** branch `WIP`, commit `d6b895f`, 308 changed paths — **this tree matches the documented baseline.**

> This inventory was captured before `docs/execution/` evidence existed. The executor did not modify, revert, stash, commit, tag, or delete any user file. The only additions are the evidence files under `docs/execution/`.

## Snapshot

| Metric | Value |
|---|---|
| `git status --porcelain` entries | **308** |
| Modified tracked files | **148** |
| Untracked entries (porcelain) | **160** |
| Untracked files (`git status -uall`) | **522** |
| Untracked bytes (non-ignored files) | **18,178,919** (≈17.3 MiB) |
| Tracked diff | **148 files changed, 6,758 insertions(+), 1,427 deletions(-)** |
| Deleted tracked files | **0** |
| Stashes | **0** |
| Existing baseline tag | **none** (`git tag --list 'wip-baseline*'` is empty) |

The plan's "308 changed paths" counts porcelain entries (148 modified + 160 untracked entries). Because `git status` collapses untracked directories, the same tree contains **522 actual untracked files**, including 291 files under `.tmp/`. The true review surface is therefore larger than the plan's headline number.

## Modified tracked files (148)

- 101 files are in the Bones product/test tree (`WIP/Wip.Bones*`); the remainder are runtime, shell, builder, validation, harness, CI/docs, and root configuration.
- No deletions (`git diff --name-status --diff-filter=D` is empty).
- No binary files were modified. The only data/config files in the modified set are `WIP/Wip.Bones.Host/appsettings.json` and the four tracked `bones-data/.bones/strategy-library/seat-*.json` fixture/state files.
- The in-flight governance guard is present in the working tree: `WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs:299` calls `WorkflowStageStateMapper.ResolveRunAdvancementTarget`; it is **absent at HEAD**, confirming WP-0.2's in-flight work.

Top groups by path (exact list: [modified-paths.txt](modified-paths.txt)):

| Path group | Files |
|---|---:|
| `WIP/Wip.Bones.Agents/` | 24 |
| `WIP/Wip.Bones.Host.Tests/` | 16 |
| `WIP/Wip.Bones.Host/` | 14 |
| `WIP/Wip.Bones.Tests/` | 13 |
| `WIP/Wip.Bones.Web.Tests/` | 13 |
| `WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/` | 7 |
| `WIP/Wip.Runtime/` | 6 |
| `.github/requirements/` | 6 |
| `harness/requirements/` | 6 |
| `WIP/Wip.Bones.ModelProviders.DeepSeek/` | 6 |
| Other (shell/runtime tests, docs, config, tooling) | 37 |

## Untracked content (160 porcelain entries / 522 files)

| Area | Files | Bytes | Nature | Baseline decision |
|---|---:|---:|---|---|
| `.tmp/` | 291 | 12,053,561 | Scratch benchmark runs, scripts, logs, throw-away data (36,475 files / 578 MB on disk; the rest is git-ignored build output) | **Exclude; add `.tmp/` to `.gitignore` (do not delete)** |
| `WIP/` | 109 | 1,137,719 | New source/tests: `Wip.Shell.E2E`, `Wip.Bones.Benchmark` (11 files), `BonesLibraryRecovery` (3), pool/champion code, viewer pool assets, strategy-library docs, 4 `_*.csx` probes, untracked pool snapshot | Include except 5 `*.csx` probes and the pool snapshot |
| `docs/` | 51 | 4,750,601 | Plan/report/prompt + PDFs (5 files, 4.46 MB) and seven `goal-bones-*` session logs (46 files, 0.29 MB) | Include (product inputs and history) |
| `harness/` | 23 | 264,239 | Requirements, rules, skills, workflows | Include |
| `.cursor/`, `.claude/`, `.github/` | 35 | 6,408 | Generated adapter stubs for 11 new harness capabilities | Include **after** `HarnessSync --generate` / `--check` |
| `tools/` | 6 | 53,037 | `tools/encoding/*`, `HarnessSync` UTF-8 validator + tests, and `_write_bones_e2e_files.py` scratch generator | Include encoding/validator tooling; exclude the `_write_bones_e2e_files.py` scratch script |
| Root files | 6 | 10,257 | `.editorconfig`, `bones.ps1`, `host-stdout.txt`, `host-stderr.txt`, `test_status_simple.cs`, `tmp-facing-repro.csx` | Include `.editorconfig`, `bones.ps1`; exclude the four scratch files |
| `.githooks/` | 1 | 247 | `pre-commit` UTF-16 encoding gate | Include; `core.hooksPath` is currently unset |

Seven files inside the groups above are classified as scratch/runtime data: four `WIP/_*.csx` probes, `WIP/BonesLibraryRecovery/debug.csx`, `tools/_write_bones_e2e_files.py`, and the untracked `WIP/Wip.Bones.Host/bones-data/.bones/pool/pool-snapshot.json`.

### Review flags

1. **`.tmp/` is not ignored and is 578 MB on disk.** It is test-run residue, not product. The smallest reversible action is to add `.tmp/` to `.gitignore` and leave the files on disk; no deletion.
2. **Runtime state inside the source tree.** The untracked `bones-data/.bones/pool/pool-snapshot.json` (207 KB) is a persisted strategy-pool snapshot; four tracked seat JSONs are modified. The baseline commit should capture tracked modifications but should not start tracking new runtime snapshots without a deliberate decision.
3. **Generated harness adapters must be regenerated and checked** before they are committed; otherwise the `.cursor`/`.claude`/`.github` stubs can drift from `harness/`.
4. **Six Bones Learning Loop analysis/plan documents** exist at `WIP/*.md` (five untracked, one already tracked) totaling ≈150 KB. They are documentation, not code; include unless the product owner wants them archived instead.
5. **PDFs are 4.1 MB** of the 17.3 MB untracked set. They are generated from the Markdown inputs; including them is harmless but not required for the build.
6. **`WIP/Wip.Bones.Tests` is outside both solutions.** Its 58 test source files are part of the untracked/working tree but cannot run under either solution until WP-0.3. This is a Phase 0 finding, not a commit-blocker.
7. **Line endings.** `core.autocrlf=true` and there is no `.gitattributes`; Git warns that LF files will be checked out as CRLF. The baseline commit will normalize line endings. This will not corrupt UTF-8 content but may make later diffs noisier.

## Secret scan (summary)

Three files match secret-shaped patterns; **zero added diff lines match** a secret pattern. All matches are documentation placeholders or pre-existing test fixtures; none is introduced by the working tree. No values are reproduced in this packet. Details: [secret-scan.md](secret-scan.md).

## Safety actions taken

- Read-only git inspection only: `status`, `diff`, `log`, `rev-parse`, `ls-files`, `grep`, `show`, `tag --list`.
- No `git reset`, `git clean`, `git checkout`, `git stash`, `git add`, `git commit`, `git tag`, or push.
- No user file was moved or deleted at inventory time. The only mutation then was creating `docs/execution/` evidence (and renaming the executor's own `preflight.log` to `preflight.txt` to avoid the repository's `*.log` ignore rule).
- After the decision record, three tracked pre-commit edits were made with the approved baseline: `.gitignore` (scratch ignore rules and evidence-log negation), `tools/HarnessSync/TextFileEncodingValidator.cs` (`.tmp` exclusion), and `tools/HarnessSync.Tests/TextFileEncodingValidatorTests.cs` (focused test). No other tracked file was changed.
- No model-authored or untrusted code was executed.

## Raw evidence

- [preflight.txt](preflight.txt) — commands and captured counts
- [command-exit-codes.txt](command-exit-codes.txt) — exit-code ledger
- [git-status-porcelain.txt](git-status-porcelain.txt) — 308-entry snapshot
- [git-status-porcelain-uall.txt](git-status-porcelain-uall.txt) — 670-line expanded snapshot (522 untracked files before evidence)
- [git-diff-stat.txt](git-diff-stat.txt) — full diffstat
- [modified-paths.txt](modified-paths.txt) / [untracked-paths.txt](untracked-paths.txt)
- [baseline-manifest.txt](baseline-manifest.txt) — proposed include/exclude groups
- [solution-coverage.txt](solution-coverage.txt) — solution/project coverage
