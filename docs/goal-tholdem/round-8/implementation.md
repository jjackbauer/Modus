# Round 8 — `Wip.Tholdem.Host` CLI, canonical hand parser, learning-loop persistence fixes

- **Date**: 2026-11-06
- **Branch**: `WIP`
- **Scope delivered**: a new `Wip.Tholdem.Host` CLI (`play`, `train`, `evaluate`, `promote`,
  `replay`, `retention`, `--help`, `--version`) with the required exit-code contract; the canonical
  `HandRecordJson` parser in `Wip.Tholdem.History`; the two learning-loop persistence fixes
  (per-run ledger accumulation and the loud champion-pointer failure); 28 new test facts; and the
  solution/architecture/waiver wiring. The WIP project count is now **48** and the Tholdem suite is
  **172/172 green**.
- **Not changed**: the engine (except the new parser file), the viewer, the runtime agent, the
  learning gate/verifier, `tools/run-wip-tests.ps1`, `.github/`, and every project outside the
  scope list.

## Files changed

### Created

| Path | Purpose |
|---|---|
| `WIP/Wip.Tholdem.Host/Wip.Tholdem.Host.csproj` | `net10.0`, `OutputType Exe`, ImplicitUsings/Nullable, references Tholdem, Agents, Abstractions and Runtime. No `InternalsVisibleTo`. |
| `WIP/Wip.Tholdem.Host/Program.cs` | Entry point: `return TholdemCli.Run(args, Console.Out, Console.Error);` plus `public partial class Program;`. |
| `WIP/Wip.Tholdem.Host/TholdemCli.cs` | Public, `Console`-free `Run(string[] args, TextWriter output, TextWriter error)`, exit-code constants, per-command option sets and usage text. |
| `WIP/Wip.Tholdem.Host/CliExceptions.cs` | Usage (2), artifact (4) and invariant (5) exceptions. |
| `WIP/Wip.Tholdem.Host/CliOptions.cs` | Deterministic long-option parser (`--name value` / `--name=value`, flags, duplicate/unknown rejection). |
| `WIP/Wip.Tholdem.Host/CliPaths.cs` | Default data directory, path resolution, safe strategy-id validation. |
| `WIP/Wip.Tholdem.Host/CliJson.cs` | Canonical, culture-free JSON primitives and invariant number formatting for the CLI files. |
| `WIP/Wip.Tholdem.Host/Storage/StrategyArtifactStore.cs` | Reads public strategy artifacts and the champion pointer as data; writes the pointer. |
| `WIP/Wip.Tholdem.Host/Storage/EvaluationFileStore.cs` | Writes/reads `cli-<challenger>-vs-<incumbent>.json` (`tholdem.cli-evaluation.v1`) including criteria and report. |
| `WIP/Wip.Tholdem.Host/Commands/PlayCommand.cs` | Runs a match into `sessions/<sessionId>/` (staging directory moved into place) and prints the metrics table or `--json` canonical report. |
| `WIP/Wip.Tholdem.Host/Commands/TrainCommand.cs` | Runs `TrainingLoop.Run`, prints one line per iteration + final champion (or `--json`). |
| `WIP/Wip.Tholdem.Host/Commands/EvaluateCommand.cs` | Loads both artifacts, evaluates held-out paired seeds, persists the CLI evaluation document and prints decision + verifier result (never promotes). |
| `WIP/Wip.Tholdem.Host/Commands/PromoteCommand.cs` | Re-reads the CLI evaluation, re-derives the policy verdict, re-runs `PromotionVerifier.Verify`, requires `--yes`, then rewrites the pointer. |
| `WIP/Wip.Tholdem.Host/Commands/ReplayCommand.cs` | Parses with `HandRecordJson`, proves file SHA-256 == record `Hash`, replays, prints the full hand. |
| `WIP/Wip.Tholdem.Host/Commands/RetentionCommand.cs` | `--status` counts sessions/hands/bytes/oldest date; `--purge --older-than-days` previews, then deletes with `--yes`. |
| `WIP/Wip.Tholdem/History/HandRecordJson.cs` | Canonical parser: fixed camelCase fields, card text via `Card.Parse`, typed actions, winners and stacks; every malformed input throws `FormatException` with a reason token. |
| `WIP/Wip.Tholdem.Tests/Host/HostCliTestSupport.cs` | Calls `TholdemCli.Run` directly; scratch directories under `AppContext.BaseDirectory/test-temp`, never `%TEMP%`. |
| `WIP/Wip.Tholdem.Tests/Host/PlayCommandTests.cs` | 3 facts: session files + metrics table, same-seed byte identity, `--json` canonical report. |
| `WIP/Wip.Tholdem.Tests/Host/TrainCommandTests.cs` | 2 facts: iteration line + final champion + pointer; ledger growth across two runs. |
| `WIP/Wip.Tholdem.Tests/Host/EvaluateCommandTests.cs` | 2 facts: canonical CLI evaluation file + decision + pointer untouched; missing challenger → 4. |
| `WIP/Wip.Tholdem.Tests/Host/PromoteCommandTests.cs` | 4 facts: no `--yes` → 3 and unchanged pointer; verifying + `--yes` → 0 and challenger pointer; below-margin + `--yes` → 3 and unchanged pointer; missing evaluation → 4. |
| `WIP/Wip.Tholdem.Tests/Host/ReplayCommandTests.cs` | 3 facts: recorded hand → 0 with board/hole cards/actions/winners/stacks; byte-tampered copy → 5; malformed/missing → 4. |
| `WIP/Wip.Tholdem.Tests/Host/RetentionCommandTests.cs` | 4 facts: status counts (text + JSON), preview without `--yes` → 3, confirmed purge → 0, malformed days → 2. |
| `WIP/Wip.Tholdem.Tests/Host/HelpCommandTests.cs` | 4 facts: `--help` → 0, `--version` → 0, unknown command → 2, unknown option → 2. |
| `WIP/Wip.Tholdem.Tests/History/HandRecordJsonTests.cs` | 3 facts: round-trip + hash for 3 played seeds, whitespace acceptance, four malformed inputs each throwing with a reason. |
| `docs/goal-tholdem/round-8/implementation.md` | This report. |

