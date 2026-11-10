# Round 8 — Refiner-policy evolution (R-12)

## Outcome

- **Default invariant preserved.** The default meta artifact is still
  `tholdem-proposer-v1-24c2109a` with hash
  `24c2109afefc9d9abe3c9f932639663193323e7594151ab3ce05fe98aa18b8b7` and exactly **344**
  canonical bytes; the default meta-evaluation document still hashes
  `2f2888d5fcd33c52057c8994b1a5656f8cc441bd5a8d43dcc010f5e241aa3bdd` and is **1545** bytes.
  Neither byte stream contains `refinementStrategy`. The recorded pre-change and post-change
  probe outputs are identical (`.tmp/goal-refinement/round8-meta-baseline.txt` and
  `.tmp/goal-refinement/round8-meta-after.txt`).
- **Bounded data-only strategy.** `RefinementStrategy` is a two-value enum
  (`SessionDriven = 0`, `SignatureDriven = 1`) at `RefinementStrategy.cs:9-16`; it rides as the last
  positional `ProposerGenome` parameter with default `SessionDriven` (`ProposerGenome.cs:26-31`).
  `Clamp()` preserves it (record `with` copy), `BehaviorEquals` includes it
  (`ProposerGenome.cs:62-67`), and the canonical writer emits it only when it is non-default
  (`MetaArtifact.cs:195-204`).
- **Policy selection.** `SignatureDrivenRefinementPolicy` gains a last optional
  `strategy = RefinementStrategy.SignatureDriven` constructor parameter
  (`SignatureDrivenRefinementPolicy.cs:32-45`); `SessionDriven` returns the exact legacy
  delegation used by the empty-context fallback (`:51-56`, `:198-200`), `SignatureDriven` keeps the
  shipped signature edit.
- **Meta-loop opt-in.** `MetaRunOptions.UseHarnessRefinement = false` is the last optional
  parameter (`MetaLoop.cs:10-28`); the production episode runner passes
  `HarnessRefinement: options.UseHarnessRefinement`, `RefinementStrategy: proposer.RefinementStrategy`
  and `SkillRefinement: false` into the unchanged `TrainingOptions`/`TrainingLoop`
  (`MetaLoop.cs:116-127`; `TrainingLoop.cs:195-198`). `DeterministicMetaProposer` gains one
  strategy-toggle candidate as the fifth candidate (`MetaArtifact.cs:244-249`), so the existing
  one-field-per-iteration meta policy can evolve the field.
- **Frozen gate untouched.** The five gate components, `FrozenComponents`, `SeedDerivation` and the
  frozen hash-pinned sources are byte-identical to the round-6 snapshot, and the pinned manifest
  hash is still `95e010703d8ef7cd4d7cc2fae4ca839f7dd80b4c99b7645c7ac3d111ca748a5d`
  (`.tmp/goal-refinement/round8-frozen-hashes.txt`; recorded value in
  `docs/goal-rsi/evidence.md:21`). The hit-rate criterion and fresh-holdout ratchet are unchanged:
  the focused meta tests assert the same `MetaDecision` value and document hash with the flag off
  and on.
- **Tests — 11 new, 386/386.** The unfiltered supplementary runner reports
  **386 passed / 0 failed / 0 skipped** (375 pre-existing + 11 new). No existing test was edited,
  skipped, deleted or weakened.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/RefinementStrategy.cs` | new: the two-value enum, `SessionDriven = 0` default |
| `WIP/Wip.Tholdem.Agents/Learning/ProposerGenome.cs` | last positional `RefinementStrategy` parameter; `BehaviorEquals` includes it |
| `WIP/Wip.Tholdem.Agents/Learning/MetaArtifact.cs` | optional `refinementStrategy` emission, absent-member parse, unknown refusal, strategy-toggle meta candidate |
| `WIP/Wip.Tholdem.Agents/Learning/MetaEvaluation.cs` | the evaluation document's proposer blocks use the same optional emission |
| `WIP/Wip.Tholdem.Agents/Learning/MetaEvaluationJson.cs` | absent member parses as the default; unknown value refuses with `meta-document-shape-invalid` |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/SignatureDrivenRefinementPolicy.cs` | last optional strategy parameter and the `SessionDriven` delegation branch |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingOptions.cs` | last optional `RefinementStrategy`, default `SignatureDriven` |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs` | pass the option into the refinement policy |
| `WIP/Wip.Tholdem.Agents/Learning/MetaLoop.cs` | `UseHarnessRefinement` option and the three-episode-runner flags |
| `WIP/Wip.Tholdem.Tests/Learning/HarnessRefinerTests.cs` | new: 11 xUnit runtime tests |
| `docs/goal-tholdem-harness/round-8/implementation.md` | this document |

