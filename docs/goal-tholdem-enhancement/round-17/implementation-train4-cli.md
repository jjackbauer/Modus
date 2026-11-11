# Round 17 — train4 CLI (implementer)

Scope: the additive `train4` command specified in
`docs/goal-tholdem-enhancement/round-17/design.md`, section 3, on top of the completed
`FourSeatTraining` loop. Only the four files in the table below were created or edited. The v1
commands, `FourSeatTraining`, the evaluator/gate/verifier and every pre-existing test are
untouched. No `dotnet test` was run.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Host/Commands/Train4Command.cs` | New: option handling, run, text and canonical JSON output. |
| `WIP/Wip.Tholdem.Host/TholdemCli.cs` | Edit: `train4` flags, valued options, dispatch and usage only. |
| `WIP/Wip.Tholdem.Tests/Host/Train4CommandTests.cs` | New: one fact. |
| `docs/goal-tholdem-enhancement/round-17/implementation-train4-cli.md` | New: this record. |

## Command surface

```text
tholdem train4 --seed <strong> --iterations <n> [--selection-blocks <n>=8]
               [--selection-hands <n>=4] [--holdout-blocks <n>=16]
               [--holdout-hands <n>=4] [--margin <bb/100>=5]
               [--json] [--data-dir <path>] [--allow-weak-seed]
```

- `--seed` is required and goes through
  `SessionSeedPolicy.RequireStrongOrExplicitOptIn(seed, --allow-weak-seed)`, exactly like
  `train`.
- `--data-dir` is resolved by `CliPaths.ResolveDataDirectory`, exactly like `train`.
- `--iterations` is required and must be at least 0; the four block/hand options must each be at
  least 1; `--margin` must be finite and non-negative. Bad values are usage errors (exit 2)
  before any artifact is written.
- The command calls `FourSeatTraining.Run(new FourSeatTrainingOptions(...))` and passes only the
  seed, iterations, data directory, the four block/hand counts and the margin. Game, pool,
  compositions, starting genome, confidence, anchor tolerance, anchor composition and proposer all
  keep the loop's designed defaults (`FourSeatCash`, `SelectionV1`, `["primary-mixed"]`, default
  genome, deterministic proposer with the neutral analysis).
- Text mode prints one line per iteration:

  ```text
  iteration=<n> champion=<id> challenger=<id> outcome=<outcome> verdict=<verdict> reason=<reason> meanBbPer100=<n>
  ```

  then one `final champion: <id>` line. `--json` prints a whitespace-free canonical object with
  schema `tholdem.cli-train4.v1`, the seed and run settings, every iteration row, the final
  champion id and the loop's `runHash`. Numbers use `CliJson.Number`/`CliJson.Format`
  (invariant culture); strings use `CliJson.Text`.

## TholdemCli edit

Added `Train4Flags = Flags("json", "allow-weak-seed")` and
`Train4Valued = Valued("seed", "iterations", "selection-blocks", "selection-hands",
"holdout-blocks", "holdout-hands", "margin", "data-dir")`; added the
`"train4"` dispatch arm between `train` and `evaluate`; added the four usage lines after
`train`; and added `train4` to the seed-policy sentence in the usage footer. No other behavior
changed.

## Red (observed before implementation)

The test was written first against the existing CLI surface, so no stub was needed: the command was
absent from dispatch and the CLI answered with its usage error. Build observed `Build succeeded.`,
`12 Warning(s)`, `0 Error(s)` (the test file compiles; the 12 are pre-existing warnings surfaced
by the incremental build).

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Host.Train4CommandTests
```

Observed targeted run, exit code 1:

```text
FAIL Train4CommandTests.Run_GivenTinyRun_WritesFourSeatArtifactsAndNoV1Pointer: EqualException: Assert.Equal() Failure: Values differ
Expected: 0
Actual:   2
TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0
```

The actual exit 2 is `unknown command 'train4'` from the unchanged dispatch; no artifacts were
created.

