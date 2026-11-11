# Round 20 — diagnosis-driven four-seat proposals (implementer)

Scope: sections 1–3 of `docs/goal-tholdem-enhancement/round-20/design.md`, proven by the two facts
in section 4. Only the six files in the table below were created or edited. The evaluator, gate,
verifier, existing outcomes, deterministic default and existing tests are unchanged; the test file
only gained two facts. No `dotnet test` was run.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/TrainingTrajectoryCapture.cs` | Edit: added the `IReadOnlyList<IPokerAgent>` overload; the two-seat body now shares the same scratch-directory core. |
| `WIP/Wip.Tholdem.Agents/Evaluation/FourSeatTraining.cs` | Edit: options, diagnosis path, capture and signatures report. |
| `WIP/Wip.Tholdem.Host/Commands/Train4Command.cs` | Edit: `--diagnosis` and `--diagnosis-hands`. |
| `WIP/Wip.Tholdem.Host/TholdemCli.cs` | Edit: train4 flag/valued sets and usage line only. |
| `WIP/Wip.Tholdem.Tests/Evaluation/FourSeatTrainingTests.cs` | Edit: two facts added; the four existing facts are byte-for-byte unchanged. |
| `docs/goal-tholdem-enhancement/round-20/implementation-diagnosis-four-seat.md` | New: this record. |

## 1. Four-seat capture overload

```csharp
public static TrajectoryCapture Capture(
    string dataDirectory,
    int iteration,
    SimulationOptions options,
    IReadOnlyList<IPokerAgent> agents,
    CancellationToken cancellationToken = default);
```

The overload null-checks `agents` and `options`, then runs
`MatchSimulator.Run(agents, options with { HistoryDirectory = directory }, cancellationToken)` into
`TrainingTrajectoryCapture.ScratchDirectory(dataDirectory, iteration)`. Both the two-seat and the
list overload now call one private `CaptureCore` that performs the same stale-directory delete,
hand read-back in hand-number order and `finally` cleanup as before. The two-seat public behaviour
is unchanged (its null-option check and simulator delegation are the pre-existing ones).

## 2. FourSeatTraining options and loop

`FourSeatTrainingOptions` gained, at the end of the positional record:

```csharp
bool DiagnosisDriven = false,
Func<int, SessionSignatures>? SignatureSource = null,
int DiagnosisHands = 20
```

When `DiagnosisDriven` is true the iteration, before creating the challenger artifact:

1. resolves `signatures = SignatureSource?.Invoke(iteration) ?? CaptureSignatures(...)`;
2. writes `four-seat/reports/iter-<n>-signatures.json` with
   `SessionSignatures.ToCanonicalJson()` (schema `tholdem.signatures.v1`);
3. proposes with `new DiagnosisDrivenProposer(signatures).Propose(champion.Genome,
   NeutralAnalysis, iteration)`.

`NeutralAnalysis` is the same `SessionAnalysis(0, 0, 0, 1, 0, null)` the deterministic path already
uses. On the diagnosis path the deterministic `Propose` (and therefore the `Proposer` option) is not
consulted, exactly as specified.

`CaptureSignatures` runs one real four-seat match:

- seat 0 is `new StrategyAgent(champion.Genome, champion.Id)`;
- seats 1–3 are `pool.Create(...)` for the first three opponent ids of
  `compositions[0]` (the first resolved composition);
- fewer than three opponents in that composition throws `InvalidOperationException` with reason
  `diagnosis-capture-requires-three-opponents`;
- the match uses `DiagnosisHands` hands and a diagnosis-only seed stream:
  `SeedDerivation.Derive(Seed, iteration, 0, (int)SeedPurpose.Training, 0x44494147)`
  (`0x44494147` is the distinct `"DIAG"` stream, disjoint from the `0x53454154` `"SEAT"` domains);
- the capture hands go through `FailureSignatureExtractor.Extract(capture.Hands)`.

The signatures report is written before the challenger is evaluated, alongside the unchanged
`iter-<n>-selection.json`, `iter-<n>-holdout.json` and `iter-<n>-decision.json`. The challenger
still gates through selection, holdout, anchor and `PopulationPromotionGate`; only its origin
changed.

`DiagnosisHands < 1` is refused with `ArgumentOutOfRangeException` only when `DiagnosisDriven` is
true, so every diagnosis-off options value keeps the old validation path.

## 3. CLI

```text
tholdem train4 ... [--margin <bb/100>=5] [--diagnosis] [--diagnosis-hands <n>=20]
               [--json] [--data-dir <path>]
