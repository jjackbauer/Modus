# Round 5 — Self-contained history compatibility tests

**Status:** complete. The Tholdem suite is green at **274 tests, 0 failed, 0 skipped**. The two
history tests that read the git-ignored `artifacts/tholdem/sessions` tree were removed and replaced
by generated-fixture tests; a 4-seat v2 hand in a sessions root is now an explicit regression. No
engine, history product code, agent, viewer, CLI, solution, architecture-test or `.github/` file was
edited. `dotnet test` was not run; the compiled test assembly was executed with the established
supplementary xUnit runner `.tmp/tholdem-verify/RunXunit`.

## 1. Scope and environment

- Repository `C:\Users\ricar\dev\Modus`, branch `WIP`, tree sampled 2026-11-07 (America/Sao_Paulo).
- Files changed, all inside the allowed scope:
  - `WIP/Wip.Tholdem.Tests/HistorySessionFixture.cs` — **new** fixture helper.
  - `WIP/Wip.Tholdem.Tests/HistorySessionCompatibilityTests.cs` — **new**, 3 executed cases.
  - `WIP/Wip.Tholdem.Tests/MultiwayHistoryTests.cs` — deleted the two `artifacts/tholdem` tests;
    the six remaining compatibility/round-trip tests are byte-for-byte unchanged.
  - `docs/plan/test-waivers.json` — `"Wip.Tholdem.Tests": 273` → **274**.
  - this document.
- Not touched: `WIP/Wip.Tholdem/` (engine/history), `WIP/Wip.Tholdem.Agents/`,
  `WIP/Wip.Tholdem.Web/`, `WIP/Wip.Tholdem.Host/`, `Wip.slnx`,
  `tests/Wip.Architecture.Tests/`, `.github/`, `artifacts/`.
- All test scratch is created under the compiled test assembly directory
  (`WIP/Wip.Tholdem.Tests/bin/Release/net10.0/test-temp/…`) and deleted by the test's `finally`
  block. `%TEMP%` was not used.

## 2. Defect and root cause

The old sweep `MultiwayHistoryTests.Parse_GivenEveryRecordedV1HandFile_ExpectedAllHashesVerifyAndAllReplay`
enumerated **every** `hand-*.json` under `artifacts/tholdem/sessions` and asserted
`record.SeatCount == 2`. A legitimate 4-seat v2 session
(`artifacts/tholdem/sessions/tholdem-d56ac5f4637e44ef/hand-00001.json`,
`"schema":"tholdem.hand.v2"`, `"seats":4`) tripped that assertion. The companion test
`Parse_GivenV1HandFileFromArtifacts_ExpectedHashVerifiesAndReplayMatches` also read the same
directory. `artifacts/` is git-ignored (`git check-ignore` → `.gitignore:2` for `bin/`, `.gitignore:5`
for `artifacts/`), so neither test could run on a clean checkout.

The fix is not a relaxed assertion: the old `Assert.Equal(2, …)` is **gone**. Seat-count
expectations are now keyed by the file's schema: v1 ⇒ exactly 2 seats; v2 ⇒ 3..6 seats. The fixture
produces the operator layout itself with the public simulator.

## 3. Test-first evidence (red)

### 3.1 Red baseline before any change

Command (from the repository root, no `dotnet test`):

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed (log `scratch/round5/red-before.txt`):

```text
FAIL MultiwayHistoryTests.Parse_GivenEveryRecordedV1HandFile_ExpectedAllHashesVerifyAndAllReplay: EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   4
TOTAL 273 PASSED 272 FAILED 1 SKIPPED 0
red_exit=1
```

### 3.2 Regression written first, against the old assertion

Before the sweep was rewritten, `HistorySessionCompatibilityTests.cs` contained only the explicit
regression. The shared fixture still carried the old sweep's v1-only check
(`Assert.Equal(2, record.SeatCount)`), so the generated 4-seat v2 file in a sessions root failed
exactly like the recorded operator file:

