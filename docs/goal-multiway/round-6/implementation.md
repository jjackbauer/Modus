# Round 6 — Correct v2 seat invariant and 2-seat unequal-stack regression

**Status:** complete. The Tholdem suite is green at **275 tests, 0 failed, 0 skipped**
(was 274). The compatibility fixture now verifies the product's **true** schema invariant instead
of the false `v2 => 3..6` rule: v1 is a 2-seat hand with equal starting stacks, v2 is 2..6 seats and
a 2-seat v2 hand has unequal starting stacks. A generated scratch sessions root now holds all three
shapes, and a dedicated regression covers the 2-seat unequal-stack v2 hand that used to fail with
`InRange(3, 6)` / `Actual: 2`. No product code, viewer, agent, CLI, solution, architecture test or
`.github/` file was edited. `dotnet test` was not run; the compiled test assembly was executed with
the established supplementary xUnit runner `.tmp/tholdem-verify/RunXunit`.

## 1. Scope and environment

- Repository `C:\Users\ricar\dev\Modus`, branch `WIP`, tree sampled 2026-11-07
  (America/Sao_Paulo). Commands ran from the repository root with the available PowerShell host
  (5.1; `pwsh` is absent in this environment, the tool exposes Windows PowerShell).
- Files changed, all inside the allowed scope:
  - `WIP/Wip.Tholdem.Tests/HistorySessionFixture.cs` — third hand shape, corrected rule.
  - `WIP/Wip.Tholdem.Tests/HistorySessionCompatibilityTests.cs` — three-shape sweep, new
    regression, targeted 4-seat v2 regression.
  - `docs/plan/test-waivers.json` — `"Wip.Tholdem.Tests": 274` -> **275**.
  - this document.
- Not touched: `WIP/Wip.Tholdem/` (engine/history), `WIP/Wip.Tholdem.Agents/`,
  `WIP/Wip.Tholdem.Web/`, `WIP/Wip.Tholdem.Host/`, `Wip.slnx`,
  `tests/Wip.Architecture.Tests/`, `.github/`, `artifacts/`,
  `WIP/Wip.Tholdem.Tests/MultiwayHistoryTests.cs` (not needed).
- All test scratch is under the compiled test assembly directory
  (`WIP/Wip.Tholdem.Tests/bin/Release/net10.0/…`) and deleted by each test's `finally` block;
  `%TEMP%` was not used. Evidence logs and the CLI data live in
  `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/round6/` (git-ignored build output).

## 2. Defect and the true invariant

`HistorySessionFixture.AssertSeatCountMatchesSchema` classified
`tholdem.hand.v2` as `SeatCount` 3..6, and `HistorySessionCompatibilityTests` repeated
`Assert.InRange(record.SeatCount, 3, 6)`. That is not the product rule:

- `WIP/Wip.Tholdem/History/HandRecordCanonicalWriter.cs:17` —
  `return record.SeatCount == 2 && AllStartingStacksEqual(record) ? WriteV1(record) : WriteV2(record);`
  so **v2 is selected for a 2-seat hand whenever the two starting stacks differ**. v1 means
  2 seats *and* equal stacks.
- `WIP/Wip.Tholdem/History/HandRecordJson.cs:142` — a v2 record is accepted with
  `seats` **2..6** (`if (seats is < 2 or > 6)` throws), not 3..6.
- `WIP/Wip.Tholdem.Tests/MultiwayHistoryTests.cs:180`
  (`From_GivenTwoSeatUnequalStacks_ExpectedV2StartingStacksRoundTripAndReplay`) already pins that a
  2-seat record with stacks `[1000, 600]` serializes as v2.

A legitimate CLI session
`play --seed 777 --hands 2 --seats 2 --starting-stacks 1000,600 --allow-weak-seed` therefore
writes 2-seat v2 hands, and the shared fixture verification threw
`Xunit.Sdk.InRangeException: Assert.InRange() Failure: Range: (3 - 6) / Actual: 2`.

The corrected rule (fixture, `AssertSeatCountMatchesSchema`):

