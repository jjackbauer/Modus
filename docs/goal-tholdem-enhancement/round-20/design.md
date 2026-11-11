# Round 20 design — diagnosis-driven four-seat proposals (item 2 completion)

**Objective:** let the four-seat loop propose from measured failure signatures instead of only the
deterministic step, with an injectable signature source for tests and a real four-seat capture path.

## 1. TrainingTrajectoryCapture — four-seat overload

~~~csharp
public static TrajectoryCapture Capture(
    string dataDirectory,
    int iteration,
    SimulationOptions options,
    IReadOnlyList<IPokerAgent> agents,
    CancellationToken cancellationToken = default);
~~~

Same scratch-directory discipline as the two-seat overload, calling
MatchSimulator.Run(agents, options with HistoryDirectory, token) and reading the hands back.

## 2. FourSeatTraining options and loop

Add to FourSeatTrainingOptions:
- bool DiagnosisDriven = false;
- Func<int, SessionSignatures>? SignatureSource = null;
- int DiagnosisHands = 20;

When DiagnosisDriven:
1. signatures = SignatureSource?.Invoke(iteration) ?? CaptureSignatures(iteration);
2. write four-seat/reports/iter-<n>-signatures.json (canonical SessionSignatures JSON);
3. challenger = new DiagnosisDrivenProposer(signatures).Propose(champion.Genome, neutral analysis,
   iteration).
Otherwise the existing deterministic path is unchanged. CaptureSignatures runs one 4-seat match:
champion + the first three opponents of the first composition (error if the composition has fewer
than three), a fresh seed derived from Seed and iteration, DiagnosisHands hands, then
FailureSignatureExtractor.Extract(capture.Hands). No v1 files.

## 3. CLI

train4 gains --diagnosis (flag) and --diagnosis-hands <n>=20 (valued); it passes DiagnosisDriven and
DiagnosisHands. Usage line updated. Other behavior unchanged.

## 4. Tests

FourSeatTrainingTests add:
1. Run_GivenDiagnosisSignatureSource_UsesDiagnosisProposal: DiagnosisDriven=true, SignatureSource
   returns fold-to-raise 10/10 high; read the challenger strategy JSON and assert callThreshold is
   0.37 (champion 0.45 minus 0.08), and the signatures report exists.
2. Run_GivenDiagnosisCapture_WritesSignaturesAndProposes: DiagnosisDriven=true, DiagnosisHands=2,
   no source; assert the signatures report exists and a challenger strategy was written; no v1
   pointer.

Train4CommandTests: no change required; the existing tiny run stays diagnosis-off.

## 5. Forbidden

Do not change the deterministic default, the evaluator/gate/verifier, the existing four-seat
outcomes, v1 behavior, or existing tests (add only). No clock/GUID/culture/ambient randomness. The
diagnosis path must still gate through selection/holdout/anchor.
