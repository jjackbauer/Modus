# Round 9 - Meta-evaluation document read-back path

## Outcome

- **One-line product fix.** `MetaLoop.Run` now hands the data directory to the store:
  `MetaStore.WriteEvaluationDocument(dataDirectory, document)`
  (`WIP/Wip.Tholdem.Agents/Learning/MetaLoop.cs:530`). No other product line changed;
  `MetaStore.WriteEvaluationDocument` and `TryReadEvaluationDocument` are untouched
  (`MetaLoop.cs:788-806`).
- **Exact path before.** Pre-fix, `MetaLoop.Run` composed `evaluationsDirectory = <data>/meta/evaluations`
  (`MetaLoop.cs:329`) and passed it as the store root at `MetaLoop.cs:530`. Because
  `WriteEvaluationDocument` prepends `meta/evaluations` itself (`MetaLoop.cs:798-806`), the document
  bytes landed at `<data>/meta/evaluations/meta/evaluations/<candidateId>-vs-<incumbentId>.json`.
  `TryReadEvaluationDocument` (`MetaLoop.cs:788-796`) and the web promote audit
  (`WIP/Wip.Tholdem.Web/Training/RsiEndpoints.cs:110-123`) read
  `<data>/meta/evaluations/<candidateId>-vs-<incumbentId>.json`, so the audit refused with
  `document-missing` (`RsiEndpoints.cs:120-123`).
- **Exact path after.** `<data>/meta/evaluations/<candidateId>-vs-<incumbentId>.json`.
- **Bytes, hash and decision unchanged.** The store still persists `document.ToCanonicalJson()`
  (`MetaLoop.cs:803-806`); the canonical writer (`MetaEvaluation.cs:246-309`), `DocumentHash`
  (`MetaEvaluation.cs:311`), the meta decision (`MetaLoop.cs:494-528`) and the ledger append with
  `document.DocumentHash` (`MetaLoop.cs:541-549`) are unchanged. The pre-existing pinned default
  document test (`WIP/Wip.Tholdem.Tests/Learning/HarnessRefinerTests.cs:72-77`: hash
  `2f2888d5fcd33c52057c8994b1a5656f8cc441bd5a8d43dcc010f5e241aa3bdd`, 1545 bytes) passes unmodified
  inside the 387/387 run below. This closes the pre-existing discrepancy recorded as an out-of-scope
  observation in round 8 (`docs/goal-tholdem-harness/round-8/implementation.md:290-299`).

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/MetaLoop.cs` | one line at `:530`: write-target argument `evaluationsDirectory` -> `dataDirectory` |
| `WIP/Wip.Tholdem.Tests/Learning/MetaDocumentPathTests.cs` | new: one real-loop regression test (`:16-59`) |
| `docs/goal-tholdem-harness/round-9/implementation.md` | this document |

No existing test, `.csproj`, `Wip.slnx`, gate/verifier component or document-schema file was edited.

## Test-first evidence

### 1. Baseline (before any edit)

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed (`RUNNER_EXIT=0`):

```text
TOTAL 386 PASSED 386 FAILED 0 SKIPPED 0
```

### 2. Red - new test compiled against the unfixed product

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q
```

Observed: `BUILD_EXIT=0` (0 errors; 12 pre-existing warnings, none in `MetaDocumentPathTests.cs`).

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll MetaDocumentPath
```

Observed (`RUNNER_EXIT=1`):

```text
FAIL MetaDocumentPathTests.MetaLoop_GivenCompletedIteration_ExpectedDocumentReadableByTryReadEvaluationDocument: NotNullException: Assert.NotNull() Failure: Value is null
TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0
```

The failure is the required read-back assertion (`MetaDocumentPathTests.cs:47`). Because the reported
exception is the `NotNullException` from `:47`, the earlier assertions at `:33-36` all passed: the
iteration was `eligible`, `iteration.Eligible` was `true`, and it carried a non-null
`MetaEvaluationDocument` whose canonical text was computed. Only the read-back failed -
`MetaStore.TryReadEvaluationDocument(dataDirectory, ...)` returned `null` because the persisted bytes
were under the doubly-prefixed directory. Applying the one-line fix at `MetaLoop.cs:530` is the only
product change made after this observation.

### 3. Green - after the one-line fix

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q
```

