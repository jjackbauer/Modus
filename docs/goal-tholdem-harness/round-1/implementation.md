# Round 1 — Harness-refinement vertical slice (R-01, R-02, R-05)

## Outcome

Implemented the first opt-in, data-only refinement slice:

- **R-01** — every training-match hand is written through the existing
  `SimulationOptions.HistoryDirectory`, parsed back through `HandRecordJson.Parse`, sorted by
  `HandNumber`, and the scratch directory is deleted in a `finally`. Legacy mode never passes a
  history directory and creates no `harness` directory.
- **R-02** — six named, bounded failure signals (`fold-to-raise`, `call-with-weak-hand`,
  `missed-value`, `aggression-by-street`, `showdown-loss`, `stack-off-loss`) with count, sample,
  rate and a fixed-vocabulary direction; canonical JSON carries no seed, card, opponent name or
  free text.
- **R-05 (minimal)** — `IRefinementPolicy`/`RefinementContext`/`RefinementProposal` boundary and a
  deterministic `SignatureDrivenRefinementPolicy` behind the new opt-in
  `TrainingOptions.HarnessRefinement`. The statistical gate, evaluator, verifier, seed derivation,
  ledger schema and artifact schema are untouched.

Status: all 8 new tests pass; the full test assembly passes 325/325; legacy-mode artifacts match the
recorded pre-change baseline on all 7 files in two fresh directories.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/TrainingTrajectoryCapture.cs` | new |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/SessionSignatures.cs` | new |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/FailureSignatureExtractor.cs` | new |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/IRefinementPolicy.cs` | new |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/SignatureDrivenRefinementPolicy.cs` | new |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingOptions.cs` | one optional positional flag at the end (`:13`) |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs` | minimal opt-in branch (`:118-145`, `:154-164`) |
| `WIP/Wip.Tholdem.Tests/Learning/HarnessRefinementTests.cs` | new, 8 tests |
| `docs/goal-tholdem-harness/round-1/implementation.md` | this document |

No engine, history, card, hand, simulator, host, web, frozen-gate, solution or project file was
modified. No existing test was edited, skipped or weakened.

## Test-first evidence

### 1. Red before any refinement source existed

Command:

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q
```

Observed (representative lines; the test file existed and no `Learning/Refinement` type did):

```text
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Learning\HarnessRefinementTests.cs(6,35): error CS0234: The type or namespace name 'Refinement' does not exist in the namespace 'Wip.Tholdem.Agents.Learning' (are you missing an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Learning\HarnessRefinementTests.cs(370,55): error CS0246: The type or namespace name 'FailureSignal' could not be found (are you missing a using directive or an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
Build FAILED.
    0 Warning(s)
    3 Error(s)
```

### 2. Red with only the API surface stubbed (runtime failures, not compile failures)

The API types were added with `NotImplementedException` bodies (removed in the green implementation);
the production loop did not yet branch on the flag. Command:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessRefinementTests
```

Observed:

```text
FAIL HarnessRefinementTests.Capture_GivenTrainingRun_ExpectedAllHandsAndScratchRemoved: NotImplementedException: red: trajectory capture is not implemented yet.
FAIL HarnessRefinementTests.Extract_GivenHands_ExpectedNoSeedCardOrNameInCanonicalJson: NotImplementedException: red: trajectory capture is not implemented yet.
FAIL HarnessRefinementTests.Extract_GivenSyntheticHands_ExpectedNamedSignals: NotImplementedException: red: signature extraction is not implemented yet.
FAIL HarnessRefinementTests.Policy_GivenFoldToRaiseSignal_ExpectedCallThresholdLoweredByStep: NotImplementedException: red: signature-driven refinement is not implemented yet.
FAIL HarnessRefinementTests.Policy_GivenNoSignals_ExpectedLegacySessionDrivenProposal: NotImplementedException: red: signature-driven refinement is not implemented yet.
FAIL HarnessRefinementTests.Run_GivenHarnessRefinement_ExpectedSignatureDrivenChallenger: NotEqualException: Assert.NotEqual() Failure: Strings are equal
Expected: Not "tholdem-strategy-v2-b155b98f"
Actual:       "tholdem-strategy-v2-b155b98f"
TOTAL 8 PASSED 2 FAILED 6 SKIPPED 0
```

The two passing tests were the legacy-mode tests, which the slice must not change
(`HarnessRefinementTests.cs:307`, `:347`).

## Green evidence

### Builds

```powershell
dotnet build WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj -c Release --no-incremental --nologo -v m
```

Observed: `Build succeeded.` / `2 Warning(s)` / `0 Error(s)`. Both warnings are pre-existing and in
untouched files: `WIP/Wip.Runtime/Runtime/AgentExecutor.cs(143,53)` (CS8425) and
`WIP/Wip.Runtime/Runtime/DeepSeekChatModel.cs(125,17)` (CA2024).

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental --nologo -v m
```

