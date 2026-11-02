# WP-0.3 — Full WIP solution topology

**Status:** done
**Date:** 2026-11-02

## Acceptance

| Criterion | Evidence |
|---|---|
| `Wip.slnx` includes all 37 WIP projects | [coverage-after.txt](coverage-after.txt) — `wip_project_count=37`, `solution_wip_project_count=37`, `solution_coverage=ok` |
| Coverage check fails on an orphan | [coverage-orphan-red.txt](coverage-orphan-red.txt) — removed `Wip.Bones.Tests` from a temp solution → exit **1**, one MISSING |
| `dotnet build Wip.slnx` compiles all 37 | [build-wip.txt](build-wip.txt) — exit **0**, **0 errors**, 113 warnings, 53.06 s |
| `Modus.slnx` remains the product solution | Unchanged; it still contains its 34 WIP projects. No WIP project is absent from both solutions. |
| Every WIP project is represented | 37 csproj on disk; 37 in `Wip.slnx` |

## Notes

- `dotnet sln add` unexpectedly also added `src/Samples.TodoApp`; it was removed with `dotnet sln remove`, leaving exactly 37 WIP projects.
- `Wip.Bones.Tests`, `Wip.Bones.Benchmark`, `BonesLibraryRecovery`, and `Wip.Shell.E2E` are now in the WIP gate.
- The coverage script is `tools/verify-solution-coverage.ps1`; it compares `WIP/**/*.csproj` with the solution's `Project` nodes.

## Rollback

Revert this commit; `Wip.slnx` returns to 24 projects and the coverage script is removed. No product code changed.
