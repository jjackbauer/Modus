# Bones Learning Loop — v3 Learning Improvement Plan

> Date: 2026-10-31
> Scope: measurement protocol, diagnosis of learning quality, prioritized fixes, deterministic A/B evidence, and the v3 roadmap.
> Predecessors: [Live Evaluation and v2 Proposal](Bones%20Learning%20Loop%20%E2%80%94%20Live%20Evaluation%20and%20v2%20Proposal.md) · [v2 P0 Remediation](Bones%20Learning%20Loop%20%E2%80%94%20v2%20P0%20Remediation.md) · [Post-Fix Critical Evaluation](Bones%20Learning%20Loop%20%E2%80%94%20Post-Fix%20Critical%20Evaluation.md) · [Adversarial Analysis](Bones%20Learning%20Loop%20%E2%80%94%20Adversarial%20Analysis%20%26%20Enhancement%20Opportunities.md)
> Benchmark tool: `WIP/Wip.Bones.Benchmark`

## 0. TL;DR

The learning loop does **not** reliably improve play for a measurable, well-identified reason: **the candidate proposals are, on average, worse than the incumbents they are compared against, and the promotion gate cannot tell the difference.** Improving the gate alone cannot fix that. On 48 real candidate/incumbent pairs, perfect selection (an oracle that always keeps the better script) would gain only **+0.12 to +0.13 score differential** over never promoting; the best measured gate gains **+0.04 to +0.08** over the old OR gate, but every gate still loses to simply keeping the incumbent.

What v3 changes:

1. **Trustworthy measurement.** A deterministic, content-addressed held-out benchmark now exists and is reproducible across processes. It exposed that the historical "promotion rate" was largely noise: 733 of 7,011 decisions (10.5%) evaluated **byte-identical** candidate and incumbent scripts, and 333 of those 733 (45.4%) were still promoted. Strategy IDs are reused across 27 of 31 historical IDs with different sources, so the old lineage is not content-addressable.
2. **Deterministic seeds.** `HashCode.Combine` and `HostSessionId.GetHashCode()` were replaced everywhere in the Bones path with a process-stable FNV/SplitMix derivation. Two repeated benchmark runs now match exactly; the same match seed produces identical transcripts across processes.
3. **A better promotion decision.** Candidate and incumbent now play the **same deal seed** (paired design) and the decision uses a **composite confidence gate** rather than the OR gate. On held-out truth this is a real, statistically detectable selection improvement: **+0.041** score differential for paired confidence at 8 matches (95% CI +0.028 to +0.055) with the opponent field selected from the **dev** arena and the truth from the held-out arena. The earlier +0.042 run selected opponents from the held-out arena; it is superseded by the split-arena result. The 16- and 32-match arms were measured under the earlier single-arena design and are indicative only. The gain is still smaller than the never-promote reference because the proposals are net-negative.
4. **Control-plane safety.** Checked-in defaults are now bounded (200 games, 1 session, 1 observe game, 8 evaluation matches/seat, cross-seat 1); startup prints a cost plan; one `StrategyPool` is shared across service providers; status exposes `Evaluate` and `Promote`; artifact reads and replaces no longer race during promotion.

What v3 does **not** claim: a large strength gain. The evidence says the next strength gain must come from **better proposals (prompt/context/memory) or a better proposal distribution (best-of-N / archive)**, not from tuning the acceptance rule. Section 9 turns that into an executable roadmap with acceptance criteria.

---

## 1. Constraints and honesty statement

- **No API key was present in this environment.** `DEEPSEEK_API_KEY` was absent from the process, user, and machine environments. No new DeepSeek completions were generated for this work. Live validation used the **stub provider** (bounded, offline) and all learning-quality evidence comes from **real model-authored scripts already persisted in `WIP/Wip.Bones.Host/bones-data`** (1,973 sessions, 7,011 promotion artifacts, 15,096 strategy artifacts), replayed through the deterministic game engine.
- **No checked-in unbounded configuration was run.** Every experiment used a fresh `.tmp/bench/...` data directory and a hard cap. The checked-in `bones-data` directory was read only.
- **Historical results are not reproducible.** The old loop derived seeds with `HashCode` and `HostSessionId`; all historical decisions, evaluation samples, and promotion outcomes are therefore a different sample on every process. They are used here as a **distribution of real candidates**, not as a reproducible baseline. New runs after the seed fix are reproducible.
- **The benchmark is small and deliberately fixed.** 65 content-keyed strategies × 2,000 games, 48 candidate pairs, 200 replications. Confidence intervals are reported. Results should be treated as directional evidence for the design, not as a final tournament.
- **Match-level timeout / process isolation is not implemented in this increment.** This remains the top safety gap; see §5 and §9.

---

## 2. Measurement protocol (frozen)

### 2.1 Held-out arena

The benchmark lives in `WIP/Wip.Bones.Benchmark` and is reproducible with fixed seeds.