### Modified

| Path | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs` | **TODO-008**: `LoadLedger` reads the existing ledger, `UpsertLedger` appends/deduplicates on `(iteration, championId, challengerId)`, and every iteration writes the combined array. **TODO-009**: `LoadChampion` no longer falls back silently — a present-but-broken pointer throws `InvalidOperationException` naming the pointer path and strategy id; a missing pointer is written for the version-1 champion actually used. `TrainingRun.LedgerJson` now reflects the combined persisted ledger. |
| `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj` | `ProjectReference` to `..\Wip.Tholdem.Host\Wip.Tholdem.Host.csproj`. |
| `WIP/Wip.Tholdem.Tests/Learning/TrainingLoopTests.cs` | 3 new facts (ledger accumulation/dedup, corrupt pointer throw, fresh-directory version-1 pointer). Two obsolete assertions that required the *absent* pointer were replaced by stronger content assertions on the pointer the new contract requires. |
| `WIP/Wip.Tholdem.Tests/Learning/LearningWebIntegrationTests.cs` | Follows the two fixed behaviours: `/api/champion` after a fresh run is now the version-1 pointer (was 404), and the second run's ledger contains its own entries **plus** the first run's (was “own ledger only”). Assertions were strengthened, not removed. |
| `Wip.slnx` | Host project added inside `/WIP/Tholdem/`. |
| `tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs` | Project-graph count `47 → 48`. |
| `tests/Wip.Architecture.Tests/ArchitectureRules.cs` | New rule `tholdem-host-no-bones` (Host may not reach Bones/Shell/ShellHost). |
| `docs/plan/test-waivers.json` | `expectedTotals["Wip.Tholdem.Tests"]` `144 → 172` (the real total; waivers stay zero). |

## Behaviour notes (contract-level)

- **Exit codes** are exactly `0 ok`, `2 usage`, `3 refused`, `4 missing/invalid artifact`,
  `5 replay invariant failure`. Unknown command/option prints usage on **stderr** and writes
  nothing to stdout.
- **`play`** runs `MatchSimulator` into `<data>/sessions/.inprogress`, then moves the directory to
  `<data>/sessions/<report.SessionId>`, so the session id (computed inside the simulator) names the
  directory without duplicating its derivation. The table has `bb/100`, both 95% intervals, win
  rate, aggression, showdown rate and the manifest SHA-256. Re-running the same seed deletes and
  rewrites the same directory with byte-identical files.
- **`train`** uses the loop defaults (120 training hands, 24 held-out seeds, 10 hands per match) and
  prints `iteration=… champion=… challenger=… promoted=… verified=… outcome=… meanBbPer100=… lcb=… seeds=…`
  per iteration plus `final champion: …`.
- **`evaluate`** writes exactly
  `{"schema":"tholdem.cli-evaluation.v1","criteria":{"marginBbPer100":…,"minSeeds":24},"report":<report canonical JSON>}`,
  prints `decision: <promoted|lcb-below-margin|insufficient-seeds>` and the verifier result, and
  never writes the pointer. A missing challenger artifact is exit 4.
- **`promote`** re-derives the `StatisticalPromotionPolicy` verdict from the persisted report and
  criteria, then **always** runs `PromotionVerifier.Verify`. A rejection prints
  `promotion refused: <reason>` and exits 3; a verified finding without `--yes` prints
  `promotion requires explicit confirmation; re-run with --yes` and exits 3. Only
  `verified && promoted && --yes` rewrites `<data>/active-champion.json`.
- **`replay`** hashes the raw file bytes (not the re-serialization). A bit/byte change that still
  parses (for example a trailing space) is therefore a **hash mismatch → 5**; an unparsable or
  missing file is **4**. `HeadsUpHandEngine.Replay` failures are wrapped as **5**.
- **`retention`** counts immediate session directories that contain `session.json`; hands are
  `hand-*.json` files, bytes are all files beneath `sessions/`, and the oldest date is the minimum
  `session.json` last-write time in UTC. Purge compares that same timestamp with
  `UtcNow.AddDays(-n)` — the clock read lives only in this CLI command, never in `Wip.Tholdem` or
  `Wip.Tholdem.Agents`.
- **`HandRecordJson`** accepts the canonical property names in any order and with arbitrary
  whitespace, but validates every required field, integer width, card text (`Card.Parse`), known
  action kinds, zero amounts for non-bet/raise actions, non-negative pots/stacks, exactly two hole
  cards per seat and exactly two final stacks. Errors carry a stable token such as
  `hand-field-missing:`, `hand-card-invalid:` or `hand-action-invalid:`.
- **No engine internals**: Host uses only `HandRecord`, `HeadsUpHandEngine.Replay`, the public
  simulation/learning types and its own public-schema JSON readers. `Wip.Tholdem` grants
  `InternalsVisibleTo` only to `Wip.Tholdem.Tests`.

## Test-first evidence (red)

The 28 facts and the two learning assertions were written first. To make the red run execute the
real assertions instead of failing to compile, a temporary shell of the Host (`Run` returned 2 for
every command) and of `HandRecordJson` (threw `FormatException("…not implemented yet")`) was
compiled; both were then replaced by the implementation below. The pre-existing 144 facts stayed in
the run.

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    1 Warning(s)
    0 Error(s)
RED_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
FAIL HandRecordJsonTests.Parse_GivenCanonicalJsonWithWhitespace_ExpectedSameRecord: FormatException: hand-json: the canonical hand-record parser is not implemented yet.
FAIL HandRecordJsonTests.Parse_GivenMalformedInputs_ExpectedFormatExceptionWithReason: ContainsException: Assert.Contains() Failure: Sub-string not found
FAIL HandRecordJsonTests.Parse_GivenPlayedHandsFromDifferentSeeds_ExpectedRoundTripAndMatchingHash: FormatException: hand-json: the canonical hand-record parser is not implemented yet.
FAIL EvaluateCommandTests.Run_GivenArtifactsAndChampionPointer_ExpectedCanonicalCliFileAndDecision: EqualException: Assert.Equal() Failure: Values differ
FAIL EvaluateCommandTests.Run_GivenMissingChallengerArtifact_ExpectedExitCode4: EqualException: Assert.Equal() Failure: Values differ
FAIL HelpCommandTests.Run_GivenHelp_ExpectedUsageListsEveryCommandAndExit0: EqualException: Assert.Equal() Failure: Values differ
FAIL HelpCommandTests.Run_GivenUnknownCommand_ExpectedUsageOnErrorAndExit2: ContainsException: Assert.Contains() Failure: Sub-string not found
FAIL HelpCommandTests.Run_GivenUnknownOption_ExpectedExit2: ContainsException: Assert.Contains() Failure: Sub-string not found
FAIL HelpCommandTests.Run_GivenVersion_ExpectedSemanticVersionAndExit0: EqualException: Assert.Equal() Failure: Values differ
FAIL PlayCommandTests.Run_GivenJsonFlag_ExpectedCanonicalReportOnly: EqualException: Assert.Equal() Failure: Values differ
FAIL PlayCommandTests.Run_GivenSameSeedTwice_ExpectedByteIdenticalSessionFiles: EqualException: Assert.Equal() Failure: Values differ
FAIL PlayCommandTests.Run_GivenSeedAndHands_ExpectedSessionManifestHandFilesAndMetricsTable: EqualException: Assert.Equal() Failure: Values differ
FAIL PromoteCommandTests.Run_GivenBelowMarginEvaluationWithYes_ExpectedExit3AndUnchangedPointer: EqualException: Assert.Equal() Failure: Values differ
FAIL PromoteCommandTests.Run_GivenMissingEvaluationFile_ExpectedExitCode4: EqualException: Assert.Equal() Failure: Values differ
FAIL PromoteCommandTests.Run_GivenVerifyingEvaluationWithYes_ExpectedExit0AndChallengerPointer: EqualException: Assert.Equal() Failure: Values differ
FAIL PromoteCommandTests.Run_GivenVerifyingEvaluationWithoutYes_ExpectedExit3AndUnchangedPointer: EqualException: Assert.Equal() Failure: Values differ
FAIL ReplayCommandTests.Run_GivenByteTamperedCopy_ExpectedExit5: EqualException: Assert.Equal() Failure: Values differ
FAIL ReplayCommandTests.Run_GivenMalformedFile_ExpectedExit4: EqualException: Assert.Equal() Failure: Values differ
FAIL ReplayCommandTests.Run_GivenRecordedHand_ExpectedExit0BoardHoleCardsActionsWinnersAndStacks: EqualException: Assert.Equal() Failure: Values differ
FAIL RetentionCommandTests.Purge_GivenConfirmation_ExpectedExit0AndDeletedSession: EqualException: Assert.Equal() Failure: Values differ
FAIL RetentionCommandTests.Purge_GivenMalformedOlderThanDays_ExpectedExit2: ContainsException: Assert.Contains() Failure: Sub-string not found
FAIL RetentionCommandTests.Purge_GivenNoConfirmation_ExpectedExit3PreviewAndNothingDeleted: EqualException: Assert.Equal() Failure: Values differ
FAIL RetentionCommandTests.Status_GivenSeedSession_ExpectedSessionHandsBytesAndOldestDate: EqualException: Assert.Equal() Failure: Values differ
FAIL TrainCommandTests.Run_GivenOneIteration_ExpectedIterationLineFinalChampionLedgerAndPointer: EqualException: Assert.Equal() Failure: Values differ
FAIL TrainCommandTests.Run_GivenTwoRunsIntoOneDirectory_ExpectedLedgerGrowsWithBothRunsEntries: EqualException: Assert.Equal() Failure: Values differ
FAIL TrainingLoopTests.Run_GivenAlwaysPromotePolicy_ExpectedVerificationFailureAndUnchangedChampion: TrueException: A fresh data directory must persist its champion pointer.
FAIL TrainingLoopTests.Run_GivenCorruptChampionPointer_ExpectedInvalidOperationExceptionNamingPointerAndStrategy: ThrowsException: Assert.Throws() Failure: No exception was thrown
FAIL TrainingLoopTests.Run_GivenExistingLedger_ExpectedEntriesFromBothRunsAndDeduplication: EqualException: Assert.Equal() Failure: Values differ
FAIL TrainingLoopTests.Run_GivenFreshDirectory_ExpectedPointerToVersionOneChampionActuallyUsed: TrueException: A fresh run must persist C:\Users\ricar\dev\Modus\.tmp\tholdem-verify\RunXunit\bin\Release\net10.0\test-temp\tholdem-learning-fresh-pointer-7ef14749323e46d99a749c6903a44d0b\active-champion.json.
FAIL TrainingLoopTests.Run_GivenTrainingOptions_ExpectedPersistedStrategyEvaluationLedgerAndChampionSchema: TrueException: Assert.True() Failure
TOTAL 172 PASSED 142 FAILED 30 SKIPPED 0
RED_RUN_EXIT=1
```