```

`--diagnosis` is a flag and `--diagnosis-hands` a valued option (minimum 1, default 20). They are
passed as `DiagnosisDriven` and `DiagnosisHands`; the JSON document, text report and every other
option are unchanged. The usage line above is the only usage change.

## Red (observed before implementation)

### Compile red — tests first

The two facts were added before the options existed. Observed build (exit code 1), four errors:

```text
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Evaluation\FourSeatTrainingTests.cs(149,13): error CS0117: 'FourSeatTrainingOptions' does not contain a definition for 'DiagnosisDriven'
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Evaluation\FourSeatTrainingTests.cs(150,13): error CS0117: 'FourSeatTrainingOptions' does not contain a definition for 'SignatureSource'
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Evaluation\FourSeatTrainingTests.cs(190,13): error CS0117: 'FourSeatTrainingOptions' does not contain a definition for 'DiagnosisDriven'
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Evaluation\FourSeatTrainingTests.cs(191,13): error CS0117: 'FourSeatTrainingOptions' does not contain a definition for 'DiagnosisHands'
Build FAILED.
```

The three option properties were then added with no loop behaviour, so the test assembly compiled
and the missing behaviour was observable. Observed targeted run (exit code 1):

```text
FAIL FourSeatTrainingTests.Run_GivenDiagnosisCapture_WritesSignaturesAndProposes: TrueException: Assert.True() Failure
Expected: True
Actual:   False
FAIL FourSeatTrainingTests.Run_GivenDiagnosisSignatureSource_UsesDiagnosisProposal: EqualException: Assert.Equal() Failure: Values are not within 12 decimal places
Expected: 0.37 (rounded from 0.37)
Actual:   0.45000000000000001 (rounded from 0.45000000000000001)
TOTAL 6 PASSED 4 FAILED 2 SKIPPED 0
```

No test assertion changed between red and green.

## Green (observed after implementation)

`dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental`:
exit code 0, `Build succeeded.`, `16 Warning(s)`, `0 Error(s)`. All 16 warnings name pre-existing
files (`Wip.Runtime`, `TrainCommand.cs`, `TrainingLoopTests.cs`,
`LearningWebIntegrationTests.cs`, `ThreatControlTests.cs`); none names a file in the scope table.

Targeted class (exit 0):

```text
observed four-seat diagnosis capture: challengerId=tholdem-strategy-v2-4ac1f97b signatures={"schema":"tholdem.signatures.v1","signals":[{"name":"fold-to-raise","count":0,"sample":1,"rate":0,"direction":"low"},{"name":"call-with-weak-hand","count":0,"sample":1,"rate":0,"direction":"low"},{"name":"missed-value","count":1,"sample":1,"rate":1,"direction":"high"},{"name":"aggression-by-street","count":0,"sample":3,"rate":0,"direction":"low"},{"name":"showdown-loss","count":1,"sample":2,"rate":0.5,"direction":"high"},{"name":"stack-off-loss","count":0,"sample":0,"rate":0,"direction":"low"}]}
observed four-seat diagnosis source: challengerId=tholdem-strategy-v2-4f4ea1bf callThreshold=0.37 signaturesReport=iter-1-signatures.json
observed four-seat inert: outcome=rejected-selection selectionMeanBbPer100=0 verdict=Rejected reason=selection-mean-not-positive finalChampionId=tholdem-strategy-v1-f100e793 challengerId=tholdem-strategy-v2-dcd95518 runHash=621eb6c4c5fbf8dfa34c265695828526e03b3f1839e48a2bc5fd723f247d7d13
observed four-seat layout: four-seat/reports/iter-1-selection.json, four-seat/strategies/tholdem-strategy-v1-f100e793.json, four-seat/strategies/tholdem-strategy-v2-b1b55149.json
observed four-seat repeat: runHash=4064f00ceb491c168af0ccb6b6e5511c52ee719723190f329c21c5587884578e files=3
TOTAL 6 PASSED 6 FAILED 0 SKIPPED 0
```

Full suite (exit 0):

```text
TOTAL 584 PASSED 584 FAILED 0 SKIPPED 0
```

The task's expected pre-change total is 582 (the same 582 recorded by the round-19 implementation
doc), so the observed 584 is exactly 582 + the two new facts. The four pre-existing four-seat facts
still pass unchanged.

## Call threshold: 0.37

The signature-source fact injects one signal:

```text
fold-to-raise count=10 sample=10 rate=1.0 direction=high
```

The default champion genome has `CallThreshold = 0.45`. `DiagnosisDrivenProposer` maps the strongest
usable signal through `SignatureDrivenRefinementPolicy`, whose step is 0.08, so the challenger
strategy JSON observed `parameters.callThreshold = 0.37` and the fact asserts `0.37` at 12-decimal
precision. This is the champion's 0.45 minus 0.08, observed from the persisted challenger artifact,
not from the policy in isolation.

## Capture result

The capture fact (`DiagnosisDriven = true`, `DiagnosisHands = 2`, no `SignatureSource`) ran one real
four-seat match and persisted
`four-seat/reports/iter-1-signatures.json` with:

```json
{"schema":"tholdem.signatures.v1","signals":[{"name":"fold-to-raise","count":0,"sample":1,"rate":0,"direction":"low"},{"name":"call-with-weak-hand","count":0,"sample":1,"rate":0,"direction":"low"},{"name":"missed-value","count":1,"sample":1,"rate":1,"direction":"high"},{"name":"aggression-by-street","count":0,"sample":3,"rate":0,"direction":"low"},{"name":"showdown-loss","count":1,"sample":2,"rate":0.5,"direction":"high"},{"name":"stack-off-loss","count":0,"sample":0,"rate":0,"direction":"low"}]}
```

The captured hands are real: the signal samples are 1, 1, 1, 3, 2 and 0, derived from the two
recorded hands. Every sample is below `SignatureDrivenRefinementPolicy.MinimumSample` (10), so the
proposer legitimately falls back to the deterministic genome; the challenger was still written
(`tholdem-strategy-v2-4ac1f97b`), and no v1 `active-champion.json` exists at the data root. The
scratch hand files under `<data>/harness/trajectory/iteration-00001` are deleted by the capture's
`finally`; only the four-seat artifacts remain as files.

An out-of-suite CLI smoke run (seed 1099511627776, one iteration, 1 block/hand throughout,
`--diagnosis --diagnosis-hands 2`) exited 0 and wrote exactly:

```text
four-seat/reports/iter-1-selection.json
four-seat/reports/iter-1-signatures.json
four-seat/strategies/tholdem-strategy-v1-f100e793.json
four-seat/strategies/tholdem-strategy-v2-4ac1f97b.json
```

with the printed iteration line and no v1 pointer. The smoke directory was deleted after the
observation.

## Deterministic default unchanged

`DiagnosisDriven` defaults to false and `SignatureSource` / `DiagnosisHands` do not participate
unless it is true. The deterministic iteration still calls the same `Propose` with
`DeterministicStrategyProposer` (or the supplied `Proposer`) and writes neither
`iter-<n>-signatures.json` nor a capture scratch directory. The observed inert, layout and repeat
lines above are byte-identical to the pre-change baseline run of the same class, including
`runHash=621eb6c4c5fbf8dfa34c265695828526e03b3f1839e48a2bc5fd723f247d7d13` for the inert scenario
and `runHash=4064f00ceb491c168af0ccb6b6e5511c52ee719723190f329c21c5587884578e` for the repeat
scenario. No existing test, evaluator, gate or verifier file was edited.

## Commands run

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.FourSeatTrainingTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental
```

No `dotnet test`. The only additional commands were the read-only inspection above and the
out-of-suite CLI smoke run whose temporary directory was removed.