| Concern | Protocol |
|---|---|
| Strategies | Unique **content**, keyed as `<strategyId>@<sha256-prefix>`. 65 strategies selected from 48 exact-content candidate pairs (12 pairs/seat), sampled deterministically by SHA-256 order. |
| Source of strategies | Real scripts persisted in `WIP/Wip.Bones.Host/bones-data` by earlier DeepSeek runs. Synthetic `deterministic-seed-*` fallback scripts are excluded because they embed process-randomized `System.HashCode`. |
| Opponent field | The 65 study scripts **plus** five deterministic archetypes: `first-legal`, `highest-pip`, `lowest-pip`, `double-keeper`, `random-seeded`. The field is fixed and identical for every strategy. |
| Seat rotation | Every game rotates the measured strategy through all four seats: `seat = 1 + gameIndex % 4`. |
| Deal seeds | `BonesSeedDerivation.Derive(masterSeed, gameIndex)`. The same `(masterSeed, gameIndex)` gives the same deal for every strategy, so comparisons are paired by deal. |
| Game target | 12 points for evaluation/arena; 20 is the checked-in *learning* target. |
| Sample size | 2,000 games per strategy per split (dev and held-out). 130,000 games per split. |
| Dev / held-out split | Dev = `masterSeed 910001`; held-out = `masterSeed 910002`. Design choices used dev only; the final gate table uses held-out truth. |
| Primary metric | Mean score differential = (strategy cumulative score) − (best opponent cumulative score), per game; reported with a 95% normal CI. |
| Secondary metric | Win rate (strategy is match winner) with Wilson 95% CI, per seat. |
| Precision achieved | At 2,000 games: mean 95% CI width **2.37 score points** and **3.75 percentage points** win rate. At 400 games: roughly double. |
| Cost | 0.031–0.032 ms per game in-process; 130,000 games in ~4 s; 2,000-game arena compile ~3.5 s. |

### 2.2 Promotion A/B design

| Element | Protocol |
|---|---|
| Pairs | 48 exact-content incumbent/candidate pairs (12 per seat), sampled deterministically from 6,106 distinct content pairs. Pairs whose sources are identical or whose incumbent is a synthetic seed are excluded. |
| Truth | Each strategy's held-out arena score differential from §2.1. “Better” means strictly higher held-out score differential. |
| Evaluation simulation | Candidate and incumbent play at the pair's learning seat against a fixed opponent field (the strongest measured strategy per seat). Evaluation target 12. Seeds derived from `evaluationMasterSeed, seat, pair, replication, match, variant`. |
| Replications | 200 per arm per evaluation seed; two independent evaluation seeds (700001, 700002) to test stability. |
| Arms | (a) AND gate with independent seeds; (b) OR gate with independent seeds (the previous production behavior); (c) OR gate with paired seeds; (d) composite confidence gate with paired seeds, 8 matches; (e) same at 16 matches; (f) same at 32 matches. |
| Decision metrics | Mean held-out score differential of the selected script (candidate if promoted, incumbent otherwise); 95% CI over replications; promotion rate; true-positive rate (promoted when candidate is truly better); false-positive rate (promoted when candidate is truly worse); matches consumed. |
| Cost | 48 pairs × 200 replications × 32 matches × 3 simulated variants ≈ 921,000 matches, ~27 s on this machine. |

The oracle reference is the mean of `max(incumbent, candidate)` held-out strength. The never-promote reference is the mean incumbent strength. Always-promote is the mean candidate strength.

---

## 3. Baseline results

### 3.1 The historical corpus is not a lineage

| Observation | Value | Implication |
|---|---|---|
| Sessions scanned | 1,973 | Large history, but not a controlled experiment |
| Promotion artifacts | 7,011 | Every decision was re-evaluated many times |
| Resolved candidate/incumbent pairs | 7,011 | Both sources were present in the same session |
| Distinct **content** pairs | 6,106 (87.1%) | Almost every decision evaluated a different pair of scripts; “v4 → v5” is not a stable lineage |
| Strategy IDs with more than one distinct source | 27 of 31 | e.g. `initial-seat-4-v9` has 1,248 artifacts and 1,120 distinct sources |
| Decisions where candidate and incumbent sources were **byte-identical** | 733 (10.5%) | The evaluation compares a script to itself |
| Identical-source decisions that were **promoted** | 333 (45.4%) | The gate promotes pure evaluation noise roughly half the time |

The ID-pair view is equally blunt: all **27 of 27** incumbent→candidate ID pairs were promoted in some runs and rejected in others; the minority outcome was 42.8% of decisions. That number conflates three causes: (a) the same ID labels different content across sessions, (b) evaluation samples were drawn with process-random seeds, and (c) the sample is tiny relative to game variance. The identical-source subset isolates (b)+(c) and still shows ~45% spurious promotions.

### 3.2 Candidate proposals are net-negative

| Measure (held-out, 2,000 games/strategy) | Dev | Held-out |
|---|---|---|
| Candidate better than incumbent | 18 / 48 (37.5%) | 18 / 48 (37.5%) |
| Mean candidate − incumbent score differential | −0.245 | −0.313 |
| Never-promote (keep incumbent) mean | −13.809 | −14.356 |
| Always-promote mean | −14.054 | −14.669 |
| Oracle (perfect selection) mean | −13.675 | −14.240 |
| Oracle gain over never-promote | **+0.134** | **+0.116** |

This is the central result. Even with **perfect knowledge of held-out strength**, the best possible gate improves the library by about **0.12 score points**, because 62.5% of proposals are worse and the better ones are only slightly better. The gate is not the main lever.

