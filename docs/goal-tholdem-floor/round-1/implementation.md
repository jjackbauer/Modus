# F1 implementation — positive-evidence signal floor

- **Scope**: `SignatureDrivenRefinementPolicy`, `TrainingOptions`, one `TrainingLoop` pass-through, the new test file, and this document. No other product file, existing test, frozen component or project file was edited.
- **Result**: all required tests are green; the shipped default (`SignalRateFloor = 0`) is unchanged and the 387-test baseline still passes (394/394 including the 7 new tests).
- **Pre-registration**: `docs/goal-tholdem-floor/goal.md:15-28` and `docs/goal-tholdem-floor/round-1/experiment-design.md:9-18` fix the arms and floors before any floor result; this round only implements the option they name.

## 1. Product change

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/SignatureDrivenRefinementPolicy.cs:37-60` | Last optional constructor parameter `double minimumSignalRate = 0.0`; validation and storage. |
| `.../SignatureDrivenRefinementPolicy.cs:51-57` | `!double.IsFinite(rate) || rate < 0.0 || rate > 1.0` throws `ArgumentOutOfRangeException` with `ParamName == "minimumSignalRate"`. |
| `.../SignatureDrivenRefinementPolicy.cs:62-66` | Public read-only `MinimumSignalRate` property (the pass-through is observable). |
| `.../SignatureDrivenRefinementPolicy.cs:153-165` | `StrongestUsable` now filters `signal.Sample >= MinimumSample && signal.Rate >= _minimumSignalRate`; order and ordinal tie-break unchanged. |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingOptions.cs:19` | Last optional record parameter `double SignalRateFloor = 0.0`. |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:195-200` | The existing construction site passes `minimumSignalRate: options.SignalRateFloor`; no other pass-through exists. |

## 2. Filter / fallback semantics

A signal is usable only when **both** hold:

1. `signal.Sample >= SignatureDrivenRefinementPolicy.MinimumSample` (10), unchanged; and
2. `signal.Rate >= _minimumSignalRate` — **inclusive**, so at floor `0.0` a rate-`0.0` signal is still eligible and today's selection is preserved.

Among usable signals the policy keeps the deterministic highest-`Rate`, ordinal-name tie-break order (`SignatureDrivenRefinementPolicy.cs:160-164`). When **no** signal passes the floor, `StrongestUsable` returns `null` and `Propose` calls the existing `Legacy(context)` path unchanged (`...:79-83`, rationale `legacy-session-driven` at `IRefinementPolicy.cs:43`). `RefinementStrategy.SessionDriven` still bypasses signal selection entirely (`...:72-77`). Because the floor is a constructor argument validated there, an out-of-range value supplied through `TrainingOptions` is refused when the loop constructs the policy, before any proposal or artifact is produced.

## 3. Test-first red evidence

The tests were written before the filter and the validation existed. To keep the failure a **runtime** failure (not a compile error), the new API surface was first added with the old selection and without validation; the new tests then compiled and failed behaviorally. Command (supplementary runner, because `dotnet test` aborts with `Win32Exception (5)` in this session):

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessSignalFloor
```

Observed (`FAIL ...` lines and final total, saved verbatim in `.tmp/goal-refinement/round-1-red.txt`):

```text
FAIL HarnessSignalFloorTests.Policy_GivenFloorAboveOneOrBelowZero_ExpectedStableRefusal: ThrowsException: Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.ArgumentOutOfRangeException)
FAIL HarnessSignalFloorTests.Policy_GivenZeroRateSignalAndPositiveFloor_ExpectedLegacyFallback: EqualException: Assert.Equal() Failure: Values differ
Expected: StrategyGenome { RaiseThreshold = 0.7, CallThreshold = 0.45, ... }
Actual:   StrategyGenome { RaiseThreshold = 0.72, CallThreshold = 0.37, ... }
FAIL HarnessSignalFloorTests.Run_GivenSignalFloorOption_ExpectedOptionReachesPolicy: EqualException: Assert.Equal() Failure: Strings differ
Expected: "tholdem-strategy-v2-0ceaa8fd"
Actual:   "tholdem-strategy-v2-d24dcf6d"
TOTAL 7 PASSED 4 FAILED 3 SKIPPED 0
```

