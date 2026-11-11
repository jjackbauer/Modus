# Round 18 — D5 diagnostics repair (implementer)

Scope: sections 1–2 of `docs/goal-tholdem-enhancement/round-18/design.md`, proven by the three
tests in section 3. Only the four files in the table below were created or edited. No `dotnet test`
was run.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/FailureSignatureExtractor.cs` | Edit: `FacedRaise` only — a per-street outstanding-aggression scan. |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/SessionSignatures.cs` | Edit: computed `FailureSignal.Observed` only; the positional record and the canonical writer are untouched. |
| `WIP/Wip.Tholdem.Tests/Learning/DiagnosticsRepairTests.cs` | New: the three design facts. |
| `docs/goal-tholdem-enhancement/round-18/implementation-diagnostics-repair.md` | New: this record. |

## Implementation

### `FailureSignal.Observed`

```csharp
public bool Observed => Sample > 0;
```

The positional record signature, its equality and `SessionSignatures.ToCanonicalJson` are unchanged.
A signal with no observations now reports `Sample` 0, `Count` 0 and `Observed` false; a sampled
hand with zero failures reports positive `Sample`, `Count` 0 and `Observed` true.

### `FailureSignatureExtractor.FacedRaise`

The previous-action pair test was replaced with a per-street scan:

- the outstanding flag resets on every street change;
- a non-seat-0 `Bet`/`Raise`/`AllIn` sets it;
- a seat-0 action while it is set is one answer facing aggression: it clears the flag and, when the
  action is `Fold`, sets `folded`;
- the scan continues through the rest of the hand, so a fold after an intervening caller is still
  counted, but the result is a per-hand flag, never a per-decision count, so a hand contributes at
  most one sample.

The first draft returned immediately after the first seat-0 answer; the targeted test observed the
same fail-to-count red, so the final scan keeps the per-hand flags and continues. The final code is
the scan above.

## Tests added

`WIP/Wip.Tholdem.Tests/Learning/DiagnosticsRepairTests.cs` — three facts; no existing test was
changed.

1. `Extract_GivenCallBetweenRaiseAndFold_CountsFoldToRaise` — a three-seat `HandRecord` (actions
   seat1 Raise, seat0 Call, seat1 Raise, seat2 Call, seat0 Fold), with `SeatCount = 3` so the
   seat-2 caller is a valid action. Asserts fold-to-raise `Count` 1, `Sample` 1, `Rate` 1.0 and
   `Observed` true.
2. `Extract_GivenNoHands_AllSignalsUnobserved` — `Extract(Array.Empty<HandRecord>())`: six
   signals, each `Count` 0, `Sample` 0, `Rate` 0 and `Observed` false.
3. `Extract_GivenCheckedHand_SampledZeroFailuresAreObserved` — one hand checked through every
   postflop street. `aggression-by-street` is `Observed` true with `Count` 0 and `Sample` 3;
   `fold-to-raise` is `Observed` false with `Count` 0 and `Sample` 0.

## Red (observed before the fix)

### Build red — `Observed` does not exist yet

The tests were written first. Observed build (exit code 1), four occurrences:

```text
C:\...\WIP\Wip.Tholdem.Tests\Learning\DiagnosticsRepairTests.cs(26,33): error CS1061: 'FailureSignal' does not contain a definition for 'Observed' and no accessible extension method 'Observed' accepting a first argument of type 'FailureSignal' could be found (are you missing a using directive or an assembly reference?)
    12 Warning(s)
    4 Error(s)
Build FAILED.
```

The 12 warnings are pre-existing; none names the new file.

### Test red — the fold case

The computed property was then added alone (no extractor change) so the suite could compile. Observed
targeted run (exit code 1):

```text
FAIL DiagnosticsRepairTests.Extract_GivenCallBetweenRaiseAndFold_CountsFoldToRaise: EqualException: Assert.Equal() Failure: Values differ
Expected: 1
Actual:   0
TOTAL 3 PASSED 2 FAILED 1 SKIPPED 0
```

The other two new facts passed with only the property in place.

## Green (observed after the fix)

Build (exit code 0): `Build succeeded.`, `12 Warning(s)`, `0 Error(s)`.

```text
targeted:                TOTAL 3 PASSED 3 FAILED 0 SKIPPED 0
HarnessRefinementTests:  TOTAL 8 PASSED 8 FAILED 0 SKIPPED 0
full suite:              TOTAL 578 PASSED 578 FAILED 0 SKIPPED 0
```

All three runs exited 0. 578 is the recorded 575-fact baseline plus the three new facts.

## Fold-to-raise count

- New test hand: `Count` 1, `Sample` 1, `Rate` 1.0, `Direction` `high`, `Observed` true.
- Existing synthetic fixture: unchanged at `Count` 1, `Sample` 3, `Rate` 0.3333333333333333,
  `Direction` `low`; `Extract_GivenSyntheticHands_ExpectedNamedSignals` passes.

## Observed values

| Case | Sample | Count | Rate | Observed |
|---|---|---|---|---|
| Empty extract, all six signals | 0 | 0 | 0 | false |
| Checked-down hand, `aggression-by-street` | 3 | 0 | 0 | true |
| Checked-down hand, `fold-to-raise` | 0 | 0 | 0 | false |
| Raise/call/raise/call/fold hand, `fold-to-raise` | 1 | 1 | 1.0 | true |
| Existing fixture, all six signals | positive | see below | see below | true |

## Canonical signature JSON unchanged

A temporary in-scope probe test (removed afterwards; the final test file has exactly three facts)
printed the canonical JSON of the existing synthetic fixture:

```json
{"schema":"tholdem.signatures.v1","signals":[{"name":"fold-to-raise","count":1,"sample":3,"rate":0.3333333333333333,"direction":"low"},{"name":"call-with-weak-hand","count":1,"sample":2,"rate":0.5,"direction":"high"},{"name":"missed-value","count":2,"sample":2,"rate":1,"direction":"high"},{"name":"aggression-by-street","count":2,"sample":7,"rate":0.2857142857142857,"direction":"low"},{"name":"showdown-loss","count":3,"sample":4,"rate":0.75,"direction":"high"},{"name":"stack-off-loss","count":1,"sample":1,"rate":1,"direction":"high"}]}
```

Every serialized value matches the pre-fix assertions in
`Extract_GivenSyntheticHands_ExpectedNamedSignals` (1/3, 1/2, 2/2, 2/7, 3/4 and 1/1 with the same
directions), so the fixture's canonical bytes are unchanged. `Observed` is a computed property with
no backing field, and the untouched `ToCanonicalJson` still writes only
`name`/`count`/`sample`/`rate`/`direction`;
`Extract_GivenHands_ExpectedNoSeedCardOrNameInCanonicalJson` asserts exactly those property names
and passes.

## Commands run

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.DiagnosticsRepairTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.HarnessRefinementTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

No `dotnet test`. No repository file outside the Files table was created or edited.