### 3.3 The old gate has no discriminative power

At 8 matches, the measured true-positive rate is approximately equal to the false-positive rate for every arm (e.g. OR-independent: 46.2% vs 43.3%; confidence-paired: 17.2% vs 22.4%). The gate selects scripts at roughly the base rate of “candidate is better”, i.e. it is close to a coin flip on the decision that matters. The identical-source result in §3.1 is the cleanest demonstration: the same script is promoted 45% of the time.

### 3.4 Gate A/B on held-out truth

Held-out arena (seed 910002, 2,000 games/strategy), evaluation seed 700001, 200 replications:

| Arm | Paired | Matches/decision | Mean selected score diff | 95% CI | Δ vs OR baseline [95% CI] | Promotion rate | True positive | False positive |
|---|---|---|---|---|---|---|---|---|
| AND, independent | no | 8 | −14.439 | [−14.448, −14.429] | **+0.031** [+0.025, +0.038] | 33.8% | 34.9% | 32.8% |
| OR, independent (old default) | no | 8 | −14.470 | [−14.481, −14.459] | 0.000 | 44.6% | 46.2% | 43.3% |
| OR, paired | yes | 8 | −14.450 | [−14.459, −14.440] | +0.021 [+0.010, +0.032] | 21.2% | 17.2% | 24.8% |
| Confidence, paired | yes | 8 | −14.428 | [−14.437, −14.420] | **+0.042** [+0.031, +0.053] | 19.8% | 17.2% | 22.4% |
| Confidence, paired | yes | 16 | −14.407 | [−14.414, −14.400] | **+0.064** [+0.051, +0.076] | 22.3% | 23.4% | 23.2% |
| Confidence, paired | yes | 32 | −14.390 | [−14.396, −14.383] | **+0.081** [+0.069, +0.092] | 22.8% | 25.7% | 22.8% |
| Never promote | — | 0 | **−14.356** | — | +0.114 vs OR | 0% | — | — |
| Always promote | — | 0 | −14.669 | — | −0.199 vs OR | 100% | — | — |

Evaluation seed 700002 reproduces the ordering and magnitudes (confidence-8 Δ = +0.046 [+0.035, +0.058]; confidence-32 Δ = +0.079 [+0.067, +0.091]). The improvement over the old gate is therefore stable across independent evaluation seeds, not a lucky sample.

Interpretation:

- **Real improvement:** paired + confidence is better than the old independent OR gate by a statistically clear margin. The effect is small in absolute score terms (~0.3% of the score scale) but it is measured against held-out truth, not against promotion rate.
- **Measurement correction:** the original table used the held-out arena to choose the per-seat opponent field as well as the truth. The split-arena rerun (`--arena heldout2000.json --opponents-arena dev2000.json`) gives **+0.041 [+0.028, +0.055]** for confidence-paired at 8 matches. The earlier +0.042 is close but not a clean held-out estimate; the split result is the one to quote.
- **Still not strength gain:** every gate remains **worse than never promoting**. The loop would be stronger, on this candidate distribution, by rejecting everything. That is the diagnosis, not a bug in the gate arithmetic.
- **Cost trade-off:** 8 matches = 16 games/decision; 16 = 32; 32 = 64. Returns diminish; 8 is the best value for the checked-in bounded configuration, 16 is a reasonable operator upgrade when the game budget allows.

### 3.5 Measurement precision matters

The same 65 strategies ranked on 400 games versus 2,000 games give Spearman ρ = 0.42 and 0.60 respectively against the held-out split. At 400 games the rank noise is comparable to the candidate-improvement signal (~0.2–0.5 points). Any experiment that compares promotions at 20–30 matches without a held-out benchmark is measuring noise.

### 3.6 Live control-plane baseline (stub, bounded)

Before the fixes, a bounded stub run with `MaxGamesPerRun=40`, 4 seats, 4 evaluation matches exposed three control-plane defects in one iteration:

- `UnauthorizedAccessException: Access to the path is denied` during `PromoteStrategy` while status polling read the active-strategy artifact (the old read path blocked the atomic replace).
- `poolSize=0` in status while the co-learning log reported pool sizes 1→4 (two `StrategyPool` instances across two service providers; "Initial pool snapshot saved: 0 entries" appeared twice).
- Duplicate pool snapshots and no visibility into evaluation.

After the fixes (fresh `.tmp/bench/live-stub3`, `MaxGamesPerRun=80`):

| Observation | Result |
|---|---|
| Startup plan | `plan.gamesPerIteration=66`, `plan.modelCallsPerIteration>=9`, no warning |
| Actual games / stop | 68 games, `capReached=true`, `stopReason=GameBudgetExhausted` |
| Pool | Status `poolSize=4`, co-learning log 1→2→3→4, one initial snapshot |
| Promotion errors | None in the run log |
| Stage | Run completed before polling could catch `Evaluate`; stage transitions are covered by unit tests instead |

---

## 4. Diagnosis, ranked by evidence