The three failing tests are exactly the three behaviors the change must add: zero-rate rejection under a positive floor, floor validation, and the `TrainingOptions` pass-through forcing the legacy fallback in a real run. The four passing tests are those whose expected behavior is already produced by the pre-floor implementation (zero-rate eligible at the default floor, above-floor selection, the pinned default run) plus the exact-at-floor test: with the floor ignored its signal is still selected, so it cannot be red at the skeleton stage; after the change it is the red-proof against a strict `>` comparison (the default-floor test pins the same inclusivity at 0).

## 4. Green evidence

Focused command (same as above), after implementing the filter and validation:

```text
TOTAL 7 PASSED 7 FAILED 0 SKIPPED 0
```

Unfiltered assembly command:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed:

```text
TOTAL 394 PASSED 394 FAILED 0 SKIPPED 0
```

The pre-change baseline on the same build path was observed as `TOTAL 387 PASSED 387 FAILED 0 SKIPPED 0`; 387 + 7 = 394, so every existing test still passes and no test was edited, skipped or weakened. Build used for every run:

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed on a clean rebuild: `0 Error(s)`, 16 pre-existing analyzer warnings in `Wip.Runtime`, `Wip.Tholdem.Host` and other test files; none in the new file or the changed product files.

## 5. The tests (new file)

`WIP/Wip.Tholdem.Tests/Learning/HarnessSignalFloorTests.cs`:

| Test | Line | Concrete assertion |
|---|---|---|
| `Policy_GivenZeroRateSignalAndDefaultFloor_ExpectedSignalEdit` | 33 | Rate-0 sample-10 signal is selected at floor 0; `fold-to-raise` rationale, `CallThreshold = 0.45 - 0.08`, other parameters unchanged, and the genome differs from the legacy proposer's. |
| `Policy_GivenZeroRateSignalAndPositiveFloor_ExpectedLegacyFallback` | 56 | Floor `0.10` makes the same context equal `new DeterministicStrategyProposer().Propose(...)` with rationale `legacy-session-driven`; `RaiseThreshold` moved by the proposer step, call threshold did not. |
| `Policy_GivenSignalAboveFloor_ExpectedSignalEdit` | 80 | Rate `0.20` at sample 10 with floor `0.10` selects the `fold-to-raise` edit. |
| `Policy_GivenSignalRateExactlyAtFloor_ExpectedSignalEdit` | 100 | Rate `0.10` at floor `0.10` selects the signal: the comparison is inclusive. |
| `Policy_GivenFloorAboveOneOrBelowZero_ExpectedStableRefusal` | 117 | `-0.0001`, `1.0001`, `double.NaN` and `double.PositiveInfinity` each throw `ArgumentOutOfRangeException` with `ParamName == "minimumSignalRate"`. |
| `Run_GivenDefaultFloor_ExpectedPinnedChallengersUnchanged` | 137 | Three-iteration arm keeps `f100e793` as every champion and `d24dcf6d`/`c34333fe`/`86f42856` as challengers, all rejected, `FinalChampionId == f100e793`, `Bootstrap == null`, `Options.SignalRateFloor == 0.0`; the first challenger artifact is well-formed and is the `+0.08` bluff edit. |
| `Run_GivenSignalFloorOption_ExpectedOptionReachesPolicy` | 176 | Real one-iteration runs at seed `20261111`: floor 0 gives `d24dcf6d` (signal edit), floor 1.0 gives exactly the legacy run's `0ceaa8fd`; challenger artifact canonical JSON and evaluation report are byte-equal between the floor-1.0 and legacy runs, and the evaluation file equals the in-memory report canonical JSON. |