Observed: `Build succeeded.` / `16 Warning(s)` / `0 Error(s)`. All 16 unique warnings are in
pre-existing files (`Wip.Runtime`, `Wip.Tholdem.Host/Commands/TrainCommand.cs`,
`Learning/TrainingLoopTests.cs`, `Learning/LearningWebIntegrationTests.cs`,
`Training/ThreatControlTests.cs`); none is in a file added by this round.

### Tests

`dotnet test` aborts in this environment as documented (Win32Exception (5) while the testhost opens
its parent process handle), so the repository's supplementary runner was used as directed:

```powershell
dotnet test WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TrainingLoopTests"
```

Observed:

```text
Unhandled exception. System.ComponentModel.Win32Exception (5): Access is denied.
...
Test Run Aborted.
```

Focused new tests:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessRefinementTests
```

Observed (saved in `.tmp/goal-refinement/round1-refinement-pass.log`):

```text
TOTAL 8 PASSED 8 FAILED 0 SKIPPED 0
```

All `Learning` tests:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Learning
```

Observed (saved in `.tmp/goal-refinement/round1-learning-pass.log`):

```text
TOTAL 44 PASSED 44 FAILED 0 SKIPPED 0
```

Full test assembly (unfiltered; not a filtered subset):

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed (saved in `.tmp/goal-refinement/round1-full-suite.log:7314`), exit code 0:

```text
TOTAL 325 PASSED 325 FAILED 0 SKIPPED 0
```

### Legacy equivalence (byte-for-byte)

The round-0 baseline records the pre-change hashes of the 7 JSON artifacts produced by the Host CLI
(`docs/goal-tholdem-harness/round-0/baseline-hashes.txt:5-11`) with seed `1099511627776` and 2
iterations.

Before any source edit, the same command reproduced that baseline exactly in two fresh directories
(`A=7 B=7 DIFF=0`). After the implementation, two fresh directories were produced with:

```powershell
dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train --seed 1099511627776 --iterations 2 --data-dir .tmp/goal-refinement/round1-a
dotnet run --project WIP/Wip.Tholdem.Host -c Release --no-build -- train --seed 1099511627776 --iterations 2 --data-dir .tmp/goal-refinement/round1-b
```

Both runs printed the same iteration lines, for example:

```text
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-0ceaa8fd promoted=false verified=true outcome=rejected meanBbPer100=0.63 lcb=-0.45 seeds=24
iteration=2 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-e1ba56f7 promoted=false verified=true outcome=rejected meanBbPer100=1.67 lcb=-0.88 seeds=24
final champion: tholdem-strategy-v1-f100e793
```

SHA-256 comparison against `round-0/baseline-hashes.txt`:

```text
baseline=7 A=7 B=7
MISMATCH=0
harnessA=False harnessB=False
```

That is: all 7 files in each fresh directory hash exactly to the recorded pre-change value, and no
`harness` directory is created with refinement off.