```text
FAIL HistorySessionCompatibilityTests.Parse_GivenFourSeatV2HandInSessionsRoot_ExpectedV2VerificationWithoutV1OnlySeatExpectation: EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   4
TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0
regression_red_exit=1
```

(log `scratch/round5/red-regression.txt`)

Only after observing this failure were the sweep and the fixture's seat expectation made
schema-aware.

## 4. What was implemented

### 4.1 Fixture — `WIP/Wip.Tholdem.Tests/HistorySessionFixture.cs`

`CreateSessionsRoot(int v1Hands = 10, int v2Hands = 5)` calls the public
`MatchSimulator.Run(...)` twice into one scratch `sessions/` root with the recorded layout:

- `sessions/heads-up-v1/` — two `RandomLegalAgent` seats, equal 1000-chip stacks, seed 42. Because
  `HandRecordCanonicalWriter` only selects v1 for 2 equal-stack seats, every file is
  `tholdem.hand.v1` (no `schema` member).
- `sessions/four-seat-v2/` — four `RandomLegalAgent` seats, equal stacks, seed 9001. Every file is
  `tholdem.hand.v2` with `schema`, `seats`, `startingStacks`, per-seat `holeCards`, `pots` and
  `finalStacks`.

The fixture exposes `EnumerateHandFiles`, `ReadSchema` (JSON `schema` member; absent ⇒ v1) and the
single verification path used by both the sweep and the regression:

```csharp
public static HandRecord VerifyHandFile(string handPath)
{
    var bytes = File.ReadAllBytes(handPath);
    Assert.False(
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
        $"{handPath} must be UTF-8 without a BOM.");
    var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
        .GetString(bytes);
    var schema = ReadSchema(handPath);
    var record = HandRecordJson.Parse(text);

    Assert.Equal(text, record.ToCanonicalJson());
    Assert.Equal(Sha256Hex(bytes), record.Hash);
    Assert.Equal(record.Hash, Sha256Hex(new UTF8Encoding(false).GetBytes(text)));
    AssertSeatCountMatchesSchema(schema, record);

    var replayed = HoldemHandEngine.Replay(record);
    Assert.Equal(record.StartingStacks.Sum(), record.FinalStacks.Sum());
    Assert.Equal(record.FinalStacks.ToArray(), replayed.Stacks.ToArray());
    Assert.Equal(record.Winners.ToArray(), replayed.Winners.ToArray());
    Assert.Equal(record.Showdown, replayed.ShowdownReached);
    Assert.Equal(record.PotAwarded, replayed.AwardedPot);

    if (schema == HandRecordSchema.V2)
    {
        Assert.Equal(record.Pots.Count, replayed.Pots.Count);
        for (var pot = 0; pot < record.Pots.Count; pot++)
        {
            Assert.Equal(record.Pots[pot].Amount, replayed.Pots[pot].Amount);
            Assert.Equal(record.Pots[pot].Winners.ToArray(), replayed.Pots[pot].Winners.ToArray());
        }
    }

    return record;
}

public static void AssertSeatCountMatchesSchema(HandRecordSchema schema, HandRecord record)
{
    switch (schema)
    {
        case HandRecordSchema.V1:
            Assert.Equal(2, record.SeatCount);
            break;
        case HandRecordSchema.V2:
            Assert.InRange(record.SeatCount, 3, 6);
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(schema), schema, "Unknown hand schema.");
    }
}
```

`HoldemHandEngine.Replay` is used for both schemas; it validates streets, seats, pot-after values,
board, every seat's hole cards, final stacks, winners, showdown, awarded pot and (for 3+ seats) the
pot layers. The explicit assertions restate the recorded result so the test does not only trust the
product verifier.

### 4.2 New sweep — `HistorySessionCompatibilityTests.cs:44`