Three implementation-time test corrections, all fixture bugs in the new tests (no production
assumption was relaxed):

1. `PlayCommandTests` initially asserted that the *hand* hash was printed; the contract prints the
   **manifest** hash, so the test now recomputes `SHA256(session.json)` and asserts exactly that.
2. `HandRecordJsonTests` initially assumed the record `seed` equals the *session* seed; the
   simulator derives a per-hand seed, so the test now compares against the pre-existing
   `AgentTestSupport.ParseRecord` seed instead.
3. `EvaluateCommandTests` exposed an ordering issue: the CLI resolved the incumbent pointer before
   loading the requested challenger. The command now loads the challenger first, so a missing
   challenger is reported even when no pointer exists yet (the intended exit-4 diagnostic).

## Green evidence

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

FINAL_TEST_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 172 PASSED 172 FAILED 0 SKIPPED 0
FINAL_RUN_EXIT=0
```

## Mutation

Mutant: `PromoteCommand` reports the policy/verifier verdict but **ignores it** and promotes on
`--yes` alone (the below-margin guard was removed).

Pre-mutation and backup SHA-256 of `WIP/Wip.Tholdem.Host/Commands/PromoteCommand.cs`:

```text
pre_mutation_sha256=67a29e41dbdf0c72da9991530158a52da435cf33406a670d2d288703aa2c2c44
backup_sha256=67a29e41dbdf0c72da9991530158a52da435cf33406a670d2d288703aa2c2c44
match=True
```

Forced rebuild of the mutant and the full suite:

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental
    2 Warning(s)
    0 Error(s)
MUTANT_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
FAIL PromoteCommandTests.Run_GivenBelowMarginEvaluationWithYes_ExpectedExit3AndUnchangedPointer: EqualException: Assert.Equal() Failure: Values differ
TOTAL 172 PASSED 171 FAILED 1 SKIPPED 0
MUTANT_EXIT=1
```