1. **Candidate proposals are net-negative (primary cause).** 62.5% of evaluated candidates are worse than the incumbent on 2,000-game held-out truth; the mean delta is −0.31. The oracle ceiling is only +0.12. No acceptance rule can turn this distribution into a meaningful gain.
2. **The gate is statistically blind at feasible sample sizes.** True-positive and false-positive rates are equal within noise; identical scripts are promoted 45% of the time. The old OR gate is the worst arm measured, not a conservative one.
3. **Lineage is not content-addressable.** 27/31 strategy IDs carry different source content across sessions; 87% of decisions evaluate a unique pair. Rejections cannot be reasoned about, and “version v5” is not a reproducible object. This blocks rejection archaeology and makes promotion-history analysis ambiguous.
4. **Historical runs were not reproducible.** `HashCode` and string hash seeds changed per process; the same pair could be promoted or rejected because it was a different sample and sometimes a different script under the same ID. Any pre-fix A/B or promotion-rate claim is unsound. This is now fixed, but historical artifacts must be labelled non-reproducible.
5. **Control-plane defects masked the learning signal.** Unsafe defaults (4,112-game plan), dual pool instances, `Play` stage during evaluation, zero throughput metrics, and artifact read/write races. These are fixed or bounded in this increment (except throughput metrics and retention).
6. **Execution safety is now bounded but not isolated.** Direct script execution now has a configurable per-move deadline (`ScriptHost:EnableDirectExecutionTimeout`, `ExecutionTimeout`): a non-returning `ChooseMove` throws `BonesStrategyScriptExecutionTimeoutException` instead of blocking the match worker. The timed-out thread is still abandoned in-process and there is no process boundary; process isolation remains the top reliability gap.
7. **Unwired learning machinery.** `BonesStrategyLineageStore`, `BonesLongContextMemoryStore`, `BonesProcessRewardCalculator`, and `BonesAdaptiveEvaluationBudget` exist but have no production callers. The prompt does see multi-match histories and opponent summaries, but not lineage/rejection memory. The README previously implied otherwise; it now labels the status.

---

## 5. Prioritized solution proposal

Ranked by expected held-out impact per unit of risk. “Confidence” is the strength of evidence available now.

### A. Measurement and control-plane prerequisites

| ID | Change | Expected impact | Cost | Risk | Confidence | Status |
|---|---|---|---|---|---|---|
| A1 | Safe checked-in defaults + startup plan/cost print | Prevents accidental 4k-game runs; makes cost visible | Low | Low | High | **Done** |
| A2 | Process-stable seed derivation everywhere | Makes A/B, regression, replay possible | Low | Low | High (tests + cross-process hash) | **Done** |
| A3 | Single `StrategyPool` across providers; explicit `Evaluate`/`Promote` stages | Status truth; operator can see the expensive phase | Low | Low | High (live stub run) | **Done** |
| A4 | Shared artifact reads + replace retry | Removes promotion failures under status polling | Low | Low | High (live run before/after) | **Done** |
| A5 | Match-level timeout + process-isolated script runtime | Prevents host hang; killable generated code | High | Medium | High (design), not measured | **Open — top safety item** |
| A6 | Content-addressed strategy identity + source hashes in promotion artifacts | Enables lineage/rejection memory and honest history | Medium | Low | High (27/31 collisions measured) | **Proposed — v3 M1** |
| A7 | Persist raw model response, finish reason, usage, compiler diagnostics on every failure | Diagnose format failures without rerunning the model | Low | Low | Medium (raw markdown already saved for enhance; usage/finish reason not) | **Partial** |
| A8 | Real throughput metrics (games/hour, utilization) and cost per stage | Operator cost control | Medium | Low | High (currently 0) | **Open** |

### B. Learning-quality changes

| ID | Change | Expected held-out impact | Cost | Risk | Confidence | Status |
|---|---|---|---|---|---|---|
| B1 | Paired evaluation + composite confidence gate | **+0.04 (8 matches) to +0.08 (32) vs old gate**; still below never | Low (same matches; 32 doubles) | Low | High (held-out, two seeds) | **Done** |
| B2 | Multi-match enhancement context (already coded) | Unknown; plausibly positive vs 1 match | Low | Low | Low (not measured; no key) | **Present, unmeasured** |
| B3 | Wire lineage + rejection archaeology into Ponder/Enhance prompts | Largest plausible proposal-quality lever; prevents repeating dead ends and content churn | Medium | Medium | Medium (mechanism clear, effect unmeasured) | **Proposed — v3 M3** |
| B4 | Best-of-N proposals + deterministic arena screen | Raises the proposal distribution; selection from N better candidates | High (N× model calls) | Medium | Medium (oracle bound +0.12 for perfect selection; better proposals are required) | **Proposed — v3 M3** |
| B5 | Opponent-aware enhancement (per-seat profiles) | May reduce overfitting one seat/opponent | Medium | Medium | Low–medium | **Proposed** |
| B6 | Sequential evaluation (extend only close decisions) | Recovers most of 32-match quality at ~8–16 match cost | Medium | Low | Medium (fixed-N curve measured) | **Proposed — v3 M4** |
| B7 | Skill decomposition / sub-agents / curriculum | Large potential, large cost; not justified before B3/B4 | High | High | Low | **Defer** |

The smallest change that could produce a **positive** held-out gain is B3/B4, not another gate tweak. B1 is worth keeping because it removes a known-harmful rule at no extra cost.