```csharp
Assert.Equal(record.SeatCount, record.StartingStacks.Count);

switch (schema)
{
    case HandRecordSchema.V1:
        Assert.Equal(2, record.SeatCount);
        AssertStartingStacksEqual(record);          // every starting stack equals the first
        break;
    case HandRecordSchema.V2:
        Assert.InRange(record.SeatCount, 2, 6);
        if (record.SeatCount == 2)
        {
            Assert.NotEqual(record.StartingStacks[0], record.StartingStacks[1]);
        }

        break;
    default:
        throw new ArgumentOutOfRangeException(nameof(schema), schema, "Unknown hand schema.");
}
```

The rule is not merely restated: `VerifyHandFile` also asserts
`Assert.Equal(text, record.ToCanonicalJson())`, and the canonical writer re-derives the schema
from the record. A 2-seat file claiming v2 with equal stacks would round-trip to v1 text and fail;
a v1 file with unequal stacks cannot round-trip either.

## 3. Test-first evidence (red)

The regression was added, and the fixture generated the new 2-seat unequal-stack v2 session,
**before** the rule was corrected. The old `InRange(3, 6)` was still in place, so both the sweep
and the new regression failed. Build first (`dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj --configuration Release`):
`Build succeeded. 0 Warning(s) 0 Error(s)`, `red_build_exit=0`.