Existing fallback coverage remains untouched and green: `HarnessRefinementTests.cs:225-253` (empty and below-sample signatures delegate to `DeterministicStrategyProposer`).

## 6. Default-pin evidence

- `TrainingOptions.cs:19` — the new parameter defaults to `0.0`, so every existing construction site (Web, Host, `MetaLoop`, `HarnessAblationRunner`, all existing tests) compiles and behaves as before; the full suite including those tests is green.
- `HarnessSkillsTests.cs:277-315` — the pre-existing three-iteration pinned test is unmodified and passes; the new `Run_GivenDefaultFloor_ExpectedPinnedChallengersUnchanged` repeats the same arm and additionally pins `Bootstrap == null` and the artifact fields (`HarnessSignalFloorTests.cs:152-167`).
- Challenger ids are content-hash suffixed (`tholdem-strategy-v2-<hash8>`), and the pinned run's ids are unchanged, so the default-floor challenger bytes are unchanged as observed through `ReadPersistedStrategy` (`HarnessSignalFloorTests.cs:162-167`).
- Floor value actually observed: `Assert.Equal(0.0, run.Options.SignalRateFloor, 12)` passes (`HarnessSignalFloorTests.cs:153`).
- Replay of the legacy arm through the new option: at floor 1.0 the whole challenger artifact and evaluation report are byte-equal to a `HarnessRefinement = false` run (`HarnessSignalFloorTests.cs:226-235`), proving the fallback path is unchanged and the pass-through is the only difference.

## 7. Extra diagnostic evidence (probe, not shipped)

Before writing the run test, a temporary probe test (removed before delivery; not part of the 7 tests) captured the iteration-1 training hands for seed `20261111`/24 hands and dumped `FailureSignatureExtractor.Extract`. Observed for that seed:

```json
{"schema":"tholdem.signatures.v1","signals":[{"name":"fold-to-raise","count":2,"sample":5,"rate":0.4,"direction":"low"},{"name":"call-with-weak-hand","count":1,"sample":4,"rate":0.25,"direction":"low"},{"name":"missed-value","count":1,"sample":2,"rate":0.5,"direction":"high"},{"name":"aggression-by-street","count":9,"sample":35,"rate":0.2571428571428571,"direction":"low"},{"name":"showdown-loss","count":3,"sample":7,"rate":0.42857142857142855,"direction":"low"},{"name":"stack-off-loss","count":1,"sample":1,"rate":1,"direction":"high"}]}
```

`aggression-by-street` is the only signal with `Sample >= 10`, at rate `0.2571428571428571`; floor `1.0` excludes it, hence the legacy fallback in `Run_GivenSignalFloorOption_ExpectedOptionReachesPolicy`. This is recorded as a comment at `HarnessSignalFloorTests.cs:178-184`; the durable proof is the challenger/report byte-equality with the legacy run, not the probe.

## 8. What I could not verify / limitations

- `dotnet test` could not be used: it aborts with `Win32Exception (5)` on the Windows testhost in this environment (given in the task). All observations use the supplementary `RunXunit` runner on the same Release assembly. No CI-green claim is made.
- Only floors `0.0` (default) and `1.0` (fallback through a real run) were exercised end-to-end through `TrainingLoop` here; floors `0.05`/`0.10`/`0.20` are covered at policy level and are the subject of the F3 dev arms, not this round.
- The option is not exposed through the web API, `MetaLoop`, the ablation runner or the host command line; those call sites keep the default `0` and were left untouched per scope. ❓ Exposing it there would be a separate change and is not required by F1.
- F2-F5 of the goal (pre-registration is present, dev tuning, fresh holdout, decision/report) were not run in this round; this document covers F1 only.
- No change to the frozen gate, `FrozenComponents`, `MetaLoop`, proposer/genome/artifact types, memory/lineage/bootstrap/ablation files or `Wip.slnx`; the only product diffs are the three files listed in section 1.
- Nothing required failed: all 7 new tests pass and the full 394-test run has 0 failures.