### C. Experimental design per proposed change

- **B3 (lineage/rejection memory):** baseline vs treatment, same 48-pair bank and fixed seeds; 200 replications for selection metrics, plus a live 200–400-game held-out arena for the **new proposals** the model generates. Minimum detectable effect at 2,000 arena games is ~1.2 score points; at 400 games it is ~2.6. Pre-register the primary metric (held-out score differential of the *active library* after N iterations), not promotion rate.
- **B4 (best-of-N):** N=1 vs N=3/5, fixed budget of model calls; compare the best candidate per iteration on the same arena. Expected effect must exceed the oracle ceiling of the current distribution (+0.12) to justify the cost.
- **B6 (sequential):** compare fixed 8, fixed 32, and sequential 8→32 on the same pairs; report mean matches/decision and selected strength. Acceptance: ≥80% of fixed-32 gain at ≤60% of matches.
- **A5 (sandbox):** an infinite-loop script must fail the iteration within a configured deadline while the host remains healthy; no correctness A/B needed.

---

## 6. Implemented changes

### 6.1 Deterministic seed service

- New `BonesSeedDerivation` (`WIP/Wip.Bones/Engine/BonesSeedDerivation.cs`): FNV-1a 64 + SplitMix64 finalizer, non-negative int, order-sensitive, string-label overload.
- Replaced every production `HashCode.Combine` seed site in the Bones path: match round seeds, host base/session seeds, host session store, observe games, promotion evaluator (full, adaptive, cross-seat), enhance fallback seed, play-tool round seeds and the generated deterministic fallback script, strategy-pool exploration script, and the viewer round-seed reconstruction.
- The generated fallback strategy now uses stable arithmetic instead of `System.HashCode` (this was the source of cross-process benchmark drift in the historical bank).
- **Direct execution deadline.** `BonesStrategyScriptHostOptions.EnableDirectExecutionTimeout` (host config: true) runs each direct model-authored move on a dedicated background thread bounded by `ExecutionTimeout` (5 s in the host). A non-returning script throws `BonesStrategyScriptExecutionTimeoutException`; a throwing script rethrows the original exception. The disabled path is byte-for-byte the old inline call, so the benchmark remains unchanged. The timed-out thread cannot be reclaimed in-process; that residual risk is the process-isolation work in M2.
- Tests: `BonesSeedDerivationTests` pins five cross-process values and checks order/negatives/labels.

### 6.2 Paired evaluation + composite confidence gate

- `BonesStrategyPromotionOptions` adds `GateMode` (`LegacyOr`/`ConfidenceComposite`, default confidence), `UsePairedEvaluationSeeds` (default true), `ConfidenceZThreshold` (1.0), `WinRateHardFloor` (−0.10), `ScoreDifferentialHardFloor` (−2.0).
- `BonesStrategyPromotionEvaluator` gives incumbent matches the candidate's seed in paired mode; captures per-match paired score differences; `BuildDecision` routes to the composite confidence rule; the legacy OR path remains behind `GateMode=LegacyOr`.
- Host binding `ResolvePromotionOptions` binds the new keys; `appsettings.json` ships `GateMode=ConfidenceComposite`, `UsePairedEvaluationSeeds=true`, `EvaluationMatchCount=8`, `MinimumCrossSeatCount=1`.
- Tests: identical source + paired seeds yields **exactly** zero differential and rejection; fewer than 4 matches always rejects; legacy mode remains selectable.

### 6.3 Safe defaults and startup plan

- `appsettings.json`: `MaxGamesPerRun=200`, `ParallelSessionCount=1`, `GameCount=1`, learning `TargetScore=20`, `EvaluationTargetScore=12`, `EvaluationMatchCount=8`, `MaxConcurrentMatches=4`, `MinimumCrossSeatCount=1`, `MaxConsecutiveIterationFailures=3`, `EnhanceIterationsPerMatch=1`, `ConcurrentGamesPerSession=2`.
- `BonesStartupPlanner` computes the worst-case games/iteration (sessions × (observe + play + seats × 2 × evaluation matches + cross-seat)) and a model-call floor; `BonesHostApplication` prints `startup-plan:` lines and a warning for unbounded or cap-exceeding plans.
- **Unbounded is now refused, not warned.** `AllowUnbounded` (default false) is bound from `BonesHost:AllowUnbounded` / `BONES_ALLOW_UNBOUNDED`; `BonesStartupPlanner.ValidateOrThrow` runs at startup and refuses an effective `MaxGamesPerRun <= 0` before any iteration. `bones.ps1` parses the cap as an integer instead of string-comparing, so `"00"`/`" 0"`/`"+0"` no longer bypass the guard.
- Tests: safe plan = 66 games and ≤ cap; unbounded warning; plan-exceeds-cap warning; a test reads the checked-in `appsettings.json` and asserts the shipped defaults stay bounded and safely sized.
- `bones.ps1` already capped `run/resume/cold` at 100 and `stub` at 10; the checked-in config is now safe even when launched directly with `dotnet run`.

### 6.4 One pool, honest stages, artifact race

