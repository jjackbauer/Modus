# Round 5 — Refalsify A (M5 COMPATIBILITY)

- Repository `C:\Users\ricar\dev\Modus`, branch `WIP`, sampled 2026-11-07 18:26–19:05 (America/Sao_Paulo).
- `dotnet test` was not used (known `Win32Exception (5)`); the compiled
  `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` was executed with
  `.tmp/tholdem-verify/RunXunit/RunXunit.csproj` (Release). CLI calls used
  `WIP/Wip.Tholdem.Host/bin/Release/net10.0/Wip.Tholdem.Host.exe`.
- No product, test, docs or configuration file was edited. Operator data was moved aside once and
  restored: 141/141 files under `artifacts/` matched by SHA-256 before/after (47 under `artifacts/tholdem`).
- Another falsifier was moving the same operator tree during the window; my before/after hashes were
  taken on the restored tree and the restore was verified byte-identical, so the observations below
  are from the current data.

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| M5 | Falsified | **(a) holds.** `Wip.Tholdem.Host.exe play --seed 42 --hands 20 --allow-weak-seed --data-dir .tmp/refalsify-a-m5/oracle` exited 0 and printed exactly `session: tholdem-06795fbeacfe528b` and `seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618`; the written `session.json` hashes to `87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618`; 20 hand files were written. A second run with explicit `--seats 2 --agents tight-aggressive,random-legal` printed the same session/manifest (`default_vs_seats2_manifest_equal=True`).<br><br>**(b) holds.** `artifacts/tholdem/sessions` contains 33 `hand-*.json` files: 30 v1 + 3 v2. For every one, SHA-256(file bytes) equals the sibling `session.json` `handHashes[handNumber - 1]` (33/33 anchors); Release CLI `replay` exits 0 for all 33 (30/30 v1, 3/3 v2). `artifacts/tholdem-run/sessions` has 2 v1 files, both replay exit 0. The original oracle recording `.tmp/tholdem-cli/sessions/tholdem-06795fbeacfe528b` has 20 v1 files, all replay exit 0 against its own recorded manifest `4b8ea23b…`. Caveat: the current CLI regenerates 12/20 of those old oracle hands with different bytes (new manifest `87f90413…`), but every recorded file still parses, hash-verifies and replays against its own recorded manifest.<br><br>**(c) fails.** Self-containment holds: 62 test source files, 0 matches for `artifacts[/\\]tholdem`; with the entire `artifacts/` directory moved aside the full suite printed `TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0`, exit 0, and `artifacts/` was restored 141/141 files byte-identical. The compatibility class alone is `TOTAL 3 PASSED 3 FAILED 0 SKIPPED 0`. The schema rule is in `HistorySessionFixture.cs:147-156`: v1 → `Assert.Equal(2, record.SeatCount)`; v2 → `Assert.InRange(record.SeatCount, 3, 6)`. The new 4-seat regression `HistorySessionCompatibilityTests.cs:91-118` passes. **Break:** a legitimate 2-seat v2 hand produced by the Release CLI (`play --seed 777 --hands 2 --seats 2 --starting-stacks 1000,600 --allow-weak-seed`, the same session id already recorded as `tholdem-f275b4ee69be0557`) is `{"schema":"tholdem.hand.v2",…,"seats":2,"startingStacks":[1000,600],…}`; it parses and `replay` exits 0, and the product pins this shape in the passing `MultiwayHistoryTests.From_GivenTwoSeatUnequalStacks…` (class `TOTAL 6 PASSED 6 FAILED 0`). `HandRecordCanonicalWriter.cs:17-19` deliberately selects v2 for 2-seat unequal stacks and `HandRecordJson.cs:142` accepts 2..6 seats. Calling the compiled fixture's single verification path `HistorySessionFixture.VerifyHandFile` on that file (via `dotnet fsi` reflection over the current test assembly) threw `Xunit.Sdk.InRangeException: Assert.InRange() Failure: Value not in range / Range: (3 - 6) / Actual: 2`. So the verifier rejects a legitimate v2 hand in a sessions root on a v2-only seat expectation, not a v1-only one.<br><br>**(d) holds.** Full suite `TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0`, exit 0, observed twice (artifacts present and artifacts absent). 0 source markers for `Skip=`, `Assert.Skip`, `#if`; 246 `[Fact]` + 8 `[Theory]` with 28 `[InlineData]` = 274 cases. `docs/plan/test-waivers.json` records `"Wip.Tholdem.Tests": 274` and has no Tholdem entry under `failures` or `knownAborted`; `Wip.Tholdem.Tests` is in the `tools/run-wip-tests.ps1` project list. | (a) Re-ran the oracle with explicit `--seats 2 --agents tight-aggressive,random-legal` and a second data directory; manifest unchanged. (b) Independently hash-checked every recorded hand against its manifest and replayed every v1 file under `artifacts/tholdem`, `artifacts/tholdem-run` and the original oracle recording; no failure. (c) Moved all of `artifacts/` away and ran the whole suite (green), then generated a legitimate 2-seat v2 hand into a real sessions root and passed it to the fixture's `VerifyHandFile`: it threw. (d) Scanned for skipped/waived tests, read the waiver register, and ran the suite both with and without artifacts. | (a), (b) and (d) are supported as stated. (c) is not: the claim's `v2 means 3..6` schema rule is factually wrong for the current product. `HandRecordCanonicalWriter` emits v2 for 2-seat unequal starting stacks, `HandRecordJson` accepts 2..6 seats, the CLI writes such files, and a product test pins round-trip/replay of exactly that shape. The fixture's shared verification path therefore rejects a legitimate v2 hand in a sessions root with `InRangeException (3-6, actual 2)`. The round-5 regression only proves that a 4-seat v2 hand is not rejected by the old v1-only expectation; the v2-only seat assumption remains and rejects another legitimate v2 hand. Because M5 is a conjunction and its schema-awareness clause is false as stated, M5 is falsified. |