No `.csproj`, `Wip.slnx`, engine/history/simulator/host/web/runtime/Bones/Shell, frozen component,
`StrategyGenome`, `StrategyArtifact`, `StrategyAgent`, `Skills/**`, `RefinementGuard`,
memory/lineage/bootstrap/ablation file was touched.

## Exact enum and serialization rule

```csharp
public enum RefinementStrategy
{
    SessionDriven = 0,
    SignatureDriven = 1,
}
```

`ProposerGenome` is `(MutationStep, ParameterSelectionPolicy, EvaluationBudgetShare, PromptTemplate,
RefinementStrategy = SessionDriven)` — the new field is the last positional parameter
(`ProposerGenome.cs:26-31`). The canonical `proposerGenome` object keeps the pre-R-12 key order and
appends the optional member **after** `promptTemplate`, **only** when the value is not the default
(`MetaArtifact.cs:158-204`):

```text
{"mutationStep":<number>,"parameterSelectionPolicy":"sessionDriven|cyclic|aggressiveFirst","evaluationBudgetShare":<number>,"promptTemplate":"<text>"[,"refinementStrategy":"signature-driven"]}
```

- `SessionDriven` (the default, ordinal 0) emits **nothing**. The default artifact therefore keeps
  `id=tholdem-proposer-v1-24c2109a`, hash `24c2109a…b8b7`, 344 bytes and SHA-256 `e92b24a6…b54f9`.
- `SignatureDriven` emits `,"refinementStrategy":"signature-driven"` and participates in the content
  hash (`MetaArtifact.Hash` covers `ToHashInputJson`).
- Readers: `MetaArtifact.TryParse` (`MetaArtifact.cs:81-86`) and `MetaEvaluationJson.ParseProposer`
  (`MetaEvaluationJson.cs:149-165`) both call `ReadRefinementStrategy`
  (`MetaArtifact.cs:125-143`). An absent member is `SessionDriven`; an unknown string (or a
  non-string) throws `FormatException`, which the artifact parser maps to `false` and the document
  parser maps to its stable reason `meta-document-shape-invalid` (`MetaEvaluationJson.cs:10`, `:140-145`).
- `MetaEvaluationDocument.AppendProposer` uses the identical rule (`MetaEvaluation.cs:313-324`), so
  no existing default-document hash moves.

The strategy-toggle meta candidate is the last of the five candidates:

```csharp
genome => genome with
{
    RefinementStrategy = genome.RefinementStrategy == RefinementStrategy.SessionDriven
        ? RefinementStrategy.SignatureDriven
        : RefinementStrategy.SessionDriven,
},
```

(`MetaArtifact.cs:244-249`). It changes exactly one field and `BehaviorEquals` now includes the
field, so the candidate is never classified as inert; `MetaProposer_GivenDefaultIncumbent_ExpectedBoundedStrategyToggle`
proves the toggle at iteration 4 and its bounded single-field shape
(`HarnessRefinerTests.cs:170-186`).

## Meta-loop wiring

`MetaRunOptions` ends with `bool UseHarnessRefinement = false` (`MetaLoop.cs:10-28`). The production
`TrainingLoopEpisodeRunner.Run` builds the existing `TrainingOptions` with
`HarnessRefinement: options.UseHarnessRefinement`, `SkillRefinement: false` and
`RefinementStrategy: proposer.RefinementStrategy` (`MetaLoop.cs:116-127`). Because the refinement
strategy is data on the proposer genome, the meta-loop needs no new `IMetaEpisodeRunner` method:

- flag off ⇒ `TrainingLoop` takes the legacy branch (`TrainingLoop.cs:173-182`) and the legacy
  proposal branch (`:202-206`); the additional genome field is inert and no existing default path
  changes.