The in-process check `Run_GivenLegacyMode_ExpectedSameArtifactsAsSecondLegacyRun`
(`HarnessRefinementTests.cs:307`) does the same comparison between two fresh in-test directories with
reduced hands for speed: it asserts exactly 7 JSON files and equal SHA-256 for every file.

## Members implemented

| Member | Location | Behaviour |
|---|---|---|
| `TrainingOptions.HarnessRefinement` | `Learning/TrainingOptions.cs:13` | last positional parameter, default `false`; all existing call sites remain valid |
| `TrainingTrajectoryCapture.ScratchDirectory` | `Refinement/TrainingTrajectoryCapture.cs:23` | `data/harness/trajectory/iteration-00001` with `D5` invariant formatting |
| `TrainingTrajectoryCapture.Capture` | `Refinement/TrainingTrajectoryCapture.cs:45` | runs the match with `HistoryDirectory`, parses `hand-*.json` through `HandRecordJson.Parse`, orders by `HandNumber`, deletes scratch in `finally` (`:72-78`) |
| `FailureSignal` | `Refinement/SessionSignatures.cs:37` | `Name`, `Count`, `Sample`, `Rate`, `Direction` |
| `SessionSignatures` | `Refinement/SessionSignatures.cs:43` | bounded value (cap constant `:47`), canonical JSON without seed/card/name (`:51-80`) |
| `FailureSignatureExtractor.Extract` | `Refinement/FailureSignatureExtractor.cs:20` | one deterministic pass over hands ordered by `HandNumber`, emits the six signals at `:117-153` |
| `RefinementProposal` / `RefinementContext` / `IRefinementPolicy` | `Refinement/IRefinementPolicy.cs:6`, `:12`, `:19` | data-only policy boundary |
| `RefinementRationaleCodes` | `Refinement/IRefinementPolicy.cs:25` | fixed vocabulary, no free-text rationale |
| `SignatureDrivenRefinementPolicy` | `Refinement/SignatureDrivenRefinementPolicy.cs:13` | step `0.08` (`:16`), outcome step `0.06` (`:19`), sample floor `10` (`:22`) |
| `SignatureDrivenRefinementPolicy.Propose` | `Refinement/SignatureDrivenRefinementPolicy.cs:31` | strongest usable signal (`:59`), clamped mapping (`:73`, `:102`), legacy fallback (`:125`), threshold flip on behavioural equality (`:139`) |
| loop integration | `Learning/TrainingLoop.cs:118-145`, `:154-164` | opt-in capture + signature proposal; legacy branch is the identical `StrategyProposer` call |
| proposal guard | `Learning/TrainingLoop.cs:169-171` | unchanged, still runs before evaluation |

**No stub or `NotImplementedException` remains in any implemented member.** Verified with:

```powershell
Select-String -Path WIP/Wip.Tholdem.Agents/Learning/Refinement/*.cs -Pattern 'NotImplementedException|red:'
```

Observed: no matches. The only `NotImplementedException` occurrences ever added were the deliberate
red-phase stubs, all replaced before the green run.

### Signal definitions (exact semantics used)

| Signal | Sample (opportunity) | Count (hit) |
|---|---|---|
| `fold-to-raise` | hands where seat 0 acted immediately after a same-street opponent `Bet`/`Raise`/`AllIn` | sample hands where that immediate action was `Fold` |
| `call-with-weak-hand` | hands with a five-card board where seat 0 called at least once | sample hands whose final category is `HighCard`/`OnePair` and that were lost |
| `missed-value` | hands with a five-card board and final category `>= TwoPair` | sample hands with no post-flop seat-0 `Bet`/`Raise`/`AllIn` |
| `aggression-by-street` | seat-0 post-flop (`Street >= 1`) actions | sample actions that are `Bet`/`Raise`/`AllIn` |
| `showdown-loss` | hands with `Showdown == true` | sample hands where seat 0 is not a winner |
| `stack-off-loss` | hands where seat 0 went `AllIn` or finished with stack 0 | sample hands that finished with stack 0 and did not win |

