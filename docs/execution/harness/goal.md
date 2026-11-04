# Goal — WIP-IMPL-001 phase 1 close-out (run of 2026-11-04)

**Outcome:** WP-1.3 worker coverage is complete and independently verified, WP-1.6 is complete and documented, and the G1 autonomy/streaming decision packet is ready for the product owner. Execution stops at the human G1 gate; no release claim.

## Definition of done (falsifiable)

| # | Check | Command / observable |
|---|---|---|
| D1 | Worker mode exercises board, hands, current player, pass streak and legal-move selection through the real worker process | `dotnet test WIP/Wip.Bones.Tests ...` — new worker-mode theory passes 4/4 |
| D2 | The host process never loads the model assembly in Worker mode | Test asserts no `BonesStrategyScript_*` assembly delta in the host AppDomain, `CompileInvocationCount == 0`, and player slot is the host `WorkerBonesPlayerSlot`; a paired InProcess positive control proves the detector can fail |
| D3 | Public player-slot legality contract is unchanged under Worker mode | Illegal worker move falls back to the first legal move and emits the warning artifact |
| D4 | Bones suite totals are deliberate and recorded | TRX total equals `docs/plan/test-waivers.json` expectedTotals for `Wip.Bones.Tests`; 2 waivers unchanged |
| D5 | Architecture graph unchanged at 41 WIP projects, 3/3 tests | `dotnet test tests/Wip.Architecture.Tests` exit 0, total 3 |
| D6 | Offline gate green over 16 projects / 27 waivers | `tools/verify-test-results.ps1 -ResultsDirectory .tmp/gate-check` exit 0 |
| D7 | `ScriptHost:Mode` flag is functional, validated, tested, default `InProcess`, and documented with the trust boundary | Host option tests + README/security docs; DEV-004 updated |
| D8 | G1 packet answers autonomy-in-scope and streaming-in-scope with evidence, options, residual risks | `docs/execution/evidence/G1/gate-packet.md` exists and cites EVD IDs |

## Hard constraints

- `Wip.Sandbox.Worker` never references `Wip.Bones`; it stays on `Wip.Sandbox.BonesContracts`.
- No reset/clean/force-push; preserve the working tree.
- One work package per commit; state.json, journal, evidence, waivers, TRX and CI updated in the same change.
- UTF-8 without BOM; staged-encoding check must pass.
- Default stays `InProcess` until G2 is fully approved and installation is handled. Record any deviation.
- Windows containment is not claimed. Do not retry the AppContainer helper.
- No untrusted model code, no release.

## Stop criteria

- Met when D1-D8 hold and the G1 packet is presented.
- Hard stop for human decision: G1 (product scope), full G2 (security), release (G5).