- flag on, strategy `SessionDriven` ⇒ the policy delegates to the same legacy proposer
  (`SignatureDrivenRefinementPolicy.cs:51-56`), so the challenger equals the legacy challenger.
- flag on, strategy `SignatureDriven` ⇒ the shipped signature edit.

Nothing in `MetaLoop.Run`, `BuildDecision`, `MetaDecisionVerifier`, `StatisticalPromotionPolicy`,
`PromotionVerifier`, `MatchEvaluator`, `EvaluationCriteria` or `SeedDerivation` was edited. The new
option is not read by any gate; it only selects the inner proposal path.

## Default-hash and frozen evidence

### Pre-change meta artifact/document baseline (before any edit)

Command:

```powershell
dotnet run --project .tmp/goal-refinement/round8-meta-probe -c Release
```

Observed (`.tmp/goal-refinement/round8-meta-baseline.txt`):

```text
ID=tholdem-proposer-v1-24c2109a
HASH=24c2109afefc9d9abe3c9f932639663193323e7594151ab3ce05fe98aa18b8b7
LEN=344
JSON_SHA256=e92b24a688bfdb7cc8ed48b9e687bdfca1590c6e7c380bc650ca7b3cdd8b54f9
DOCUMENT_HASH=2f2888d5fcd33c52057c8994b1a5656f8cc441bd5a8d43dcc010f5e241aa3bdd
DOCUMENT_LEN=1545
```

The id `tholdem-proposer-v1-24c2109a` is the same value the earlier Mini-RSI evidence recorded
(`docs/goal-rsi/evidence.md:21`), which independently confirms the canonical bytes.

### Post-change re-probe (same source, rebuilt after R-12)

Observed (`.tmp/goal-refinement/round8-meta-after.txt`): `PINNED_MANIFEST=95e010703d8ef7cd4d7cc2fae4ca839f7dd80b4c99b7645c7ac3d111ca748a5d`,
and the `ID`, `HASH`, `LEN`, `JSON_SHA256`, `DOCUMENT_HASH` and `DOCUMENT_LEN` lines are byte-for-byte
identical to the pre-change output above. The pinned manifest hash is the same value recorded in
`docs/goal-rsi/evidence.md:21`.

### Legacy CLI baseline (D1 invariant)

Command (twice, into fresh directories):

```powershell
dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train --seed 1099511627776 --iterations 2 --data-dir .tmp/goal-refinement/round8-legacy-a
dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train --seed 1099511627776 --iterations 2 --data-dir .tmp/goal-refinement/round8-legacy-b
```

Observed: both runs report the recorded outcomes (`challenger=…-0ceaa8fd` and `-e1ba56f7`, both
`rejected`, `final champion: tholdem-strategy-v1-f100e793`), and all **7/7** JSON artifacts in both
directories hash exactly to `docs/goal-tholdem-harness/round-0/baseline-hashes.txt`
(`.tmp/goal-refinement/round8-legacy-hashes.txt`: seven `MATCH` lines, A and B and baseline all
equal).

### Frozen sources

All 13 files in `docs/goal-tholdem-harness/round-6/frozen-after-skills.txt` (the five gate
components, `FrozenComponents`, `StrategyGenome`, `StrategyArtifact`, `StrategyAgent`, the two skill
files, `RefinementGuard`, `MatchSimulator`) re-hash to the recorded SHA-256 values after this round
(`.tmp/goal-refinement/round8-frozen-hashes.txt`: thirteen `MATCH` lines). No frozen manifest input
changed.

## Test-first evidence

### 1. Pre-change baseline

```text
TOTAL 375 PASSED 375 FAILED 0 SKIPPED 0
```

(`.tmp/goal-refinement/round8-baseline-full.log`; command below.)

### 2. Red: tests compiled against the API surface, behaviour not yet implemented