Exactly the intended fact fails: the below-margin evaluation with `--yes` is now wrongly promoted
(exit 0 instead of 3, pointer moved).

Restored from the backup and verified by hash:

```text
original_sha256=67a29e41dbdf0c72da9991530158a52da435cf33406a670d2d288703aa2c2c44
restored_sha256=67a29e41dbdf0c72da9991530158a52da435cf33406a670d2d288703aa2c2c44
match_pre_mutation=True
mutant_marker_removed=True
```

Forced rebuild and green re-run after the restore:

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental
    2 Warning(s)
    0 Error(s)
RESTORED_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 172 PASSED 172 FAILED 0 SKIPPED 0
RESTORED_EXIT=0
```

(The two build warnings are the pre-existing `Wip.Runtime` `CS8425`/`CA2024` warnings; no
Tholdem project emits a warning.)

One post-mutation hardening edit: `promote` no longer rejects a persisted report whose
`challengerId`/`incumbentId` strings differ from the CLI arguments (the file name already scopes the
evaluation; the contract never asked for an identity re-check). The verifier gate is the same code
exercised above. The final tree with that edit was re-run: `TOTAL 172 PASSED 172 FAILED 0`, and the
final CLI invocations below all come from the rebuilt Release binary.

## Verification commands (final tree, exact observed output)

### 1. Test project build + supplementary runner

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

FINAL_TEST_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 172 PASSED 172 FAILED 0 SKIPPED 0
FINAL_RUN_EXIT=0
```