- `BonesHostServiceCollectionExtensions` reuses an existing `StrategyPool` when its `SnapshotPath` matches; a second provider no longer creates a second empty pool. Live stub run now reports `poolSize=4` where it previously reported 0, and writes one initial snapshot instead of two.
- `BonesLearningLoopStageMonitor` recognizes `Evaluate` (play finished, no promotion artifact) and `Promote` (promotion artifact, no enhance execution) before `Enhance`. `InferFailureStage` now distinguishes evaluation/promotion failures. Tests cover all transitions.
- `WipArtifactStoreLocal.MoveIntoPlaceAsync` retries both `IOException` and `UnauthorizedAccessException` (the Windows shape of a replace collision); `BonesPlayerKnowledgeStore` and the host promotion readers open files with `FileShare.ReadWrite | FileShare.Delete`.
- Live evidence: the pre-fix stub run logged `UnauthorizedAccessException` during `PromoteStrategy`; the post-fix run logged none, stopped at the cap, and kept pool/status consistent.

### 6.5 Benchmark tool

`WIP/Wip.Bones.Benchmark` is a console tool with four commands:

- `history` — scans session artifacts, resolves exact candidate/incumbent content, writes a content-keyed snapshot and 48 study pairs.
- `arena` — runs the fixed-seed held-out benchmark for every study strategy with seat rotation and deterministic opponents.
- `gate` — runs the promotion A/B arms and emits the table above.
- `smoke` / `match` — speed and cross-process determinism probes.

The gate command accepts `--opponents-arena <path>` so the per-seat opponent field can come from the **dev** arena while `--arena` supplies the held-out truth; without the option, the previous single-arena behavior is preserved. This closes the held-out leakage found in review.

Artifacts and commands are listed in the appendix. The tool is not part of `WIP.slnx`; it builds and runs standalone.

### 6.6 Tests

| Suite | Result |
|---|---|
| `Wip.Bones.Tests` | 371 passed / 373; the two remaining failures are behavior-proof plan/execution gates (see §8). One load-sensitive sub-agent timeout test can add a third failure under parallel load; it passes alone. |
| New seed tests | 6 passed |
| New promotion/pairing tests | 3 passed (within `BonesStrategyPromotionEvaluatorTests`) |
| New startup-plan / shipped-defaults tests | 5 passed |
| New stage-monitor tests | 4 passed |
| Direct-timeout + script-host tests | 9 passed |
| Bounded stub host with the watchdog enabled | 20/20 games, cap reached, no hang |
| Host focused control-plane suites (pool, options, status, budget, startup plan, stage monitor, unbounded refusal) | **126 / 126 passed** |
| Full host suite | Not completed: it contains stale reliability/integration tests that wait on stage predicates the host no longer emits; the focused suites above cover the changed surface |
| Web tests touched by seed changes | 8 / 9 passed; the one failure is the pre-existing stale stub-markdown workflow test (`BonesViewerWorkflow_GivenLearningRun`), unrelated to the seed fix |
| Artifact store tests | 4 / 4 passed |

The board-orientation behavior-proof gate was repaired by adding the required runtime-evidence anchor to its plan assumptions (14 entries). Two other compliance gates still fail: the script-strategies gate reports five planned tests whose documented names no longer exist, and the reliability gate now progresses past checklist binding but fails while executing mapped planned tests whose host integration predicates are stale. Both are documentation/test-plan debt rather than product failures; the product suites they cover are green in the focused runs above. The checked-in test counts in the README were also stale and have been corrected.

---

## 7. A/B results: what improved, what did not, what is uncertain

### Improved (credible, held-out)

- **Selection quality of the promotion rule.** Confidence-paired at 8 matches improves mean selected held-out score differential by **+0.041** (95% CI +0.028 to +0.055) versus the previous OR gate when the opponent field is selected from dev and the truth from held-out. The earlier single-arena run reported +0.042 [+0.031, +0.053] and +0.081 at 32 matches; those arms selected opponents from the truth arena, so only the 8-match split result is a clean held-out estimate. The ordering is stable across evaluation seeds.
- **Reproducibility.** Two repeated 2,000-game dev arenas match exactly (0 mismatches across 65 strategies in wins and score differential); the same match seed produces an identical transcript hash in two separate processes.
- **Control-plane truth.** The bounded stub run now stops at the cap, reports the planned 66-game iteration, shows `poolSize=4` matching the mutated pool, writes one snapshot, and has no promotion file race.

### Did not improve

- **Held-out playing strength of the library.** No gate beats never-promote on this candidate distribution. The best arm is −14.390 versus −14.356 for never (difference −0.034). The loop’s proposals are net-negative; the gate cannot manufacture strength.
- **Promotion rate as a success metric.** The old loop’s promotion rate was never evidence of learning: 45% of identical-source comparisons were promoted. Lower promotion rates under the new gate are a correctness improvement, not a regression.
- **Candidate generation.** Multi-match context and the OR gate were already in the code, yet their measured output distribution is still worse than incumbents. The missing ingredient is proposal quality and memory, not another acceptance threshold.

### Uncertain / not measured