## Green (observed after implementation)

The same build command observed `Build succeeded.`, `14 Warning(s)`, `0 Error(s)` (the two
additional warnings are the pre-existing nullable warnings in `TrainCommand.cs`, surfaced when the
Host project rebuilt). A clean `--no-incremental` rebuild reports `16 Warning(s)`, `0 Error(s)`
(two more pre-existing warnings are in `Wip.Runtime`); no warning names `Train4Command.cs`,
`TholdemCli.cs` or `Train4CommandTests.cs`.

Targeted class, exit code 0:

```text
TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0
```

Full suite, exit code 0:

```text
TOTAL 575 PASSED 575 FAILED 0 SKIPPED 0
```

That is the recorded 574-fact baseline plus the one new fact.

## Observed command output

Run from the repository root against a scratch data directory under `.tmp` (deleted after the
observation):

```powershell
dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train4 --seed 1099511627776 --iterations 1 --selection-blocks 1 --selection-hands 1 --holdout-blocks 1 --holdout-hands 1 --margin 5 --data-dir .tmp/train4-cli-observation
```

Observed stdout, exit code 0:

```text
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-4ac1f97b outcome=rejected-selection verdict=Rejected reason=selection-mean-not-positive meanBbPer100=0.00
final champion: tholdem-strategy-v1-f100e793
```

The seed is exactly `SessionSeedPolicy.MinimumSeed` (strong), so no `--allow-weak-seed` was
needed. With one selection block and one hand the default-proposer challenger's paired selection
mean was 0, so the iteration stopped at selection: no holdout spend, no decision, no promotion.

## Four-seat file layout (observed)

```text
<data>/four-seat/
  reports/iter-1-selection.json
  strategies/tholdem-strategy-v1-f100e793.json
  strategies/tholdem-strategy-v2-4ac1f97b.json
```

There is no `four-seat/active-champion.json` because the verdict was not
`VerifiedImprovement`, and there is no `iter-1-holdout.json` or `iter-1-decision.json` for the
same reason. The one-file-per-artifact names are the loop's content-addressed names; every file is
canonical UTF-8 without BOM.

## JSON smoke (observed)

The same tiny run with `--json` was parsed by `ConvertFrom-Json`: schema
`tholdem.cli-train4.v1`, final champion `tholdem-strategy-v1-f100e793`, one iteration row,
`runHash=5b6f1d2bdb1979f40bd7bdd33ed1b2bacdb9faae3138d1235a2adc5f998c77b0`, and
`V1POINTER=False`. The scratch directory was deleted afterwards.

## v1 pointer absent (observed)

After the CLI run above, the check `Test-Path <data>/active-champion.json` observed:

```text
V1POINTER=False
```

The test asserts the same absence after its tiny run. `Train4Command` never references a v1
pointer, ledger, `strategies/` or `evaluations/` path; the only writes are performed by
`FourSeatTraining` under `<data>/four-seat/`.

## Test added

`WIP/Wip.Tholdem.Tests/Host/Train4CommandTests.cs`:

`Run_GivenTinyRun_WritesFourSeatArtifactsAndNoV1Pointer` — a strong seed
(`1099511627776`), 1 iteration, 1/1/1/1 blocks/hands, margin 5 and a temp data directory. It
asserts exit 0, empty stderr, that `four-seat/strategies` and `four-seat/reports` exist, that
exactly one `iteration=1` line and a `final champion:` line were printed, and that
`<data>/active-champion.json` does not exist.

## Commands run

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Host.Train4CommandTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental
dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train4 --seed 1099511627776 --iterations 1 --selection-blocks 1 --selection-hands 1 --holdout-blocks 1 --holdout-hands 1 --margin 5 --data-dir .tmp/train4-cli-observation
```

No `dotnet test`. No repository file outside the Files table was created or edited; the scratch
data directory was deleted after the observed run.