`Parse_GivenGeneratedV1AndV2SessionsInOneRoot_ExpectedSchemaAwareVerificationOfEveryHand`:

- generates 10 v1 + 5 v2 hands into one scratch `sessions/` root;
- enumerates every `hand-*.json` in the generated tree (ordered ordinally);
- asserts the root contains **both** a v1 and a v2 file (`Assert.Contains` on the schema map);
- for every file: parse, canonical round-trip, file-hash = record hash, schema-aware seat count,
  replay to the same final stacks/winners/showdown/award, and (v2) the same pot layers;
- asserts the counted files are exactly 10 v1 + 5 v2, matching the simulator's `Hands` values.

### 4.3 Explicit regression — `HistorySessionCompatibilityTests.cs:91`

`Parse_GivenFourSeatV2HandInSessionsRoot_ExpectedV2VerificationWithoutV1OnlySeatExpectation`:

- generates a 2-hand v1 session and a 3-hand 4-seat v2 session in one sessions root;
- picks the v2 hand files from that root and asserts there are 3;
- asserts the file really starts with `{"schema":"tholdem.hand.v2",` and that the record has
  `SeatCount == 4`, four hole-card lists, four starting stacks, four final stacks and non-empty pots;
- runs the same `VerifyHandFile` path as the sweep, i.e. the legitimate v2 hand is parsed,
  hash-verified and replayed under the v2 seat rule. It never encounters a v1-only expectation.
  Under the mutation (old `Assert.Equal(2, …)` restored) this test fails with
  `Expected: 2 / Actual: 4`; see §6.

### 4.4 Generated v1 single-file coverage — `HistorySessionCompatibilityTests.cs:14`

`Parse_GivenGeneratedV1HandFile_ExpectedCanonicalHashVerifiesAndReplayMatches` keeps the useful
coverage of the deleted artifact single-file test: a file generated through `MatchSimulator` is
read from disk, asserted to be the v1 shape (no `schema`, no `startingStacks`, `holeCardsSeat0/1`,
no v2 `holeCards` array), then canonical-round-tripped, SHA-256-verified against the file bytes and
replayed. The known 2-seat canonical shape is still pinned independently by
`MultiwayHistoryTests.From_GivenTwoSeatHand_ExpectedV1CanonicalJsonAndBaselineHashUnchanged`
(`c953464b…`), which was not modified.

### 4.5 Deletions

From `MultiwayHistoryTests.cs` only these two methods were removed:

- `Parse_GivenV1HandFileFromArtifacts_ExpectedHashVerifiesAndReplayMatches`
- `Parse_GivenEveryRecordedV1HandFile_ExpectedAllHashesVerifyAndAllReplay`

The remaining six tests (v1 pinned hash/round-trip, 4-seat v2 round-trip/replay, 4-seat fold v2,
3-seat unequal stacks, 2-seat unequal stacks, malformed seat count) are unchanged. No test was
skipped, disabled with `#if`, or had an assertion weakened; the suite count reflects the replacement
(273 − 2 + 3 = 274).

## 5. Clean-checkout simulation (observed)

The tests never consult `artifacts/`. To prove it, `artifacts/tholdem` (operator data, untouched and
still present) was renamed aside, the full suite ran, then the directory was restored. A per-file
SHA-256 manifest of all 47 files was taken before and after and compared.

```text
resolved_target=C:\Users\ricar\dev\Modus\artifacts\tholdem
final_artifacts_files_before=47
final_artifacts_present_during_run=False
final_clean_checkout_run_exit=0
TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0
final_artifacts_restored=True
final_artifacts_files_after=47
final_artifacts_tree_unchanged=True
```

(logs `scratch/round5/final-clean-checkout-run.txt`, `final-artifacts-before.txt`,
`final-artifacts-after.txt`; the same simulation was also run before the final cleanup and produced
the same 274/0/0 result)