`Rate == 0` when `Sample == 0`. Direction is `high`/`low` by the `0.5` threshold (aggression adds
`neutral` at exactly `0.5`). The evaluator is called only for a five-card board
(`FailureSignatureExtractor.cs:194-203`), so pre-flop-ending hands cannot be classified weak or
strong.

## Deviations from REQUIRED DESIGN and interpretations

1. **Optional legacy-proposer injection.** The required fallback is
   `new DeterministicStrategyProposer().Propose(...)`. The constructor instead accepts an optional
   `IStrategyProposer? legacyProposer = null` defaulting to `new DeterministicStrategyProposer()`, and
   `TrainingLoop` passes the proposer it already resolved. With the default proposer the bytes are
   identical to the required fallback; with a caller-supplied proposer an empty context keeps using
   that caller's proposer instead of silently replacing it. Chosen so "empty context is the legacy
   proposal path" stays true for custom proposers too.
2. **`aggression-by-street` with a `high` direction.** The required mapping only names `low ->
   BluffFrequency +0.08`. A mirrored `high -> BluffFrequency -0.08` was added and `neutral` falls back
   to the legacy proposal. Without it, a strongest high-aggression signal would have no legal edit.
3. **`AllIn` counts as aggression.** The engine records a distinct `AllIn` kind; the simulator itself
   counts it as aggressive. It is treated as a bet/raise for `fold-to-raise` and `missed-value` and as
   an aggressive action for `aggression-by-street`. Excluding it would overlook shoves.
4. **`call-with-weak-hand` sample requires a known category.** Only hands with a five-card board are
   in the sample, matching the "evaluate only when the board has at least 5 cards" rule; pre-flop
   endings are unclassifiable rather than assumed weak.
5. **`stack-off-loss` sample.** The requirement defines only the hit condition (stack 0, not a
   winner); the denominator is hands where seat 0 went all-in or finished at 0. Without a denominator
   the rate would be meaningless.
6. **Scratch parent directories.** `Capture` deletes the iteration directory itself (as required) and
   leaves the empty `harness/trajectory` parents behind. No hand or session file survives; the
   refinement test asserts no `*.json` remains under `harness`.
7. **Signal selection is literal.** Any signal with `Sample >= 10` is usable, even at `Rate == 0`, as
   the required selection rule states; there is no extra rate floor.

## What could not be made to pass

Nothing. All 8 new tests and all 325 tests in the assembly pass. No existing test was changed,
skipped or weakened, and the frozen-gate files are untouched (the Agents build and the full suite
exercise `FrozenComponents`/`MetaLoopTests`).

## Notes and limits

- The full suite was run through the supplementary `RunXunit` runner because `dotnet test` aborts
  with `Win32Exception (5)` in this environment; the runner itself documents that CI stays the
  authoritative gate (`.tmp/tholdem-verify/RunXunit/Program.cs:4-7`).
- The D1-style hash check was run outside xUnit through the real Host CLI; the in-test equivalence
  check uses smaller hands/evaluation counts for runtime, but still round-trips real stored files.
- The new signature and rationale values are consumed only to choose a genome; they are not added to
  strategy/evaluation/ledger JSON, so all existing artifact schemas and hashes are unchanged.
- Supporting probe (scratch, not a deliverable): a real 120-hand training match for
  `HarnessRefinementTests.Run_GivenHarnessRefinement_ExpectedSignatureDrivenChallenger` produced
  `missed-value count=17 sample=22 rate=0.7727`; the policy returned rationale `missed-value` and
  `raiseThreshold 0.72 -> 0.64`, while the legacy proposer moved `sizingMultiplier 1.0 -> 0.98`.
  This is the concrete reason the two challengers are distinct.