The surface (enum, positional genome field, optional constructor/option parameters) was added first
so the red run is a runtime failure set, not a compile failure. Command:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessRefiner
```

Observed (`.tmp/goal-refinement/round8-red-focused.log`):

```text
FAIL HarnessRefinerTests.Document_GivenSignatureDrivenStrategy_ExpectedOptionalEmissionAndStableRejection: ContainsException: Assert.Contains() Failure: Sub-string not found
FAIL HarnessRefinerTests.Genome_GivenSignatureDrivenStrategy_ExpectedBehaviorInequalityAndEmittedField: FalseException: Assert.False() Failure
FAIL HarnessRefinerTests.MetaLoop_GivenUseHarnessRefinement_ExpectedSameFrozenManifestGateAndFreshHoldout: EqualException: Assert.Equal() Failure: Strings differ
FAIL HarnessRefinerTests.MetaProposer_GivenDefaultIncumbent_ExpectedBoundedStrategyToggle: InvalidOperationException: Sequence contains no matching element
FAIL HarnessRefinerTests.Policy_GivenSessionDrivenStrategy_ExpectedLegacyProposal: EqualException: Assert.Equal() Failure: Values differ
FAIL HarnessRefinerTests.Run_GivenSessionDrivenOption_ExpectedRefinementPolicyDelegates: EqualException: Assert.Equal() Failure: Strings differ
FAIL HarnessRefinerTests.TrainingLoopEpisodeRunner_GivenUseHarnessRefinement_ExpectedFlagsReachTrainingLoop: NotEqualException: Assert.NotEqual() Failure: Strings are equal
TOTAL 11 PASSED 4 FAILED 7 SKIPPED 0
```

The four passing tests are the ones whose contract already holds at the surface stage: the two
default-bytes pins (the default serializer had not started emitting the field, which is exactly the
invariant) and the two existing-behaviour preservation tests. The seven failures are the new
behaviours: no field emitted, `BehaviorEquals` ignoring the field, no meta candidate, no
`SessionDriven` branch, and no flag/wiring. The policy failure output showed the ignored option
directly: the supplied 0.08-step legacy proposer expected `SizingMultiplier = 1.08`, while the
unmodified policy returned the signature edit `CallThreshold = 0.37`.

### 3. Green

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessRefiner
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed:

```text
BUILD_EXIT=0 (0 errors; 12 warnings, all pre-existing files, none in a new R-12 file)
TOTAL 11 PASSED 11 FAILED 0 SKIPPED 0
TOTAL 386 PASSED 386 FAILED 0 SKIPPED 0
```

(`.tmp/goal-refinement/round8-green2-build.log`, `.tmp/goal-refinement/round8-green2-focused.log`,
`.tmp/goal-refinement/round8-full-final.log`; the final rerun produced the same totals in
`.tmp/goal-refinement/round8-final-build.log`, `round8-final-focused.log` and
`round8-final-full.log`.) `dotnet test` still aborts with
Win32Exception (5) on this machine, so the supplied supplementary runner executed the same compiled
assembly, as in prior rounds.

### 4. New tests

| Test (`HarnessRefinerTests.cs`) | Behaviour proven |
|---|---|
| `Genome_GivenDefaultStrategy_ExpectedMetaArtifactBytesUnchanged` (`:30`) | exact default id/hash/344-byte canonical JSON, no `refinementStrategy` member, absent-member parse returns the default and re-canonicalizes identically |
| `Genome_GivenSignatureDrivenStrategy_ExpectedBehaviorInequalityAndEmittedField` (`:49`) | `BehaviorEquals` inequality both ways, `Clamp()` keeps the strategy, the emitted `"signature-driven"` member, a changed artifact hash/id, round-trip parse, unknown string refused |
| `Document_GivenDefaultStrategy_ExpectedCanonicalBytesAndHashUnchanged` (`:72`) | pinned default document hash `2f2888d5…` and 1545 bytes, no member, document round-trip |
| `Document_GivenSignatureDrivenStrategy_ExpectedOptionalEmissionAndStableRejection` (`:87`) | document emits the member and round-trips; an unknown value refuses with the stable `meta-document-shape-invalid` reason |
| `Policy_GivenSessionDrivenStrategy_ExpectedLegacyProposal` (`:107`) | supplied legacy proposer's exact genome and `legacy-session-driven` rationale; the signature path gives a different genome |
| `Policy_GivenSignatureDrivenStrategy_ExpectedSignatureEdit` (`:136`) | exact 0.08 call-threshold edit and rationale; empty context still falls back to the exact legacy proposal |
| `MetaProposer_GivenDefaultIncumbent_ExpectedBoundedStrategyToggle` (`:170`) | exactly one strategy-toggle candidate in iterations 1..5, with every other genome field unchanged and behaviour unequal both ways |
| `MetaLoop_GivenUseHarnessRefinement_ExpectedSameFrozenManifestGateAndFreshHoldout` (`:190`) | two bounded meta-runs (flag off/on) with identical pinned manifest hash, identical `MetaDecision` value and document hash, the unchanged `requiredHitRateImprovement`/margin/min-seed/z fields, both primary and fresh reports, and three real evaluator comparisons (2 primary + 1 fresh) |
| `MetaLoop_GivenCandidateWinsMetaLosesFresh_ExpectedRefusal` (`:246`) | with the flag on, a fresh-holdout regression is refused with `rejected:fresh-holdout-regression`, the document verifier returns `meta-verifier:fresh-holdout-regression`, no eligible proposer and no active-pointer move |
| `Run_GivenSessionDrivenOption_ExpectedRefinementPolicyDelegates` (`:278`) | one real training run: `SessionDriven` challenger id and genome equal the legacy run while the default `SignatureDriven` challenger differs |
| `TrainingLoopEpisodeRunner_GivenUseHarnessRefinement_ExpectedFlagsReachTrainingLoop` (`:326`) | production episode runner with a real `TrainingLoop`: flag-off and flag-on+`SessionDriven` produce the legacy challenger id, flag-on+`SignatureDriven` differs, proving both flags reach the inner loop |

## Deviations (deliberate, none weakening an invariant)

1. **Eleven tests instead of the required minimum seven.** Four additive tests pin the default
   document bytes, the document's optional-member behaviour, the deterministic meta candidate, and
   the production `TrainingLoopEpisodeRunner` flag propagation. They are additional coverage, not
   replacements.
2. **The meta-gate test uses the existing fake episode runner/evaluator.** A tiny all-real meta run
   can legitimately reach the A5 self-comparison refusal when the inner loop promotes nothing, which
   would leave no `MetaDecision` to inspect. The gate test therefore uses the same
   `FakeEpisodeRunner`/`FakeMetaEvaluator` pattern as `MetaLoopTests.cs:224-257`, while the
   production-runner wiring is proven separately by
   `TrainingLoopEpisodeRunner_GivenUseHarnessRefinement_ExpectedFlagsReachTrainingLoop` (`:326`)
   with a real 120-hand `TrainingLoop` and the recorded training seed.
3. **Two red-phase test corrections, both disclosed.** The first implementation attempt exposed two
   authoring errors in the new tests: the `SessionDriven` policy test expected a call-threshold edit
   although the supplied 0.08-step proposer moves sizing on that profitable/aggressive session, and
   an extra persisted-document assertion failed on a pre-existing path discrepancy (see below).
   The corrected policy test still asserts exact genome equality against the supplied proposer
   (now also pinning `SizingMultiplier + 0.08` and unchanged `CallThreshold`); the removed assertion
   was not part of the required contract and is replaced by the `Document` round-trip tests.
   No existing test was changed.
4. **Supplementary runner.** `dotnet test` aborts with Win32Exception (5) on this machine; the
   supplied `.tmp/tholdem-verify/RunXunit` executed the same compiled test assembly, as in prior
   rounds.

## Out-of-scope observation (❓, not changed)

While writing the removed persistence assertion the new test observed that
`MetaStore.TryReadEvaluationDocument(dataDirectory, …)` returns `null` after a meta run even when
`MetaEvaluationDocument` was persisted: `MetaLoop.Run` passes its already-composed
`<data>/meta/evaluations` path into `MetaStore.WriteEvaluationDocument`
(`MetaLoop.cs:530`), which prepends `meta/evaluations` again (`MetaLoop.cs:798-806`), while the read
helper (`MetaLoop.cs:788-796`) and `RsiEndpoints.cs:110-113` look in `<data>/meta/evaluations`.
This is pre-existing code untouched by R-12 and outside the round's scope; ❓ whether the web
promote path is affected was not investigated, and the R-12 tests do not depend on that path.

## Anything not passing

Nothing fails. The build has 0 errors; the focused class is 11/11 and the unfiltered assembly is
**386/386** with 0 failed and 0 skipped. The only limitations are the known `dotnet test`
Win32Exception (5) workaround and the untouched pre-existing path observation above.