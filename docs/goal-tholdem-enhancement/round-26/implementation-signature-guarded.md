# Round 26 — signature-guarded step proposer (implementer)

Scope: `docs/goal-tholdem-enhancement/round-26/design.md`, proven by the four facts it names.
Only the three files in the table below were created. The diagnosis policy, the deterministic
proposer, existing tests and the evaluator are unchanged, and no `dotnet test` was run.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/SignatureGuardedProposer.cs` | New: the guard. |
| `WIP/Wip.Tholdem.Tests/Learning/SignatureGuardedProposerTests.cs` | New: the four specified facts. |
| `docs/goal-tholdem-enhancement/round-26/implementation-signature-guarded.md` | New: this record. |

## API (as specified)

```csharp
public sealed class SignatureGuardedProposer : IStrategyProposer
{
    public const double Step = 0.02;
    public SignatureGuardedProposer(SessionSignatures signatures, IStrategyProposer? fallback = null);
    public StrategyGenome Propose(StrategyGenome incumbent, SessionAnalysis analysis, int iteration);
}
```

## Behaviour

- A signal is eligible only when its name is ordinal `fold-to-raise`, its `Sample` is at least the
  literal ten-sample floor, and its direction is ordinal `high`. Every eligible signal selects the
  same edit, so presence is enough: signal order, rate values and ties cannot change the result.
- When eligible, the guard returns `(incumbent with { CallThreshold = incumbent.CallThreshold - 0.02 }).Clamp()`.
  The prompt text and every other field are copied unchanged, matching the policy's signature-edit
  path. The diagnosis policy's 0.08 `Step` is never used.
- An empty signature set, any other signal, a below-floor signal, or an eligible signal whose
  clamped edit is behaviourally inert (`BehaviorEquals(incumbent)`, e.g. a call threshold already
  at 0) returns `_fallback.Propose(incumbent, analysis, iteration)`. A null fallback is
  `DeterministicStrategyProposer`, so the default is the deterministic proposer.
- The class uses no clock, GUID, culture or ambient random source; the only string comparisons are
  `StringComparison.Ordinal`.

## Red (observed before implementation)

Tests were written first against a stub whose `Propose` threw `NotImplementedException`, so the red
run compiled and failed on observed behaviour rather than on a compile error. The stub was then
replaced; it is not part of the delivered code.

Build (exit 0): `Build succeeded.`, `14 Warning(s)`, `0 Error(s)` — every warning names a
pre-existing file outside this round's scope.

Targeted run (exit 1):

```text
FAIL SignatureGuardedProposerTests.Propose_GivenBelowSampleFloor_EqualsDeterministicProposer: NotImplementedException: The method or operation is not implemented.
FAIL SignatureGuardedProposerTests.Propose_GivenEmptySignatures_EqualsDeterministicProposer: NotImplementedException: The method or operation is not implemented.
FAIL SignatureGuardedProposerTests.Propose_GivenFoldToRaiseHigh_LowersCallByDeterministicStep: NotImplementedException: The method or operation is not implemented.
FAIL SignatureGuardedProposerTests.Propose_GivenSameInputs_IsDeterministic: NotImplementedException: The method or operation is not implemented.
TOTAL 4 PASSED 0 FAILED 4 SKIPPED 0
```

No assertion changed between red and green.

## Green (observed after implementation)

Build (exit 0): `Build succeeded.`, `14 Warning(s)`, `0 Error(s)`; no warning names a file in the
scope table. The final incremental rebuild after making the floor a literal reported
`0 Warning(s)`, `0 Error(s)`.

Targeted run (exit 0):

```text
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

Full suite (exit 0):

```text
TOTAL 588 PASSED 588 FAILED 0 SKIPPED 0
```

588 is the task's expected pre-change 584 plus the four new facts; no pre-existing fact changed
outcome.

## Call threshold: 0.45 -> 0.43

`StrategyGenome`'s default `CallThreshold` is `0.45` and `SignatureGuardedProposer.Step` is
`0.02`, so the guarded genome's call threshold is the 0.43 value. Fact 1 asserts `0.43` at
12-decimal precision and also asserts `incumbent.CallThreshold - SignatureGuardedProposer.Step`;
it further pins raise threshold, bluff frequency, sizing multiplier and prompt text to the
incumbent and asserts the challenger is not behaviourally equal. The 0.08 policy jump, which would
have produced 0.37, is absent.

## Fallback equality

Fact 2 (empty `SessionSignatures`) and fact 3 (a 9/9 `fold-to-raise`, `high` signal — one sample
short of the floor) assert `Assert.Equal(expected, proposed)` on the whole `StrategyGenome`
record, where `expected` is `new DeterministicStrategyProposer().Propose(...)` with the same
incumbent, analysis and iteration. Record equality includes the prompt text and skills, so the
fallback is the entire deterministic result, not a re-derived approximation. Fact 3 additionally
pins the deterministic losing-session move (raise threshold 0.72 -> 0.70 at odd iteration 1) and
an unchanged call threshold, proving the guard did not apply its edit.

## Determinism

Fact 4 calls the same proposer twice and a second proposer over an equal signature set
(`fold-to-raise` and `missed-value`, both 10/10 and `high`) three times over the same inputs; all
genomes are equal and the call threshold is 0.43 each time. Because eligibility is presence-based,
the extra `missed-value` signal cannot divert the edit, and no ambient source exists in the code
path.

## Commands run

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.SignatureGuardedProposerTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

The same three commands produced the red observation (against the throwing stub) and the green
observation above. No `dotnet test` was run.
