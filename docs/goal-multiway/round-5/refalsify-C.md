# Round 5 — M5 refalsification (C)

**Tree:** `C:\Users\ricar\dev\Modus`, branch `WIP`, sampled 2026-11-07 (America/Sao_Paulo).
**Build:** `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental` → `Build succeeded`, 0 errors (2 pre-existing `Wip.Runtime` warnings). `dotnet test` was not run (local `Win32Exception (5)` limit); the supplementary `.tmp/tholdem-verify/RunXunit` runner executed the compiled test assembly.

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| M5 | Falsified | **(a) PASS.** Release CLI (`play --seed 42 --hands 20 --allow-weak-seed`, fresh `.tmp/falsify-c-oracle-data`) printed exactly `session: tholdem-06795fbeacfe528b` and `seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618`; exit 0; the written `session.json` SHA-256 is the same `87f90413…`.<br>**(b) PASS.** All 33 recorded hand files under `artifacts/tholdem/sessions` (30 v1 + 3 v2) replay through the Release CLI with exit 0 and matching session-manifest anchors; the 30 v1 files are exactly 30/30 with exit 0. Also 20 v1 hands in `.tmp/tholdem-cli` and 2 v1 hands in `artifacts/tholdem-run` replay with exit 0. Logs: `.tmp/falsify-c-v1-replay.txt`, `.tmp/falsify-c-oracle.txt`.<br>**(c) MIXED — this clause fails.** Self-containment holds: exact source grep for `artifacts[/\\]tholdem` and `.tmp` is 0; with the **entire** gitignored `artifacts/` tree moved aside the full suite still passed `TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0` (exit 0, 133.6 s) and the tree was restored byte-identically (141/141 SHA-256 entries match). The 3 compatibility tests pass 3/3, and the 4-seat v2 regression passes. **But** the claimed schema rule “v2 means 3..6” is false: the suite itself accepts a legitimate 2-seat v2 hand, and the new fixture’s rule rejects it.<br>**(d) PASS.** Full Tholdem suite on the current `Wip.Tholdem.Tests.dll`: `TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0`, exit 0 (also 274/274/0 with `artifacts/` absent). 246 `[Fact]` + 28 `[InlineData]` = 274; 0 `Skip`, 0 `#if`, 0 `Trait`, no runner config; `docs/plan/test-waivers.json` has `expectedTotals["Wip.Tholdem.Tests"]=274` and 0 Tholdem entries in `failures`/`knownAborted`. | **(a)** Ran the oracle twice in clean data directories; output and manifest hash were exact both times; no break. **(b)** Replayed every recorded v1 file (plus the v2 files and the oracle/`tholdem-run` copies); no parse/hash/replay failure; no break. **(c)** Generated a legitimate 2-seat v2 session with the Release CLI: `play --seed 42 --hands 2 --seats 2 --starting-stacks 1000,600 --allow-weak-seed --data-dir .tmp/falsify-c-v2two`. The file starts `{"schema":"tholdem.hand.v2",...,"startingStacks":[1000,600],...,"seats":2,...}` (SHA-256 `eb48386e…`) and CLI `replay` exits 0. Invoking the new fixture’s `HistorySessionFixture.VerifyHandFile` on that file via `dotnet fsi .tmp/falsify-c-verify-v2two.fsx` produced `Xunit.Sdk.InRangeException: Assert.InRange() Failure: Value not in range  Range: (3 - 6)  Actual: 2` (log `.tmp/falsify-c-verify-v2two.log`). `MultiwayHistoryTests` (6/6 pass) independently pins the same 2-seat v2 shape. **(d)** Ran the full suite twice, scanned every source file for skip/waiver markers, and checked the waiver register; no break. | The product defines v2 as 2..6 seats, not 3..6: `HandRecordCanonicalWriter.cs:17-19` writes v2 for a 2-seat hand whose starting stacks are unequal, `HandRecordJson.cs:141-145` accepts `seats` 2..6, and `MultiwayHistoryTests.From_GivenTwoSeatUnequalStacks_ExpectedV2StartingStacksRoundTripAndReplay` (lines 181-218, passing) pins a 2-seat v2 record. The new fixture hard-codes `Assert.InRange(record.SeatCount, 3, 6)` for V2 (`HistorySessionFixture.cs:155`) and the sweep duplicates it (`HistorySessionCompatibilityTests.cs:70`), so a legitimate 2-seat v2 hand in a sessions root fails the compatibility check. The explicit regression (`HistorySessionCompatibilityTests.cs:91-118`) only covers a 4-seat v2 hand, so it does not catch this. Because clause (c)’s schema-aware definition is false, M5 as stated is falsified even though (a), (b), (d), self-containment, and the 4-seat regression pass. |

