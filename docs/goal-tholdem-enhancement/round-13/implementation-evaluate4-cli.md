# Round 13b — additive evaluate4 CLI (implementer)

Scope: the additive `evaluate4` command exactly as specified in
`docs/goal-tholdem-enhancement/round-13/design-cli.md`, built on the strict plan parser
`EvaluationPlanV2Json.Parse`. Only the files listed below were created or edited; the v1 commands,
evaluator, gate, verifier, plan parser, strategy store and every existing test are untouched.

## Files

- `WIP/Wip.Tholdem.Host/Commands/Evaluate4Command.cs` (new)
- `WIP/Wip.Tholdem.Host/TholdemCli.cs` (edit: `Evaluate4Flags`, `Evaluate4Valued`, dispatch and two usage lines)
- `WIP/Wip.Tholdem.Tests/Host/Evaluate4CommandTests.cs` (new, five facts)
- `docs/goal-tholdem-enhancement/round-13/implementation-evaluate4-cli.md` (new, this file)

## Command

```text
tholdem evaluate4 --plan <path> --challenger <id> --incumbent <id>
                 [--anchor-plan <path>] [--out <dir>] [--json] [--data-dir <path>]
```

`--plan` is parsed with `EvaluationPlanV2Json.Parse`; a `FormatException` is wrapped in
`CliArtifactException` (exit 4). The pool is resolved from `plan.OpponentPoolId`:
`tholdem.opponents.v1` -> `FrozenOpponentPool.SelectionV1`,
`tholdem.opponents.holdout.v1` -> `FrozenOpponentPool.HoldoutV1`; every other id is a
`CliArtifactException` (exit 4). Both strategies are loaded with `StrategyArtifactStore.Load`, the
policy hashes are `StrategyArtifact.Hash`, and every evaluation/verification pass constructs fresh
`StrategyAgent(genome, id)` instances. The report is written as `report-<hash8>.json` in `--out`
(default `<data>/evaluations4`), where `<hash8>` is the first eight hex characters of the primary
report's content hash. With `--anchor-plan` the anchor report is computed under the same pool rules,
`PopulationPromotionGate.Decide` produces the decision, and `PopulationPromotionVerifier.Verify`
recomputes both reports with fresh agents before the verification file is written. A refused
verification throws `CliInvariantException` (exit 5). `--json` prints the canonical report;
otherwise the command prints the plan hash, mean, lower bound, units and artifact paths. No champion
pointer is read or written.

## Red (observed before implementation)

The four design tests were written first against a compiling stub whose
`Evaluate4Command.Execute` threw `NotImplementedException("stub: Evaluate4Command.Execute")`, with
the new flags, valued options and dispatch already wired so the tests reached the stub.

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed: `Build succeeded.` with `0 Error(s)` (14 pre-existing warnings in untouched test files).

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Host.Evaluate4CommandTests
```

Observed (exit code 1):

```text
FAIL Evaluate4CommandTests.Run_GivenAnchorPlan_WritesDecisionAndVerifiedVerification: NotImplementedException: stub: Evaluate4Command.Execute
FAIL Evaluate4CommandTests.Run_GivenMalformedPlan_ExitArtifact: NotImplementedException: stub: Evaluate4Command.Execute
FAIL Evaluate4CommandTests.Run_GivenPlanAndArtifacts_WritesCanonicalReport: NotImplementedException: stub: Evaluate4Command.Execute
FAIL Evaluate4CommandTests.Run_GivenUnknownPool_ExitArtifact: NotImplementedException: stub: Evaluate4Command.Execute
TOTAL 4 PASSED 0 FAILED 4 SKIPPED 0
```

A fifth fact, `Run_GivenUnknownComposition_ExitArtifact`, was added after review to cover a
supported pool whose plan names a composition the pool does not define; it exercises the same
dispatch surface and is not part of the historical red run above.

## Green (observed after implementation)

Build (same command as the red build): `0 Error(s)`. The warning count varies between clean,
incremental and rebuild builds (14, 12, 16 and 18 observed) and comes only from pre-existing nullable and
xUnit-analyzer warnings in untouched test files; no warning names `Evaluate4Command.cs` or
`Evaluate4CommandTests.cs`.

Filtered tests (same command as the red run), observed exit code 0:

```text
observed evaluate4 gate: verdict=Unsafe reason=unsafe-anchor-regression verifier=verifier:verified decision=decision-920f9dcb.json verification=verification-920f9dcb.json
observed evaluate4 malformed-plan stderr: tholdem: evaluation plan '<test-temp>/plan.json' is not a valid persisted plan: plan-not-json: Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. LineNumber: 0 | BytePositionInLine: 1.
observed evaluate4 report: planHash=63eb2c968b6d51d3fe0ef7c7caca91435ae9351d6e07144eb788f012d5617257 units=4 file=report-920f9dcb.json
observed evaluate4 unknown-composition stderr: tholdem: evaluation-plan-invalid: the persisted plan cannot be evaluated: composition-mismatch: the plan composition 'missing-composition' does not resolve in pool 'tholdem.opponents.v1'. (Parameter 'request')
observed evaluate4 unknown-pool stderr: tholdem: unknown-opponent-pool: evaluation plan '<test-temp>/plan.json' names opponent pool 'tholdem.opponents.unknown.v1'; supported pools are 'tholdem.opponents.v1' and 'tholdem.opponents.holdout.v1'.
TOTAL 5 PASSED 5 FAILED 0 SKIPPED 0
```

Full suite, observed exit code 0:

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
TOTAL 566 PASSED 566 FAILED 0 SKIPPED 0
```