### 2. Whole solution

```text
$ dotnet build Wip.slnx --configuration Release
    6 Warning(s)
    0 Error(s)

Time Elapsed 00:00:26.60
FINAL3_SOLUTION_PLAIN_EXIT=0

$ dotnet build Wip.slnx --configuration Release --no-incremental
    125 Warning(s)
    0 Error(s)

Time Elapsed 00:01:32.10
FINAL3_SOLUTION_BUILD_EXIT=0
```

The 125 warnings are the pre-existing solution-wide set (incremental repeats of this build report
6–126 depending on which project graph is recompiled); no `Wip.Tholdem*` warning is among them.

### 3. Architecture runner

```text
$ dotnet build tests/Wip.Architecture.Tests -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

FINAL_ARCH_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
FINAL_ARCH_RUN_EXIT=0
```

`ArchitectureReferenceTests.ProjectGraph_GivenWipSolution_ExpectedAllWipProjects` now asserts
`Assert.Equal(48, graph.Count)` and passes; the new `tholdem-host-no-bones` rule is evaluated and
its source is present in the graph.

### 4. Solution coverage

```text
$ powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
solution=Wip.slnx
wip_project_count=48
solution_wip_project_count=48
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
FINAL_COVERAGE_EXIT=0
```

### 5. Harness sync