This is the clean-checkout condition that matters for the tests: the entire Tholdem suite is green
with the operator sessions tree absent. No test generated, wrote to, or read
`artifacts/tholdem`.

## 6. Mutation evidence

After green, the old v1-only sweep expectation was restored **in isolation** in the shared
verification path (`AssertSeatCountMatchesSchema` body → `Assert.Equal(2, record.SeatCount);`).

```text
final_fixture_sha256=5eacd5149845d4c9227a9a7e0984299e8e4cc751c456363268f58af6b337e293
```

Mutation rebuild and class run:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
final_mutation_build_exit=0

FAIL HistorySessionCompatibilityTests.Parse_GivenFourSeatV2HandInSessionsRoot_ExpectedV2VerificationWithoutV1OnlySeatExpectation: EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   4
FAIL HistorySessionCompatibilityTests.Parse_GivenGeneratedV1AndV2SessionsInOneRoot_ExpectedSchemaAwareVerificationOfEveryHand: EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   4
TOTAL 3 PASSED 1 FAILED 2 SKIPPED 0
final_mutation_run_exit=1
```

(The v1-only single-file test still passed because its file is v1; the regression and the sweep
failed exactly as required.) Logs: `scratch/round5/final-mutation-build.txt`,
`final-mutation-run.txt`.

Restored from the saved copy and hash-verified, then force-rebuilt with `--no-incremental`:

```text
restored_sha256=5eacd5149845d4c9227a9a7e0984299e8e4cc751c456363268f58af6b337e293
matches_original=True
final_restored_build_exit=0
Build succeeded.
    2 Warning(s)
    0 Error(s)
```

(The two warnings are the pre-existing `Wip.Runtime` CA2024 warnings from the forced dependency
rebuild; no Tholdem warning.)

Restored full run:

```text
final-restored-full-run.txt: TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0
final-full-run.txt:          TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0
```

## 7. Grep proof

Over the test project's source files (`*.cs`, `*.csproj`, excluding `bin/`/`obj/` build output):

```powershell
$sourceFiles = Get-ChildItem -Path WIP\Wip.Tholdem.Tests -Recurse -File -Include *.cs,*.csproj |
    Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' }
@($sourceFiles | Select-String -Pattern 'artifacts[/\\]tholdem').Count
@($sourceFiles | Select-String -Pattern 'artifacts/tholdem' -SimpleMatch).Count
```

```text
source_files=62
source_grep_artifacts_tholdem=0
source_grep_literal_artifacts_slash_tholdem=0
```

(log `scratch/round5/grep-proof.txt`)

Honesty note: a **bare recursive** grep that deliberately ignores `.gitignore` still finds the
string `artifacts\tholdem` in old, git-ignored build-output logs under
`WIP/Wip.Tholdem.Tests/bin/Release/net10.0/round13-evidence/` (they quote the CLI's default
`--data-dir` text). Those are not test sources, not dependencies, are untracked/git-ignored
(`[Bb]in/`), and are cited by `docs/goal-tholdem/round-13/implementation.md`, so they were not
deleted. No source or project file in `WIP/Wip.Tholdem.Tests` references the operator tree.

## 8. Verification (observed output)

All commands from the repository root. `dotnet test` was **not** run.

### 8.1 Test project build

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
test_build_exit=0
```

### 8.2 Supplementary runner (full suite, final tree)

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0
final_full_run_exit=0
```

**Exact test count: 274** (was 273; two artifact tests removed, three generated-fixture tests added).
`docs/plan/test-waivers.json` records `"Wip.Tholdem.Tests": 274`.

### 8.3 Solution build

The literal command in the task uses `--config Release`, which MSBuild rejects (`--config` is not a
valid switch; see §11). The supported equivalent was run:

```powershell
dotnet build Wip.slnx --configuration Release
```

```text
Build succeeded.
    125 Warning(s)
    0 Error(s)