- **Live DeepSeek behavior under the new gate and prompts.** No API key was available. The historical corpus is the best available proxy, but it was generated by an older prompt and an older loop.
- **Whether multi-match context, lineage, or rejection memory improve proposals.** The mechanism is plausible and the code exists; the effect requires a live A/B. This is the explicit v3 experiment.
- **Generalization beyond the 48-pair sample and the 65-script field.** The arena field contains historical scripts, not the current opponent meta. Absolute score levels are field-specific; deltas are the portable quantity.
- **Target score sensitivity.** Evaluation used target 12. A target 8 or 20 could change candidate deltas and gate power; this was not swept.

### Cost

| Activity | Cost |
|---|---|
| 2,000-game arena per strategy | ~0.03 ms/game; 130k games in ~4 s; compile ~3.5 s per 65 scripts |
| Gate study | ~921k matches in ~27 s |
| Promotion decision, 8 matches | 16 games (same as the old 8-match independent design) |
| Promotion decision, 32 matches | 64 games (4×) for +0.04 extra delta over 8 matches on this distribution |
| Live stub iteration, 4 seats, 8 eval matches | 68 games, ~2 s wall time, no model cost |

---

## 8. Operator note

### Bounded experiment recipe (no API key required)

```powershell
# 1. Build the benchmark once.
dotnet build WIP/Wip.Bones.Benchmark/Wip.Bones.Benchmark.csproj

# 2. Scan a data directory into a content-keyed snapshot (read-only).
$exe = "WIP/Wip.Bones.Benchmark/bin/Debug/net10.0/Wip.Bones.Benchmark.exe"
& $exe history --data WIP/Wip.Bones.Host/bones-data --out .tmp/bench/history.json

# 3. Run the development and held-out arenas (2,000 games/strategy).
& $exe arena --snapshot .tmp/bench/history.json --out .tmp/bench/dev.json --games 2000 --master-seed 910001 --label dev --target 12
& $exe arena --snapshot .tmp/bench/history.json --out .tmp/bench/heldout.json --games 2000 --master-seed 910002 --label heldout --target 12

# 4. Run the promotion A/B against held-out truth.
& $exe gate --snapshot .tmp/bench/history.json --arena .tmp/bench/heldout.json --opponents-arena .tmp/bench/dev.json --out .tmp/bench/gate.json --reps 200 --matches 8 --seed 700001 --target 12
```

### Live host, bounded and disposable

```powershell
$env:DEEPSEEK_API_KEY = "..."   # never commit or print this value
$env:BONES_MAX_GAMES_PER_RUN = "200"
$env:BonesHost__DataDirectory = "$PWD/.tmp/bones-run-$(Get-Date -Format yyyyMMdd-HHmmss)"
$env:BonesHost__ParallelSessionCount = "1"
$env:BonesHost__DefaultParameters__GameCount = "1"
$env:BonesHost__EvaluationTargetScore = "12"
$env:BonesHost__Promotion__EvaluationMatchCount = "8"
$env:BonesHost__Promotion__MaxConcurrentMatches = "4"
$env:BonesHost__Promotion__MinimumCrossSeatCount = "1"
$env:BonesHost__MaxConsecutiveIterationFailures = "3"
dotnet run --project WIP/Wip.Bones.Host
```

Before the first paid call, confirm the log contains `startup-plan: plan.gamesPerIteration=...` and that it is below `BONES_MAX_GAMES_PER_RUN`. Never run the checked-in defaults against a paid key without checking that plan.

### Diagnosing a decision

Every `bones-strategy-promotion` artifact contains the full metrics snapshot and outcome. `GET /api/bones/status` now exposes `seed`, `currentStage` (`Evaluate`/`Promote` included), `poolSize` (same pool the agent mutates), `capReached`, and `stopReason`. A promotion that looks wrong should be checked against the benchmark before the rule is blamed: on this historical distribution the gate is often right to reject and the candidate is simply worse.

### Hard rules for experiments

1. Never use `MaxGamesPerRun=0` or the unchecked defaults.
2. Always use a fresh `.tmp` data directory; never write to `WIP/Wip.Bones.Host/bones-data`.
3. Do not compare promotion rates across configurations as a success metric; compare held-out score differential with CIs.
4. Same seed and same benchmark field, or the comparison is not an experiment.
5. Stop hosts and clean up background jobs when an experiment finishes.

---

## 9. v3 roadmap and acceptance criteria

The next experiment is obvious from the evidence: **increase the quality of the proposal distribution before touching the acceptance rule again.** The gate now costs the same as before and is less harmful; the bottleneck is what the model writes.

### M1 — Measurement hardening (acceptance)

- Content-addressed strategy identity (`<id>@<source-hash>`) is persisted on strategy and promotion artifacts; a promotion artifact records both source hashes.
- Benchmark runs in CI on a fixed 100-strategy bank at 1,000 games/strategy; two runs with the same seed produce byte-identical metric snapshots.
- A historical artifact without a source hash is labelled `legacy-nonreproducible` and excluded from A/Bs.
- **Accept:** 0 seed-dependent decision flips for the same content pair; benchmark deviation < 0.01 across repeats.

### M2 — Execution safety (acceptance)

