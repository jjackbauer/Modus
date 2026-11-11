# Round 19 — Diagnosis-driven proposer adapter (implementer)

Scope: the three new files in the table below. No existing source, test, refinement policy,
signature type or project file was edited. No `dotnet test` was run.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/DiagnosisDrivenProposer.cs` | New: the `IStrategyProposer` adapter. |
| `WIP/Wip.Tholdem.Tests/Learning/DiagnosisDrivenProposerTests.cs` | New: the four contract facts. |
| `docs/goal-tholdem-enhancement/round-19/implementation-diagnosis-proposer.md` | New: this record. |

## Contract delivered

```csharp
public sealed class DiagnosisDrivenProposer : IStrategyProposer
{
    public DiagnosisDrivenProposer(
        SessionSignatures signatures,
        IStrategyProposer? fallback = null,
        RefinementStrategy strategy = RefinementStrategy.SignatureDriven,
        double minimumSignalRate = 0.0);
    public StrategyGenome Propose(StrategyGenome incumbent, SessionAnalysis analysis, int iteration);
}
```

## Implementation

The adapter is a thin, stateless-per-call wrapper around the shipped policy. The constructor
null-checks `signatures`, stores the immutable signature record, and builds:

```csharp
_policy = new SignatureDrivenRefinementPolicy(
    fallback,
    enableSkillEdits: false,
    strategy,
    minimumSignalRate);
```

`enableSkillEdits` is pinned to false so the adapter exposes only the numeric genome path (the
policy default is also false). `Propose` null-checks `incumbent` and `analysis` and returns
`_policy.Propose(new RefinementContext(incumbent, analysis, _signatures, iteration)).Genome`.

No policy or signature code was changed. An empty signature set, or one whose signals all fall
below `SignatureDrivenRefinementPolicy.MinimumSample` (10) or the supplied rate floor, is handled
entirely by the existing policy: `StrongestUsable` returns null and the legacy path calls
`fallback ?? new DeterministicStrategyProposer()`.

## Red (observed before implementation)

Tests were written first, then the class was created as a stub whose `Propose` threw
`NotImplementedException`. `dotnet build` succeeded, so the red is behavioural rather than
compile-time. Observed targeted run (exit code 1):

```text
FAIL DiagnosisDrivenProposerTests.Propose_GivenBelowSampleFloor_FallsBackToDeterministic: NotImplementedException: DiagnosisDrivenProposer is not implemented yet.
FAIL DiagnosisDrivenProposerTests.Propose_GivenFoldToRaiseSignal_LowersCallThresholdByStep: NotImplementedException: DiagnosisDrivenProposer is not implemented yet.
FAIL DiagnosisDrivenProposerTests.Propose_GivenNoSignatures_EqualsDeterministicProposer: NotImplementedException: DiagnosisDrivenProposer is not implemented yet.
FAIL DiagnosisDrivenProposerTests.Propose_GivenSameInputs_IsDeterministic: NotImplementedException: DiagnosisDrivenProposer is not implemented yet.
TOTAL 4 PASSED 0 FAILED 4 SKIPPED 0
```

The stub was then replaced by the implementation above; no test assertion was changed between red
and green.

## Green (observed after implementation)

Build `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release`: exit code 0,
`0 Error(s)`; the full build's 14 warnings are pre-existing and none references either new file.

```text
targeted:   TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
full suite: TOTAL 582 PASSED 582 FAILED 0 SKIPPED 0
```

Both runs exited 0. 578 is the pre-change total recorded in the round-18 implementation doc; the
observed 582 is exactly 578 + the four new facts.

## Fold-to-raise delta

Input: one signal `fold-to-raise` with count 10, sample 10, rate 1.0, direction `high`; incumbent
`new StrategyGenome()` (call 0.45); iteration 1. The policy maps the signal to
`CallThreshold - SignatureDrivenRefinementPolicy.Step`. The observed passing assertions pin:

- `proposed.CallThreshold == 0.37` at 12-decimal precision;
- `proposed.CallThreshold == incumbent.CallThreshold - 0.08` at 12-decimal precision;
- raise 0.72, bluff 0.12, sizing 1.0 and `PromptText` unchanged.

So the fold-to-raise delta is **-0.08** on the call threshold (0.45 -> 0.37) and only that parameter
moves (`BehaviorEquals(incumbent)` is false).

## Fallback equality

- **Empty signatures:** `new DiagnosisDrivenProposer(SessionSignatures.Empty)` on a winning
  analysis (iteration 1) returned a genome record-equal to
  `new DeterministicStrategyProposer().Propose(incumbent, analysis, 1)`. The test also asserts the
  deterministic proposal differs behaviourally from the incumbent, so equality is not the trivial
  no-op case.
- **Below sample floor:** a 3/3 fold-to-raise signal (rate 1.0 but sample 3 < 10) returned the same
  deterministic genome. Observed values: call threshold unchanged at 0.45 and raise threshold
  0.70 = 0.72 - `DeterministicStrategyProposer.Step` (0.02), i.e. the deterministic raise move,
  not the -0.08 call edit the signal would have chosen.

## Determinism

The determinism fact calls the adapter twice on the same instance and once on a freshly constructed
instance with identical signatures, analysis and iteration; all three genomes are record-equal. The
signature set contains two rate-1.0 signals (`fold-to-raise` and `missed-value`), and the observed
selection is still `fold-to-raise`, matching the policy's ordinal name tie-break; the call
threshold is 0.37 in both runs.

Static inspection of the new adapter for ambient sources returned no matches:

```text
grep "DateTime|Guid|CultureInfo|Random|Stopwatch|Environment.TickCount"
-> 0 matches
```

The adapter adds no clock, GUID, culture or random read; all state is the supplied signatures and
per-call arguments.

## Tests added

`WIP/Wip.Tholdem.Tests/Learning/DiagnosisDrivenProposerTests.cs` — four facts, no existing test
changed.

1. `Propose_GivenFoldToRaiseSignal_LowersCallThresholdByStep` — 10/10 high signal -> call 0.45 to
   0.37; only the call threshold moves.
2. `Propose_GivenNoSignatures_EqualsDeterministicProposer` — `SessionSignatures.Empty` -> exact
   record equality with the deterministic proposal.
3. `Propose_GivenBelowSampleFloor_FallsBackToDeterministic` — 3/3 signal -> exact deterministic
   fallback; call stays 0.45 and raise moves by 0.02.
4. `Propose_GivenSameInputs_IsDeterministic` — repeated and fresh-instance proposals are equal;
   the equal-rate tie resolves to fold-to-raise.

## Commands run

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.DiagnosisDrivenProposerTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

No `dotnet test`. This task created only the three files in the Files table. The shared working
tree already contained unrelated modified and untracked files from other slices; none of them was
touched by this task.