```text
$ dotnet run --project tools/HarnessSync -- --check
FINAL_HARNESS_EXIT=0
```

`HarnessSync --check` prints nothing on success; the exit code is the observation. It also confirms
every new text file is UTF-8 without BOM (a separate byte scan of the Host sources, the parser, the
new tests, the solution, the architecture files, the waiver register and this report found no BOM and
no UTF-16).

## CLI execution against the built Host (final Release binary)

Data directory: `.tmp/round8-cli-final3` (workspace scratch, not `%TEMP%`). Executable:
`WIP/Wip.Tholdem.Host/bin/Release/net10.0/Wip.Tholdem.Host.exe`.

### `play --seed 42 --hands 20`

```text
session: tholdem-06795fbeacfe528b
seed: 42  hands: 20  manifest sha256: 4b8ea23bb488ddb7d9a62a35fa7cecc35310d3b50d0625a151fac080d0aef2c7
agent                    hands    bb/100       95% CI (bb/100)  win rate     95% CI (win rate)  aggression  showdown
tight-aggressive            20  -1872.50   -3466.99 .. -278.01      0.15          0.05 .. 0.36        0.22      0.15
random-legal                20   1872.50     278.01 .. 3466.99      0.85          0.64 .. 0.95        3.25      0.15
PLAY_EXIT=0
```

### `train --iterations 1 --seed 7`

```text
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-0ceaa8fd promoted=false verified=true outcome=rejected meanBbPer100=-19.38 lcb=-59.50 seeds=24
final champion: tholdem-strategy-v1-f100e793
TRAIN_EXIT=0
```

### `evaluate --challenger tholdem-strategy-v2-0ceaa8fd --seeds 4`

```text
evaluation: C:\Users\ricar\dev\Modus\.tmp\round8-cli-final3\evaluations\cli-tholdem-strategy-v2-0ceaa8fd-vs-tholdem-strategy-v1-f100e793.json
challenger: tholdem-strategy-v2-0ceaa8fd  incumbent: tholdem-strategy-v1-f100e793  seeds: 4  hands per match: 10
mean bb/100: 2.50  LCB: -0.33  margin: 5.00
decision: lcb-below-margin
verifier: verified (rejection-justified)
EVALUATE_EXIT=0
```

The persisted evaluation document (canonical, single line):

```text
{"schema":"tholdem.cli-evaluation.v1","criteria":{"marginBbPer100":5,"minSeeds":24},"report":{"challengerId":"tholdem-strategy-v2-0ceaa8fd","incumbentId":"tholdem-strategy-v1-f100e793","seeds":4,"handsPerMatch":10,"bigBlind":10,"meanBbPer100":2.5,"bbPer100Ci":{"low":-0.32896433519042967,"high":5.328964335190429},"lowerBoundBbPer100":-0.32896433519042967,"challengerWins":5,"incumbentWins":3,"ties":0,"challengerNetChips":20}}
```

`evaluate --json` prints that same canonical document instead of the human summary:

```text
$ Wip.Tholdem.Host.exe evaluate --challenger tholdem-strategy-v2-0ceaa8fd --seeds 4 --data-dir .tmp/round8-cli-final3 --json
{"schema":"tholdem.cli-evaluation.v1","criteria":{"marginBbPer100":5,"minSeeds":24},"report":{"challengerId":"tholdem-strategy-v2-0ceaa8fd","incumbentId":"tholdem-strategy-v1-f100e793","seeds":4,"handsPerMatch":10,"bigBlind":10,"meanBbPer100":2.5,"bbPer100Ci":{"low":-0.32896433519042967,"high":5.328964335190429},"lowerBoundBbPer100":-0.32896433519042967,"challengerWins":5,"incumbentWins":3,"ties":0,"challengerNetChips":20}}
EVALUATE_JSON_EXIT=0
```

### `promote --challenger tholdem-strategy-v2-0ceaa8fd` (expect 3)

```text
promotion requires explicit confirmation; re-run with --yes
promotion refused: lcb-below-margin
verifier: rejection-justified
PROMOTE_EXIT=3
```

The pointer was not moved by the evaluate or the refused promote:

```text
{"strategyId":"tholdem-strategy-v1-f100e793","iteration":0}
```