Observed: `BUILD_EXIT=0` (0 errors).

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll MetaDocumentPath
```

Observed (`RUNNER_EXIT=0`):

```text
TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0
```

### 4. Full suite - final

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed (`RUNNER_EXIT=0`):

```text
TOTAL 387 PASSED 387 FAILED 0 SKIPPED 0
```

386 pre-existing tests + 1 new = 387; no existing test was edited, skipped, deleted or weakened.

## Regression test and options used

`MetaLoop_GivenCompletedIteration_ExpectedDocumentReadableByTryReadEvaluationDocument`
(`MetaDocumentPathTests.cs:16-59`) calls the real `MetaLoop.Run` (`:21-30`) with a temporary data
directory from `AgentTestSupport.CreateTempDirectory` and the existing in-assembly episode doubles
(`WIP/Wip.Tholdem.Tests/Learning/MetaLoopTests.cs:9-124`: `FakeMetaProposer`, `FakeEpisodeRunner`,
`FakeMetaEvaluator`). Options used (`MetaDocumentPathTests.cs:61-78`):

| Option | Value |
|---|---|
| `Seed` | `1_099_511_627_901` |
| `MetaIterations` | 1 |
| `InnerIterations` | 1 |
| `TrainingHands` | 4 |
| `EvaluationSeeds` | 2 |
| `EvaluationHandsPerMatch` | 1 |
| `Criteria` | `MarginBbPer100=0.0`, `MinSeeds=2` |
| `MetaSeeds` | 2 |
| `MetaHandsPerMatch` | 1 |
| `FreshSeeds` | 2 |
| `FreshHandsPerMatch` | 1 |
| `RequiredHitRateImprovement` | 0.1 |
| `MaxInnerIterations` / `MaxEvaluationsPerEpisode` / `MaxWallClock` | 50 / 20 / 1 min |

The proposer is `ProposerGenome.Default with { MutationStep = 0.05 }` against the default incumbent
(`MutationStep = 0.02`, `ProposerGenome.cs:27`); the evaluator uses
`FreshBaseSeed = MetaSeedDerivation.FreshComparisonSeed(Seed)` with lower bounds 20.0 / 5.0 - the same
promoting setup as the existing `Run_GivenEligibleCandidate...` test (`MetaLoopTests.cs:259-309`).
The iteration is therefore `eligible` (primary 20.0 >= margin 0.0, fresh 5.0 >= -margin 0.0, hit-rate
improvement 1.0 >= 0.1). The test pins `MetaDecision.EligibleReason` and `iteration.Eligible`
(`MetaDocumentPathTests.cs:33-34`), so a self-comparison or inert refusal cannot reach the read-back
assertion. No product gate or verifier is stubbed; only episode execution and report comparison use
the existing test doubles.

Assertions (`MetaDocumentPathTests.cs:43-53`):

- `TryReadEvaluationDocument(dataDirectory, document.CandidateId, document.IncumbentId)` is non-null
  (`:47`) and byte-equal to `document.ToCanonicalJson()` (`:48`).
- `<data>/meta/evaluations/meta/evaluations` does not exist (`:50`) and the nested file does not
  exist (`:51`).
- The document exists at `<data>/meta/evaluations/<candidate>-vs-<incumbent>.json` (`:52`), and its
  persisted text equals the in-memory canonical text (`:53`), i.e. no schema or hash change.

## Anything not passing

Nothing fails. Build 0 errors, focused `1/1`, unfiltered `387/387` with 0 failed and 0 skipped.
`dotnet test` was not used because it aborts with Win32Exception (5) on this machine (documented in
prior rounds); the supplied supplementary runner executed the same compiled test assembly. The one
evidence limit is that the red run stops at the read-back assertion (`:47`), so the doubly-prefixed
directory is not directly listed in the red output; it is established by the pre-fix code composition
(`MetaLoop.cs:329` + `:530` pre-fix + `:801-805`) and is now guarded by the green nested-path
assertions (`:50-51`).