## Observed outputs (raw)

```text
ORACLE_EXIT=0
session: tholdem-06795fbeacfe528b
seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
SESSION_JSON_SHA256=87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618

SEATS2_EXIT=0
session: tholdem-06795fbeacfe528b
seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
default_vs_seats2_manifest_equal=True

ARTIFACTS_REPLAY total=33 pass=33 fail=0 v1_pass=30 v2_pass=3
MANIFEST_HASH_ANCHOR ok=33 bad=0
THOLDEM_RUN_REPLAY total=2 pass=2 fail=0
THOLDEM_CLI_SESSION replay total=20 pass=20 fail=0

FULL_SUITE_EXIT=0
TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0

artifacts_present_during_run=False
tholdem_files_during_run=0
SUITE_WITHOUT_ARTIFACTS_EXIT=0
TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0
ARTIFACTS_RESTORED_BYTE_IDENTICAL=True  artifacts_files_before=141 artifacts_files_after=141
tholdem_files_after=47

COMPAT_CLASS_EXIT=0
TOTAL 3 PASSED 3 FAILED 0 SKIPPED 0
MULTIWAY_HISTORY_EXIT=0
TOTAL 6 PASSED 6 FAILED 0 SKIPPED 0

V2TWO_EXIT=0
session: tholdem-f275b4ee69be0557
hand starts: {"schema":"tholdem.hand.v2","handNumber":1,...,"startingStacks":[1000,600],"buttonSeat":0,"seats":2,...}
replay_exit=0

verify_method_found=true hand_exists=true
VERIFY_THREW Xunit.Sdk.InRangeException
VERIFY_MESSAGE Assert.InRange() Failure: Value not in range
Range:  (3 - 6)
Actual: 2
```

The `dotnet test`-free verification used the current compiled test assembly (fixture source SHA-256
`5eacd514…`, matching the round-5 implementation record); the FSI helper script and the suite log are
scratch files under the git-ignored `.tmp/refalsify-a-m5/` directory.