### `replay ...\sessions\tholdem-06795fbeacfe528b\hand-00001.json` (expect 0)

```text
hand: 1
board: 8h 8s 8d Th 6c
seat 0: Qs 4s
seat 1: Tc Jd
actions:
  preflop seat=0 kind=Call amount=0 potAfter=20
  preflop seat=1 kind=AllIn amount=0 potAfter=1010
  preflop seat=0 kind=Call amount=0 potAfter=0
showdown: true
winners: 1
pot awarded: 2000
final stacks: 0 / 2000
replayed stacks: 0 / 2000  pot: 0
REPLAY_EXIT=0
```

### `retention --status`

```text
sessions: 1
hands: 20
bytes: 10283
oldest session: 2026-11-06T22:51:24.974Z
RETENTION_EXIT=0
```

### `--help`

```text
tholdem 0.1.0

usage:
  tholdem play --seed <n> [--hands <n>=200]
               [--agent0 <random-legal|tight-aggressive|pot-odds-equity>]
               [--agent1 <random-legal|tight-aggressive|pot-odds-equity>]
               [--json] [--data-dir <path>]
  tholdem train --iterations <n> --seed <n> [--json] [--data-dir <path>]
  tholdem evaluate --challenger <id> --seeds <n> [--hands <n>=10]
               [--incumbent <id>] [--margin <bb/100>=5] [--seed <n>=42]
               [--json] [--data-dir <path>]
  tholdem promote --challenger <id> [--incumbent <id>] [--yes] [--data-dir <path>]
  tholdem replay <hand.json> [--data-dir <path>]
  tholdem retention --status [--json] [--data-dir <path>]
  tholdem retention --purge --older-than-days <n> [--yes] [--data-dir <path>]
  tholdem --help | --version

  --data-dir <path>  artifact directory (default: <current directory>\artifacts\tholdem)

promote never auto-promotes: it requires explicit confirmation (--yes) and a
verified lower confidence bound above the configured margin.

exit codes: 0 ok, 2 usage error, 3 refused, 4 missing/invalid artifact, 5 replay invariant failure.
HELP_EXIT=0
```

## Test count

**172 test cases** (0 `[Theory]`, 0 skips): 144 pre-existing + **28 new `[Fact]` methods**.

| New facts | Count | Covers |
|---|---|---|
| `PlayCommandTests` | 3 | session layout, metrics table + manifest hash, same-seed byte identity, `--json` |
| `TrainCommandTests` | 2 | iteration/final-champion output + fresh pointer, ledger growth across runs |
| `EvaluateCommandTests` | 2 | canonical CLI evaluation + decision, missing challenger → 4 |
| `PromoteCommandTests` | 4 | refusal without `--yes`, verified promotion with `--yes`, below-margin refusal, missing file |
| `ReplayCommandTests` | 3 | full replay output, tampered bytes → 5, malformed/missing → 4 |
| `RetentionCommandTests` | 4 | status text+JSON, preview, confirmed purge, malformed days |
| `HelpCommandTests` | 4 | help, version, unknown command, unknown option |
| `HandRecordJsonTests` | 3 | round-trip + hash on 3 seeds, whitespace, four malformed cases |
| `TrainingLoopTests` | 3 | ledger accumulation/dedup, corrupt-pointer throw, fresh pointer |

`docs/plan/test-waivers.json` parses, its `expectedTotals["Wip.Tholdem.Tests"]` is **172**, and the
waiver/failure lists are unchanged (zero new waivers).

## What I could not verify / could not make pass

- **Nothing in the required verification set failed.** The test suite, solution build, architecture
  runner, coverage script, harness check, all seven CLI invocations and the mutation cycle were all
  observed on this machine with the output above.
- `dotnet test` was **not** run (forbidden by the dispatch; the local VSTest host aborts with
  `Win32Exception (5)` as documented in `docs/goal-tholdem/goal.md`). The green claim is the
  supplementary `RunXunit` runner over the compiled test assembly, which executes the same facts
  sequentially; CI is not observable from here.
- `pwsh` is absent; the coverage script was run with Windows PowerShell exactly as requested.
- The viewer was not started and no screenshot was produced for this round (out of scope).
- `Wip.Tholdem.Host` is the only place that reads the clock (`retention --purge` needs `UtcNow`);
  the engine and agent libraries remain clock-free. No `System.Random`, GUID or ambient randomness
  is used anywhere in Host.