## Exact observations

### (a) 2-seat CLI oracle

```text
session: tholdem-06795fbeacfe528b
seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
exit_code=0
manifest_file_sha256=87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
```

### (b) Recorded hands

- `artifacts/tholdem/sessions`: 33 hand files = 30 v1 (no `schema` member) + 3 v2 (`tholdem-d56ac5f4637e44ef`, 4 seats). Every file replayed by the Release CLI with exit 0; every file’s SHA-256 equals its `session.json` `handHashes[handNumber-1]`.
- `.tmp/tholdem-cli/sessions/tholdem-06795fbeacfe528b`: 20 v1 hands, all replay exit 0.
- `artifacts/tholdem-run/sessions/tholdem-f275b4ee69be0557`: 2 v1 hands, both replay exit 0.
- No recorded v1 parse/hash/replay failure was found.

### (c) Self-contained clean-checkout simulation

The **entire** gitignored `artifacts/` directory (141 files, including `artifacts/tholdem`’s 47 files) was moved to `.tmp/falsify-c-artifacts-aside/artifacts`; `Test-Path artifacts` was false during the run; the full suite exited 0:

```text
TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0
exit_code=0 elapsed_seconds=133.6
artifacts_present_after_run=False
```

After restoring, all 141 file hashes match the pre-move SHA-256 manifest (`ALL_ARTIFACTS_MATCH_PRE=True`); `artifacts/tholdem` again has 47 files. Source grep over the 62 test source files (`*.cs`/`*.csproj`, excluding `bin`/`obj`) found 0 matches for `artifacts[/\\]tholdem`, 0 for `tholdem-cli`, and 0 for `.tmp`.

### (c) The failing schema rule

Product code accepts a 2-seat v2 hand:

- `WIP/Wip.Tholdem/History/HandRecordCanonicalWriter.cs:17-19` — v2 is selected whenever the hand is not (2 seats with all equal starting stacks).
- `WIP/Wip.Tholdem/History/HandRecordJson.cs:141-145` — parsed `seats` is accepted for 2..6.
- `WIP/Wip.Tholdem.Tests/MultiwayHistoryTests.cs:181-218` — the current suite pins a `tholdem.hand.v2` record with `startingStacks:[1000,600]` and `SeatCount == 2`; it passes.

Compatibility fixture rejects that same shape:

- `WIP/Wip.Tholdem.Tests/HistorySessionFixture.cs:147-159` — `AssertSeatCountMatchesSchema`: V1 → `Assert.Equal(2, …)`; V2 → `Assert.InRange(record.SeatCount, 3, 6)`.
- `WIP/Wip.Tholdem.Tests/HistorySessionCompatibilityTests.cs:69-71` — the sweep repeats `Assert.InRange(record.SeatCount, 3, 6)` for V2.
- `WIP/Wip.Tholdem.Tests/HistorySessionCompatibilityTests.cs:91-118` — the regression generates only a 4-seat v2 session, so it never exercises the 2-seat v2 case.

Observed with `dotnet fsi .tmp/falsify-c-verify-v2two.fsx` on the CLI-generated 2-seat v2 hand:

```text
hand=...\tholdem-64035e9af3d4c23e\hand-00001.json
VERIFY_THREW type=Xunit.Sdk.InRangeException
VERIFY_MESSAGE=Assert.InRange() Failure: Value not in range
Range:  (3 - 6)
Actual: 2
```

The same file is accepted by the product CLI replay (`exit=0`), so this is a legitimate v2 hand, not a malformed one.

### (d) Full suite

```text
TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0
exit_code=0 elapsed_seconds=147.2
```

The same 274/274/0 result was observed in the clean-checkout simulation. `docs/plan/test-waivers.json` contains no Tholdem entry under `failures` or `knownAborted`; the only Tholdem entry is `expectedTotals["Wip.Tholdem.Tests"] = 274`.

## Limitations / not checked

- A literal fresh `git clone` was not performed; the whole `WIP/Wip.Tholdem*` tree is currently untracked, so a clone would not contain it. The clean-checkout condition was simulated by removing the entire gitignored `artifacts/` tree (the data dependency in question).
- `dotnet test` was not run (the local VSTest testhost aborts with `Win32Exception (5)`); the compiled test assembly was executed by the established supplementary runner.
- The full repository (all-project) suite and CI were not run. Clause (d) was checked on the Tholdem suite (274 tests), which is the suite the round-5 change touches.
- No product, test, documentation, or configuration file was edited. Scratch data and logs are under `.tmp/`; the only file added under `docs/` is this report.