Command (type filter selects the compatibility class):

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HistorySessionCompatibilityTests
```

Observed (`scratch/round6/red-regression.txt`):

```text
FAIL HistorySessionCompatibilityTests.Parse_GivenGeneratedV1AndV2SessionsInOneRoot_ExpectedSchemaAwareVerificationOfEveryHand: InRangeException: Assert.InRange() Failure: Value not in range
Range:  (3 - 6)
Actual: 2
FAIL HistorySessionCompatibilityTests.Parse_GivenTwoSeatUnequalStackV2HandInSessionsRoot_ExpectedSharedVerificationAcceptsV2SeatCount: InRangeException: Assert.InRange() Failure: Value not in range
Range:  (3 - 6)
Actual: 2
TOTAL 4 PASSED 2 FAILED 2 SKIPPED 0
red_regression_exit=1
```

The second `FAIL` line is the required red for the dedicated regression: the 2-seat unequal-stack
v2 hand in a sessions root was rejected only because of the false `3..6` range.

## 4. What was implemented

### 4.1 Fixture: three hand shapes in one scratch sessions root

`HistorySessionFixture.CreateSessionsRoot(int v1Hands = 10, int twoSeatV2Hands = 3, int fourSeatV2Hands = 5)`
now runs three sessions through the public `MatchSimulator.Run` surface with explicit per-seat
starting stacks, into the recorded layout `sessions/<session>/hand-NNNNN.json`:

| Session directory | Seats / stacks | Seed | Product schema |
|---|---|---|---|
| `sessions/heads-up-v1` | 2 / `[1000, 1000]` | 42 | v1 (equal stacks) |
| `sessions/heads-up-unequal-v2` | 2 / `[1000, 600]` | 777 | v2 (unequal stacks) |
| `sessions/four-seat-v2` | 4 / `[1000, 1000, 1000, 1000]` | 9001 | v2 (four seats) |

`GeneratedHistorySessions` now exposes `V1SessionDirectory`, `TwoSeatV2SessionDirectory`,
`FourSeatV2SessionDirectory`, `V1HandCount`, `TwoSeatV2HandCount`, `FourSeatV2HandCount` and
`V2HandCount => TwoSeatV2HandCount + FourSeatV2HandCount`. `ReadSchema`, `EnumerateHandFiles`,
`VerifyHandFile` and `Sha256Hex` are unchanged except for the corrected rule; every generated hand
is still parsed, canonical-round-tripped, SHA-256-verified and replayed.

### 4.2 Schema-aware sweep of every hand

`HistorySessionCompatibilityTests.cs:47`
(`Parse_GivenGeneratedV1AndV2SessionsInOneRoot_ExpectedSchemaAwareVerificationOfEveryHand`):

- generates 10 v1 + 3 two-seat-v2 + 5 four-seat-v2 hands (18 files) into one scratch root;
- enumerates every `hand-*.json` and calls the shared `VerifyHandFile` on each;
- buckets and asserts **all three shapes are present and accepted**:
  `v1Count > 0` (2-seat equal stacks), `twoSeatV2Count > 0` (2-seat unequal stacks),
  `fourSeatV2Count > 0` (4-seat v2);
- asserts the bucket counts equal the generator's `Hands` values exactly (10 / 3 / 5) and
  `V2HandCount == twoSeatV2Count + fourSeatV2Count`;
- throws for any other v2 seat count in the generated root, so the sweep cannot silently stop
  checking a shape.

### 4.3 Dedicated 2-seat-v2 regression

`HistorySessionCompatibilityTests.cs:147`
(`Parse_GivenTwoSeatUnequalStackV2HandInSessionsRoot_ExpectedSharedVerificationAcceptsV2SeatCount`):

- generates a sessions root containing 3 two-seat unequal-stack v2 hands;
- selects the hand files that live under `sessions/heads-up-unequal-v2/` (via the root enumeration,
  i.e. a real sessions-root layout) and asserts their count;
- asserts the file starts with `{"schema":"tholdem.hand.v2",`, contains `"seats":2,` and has no
  v1 `holeCardsSeat0` member;
- runs the shared `VerifyHandFile` path (the exact call that previously threw
  `InRange(3, 6)`), then asserts `SeatCount == 2`, two hole-card lists, two starting stacks
  `[1000, 600]` and that the stacks differ.

### 4.4 Existing coverage kept and tightened

- `Parse_GivenGeneratedV1HandFile_ExpectedCanonicalHashVerifiesAndReplayMatches` (v1 single-file
  oracle) is unchanged apart from the new `CreateSessionsRoot` call.
- `Parse_GivenFourSeatV2HandInSessionsRoot_ExpectedV2VerificationWithoutV1OnlySeatExpectation`
  is kept; it now enumerates the **four-seat session directory** specifically instead of every v2
  file in the root, so it cannot accidentally pick the 2-seat v2 hand. Its four-seat assertions are
  unchanged.
- `MultiwayHistoryTests.cs` was not modified; the pinned v1 hash `c953464b…` and the existing
  2-seat/3-seat unequal-stack round-trip tests are untouched.

## 5. CLI proof (real Release CLI, observed)

Build: `dotnet build WIP/Wip.Tholdem.Host/Wip.Tholdem.Host.csproj --configuration Release` ->
`0 Warning(s) 0 Error(s)`, `cli_build_exit=0`.

Command:

```powershell
dotnet WIP/Wip.Tholdem.Host/bin/Release/net10.0/Wip.Tholdem.Host.dll play --seed 777 --hands 2 --seats 2 --starting-stacks 1000,600 --allow-weak-seed --data-dir <scratch>\cli-data
```

Observed (`scratch/round6/cli-play.txt`), `cli_play_exit=0`:

```text
session: tholdem-f275b4ee69be0557
seed: 777  hands: 2  manifest sha256: 0ea96416c6fe3e74a844df5daacaf622b8179b999d4adedbb269ca0be628f577
agent                    hands    bb/100       95% CI (bb/100)  win rate     95% CI (win rate)  aggression  showdown
tight-aggressive             2    -75.00     -124.00 .. -26.00      0.00          0.00 .. 0.66        0.00      0.00
random-legal                 2     75.00       26.00 .. 124.00      1.00          0.34 .. 1.00        1.00      0.00
```

Generated hand (`hand-00001.json`, SHA-256 `77459c9c…`):

```text
{"schema":"tholdem.hand.v2","handNumber":1,…,"startingStack":1000,"startingStacks":[1000,600],"buttonSeat":0,"seats":2,…
```

No CLI command can invoke the internal test fixture, so the shared fixture path was executed
through a temporary reflection probe compiled under the test output directory
(`scratch/round6/fixture-probe`, sources retained as `fixture-probe.csproj.txt` /
`fixture-probe-Program.cs.txt`). The probe loads the compiled test assembly and calls
`HistorySessionFixture.EnumerateHandFiles` / `ReadSchema` / `VerifyHandFile` directly.

Command:

```powershell
dotnet <scratch>\fixture-probe\bin\Release\net10.0\FixtureProbe.dll WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll <scratch>\cli-data\sessions
```

Observed (`scratch/round6/cli-fixture-proof.txt`), `cli_fixture_proof_exit=0`:

```text
sessions_root=…\scratch\round6\cli-data\sessions
hand_files=2
PASS schema=V2 seats=2 startingStacks=[1000,600] hash=77459c9ce22677551aea13937e5652e894aaf0c3127145ec70ed099a1fc0b787 file=…\tholdem-f275b4ee69be0557\hand-00001.json
PASS schema=V2 seats=2 startingStacks=[1000,600] hash=8ce84ce849d1a96ad7a2fafee36124f7dd2d366b7daa27cbc52e4595707a7e9d file=…\tholdem-f275b4ee69be0557\hand-00002.json
fixture_total=2 fixture_failed=0
```

The probe build directory was removed after its output was captured (its `.csproj` would be the
49th project and break the 48-project coverage check); the observed output is preserved, and the
probe sources are preserved as `.txt` in the same scratch directory.

## 6. Clean-checkout simulation (observed)

The tests never read `artifacts/`. To prove it, `artifacts/tholdem` (operator data, untouched,
47 files) was renamed aside, the full suite ran, then the tree was restored. A per-file SHA-256
manifest was compared before and after.

```text
artifacts_files_before=47
artifacts_present_during_run=False
TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0
clean_checkout_exit=0
artifacts_restored=True
artifacts_files_after=47
artifacts_tree_unchanged=True
```

Logs: `scratch/round6/clean-checkout-full.txt`, `artifacts-before.txt`, `artifacts-after.txt`.
No test generated, wrote to, or read `artifacts/tholdem`; the operator data was restored
hash-identically and not deleted.

## 7. Mutation evidence

After green, the old `v2 => 3..6` assertion was restored **in isolation** in
`AssertSeatCountMatchesSchema` (`Assert.InRange(record.SeatCount, 3, 6);` replacing the corrected
v2 block); the v1 branch was left as-is.

```text
fixture_mutated_sha256=5c768f7d9c63bf2b2bcef8bf6c77f343a943428aef3dee849121eb56f07a2e45
Build succeeded.
    0 Warning(s)
    0 Error(s)
mutation_build_exit=0
FAIL HistorySessionCompatibilityTests.Parse_GivenGeneratedV1AndV2SessionsInOneRoot_ExpectedSchemaAwareVerificationOfEveryHand: InRangeException: Assert.InRange() Failure: Value not in range
Range:  (3 - 6)
Actual: 2
FAIL HistorySessionCompatibilityTests.Parse_GivenTwoSeatUnequalStackV2HandInSessionsRoot_ExpectedSharedVerificationAcceptsV2SeatCount: InRangeException: Assert.InRange() Failure: Value not in range
Range:  (3 - 6)
Actual: 2
TOTAL 4 PASSED 2 FAILED 2 SKIPPED 0
mutation_run_exit=1
```

The dedicated 2-seat-v2 regression fails exactly as required; the v1 single-file test and the
4-seat v2 regression still pass. Logs: `scratch/round6/mutation-build.txt`, `mutation-run.txt`.

Restored from the saved copy and hash-verified, then force-rebuilt with `--no-incremental`:

```text
restored_sha256=87876a33db34e11f058b6981a6d9cefe5538f8752d6d7a39b24752d8045b84e6
saved_sha256=87876a33db34e11f058b6981a6d9cefe5538f8752d6d7a39b24752d8045b84e6
matches_original=True
Build succeeded.
    2 Warning(s)
    0 Error(s)
restored_force_build_exit=0
TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0
restored_full_run_exit=0
```

(The two warnings are the pre-existing `Wip.Runtime` CA2024 warnings from the forced dependency
rebuild; no Tholdem warning.) Logs: `restored-force-build.txt`, `restored-full-run.txt`.

## 8. Verification (observed output)

All commands from the repository root. `dotnet test` was **not** run.

### 8.1 Supplementary runner — compatibility class

```text
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
compat_exit=0
```

### 8.2 Supplementary runner — full Tholdem suite (final tree)

```text
TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0
full_exit=0
```

**Exact test count: 275** (was 274; one dedicated regression added, the sweep extended in place).
`docs/plan/test-waivers.json` records `"Wip.Tholdem.Tests": 275` (validated with
`ConvertFrom-Json`; all five top-level keys intact).

### 8.3 Solution build

```powershell
dotnet build Wip.slnx --configuration Release
```

```text
Build succeeded.
    125 Warning(s)
    0 Error(s)
Time Elapsed 00:01:15.08
final_solution_build_exit=0
```

(Warnings are the pre-existing solution warnings; no Tholdem project warning.)

### 8.4 Architecture

```text
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
final_architecture_exit=0
```

`tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs` asserts
`Assert.Equal(48, graph.Count);` inside that green run; the project count is unchanged at **48**.

### 8.5 Solution coverage

```text
solution=Wip.slnx
wip_project_count=48
solution_wip_project_count=48
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
final_coverage_exit=0
```

### 8.6 HarnessSync

```powershell
dotnet run --project tools/HarnessSync -- --check
```

```text
harnesssync_exit=0
```

(no output; all text files remain UTF-8 without BOM, including the two touched test files, the
waiver JSON and this document)

### 8.7 Grep proof

Over the test project's source files (`*.cs`, `*.csproj`, excluding `bin/`/`obj/` build output):

```text
source_files=62
source_grep_artifacts_tholdem=0
source_grep_literal_artifacts_slash_tholdem=0
old_v2_rule_occurrences=0
```

Log: `scratch/round6/grep-proof.txt`. No source or project file in `WIP/Wip.Tholdem.Tests`
references the operator `artifacts/tholdem` tree.

## 9. Coverage mapping to the required items

| Required | Where |
|---|---|
| True invariant: v1 = 2 seats + equal stacks; v2 = 2..6 seats, 2-seat v2 has unequal stacks | `HistorySessionFixture.cs:201`; §2; round-trip in `VerifyHandFile` re-derives the schema |
| Fixture root holds the three shapes generated with `MatchSimulator.Run` and per-seat stacks | `HistorySessionFixture.cs:54`; §4.1 |
| Schema-aware sweep verifies parse, canonical round-trip, SHA-256, replay; asserts all three shapes present/accepted | `HistorySessionCompatibilityTests.cs:47`; §4.2 |
| Dedicated 2-seat unequal-stack v2 regression passes the shared verification | `HistorySessionCompatibilityTests.cs:147`; red §3, mutation §7 |
| Keep v1 oracle and the v2-in-sessions regression; no vacuous test; no artifact dependency | §4.4; §6; §8.7 |
| Waiver count updated | 274 -> 275; §8.2 |
| Real CLI proof | §5; CLI `exit 0` and 2/2 hands PASS through the fixture |
| Mutation reproduces red, restore hash-verified and re-run | §7 |

## 10. Limits / what could not be verified

- **`dotnet test` was not run** (prohibited by the task, and the VSTest testhost aborts with
  `Win32Exception (5)` on this machine). The local evidence is the supplementary runner over the
  same compiled assembly; CI was not triggered or observed.
- **A literal fresh `git clone` was not performed.** The whole `WIP/Wip.Tholdem*` tree is currently
  untracked, so a clone would not contain it. The condition the tests depend on was simulated:
  the operator `artifacts/tholdem` tree was removed from the data path (renamed aside, suite green,
  restored hash-identical).
- **No CLI command can invoke the test fixture**, so the CLI proof used a temporary reflection probe
  (sources retained as `.txt`); the compiled probe directory was deleted after capture so the
  coverage scan still finds exactly 48 projects.
- **The exact `dotnet build Wip.slnx --config Release` spelling** (from an older task text) is
  rejected by MSBuild; the supported `--configuration Release` form was run and is green.
- The generated sessions use `RandomLegalAgent` only (the CLI proof used the default
  tight-aggressive/random-legal agents); the history tests do not exercise learning/strategy agent
  history output, which is covered elsewhere in the suite and was out of scope.
- No test was skipped or disabled and no assertion was weakened. All test-level verification items
  passed: 275/275, architecture 4/4 at 48 projects, coverage 48/48, HarnessSync 0.

## 11. Test count bookkeeping

- Before: **274** (`TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0` baseline).
- Added: 1 dedicated regression
  (`Parse_GivenTwoSeatUnequalStackV2HandInSessionsRoot_ExpectedSharedVerificationAcceptsV2SeatCount`).
- Modified in place: the three-shape sweep and the existing 4-seat v2 regression (same test
  methods, stronger/targeted bodies).
- After: **275** (`TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0`).
- `docs/plan/test-waivers.json`: `expectedTotals["Wip.Tholdem.Tests"] = 275`; no new waiver entry
  (no known failure, no aborted project, no skip).

Evidence logs and saved sources live under
`WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/round6/`.