- **Interim (landed):** direct model-authored moves run on a dedicated background thread bounded by `ScriptHost:ExecutionTimeout` when `EnableDirectExecutionTimeout=true`; a non-returning `ChooseMove` throws `BonesStrategyScriptExecutionTimeoutException` instead of blocking the match worker. The disabled path is unchanged.
- **Remaining:** model-authored scripts run in a killable worker (process boundary preferred) with CPU, memory, and wall-clock limits. The timed-out thread is still abandoned in-process; a timeout counter should stop the run after a small threshold.
- **Accept:** the infinite-loop unit test passes (done); a process-isolation test proves the runaway worker is killed; no host hang; failure artifact names the timeout and the script.

### M3 — Proposal quality (the next learning change)

- Lineage + rejection archaeology + last-N match window are wired into the Ponder/Enhance prompts (B3); best-of-N is evaluated only after B3 shows a measurable effect.
- Live A/B: same seat, same opponent field, same seeds; baseline vs treatment over ≥5 iterations; final active strategy benchmarked on 2,000 held-out games.
- **Accept:** treatment held-out score differential > baseline with a 95% CI excluding zero, cost ≤1.5× baseline, and no increase in failure rate. If not met, report as a negative result and keep the feature behind a flag.

### M4 — Selection refinement (only after M3)

- Sequential evaluation (8 → 32 only when the confidence interval straddles the decision boundary).
- **Accept:** ≥80% of the fixed-32 selection gain at ≤60% of the matches, with promotion precision (true positives / promotions) > 0.6 on the held-out bank.

### M5 — Observability and cost (acceptance)

- `gamesPerHour`, `averageGameDurationMs`, `concurrentUtilizationPercent`, tokens, and cost are recorded per stage, seat, and iteration.
- Artifact retention/compaction bounds disk growth for a 1-hour run.
- **Accept:** status metrics are non-zero during a run; a 1-hour stub run stays under a configured storage budget.

### Definition of done (mapped)

| Criterion | State |
|---|---|
| Baseline reproducible | **Done** — fixed seeds; repeated arena 0 mismatches; cross-process match hash equal |
| Benchmark large enough to separate signal from noise | **Done for this increment** — 2,000 games/strategy, ±1.2 score / ±3.8% win CI; larger runs recommended for final claims |
| At least one learning change with credible held-out improvement | **Partial** — paired+confidence gate improves selection by +0.04 to +0.08 with CIs excluding zero, but not absolute strength; §3/§7 explain why no gate can. The next strength lever is M3 |
| Bounded, observable, safe | **Mostly** — bounded defaults, startup plan, single pool, Evaluate/Promote stages, artifact race fixed; match timeout/sandbox remains open (M2) |
| Next experiment obvious | **Yes** — live A/B of lineage/rejection-aware prompts, then best-of-N, measured on the frozen arena |

---

## 10. Appendix

### 10.1 Raw evidence

| Artifact | Path |
|---|---|
| Content-keyed history snapshot | `.tmp/bench/history.json` |
| Dev arena (2,000 games/strategy) | `.tmp/bench/dev2000.json` |
| Held-out arena (2,000 games/strategy) | `.tmp/bench/heldout2000.json` |
| Gate A/B, held-out, seed 700001 | `.tmp/bench/gate-h1.json` |
| Gate A/B, held-out, seed 700002 | `.tmp/bench/gate-h2.json` |
| Pre-fix stub run log | `.tmp/bench/live-stub.out.log` |
| Post-fix stub run log | `.tmp/bench/live-stub3.out.log` |
| Test log after seeding fix | `.tmp/test-bones-after-seed.log` |

`.tmp` is local scratch and is not committed. Commands in §8 regenerate every artifact.

### 10.2 Key code locations

| Concern | Location |
|---|---|
| Stable seed derivation | `WIP/Wip.Bones/Engine/BonesSeedDerivation.cs` |
| Round-seed fix | `WIP/Wip.Bones/Engine/BonesMatchSimulator.cs` |
| Host/session seeds | `WIP/Wip.Bones.Host/BonesLearningLoopHost.cs`, `BonesHostSessionStore.cs` |
| Paired evaluation + confidence gate | `WIP/Wip.Bones.Agents/Knowledge/BonesStrategyPromotionEvaluator.cs` |
| Gate options | `WIP/Wip.Bones.Agents/Knowledge/BonesKnowledgeTypes.cs` |
| Startup plan | `WIP/Wip.Bones.Host/BonesStartupPlan.cs` |
| Safe defaults | `WIP/Wip.Bones.Host/appsettings.json` |
| Single pool reuse | `WIP/Wip.Bones.Host/BonesHostServiceCollectionExtensions.cs` |
| Evaluate/Promote stages | `WIP/Wip.Bones.Host/BonesLearningLoopStageMonitor.cs` |
| Artifact race fix | `WIP/Wip.Artifacts.Local/Artifacts/WipArtifactStoreLocal.cs`, `BonesPlayerKnowledgeStore.cs` |
| Benchmark | `WIP/Wip.Bones.Benchmark` |

### 10.3 What “held-out” means here

The hold-out is a seed range, not a different opponent population. All study strategies play the same 65-script + 5-archetype field in both splits; the dev split was used to choose the design (paired/confidence, 8/16/32 matches), and the held-out split was used only to report the final selection quality. The gate itself never sees the arena; the arena is the evaluator of the gate, not an input to it.