The round-13 baseline was 561 facts, so 566 = 561 + the five new facts. No pre-existing test was
changed, skipped or weakened. `dotnet test` was not run; the supplementary runner executed the same
compiled xUnit assembly and CI remains the authoritative gate.

## Observed plan, report and gate

The success-path tests use a tiny pre-registered plan: Training 2, Selection 2, Holdout 4, Audit 2 seeds,
2 hands per seat match, one composition (`primary-mixed`), minimum practical effect 2 bb/100,
confidence 0.95, 5 planned candidates and anchor tolerance 2 bb/100. The observed content address and
report shape are:

- plan hash: `63eb2c968b6d51d3fe0ef7c7caca91435ae9351d6e07144eb788f012d5617257`
- units: `4` (4 holdout blocks x 1 composition)
- report file: `report-920f9dcb.json` (the eight-character report-hash prefix)
- report schema: `tholdem.population-report.v2`; the file is whitespace-free UTF-8 without a BOM,
  contains no newline, and its `planHash` equals the plan hash above.

The anchor test uses the same plan and an anchor plan with the same game and holdout domain but the
`tholdem.opponents.holdout.v1` pool. Observed gate verdict `Unsafe` with reason
`unsafe-anchor-regression` (the anchor's lower bound fell below the 2 bb/100 tolerance); the
independent verifier recomputed both reports and returned `verifier:verified`, so the command exited
0 and wrote the verification document. The decision document is `tholdem.evaluate4-decision.v1`
(verdict, reason, lower bound, margin, unit count, adjusted confidence and all four hashes); the
verification document is `tholdem.evaluate4-verification.v1` (`verified`, verifier `reason`,
gate `verdict`, gate `gateReason` and the four hashes). The verification file is written only after
`PopulationPromotionVerifier.Verify` returns true.

## Refusals (observed exact codes)

1. Unknown pool — exit 4:

```text
tholdem: unknown-opponent-pool: evaluation plan '<test-temp>/plan.json' names opponent pool 'tholdem.opponents.unknown.v1'; supported pools are 'tholdem.opponents.v1' and 'tholdem.opponents.holdout.v1'.
```

`unknown-opponent-pool` is the command's stable artifact code; the numeric exit is 4 and the pool id
is present verbatim.

2. Malformed plan (a file containing a single open brace) — exit 4:

```text
tholdem: evaluation plan '<test-temp>/plan.json' is not a valid persisted plan: plan-not-json: Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. LineNumber: 0 | BytePositionInLine: 1.
```

`plan-not-json` is the parser's stable code, preserved verbatim inside the `CliArtifactException`
message.

3. Unknown composition under a supported pool — exit 4:

```text
tholdem: evaluation-plan-invalid: the persisted plan cannot be evaluated: composition-mismatch: the plan composition 'missing-composition' does not resolve in pool 'tholdem.opponents.v1'. (Parameter 'request')
```

`evaluation-plan-invalid` is the command's stable code for evaluator-side plan refusals; the numeric
exit is 4 and the composition id and pool id are present verbatim. All three refusal tests also assert
that no `evaluations4` directory was created, so a refused run writes nothing.

## No pointer was written

`Evaluate4Command` never calls `StrategyArtifactStore.WriteChampionPointer` and never reads
`active-champion.json`; both the report test and the anchor test assert that
`<data>/active-champion.json` does not exist after a successful run. A content search over the new
command found no `WriteChampionPointer`, `ReadChampionStrategyId`, `active-champion`,
`NotImplementedException`, `stub:`, `TODO` or `FIXME` occurrence.

## Implementation notes

- `TholdemCli` adds `Evaluate4Flags = { "json" }`, `Evaluate4Valued = { "plan", "challenger",
  "incumbent", "anchor-plan", "out", "data-dir" }`, the `"evaluate4"` dispatch branch and two usage
  lines. `git diff` over `TholdemCli.cs` shows only those additions; no v1 flag, command, exit code
  or usage line was altered.
- The plan is parsed and the pool resolved before strategies are loaded and before any output
  directory is created, so every refusal path is side-effect free.
- Evaluation refusals from the evaluator itself (for example a non-four-seat plan, a pool mismatch or
  an unknown composition) are wrapped as `evaluation-plan-invalid` `CliArtifactException`s
  (exit 4). The wrapper catches `ArgumentException` and `KeyNotFoundException`, so a pool lookup
  failure inside the evaluator can never escape the CLI as an unhandled exception. The unknown
  composition fact exercises the `ArgumentException` branch; the `KeyNotFoundException` branch is
  defense in depth for any future pool-member lookup.
- The primary report is persisted through `PopulationEvaluationReport.ToCanonicalJson`; decision and
  verification documents are built with `CliJson.Text`/`CliJson.Number` in fixed key order. All
  three files use `CliJson.Utf8` (UTF-8 without BOM) and the report contains no trailing newline.
- `<hash8>` for the decision and verification file names is the same primary report-hash prefix as
  the report file, so `report-920f9dcb.json`, `decision-920f9dcb.json` and
  `verification-920f9dcb.json` form one evaluation set. The suffix is derived from data, not from a
  clock, culture, GUID or ambient randomness.
- The verifier is given fresh `StrategyAgent` instances through recomputation delegates; it
  independently reproduces both reports and re-runs the gate before the verification document is
  written.

## Tests added

`WIP/Wip.Tholdem.Tests/Host/Evaluate4CommandTests.cs`:

1. `Run_GivenPlanAndArtifacts_WritesCanonicalReport` — exit 0, empty stderr, exactly one
   `report-*.json`, canonical schema/plan hash, 4 units, no BOM, no newline, `--json` emits the
   canonical report, `--out` redirects the artifact directory, and no champion pointer is written.
2. `Run_GivenAnchorPlan_WritesDecisionAndVerifiedVerification` — exit 0, exactly one decision and
   one verification file, `verified` true with `verifier:verified`, and the verification names the
   decision's gate verdict and reason; no champion pointer is written.
3. `Run_GivenUnknownPool_ExitArtifact` — exit 4, error contains `unknown-opponent-pool` and the
   exact pool id, no output directory created.
4. `Run_GivenUnknownComposition_ExitArtifact` — a supported pool with composition
   `missing-composition` exits 4 with `evaluation-plan-invalid` and the composition/pool names in
   the message, no output directory created.
5. `Run_GivenMalformedPlan_ExitArtifact` — a single open brace exits 4 with `plan-not-json`, no
   output directory created.

## Files changed

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Host/Commands/Evaluate4Command.cs` | new additive command |
| `WIP/Wip.Tholdem.Host/TholdemCli.cs` | evaluate4 flags, valued options, dispatch, usage |
| `WIP/Wip.Tholdem.Tests/Host/Evaluate4CommandTests.cs` | five new facts |
| `docs/goal-tholdem-enhancement/round-13/implementation-evaluate4-cli.md` | this record |

No other file was edited.
