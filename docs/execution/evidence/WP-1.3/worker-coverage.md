# WP-1.3 — worker-mode coverage and host isolation proof

**Status:** coverage complete; verified 2026-11-04. Worker remains trusted-only, default-off (DEV-004).

## Delivered

- `WIP/Wip.Bones.Tests/Engine/BonesStrategyScriptHostWorkerModeTests.cs` — 9 test cases in a serialized collection with one worker process per class:
  - 4-case theory: a state-aware strategy selects `legalMoves[(boardTiles + activeHandTiles + passStreak) % legalCount]`. The four states differ so that losing board tiles, the current player’s hand, the pass streak, or the move identity changes the expected move. Cases: `(1,2,0)→1`, `(2,2,0)→0`, `(1,1,0)→0`, `(1,2,1)→0`; seats 1/3/4 hold two tiles each so selecting the wrong current player also flips the index.
  - Illegal worker move through the public `BonesScriptStrategyPlayerSlot`: falls back to `legalMoves[0]` and emits the exact warning artifact (`RejectedMove` round-trips the move id, tile and side).
  - Worker mode adds no `BonesStrategyScript_*` assembly to the host AppDomain, `CompileInvocationCount == 0`, cached `PlayerSlot` is `WorkerBonesPlayerSlot` from `Wip.Bones` (host assembly).
  - Positive control: InProcess mode does add exactly one `BonesStrategyScript_*` assembly (the detector can fail).
  - Worker compile cache reuses the same bridge (no recompile).
  - Worker-rejected source (`System.Diagnostics`, allowed by the host deny-list but not the worker) fails closed with a deterministic message and does not increment the host compile counter. The client kills the worker on protocol errors, so this test owns a dedicated client.
- Worker memory fix (found by the new coverage): the worker retained ~34 MB per Roslyn compile and hit the default 256 MiB Job Object cap at compile 7 (`OutOfMemoryException` observed through a direct worker probe). Fix in `WIP/Wip.Sandbox.Worker/Program.cs`: metadata references are created once per process and a forced full collection runs after each compile request.
  - Measured before: 53.7 → 258.9 MB over 7 compiles, OOM at 7; 16-compile probe peaked at 328.5 MB (first GC at compile 10).
  - Measured after: peak 60.7 MB over 16 compiles; ~15 ms per compile after a 683 ms Roslyn warm-up.

## Verification (2026-11-04)

| Check | Result | Artifact |
|---|---|---|
| New worker-mode class | 9/9 passed | `.tmp/wp13-check/wp13-worker.trx` |
| Full Bones suite | 395 total, 393 passed, 2 waived (same two compliance meta-tests) | `.tmp/wp13-check/Wip.Bones.Tests.trx` |
| Sandbox worker suite after the worker fix | 18/18 | `.tmp/wp13-check/sandbox-after-worker-fix.trx` |
| Architecture | 3/3, graph count 41 | re-run after the change |
| Behavioral change in move legality | none: selection/fallback assertions unchanged and now exercised through the worker | test theory + fallback test |

## Remaining / not claimed

- Default stays `InProcess` (DEV-004) until full G2 and worker deployment are handled.
- Windows OS containment not claimed; P/Invoke (TM-13) remains an accepted gap.
- The workflow runtime’s nested service provider starts a second script host/worker during a run (WP-2.1 remediation); see `docs/security/sandbox-threat-model.md` §9.