Time Elapsed 00:01:10.23
final_solution_build_exit=0
```

(Warnings are the pre-existing solution warnings; no Tholdem project warning.)

### 8.4 Architecture

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
```

```text
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
final_architecture_exit=0
```

`tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs:16` asserts `Assert.Equal(48, graph.Count)`
inside that green run; the project count is unchanged at **48**.

### 8.5 Solution coverage

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
```

```text
solution=Wip.slnx
wip_project_count=48
solution_wip_project_count=48
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
coverage_exit=0
```

### 8.6 HarnessSync

```powershell
dotnet run --project tools/HarnessSync -- --check
```

```text
harnesssync_exit=0
```

(no output; all text files remain UTF-8 without BOM, including the three touched test files and this
document)

## 9. Coverage mapping to the required items

| Required | Where |
|---|---|
| Generate hands in scratch via public `MatchSimulator.Run`; no test reads `artifacts/tholdem` | `HistorySessionFixture.CreateSessionsRoot`; both new test files; §5 clean-checkout run; §7 grep |
| Schema-aware sweep over generated tree; parse/round-trip/hash/replay; v1=2, v2=3..6; both present | `HistorySessionCompatibilityTests.cs:44` |
| Explicit 4-seat v2-in-sessions regression, old assertion gone (not relaxed) | `HistorySessionCompatibilityTests.cs:91`; §6 mutation fails it; only schema-aware `Assert.Equal(2)` for v1 remains |
| Keep known v1 canonical shape/round-trip and every generated hand replay to final stacks/hash | `MultiwayHistoryTests.cs:13` (pinned `c953464b…`), `HistorySessionFixture.VerifyHandFile`, generated v1 single-file test |
| No `artifacts/` dependency remains in the test project | §7, 0 source references |
| No skipped tests; suite count and waiver updated | 274 / 0 skipped; waiver 274 |

## 10. Limits / what could not be verified

- **`dotnet test` was not run** (prohibited by the task and unusable in this session: the VSTest
  testhost aborts with `Win32Exception (5)` on this machine). The local evidence is the
  supplementary runner over the same compiled assembly; CI was not triggered or observed.
- **The exact `dotnet build Wip.slnx --config Release` command could not be executed.** MSBuild
  rejects `--config` with `MSB1001: Unknown switch` (the SDK exposes only `-c, --configuration`).
  The supported `--configuration Release` form was run and is green. This is a CLI-spelling
  limitation, not a build failure.
- **A literal fresh `git clone` was not performed.** The whole `WIP/Wip.Tholdem*` tree is currently
  untracked in this workspace, so a clone would not contain it. The condition the tests depend on
  was simulated instead: the operator `artifacts/tholdem` tree was removed from the data path
  (renamed aside, suite green, restored hash-identical). Nothing in the test project resolves that
  path any more.
- The generated sessions use `RandomLegalAgent` only; the hand-history compatibility tests do not
  exercise the learning/strategy agents' history output. That coverage already exists elsewhere in
  the suite and was not in scope.
- No test was made to skip, and every test-level verification item passed (274/274, architecture
  4/4 at 48 projects, coverage 48/48, HarnessSync 0). The only unobserved/unavailable items are the
  external CI run and the exact `--config` spelling above.

## 11. Test count bookkeeping

- Before: **273** (`TOTAL 273 PASSED 272 FAILED 1 SKIPPED 0`).
- Removed: 2 artifact-dependent tests.
- Added: 3 generated-fixture tests (v1 single file, schema-aware mixed-root sweep, 4-seat v2
  regression).
- After: **274** (`TOTAL 274 PASSED 274 FAILED 0 SKIPPED 0`).
- `docs/plan/test-waivers.json`: `expectedTotals["Wip.Tholdem.Tests"] = 274`; no new waiver entry
  (no known failure, no aborted project, no skip).

Evidence logs and the pre-mutation fixture copy live under
`WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/round5/`.
