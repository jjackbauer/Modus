# Round 6 — Agentic learning loop in `Wip.Tholdem.Agents.Learning`

- **Date**: 2026-11-06
- **Branch**: `WIP`
- **HEAD at verification**: `d7d9723` (unchanged; this round's files are uncommitted working-tree additions, matching rounds 1–5)
- **Scope delivered**: data-only strategies with content-addressed ids, a deterministic proposer, a paired held-out-seed evaluator, the statistical promotion gate, the re-deriving `PromotionVerifier`, `TrainingLoop` persistence (strategies / evaluations / ledger / champion pointer), the viewer ledger rendering fix (TODO-006), and 21 new committed tests including the always-promote control, the negative control and the mutation kill.
- **Not created / not changed**: no `Wip.Tholdem.Host`; no file under `WIP/Wip.Tholdem/**`, `WIP/Wip.Tholdem.Agents/Baselines|Simulation|Runtime|Equity|Randomness/**`, the existing Agents root sources, `WIP/Wip.Tholdem.Web/*.cs`, the existing web tests (`TholdemWebViewerTests.cs`, `ViewerTestSupport.cs`), `Wip.slnx`, `tests/Wip.Architecture.Tests/**`, `tools/run-wip-tests.ps1`, or any other `docs/goal-tholdem/` file. The WIP project count stays 47.

## Files created

| Path | Purpose |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/StrategyGenome.cs` | `StrategyGenome` record + `Clamp()` into `[0,1]` thresholds / `[0.25,3]` sizing |
| `.../Learning/StrategyArtifact.cs` | content-addressed artifact: `Create`, `ToCanonicalJson`, `ToHashInputJson`, `Hash`, `ComputeHash` |
| `.../Learning/SessionAnalysis.cs` | `SessionAnalysis` record + `StrategyAnalyzer.Analyze(SimulationReport)` |
| `.../Learning/StrategyAgent.cs` | deterministic `IPokerAgent` driven by a genome (thresholds, seeded bluff, genome sizing) |
| `.../Learning/StrategyProposer.cs` | `IStrategyProposer` + `DeterministicStrategyProposer` (one-parameter, fixed-step, data-only) |
| `.../Learning/EvaluationCriteria.cs` | `EvaluationCriteria(5.0 bb/100, 24 seeds, Z = 1.95996…)` |
| `.../Learning/EvaluationReport.cs` | canonical evaluation report + SHA-256 `ReportHash` |
| `.../Learning/MatchEvaluator.cs` | paired evaluation: seed `Derive(baseSeed, 9000+index, 0,0,0)`, two mirrored matches per seed |
| `.../Learning/PromotionPolicy.cs` | `PromotionDecision`, `IPromotionPolicy`, `StatisticalPromotionPolicy`, `AlwaysPromotePolicy` |
| `.../Learning/PromotionVerifier.cs` | re-derives the criterion from the report alone |
| `.../Learning/TrainingOptions.cs` | training options record |
| `.../Learning/TrainingIteration.cs` | one iteration record |
| `.../Learning/TrainingRun.cs` | run record with per-iteration ledger JSON |
| `.../Learning/TrainingLoop.cs` | the loop + reproducible persistence |
| `WIP/Wip.Tholdem.Tests/Learning/LearningTestSupport.cs` | recording agent decorator, SHA-256/UTF-8 helpers, synthetic report builder, legality assertion |
| `.../Learning/StrategyArtifactTests.cs` | 3 facts: identity/hash, parent chain, fixed canonical order |
| `.../Learning/StrategyAgentTests.cs` | 2 facts: legality over 40 simulated hands, determinism over every recorded turn |
| `.../Learning/MatchEvaluatorTests.cs` | 2 facts: exact paired recomputation + interval; one-seed degenerate interval |
| `.../Learning/PromotionGateTests.cs` | 4 facts: gate math, documented rejections, always-promote control, verifier accept/reject |
| `.../Learning/StrategyProposalTests.cs` | 2 facts: exactly one parameter moves by exactly one step; boundaries still change |
| `.../Learning/StrategyAnalyzerTests.cs` | 1 fact: seat-zero metrics, documented VPIP, determinism |
| `.../Learning/TrainingLoopTests.cs` | 4 facts: always-promote control, negative control, reproducibility, persistence/champion schema |
| `.../Learning/LearningWebIntegrationTests.cs` | 1 fact: real Kestrel host serves the trained `GET /api/strategies` and `GET /api/ledger` |
| `.../Learning/ViewerLedgerRenderingTests.cs` | 1 fact: nine ledger labels + text-only cell rendering (TODO-006) |
| `.../Learning/LearningSourceSafetyTests.cs` | 1 fact: no `Process`, `HttpClient`, `git `, `Assembly.Load`, `Compile` in the learning layer |
| `docs/goal-tholdem/round-6/implementation.md` | this report |

## Files modified

| Path | Change |
|---|---|
| `WIP/Wip.Tholdem.Web/wwwroot/viewer/index.html` | ledger table header now has the nine stable labels `Iteration, Champion, Challenger, Promoted, Reason, Win rate, bb/100, LCB Δ, Samples`; `id="ledger"` tbody unchanged |
| `WIP/Wip.Tholdem.Web/wwwroot/viewer/app.js` | `renderLedger(entries)` writes one `<tr>` per entry with nine `<td>` cells via `document.createElement` + `textContent`; win rate from `entry.winRate` (fallback: `challengerWins / (2 * seeds)`); `renderStrategyDetail(id, payload)` keeps showing the artifact JSON as text in `#strategy-detail`; empty state spans 9 columns; no `innerHTML` in either renderer |
| `docs/plan/test-waivers.json` | `expectedTotals["Wip.Tholdem.Tests"]` 103 → **124** (file parses; verified with `ConvertFrom-Json`) |

## Contract surface (for later rounds)

Namespace `Wip.Tholdem.Agents.Learning`:

- `StrategyGenome(double RaiseThreshold = 0.72, double CallThreshold = 0.45, double BluffFrequency = 0.12, double SizingMultiplier = 1.0, string PromptText = "…")` + `Clamp()`.
- `StrategyArtifact(string Id, int Version, string? ParentId, int CreatedAtIteration, StrategyGenome Genome)` + `Hash`, `ToCanonicalJson()`, `ToHashInputJson()`, `static Create(genome, iteration, parent)`, `static ComputeHash(string)`.
- `SessionAnalysis(int Hands, double WinRate, double BbPer100, double AggressionFactor, double ShowdownRate, double Vpip)` + `StrategyAnalyzer.Analyze(SimulationReport)`.
- `StrategyAgent(StrategyGenome? genome = null, string? name = null)` with `Genome`, `Name`; `IStrategyProposer.Propose(genome, analysis, iteration)`.
- `EvaluationCriteria`, `EvaluationReport` (+ `ToCanonicalJson()`, `ReportHash`), `MatchEvaluator.Evaluate(challenger, challengerId, incumbent, incumbentId, baseSeed, seeds, handsPerMatch, startingStack = 1000, bigBlind = 10)`.
- `PromotionDecision`, `IPromotionPolicy`, `StatisticalPromotionPolicy`, `AlwaysPromotePolicy`, `PromotionVerifier.Verify(decision, report, criteria, out reason)`.
- `TrainingOptions`, `TrainingIteration`, `TrainingRun`, `TrainingLoop.Run(options, policy = null, cancellationToken = default)`.

Reason strings are exactly the contracted ones (`promoted`, `lcb-below-margin`, `insufficient-seeds`, `always-promote-control`, `verifier:lcb-below-margin`, `verifier:insufficient-seeds`); the verifier's two success reasons are `verified` and `rejection-justified`, and an unjustified rejection is `verifier:unjustified-rejection`.

## Implementation notes

### The content address cannot contain itself (documented deviation)

The contract asks for `Hash` = SHA-256 of the canonical JSON *and* `id = tholdem-strategy-v{version}-{first 8 hash chars}`, while the canonical JSON starts with `id`. That is self-referential and unsatisfiable: including the id in the hashed bytes makes the id depend on its own hash. The artifact therefore exposes two canonical forms:

- `ToHashInputJson()` — the canonical payload (`version, parentId, createdAtIteration, parameters{...}, promptText`), which `Hash` is computed over;
- `ToCanonicalJson()` — the persisted form with `id` first, then the identical payload.

`Create` computes the payload hash, builds the id from it, and stores both; the id is reproduced from data alone forever, and `Hash` is reproduced from `ToHashInputJson()` (test 1 asserts the exact strings and the exact SHA-256 relation). No clock, GUID, `System.Random` or culture participates anywhere (`CanonicalJsonWriter`'s `"R"` formatting and `InvariantCulture` throughout).

### Evaluation math

For every held-out seed `s = SeedDerivation.Derive(baseSeed, 9000 + index, 0, 0, 0)` the evaluator plays `challenger@seat0 vs incumbent@seat1` and the mirrored `incumbent@seat0 vs challenger@seat1` with `AlternateButton = true`. The challenger's two net-chip totals are summed per seed, split by `bigBlind` into big blinds, and normalized to bb/100 per session hand (`2 * handsPerMatch`). The mean and the sample standard deviation are computed over those per-seed bb/100 values, and the half-width is `Z * sd / sqrt(seeds)` in the same units, so `LowerBoundBbPer100 = mean - halfWidth` is directly comparable to `EvaluationCriteria.MarginBbPer100`. One seed yields a degenerate interval `low == mean == high`. Small blind is `bigBlind / 2`.

### The gate and the verifier

`StatisticalPromotionPolicy` requires `LowerBoundBbPer100 > MarginBbPer100` **and** `Seeds >= MinSeeds` (exactly-at-margin is a rejection). `PromotionVerifier.Verify` ignores the policy's intent and re-derives the same two conditions from the report: a promotion without them is refused with the contracted verifier reason, a rejection that the report contradicts is refused as unjustified, and a rejection the report justifies verifies true. In `TrainingLoop` a verification failure forces `Promoted = false`, sets `Outcome = "verification-failed"`, and never touches the champion.

### Training loop and persistence

Seeds are derived from `TrainingOptions.Seed` only, and training and evaluation never share a stream: training base seed `Derive(seed, 1000 + iteration, 0, 0, 1)` (stream 1), evaluation base seed `Derive(seed, 5000 + iteration, 0, 0, 3)` (stream 3), which `MatchEvaluator` expands into the `9000+index` held-out stream (stream 0). The champion is the artifact referenced by `<data>/active-champion.json` (`{"strategyId":…,"iteration":n}`), parsed back from `<data>/strategies/<id>.json`; a missing/corrupt pointer falls back to the version-1 default genome, which is then persisted.

Per iteration: training match (champion vs `TightAggressiveAgent` baseline) → `StrategyAnalyzer` → `DeterministicStrategyProposer` → `StrategyArtifact.Create(proposal, iteration, champion)` → `MatchEvaluator` → policy → verifier → persist `strategies/<challenger>.json` (canonical JSON, UTF-8 no BOM), `evaluations/<challenger>-vs-<champion>.json` (canonical report JSON), `ledger.json` and — only on a verified promotion — `active-champion.json`, then advance the champion. The ledger entry is a fixed-order object: `iteration, championId, challengerId, promoted, reason, winRate, meanBbPer100, lowerBoundBbPer100, marginBbPer100, seeds, verified, outcome`; `winRate` is the challenger's match win rate (`challengerWins / (2 * seeds)`), the optional field the dispatch marked `winRate?`. The ledger holds exactly one entry per iteration of the run (a later run overwrites it; per-run history remains in the strategy/evaluation artifacts and the champion pointer). Same options + same seed reproduce byte-identical artifacts, reports and ledger in any directory.

### `SessionAnalysis.Vpip`

`SimulationReport` / `AgentMetrics` expose hands, win rate, net chips, bb/100, aggression factor and showdown rate, but no voluntary-participation count and no action log — VPIP is not observable from an aggregate report. `Analyze` reports `Vpip = 0` and says so in its XML documentation rather than fabricating a statistic from unrelated metrics. The proposer never uses VPIP; it branches on bb/100, aggression factor and the iteration parity.

### Proposer determinism

`DeterministicStrategyProposer` moves exactly one parameter by exactly `0.02`: a losing session tightens `CallThreshold` (even iterations) or `RaiseThreshold` (odd iterations); a winning session raises `BluffFrequency` (even) or `SizingMultiplier` (odd), with the direction flipped when the measured aggression factor is below 1. The result is clamped; if clamping would return the incumbent unchanged, one threshold flips by `0.02` in a direction that stays inside `[0,1]`, so every proposal differs from its incumbent (proved for saturated boundaries in test `StrategyProposalTests`).

### Web test without touching the committed web test file

The dispatch's scope list forbids modifying the existing web tests but test 8 asks the committed web test to also assert `GET /api/strategies` and `GET /api/ledger` over the trained directory. The conflict was resolved inside scope: `WIP/Wip.Tholdem.Tests/Learning/LearningWebIntegrationTests.cs` is a new committed web test in the same assembly that starts the real `TholdemWebHost` on an ephemeral loopback port (`ViewerTestSupport`), trains into `server.DataDirectory`, and asserts the served strategy list (ids, version, parameters, prompt text), the served ledger entries (all nine persisted fields, `verified`, `outcome`) and the champion endpoint before/after a verified promotion. `TholdemWebViewerTests.cs` and `ViewerTestSupport.cs` are byte-unchanged.

## Test-first evidence (red)

The eleven test files were written before any production type existed. The first build could not compile the test assembly (it failed on the missing namespace and the missing report type):

```text
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Learning\StrategyArtifactTests.cs(2,26): error CS0234: The type or namespace name 'Learning' does not exist in the namespace 'Wip.Tholdem.Agents' (are you missing an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Learning\TrainingLoopTests.cs(2,26): error CS0234: The type or namespace name 'Learning' does not exist in the namespace 'Wip.Tholdem.Agents' (are you missing an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Learning\LearningTestSupport.cs(51,19): error CS0246: The type or namespace name 'EvaluationReport' could not be found (are you missing a using directive or an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
... (each of the eleven new test files reported the same missing namespace)
    11 Error(s)

Time Elapsed 00:00:20.90
[exit code: 1]
```

(A first build attempt before that was blocked by `MSB3027`/`MSB3021`: a concurrent orchestrator demo viewer held `WIP/Wip.Tholdem.Web/bin/Release/net10.0/Wip.Tholdem.Agents.dll`. The orchestrator stopped it and now runs the viewer from a copy under `.tmp/viewer-run`; no source was affected.)

The production files were then written; the first implementation build was green (0 errors), and the first Learning-only run failed exactly one assertion whose own expectation was wrong (a sibling artifact was linked to a version-3 parent, so the expected version was 4). After that test was corrected, its replacement fixture exposed a second fixture bug — the "different parent" root artifact was created from the same default genome, so it was the same artifact — and was corrected to use a distinct genome. Both fixes were in the test, never in the production code; no production assertion was weakened, skipped or deleted. The first fixture failure is quoted below:

```text
FAIL StrategyArtifactTests.Create_GivenParentChain_ExpectedIncrementedVersionAndParentLink: EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   4
TOTAL 21 PASSED 20 FAILED 1 SKIPPED 0
```

## Mutation

Mutant: `PromotionVerifier.Verify` accepts every decision without re-deriving the criterion (guarded `if (decision is not null && report is not null && criteria is not null) { reason = "verified"; return true; }` inserted before the derivation).

Pre-mutation SHA-256 of `WIP/Wip.Tholdem.Agents/Learning/PromotionVerifier.cs`:
`72252cdd9bdbf838a3ddc0a5e5abb60298ca3d356ee4bb455fd521802e0029f0`

Forced rebuild (`--no-incremental`) and Learning run:

```text
    0 Error(s)

Time Elapsed 00:00:45.73
--- mutant run ---
FAIL PromotionGateTests.Verify_GivenRederivedCriterion_ExpectedControlsRejectedAndPassesAccepted: FalseException: Assert.False() Failure
FAIL TrainingLoopTests.Run_GivenAlwaysPromotePolicy_ExpectedVerificationFailureAndUnchangedChampion: FalseException: Assert.False() Failure
TOTAL 21 PASSED 19 FAILED 2 SKIPPED 0
EXIT=1
```

The always-promote control test (test 5) fails as required, through the pipeline, because the loop trusts the verifier rather than the policy. Restored from the pre-mutation backup and verified by hash:

```text
restored_sha256=72252cdd9bdbf838a3ddc0a5e5abb60298ca3d356ee4bb455fd521802e0029f0
match_pre_mutation=True
mutant_marker_removed=True
```

Forced rebuild and re-run after the restore:

```text
    0 Error(s)

Time Elapsed 00:00:48.92
--- restored run ---
TOTAL 124 PASSED 124 FAILED 0 SKIPPED 0
EXIT=0 elapsed_seconds=100.4
```

## Test count

**124 test cases**: 103 pre-existing + **21 new `[Fact]` methods**, 0 `[Theory]`, 0 skips, 0 waived failures.

| New suite | Facts | Covers dispatch test |
|---|---|---|
| `StrategyArtifactTests` | 3 | 1 (ids/versions/hashes, canonical order, no timestamps) |
| `StrategyAgentTests` | 2 | 2 (legality over 40 hands, determinism) |
| `MatchEvaluatorTests` | 2 | 3 (paired seeds, recomputed net chips, normal interval, degenerate one-seed interval) |
| `PromotionGateTests` | 4 | 4 (6.0→promoted, 4.9→lcb-below-margin, 6.0/23→insufficient-seeds, exactly 5.0→not promoted) and 5a (always-promote control rejected by the verifier) |
| `StrategyProposalTests` | 2 | data-only proposal invariant |
| `StrategyAnalyzerTests` | 1 | metrics mapping + documented VPIP |
| `TrainingLoopTests` | 4 | 5b (always-promote loop run), 6 (negative control), 7 (reproducibility), 8 (persistence/champion schema) |
| `LearningWebIntegrationTests` | 1 | 8 (web test: `/api/strategies` + `/api/ledger` over the trained directory) |
| `ViewerLedgerRenderingTests` | 1 | 9 (nine labels, field names, text-only cells) |
| `LearningSourceSafetyTests` | 1 | 10 (safety scan) |
| **Total new** | **21** | |

`docs/plan/test-waivers.json` parses and `expectedTotals["Wip.Tholdem.Tests"] == 124`.

## Verification commands (final tree, exact observed output)

### 1. Test project build

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:26.48
BUILD_EXIT=0
```

(The build was forced to recompile by touching the new sources; incremental re-runs print the same `0 Warning(s) / 0 Error(s)`. `Wip.Tholdem.Agents`, `Wip.Tholdem.Web` and `Wip.Tholdem.Tests` were all recompiled in that run. The only warnings this project produced before the final cleanup were two `xUnit1031` warnings in `StrategyAgentTests`; that test is now `async Task` and awaits, so the Tholdem build is warning-free.)

### 2. Tholdem suite (supplementary runner; `dotnet test` is forbidden here)

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 124 PASSED 124 FAILED 0 SKIPPED 0
EXIT=0 wall_seconds=114.5
```

Baseline before this round for the same runner: `TOTAL 103 PASSED 103 FAILED 0 SKIPPED 0`, `EXIT=0 elapsed_seconds=113.6` — the 21 added facts cost about one second of the wall time.

### 3. Whole solution

```text
$ dotnet build Wip.slnx --configuration Release
    126 Warning(s)
    0 Error(s)

Time Elapsed 00:01:59.10
EXIT=0 wall_seconds=122.3
```

The 126 warnings are the pre-existing solution-wide set (Bones/Runtime/shell projects); no Tholdem warning is among them. No unrelated project was failing, so no project had to be named as broken.

### 4. Architecture tests

```text
$ dotnet build tests/Wip.Architecture.Tests -c Release
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:09.94
BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
RUN_EXIT=0
```

`ArchitectureReferenceTests.ProjectGraph_GivenWipSolution_ExpectedAllWipProjects` asserts `Assert.Equal(47, graph.Count)` and passed; this round adds no project.

### 5. Solution coverage

```text
$ powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
solution=Wip.slnx
wip_project_count=47
solution_wip_project_count=47
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
EXIT=0
```

### 6. Harness sync

```text
$ dotnet run --project tools/HarnessSync -- --check
EXIT=0
```

`HarnessSync --check` prints nothing on success; the exit code is the observation. It also proves every new file is UTF-8 without a BOM (independently byte-checked: every touched file — 14 learning sources, 11 test sources, 2 viewer assets, the waiver file and this report — 0 BOM, 0 UTF-16).

## What I could not verify / could not make pass

- **Nothing in the required verification set failed.** All six commands above were observed green on the final tree.
- `dotnet test` was not run (forbidden by the dispatch); local verification is the supplementary `RunXunit` runner over the compiled assembly, as in rounds 4–5. CI is not observable from this machine, so this is a local green claim, never presented as CI green.
- The supplementary runner executes tests sequentially and ignores xUnit collections; that is unchanged from previous rounds and does not affect any test this round added.
- The negative control and the "different seed changes an id" check are asserted on fixed seeds (`baseSeed 20260101`, `314159` vs `314160`); they are deterministic, so they cannot flake, but they are chosen examples rather than exhaustive sampling. The statistical property itself is proven in exact arithmetic by `MatchEvaluatorTests` (interval recomputed from the raw match results) and by the synthetic-report gate tests.
- `SessionAnalysis.Vpip` is 0 because the aggregate report cannot observe voluntary participation (documented above); no test claims a measured VPIP above zero.
- The ledger is per-run (one entry per iteration of that run); cross-run ledger accumulation was not implemented so that the same options always reproduce the same ledger bytes. A future round can append if the audit-trail reading is preferred.

## Explicitly out of scope and untouched

`WIP/Wip.Tholdem/**`, `WIP/Wip.Tholdem.Web/*.cs`, `WIP/Wip.Tholdem.Agents/**` outside `Learning/`, `WIP/Wip.Tholdem.Tests/*.cs` (the committed web tests included), `Wip.slnx`, `tests/Wip.Architecture.Tests/**`, `tools/run-wip-tests.ps1`, `tools/verify-solution-coverage.ps1`, `.tmp/tholdem-verify/RunXunit/**` (the pre-existing out-of-tree runner), and every `docs/goal-tholdem/` file other than this report. The loop contains no merge, publish, deploy, network, process or code-generation path; strategies remain data.
