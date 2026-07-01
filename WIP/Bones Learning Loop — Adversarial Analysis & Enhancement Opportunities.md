
# Bones Learning Loop — Adversarial Analysis & Enhancement Opportunities

## 1. Executive Summary

The Bones co-learning loop is a functioning but severely under-optimized 4-player Dominoes strategy learning system. After 2,814 game simulations across 5 iterations, only 1 strategy has been promoted out of 3 evaluations — a 33% promotion rate that undersells the actual quality improvement the system produces. The root cause is a promotion gate that imposes a rigid AND of win-rate _and_ score-differential criteria, rejecting candidates that demonstrate enormous score improvements (e.g., +4.15 points per game) simply because their win rate didn't cross an arbitrary 5% threshold in only 20 evaluation matches.

Beyond the promotion gate, the system suffers from a fundamentally impoverished enhancement signal. Each enhancement cycle provides the LLM with exactly **one** match history transcript (`matchHistoriesLoaded=1`), forcing strategy refinement through the lens of a single game. The LLM cannot detect patterns, cannot distinguish signal from noise, and has no memory of what it tried before — it is effectively re-authoring from scratch every iteration, blind to prior successes and failures.

The highest-value gaps are cascading: (1) the promotion gate throws away good strategies, (2) the single-match enhancement context starves the LLM of learning signal, (3) there is no cross-iteration memory, and (4) Markdown strategies that can't compile force unnecessary re-Ponder cycles. Fixing these four issues in sequence would unlock substantial gains before any ambitious continual-harness-level capabilities are attempted.

---

## 2. Session Analysis

### 2.1 Overall Run Metrics

| Metric | Value | Assessment |
|--------|-------|------------|
| Games simulated | 2,814 | Substantial for 5 iterations |
| Strategies compiled | 99 | ~20 per iteration — aggressive exploration |
| Promoted | 1 | Catastrophically low |
| Rejected | 2 | Both likely good strategies |
| coLearningIterationsCompleted | 20 | Across 4 seats × 5 iterations |
| Library entries | 4 (1 per seat) | Seat 1: Script, Seats 2-4: Markdown |

The library composition reveals a structural problem: seats 2-4 have never produced a compilable C# script that survived promotion, so they remain stuck at Markdown (the bootstrapped observation-based strategy description). Every iteration, these seats must re-Ponder from scratch because Markdown entries are classified as non-viable by `IsLibraryEntryViable()`.

### 2.2 Deep Dive: Session ef4a56e5

This session crystallizes the system's core pathology:

**Seat 1 — The AND-Gate Victim:**

| Metric | Candidate | Incumbent | Delta |
|--------|-----------|-----------|-------|
| Win Rate | 0.30 | 0.35 | **-0.05** |
| Avg Score | 12.65 | 8.50 | **+4.15** |

- The candidate lost win rate by exactly the threshold (5 percentage points) but gained a staggering **4.15 points per game** in score differential.
- In a Dominoes game to 8 points, a +4.15 average score improvement means the candidate consistently held opponents to fewer points and scored more itself, even if it didn't cross the finish line first in enough of the 20 evaluation games.
- **Verdict: REJECTED.** The AND-gate (`passesWinRate && passesScoreDelta`) requires **both** criteria. A strategy that scores massively better but wins slightly less often is discarded.

This is not a theoretical edge case. In a 4-player stochastic game, win rate over 20 matches has a 95% confidence interval of approximately ±22%. A 5-percentage-point swing is well within noise. The score differential metric (+4.15) has far more statistical power and is a better learning signal. Yet the system uses the noisy metric as a hard gate and wastes the high-signal metric.

**Seat 2 — Clearly Worse:**

| Metric | Candidate | Incumbent |
|--------|-----------|-----------|
| Win Rate | 0.20 | 0.45 |

Rejected for obvious reasons. No controversy here.

### 2.3 The 2,814 Games Efficiency Question

Each iteration burns approximately `GameCount + 1 + EvaluationMatchCount × 2` games per session × 4 parallel sessions. At (roughly) 3 observation games + 1 play match + 40 eval matches = 44 games per session, 5 iterations × 4 sessions × 44 = ~880 evaluation games plus observation/play games ≈ 2,814 total. The evaluation budget dominates — potentially 80%+ of all compute is spent on the promotion evaluation rather than on improving the strategies. This ratio is defensible if the evaluation reliably identifies better strategies. It does not.

---

## 3. Promotion Gate Critique

### 3.1 The AND-Gate Problem

Source: `Wip.Bones.Agents/Knowledge/BonesStrategyPromotionEvaluator.cs`, lines 147-152:

```csharp
var passesWinRate = winRateImprovement >= request.Options.MinimumWinRateImprovement;
var passesScoreDelta = scoreDifferentialDelta >= request.Options.MinimumScoreDifferentialImprovement;
var outcome = passesWinRate && passesScoreDelta
    ? BonesPromotionDecisionOutcome.Promoted
    : BonesPromotionDecisionOutcome.Rejected;
```

Default thresholds from `BonesKnowledgeTypes.cs`, `BonesStrategyPromotionOptions`:
- `EvaluationMatchCount = 20`
- `MinimumWinRateImprovement = 0.05`
- `MinimumScoreDifferentialImprovement = 1`

**Why this fails:**

1. **Statistical insignificance of win-rate at N=20.** With 20 games, the standard error on a 0.30 win rate is `sqrt(0.3 × 0.7 / 20) = 0.102`. A 95% confidence interval spans ±0.20. The 0.05 threshold is one-quarter of the noise floor. The system is making deterministic promotion decisions based on statistically meaningless comparisons.

2. **Score differential is a far stronger signal.** Score differential accumulates across all 20 games with continuous granularity. A +4.15 delta reflects real, repeatable strategy quality. It should be the _primary_ criterion, with win rate as a secondary check.

3. **AND-gate defeats the purpose of having two metrics.** An OR-gate (promote if _either_ metric passes) or a weighted composite would allow the score signal to rescue strategies that show clear improvement despite noisy win-rate. The current AND behavior means the more-stringent metric dictates all outcomes.

4. **No soft retention.** Rejected candidates are stored as `BonesPromotionStatus.Rejected` artifacts but are never reconsidered. A candidate that scores +4.15 better but wins 5% less is permanently discarded. The system cannot later decide "actually, that was a good strategy after all."

### 3.2 The 20-Match Budget

20 evaluation matches per candidate-incumbent pair (40 total matches per promotion decision) is:
- Likely too few for a 4-player stochastic game (standard error ~10 percentage points)
- Simultaneously too expensive in aggregate (80%+ of simulation budget)
- Not adaptive — it doesn't increase when the decision is close (as in Seat 1 where win rates were 0.30 vs 0.35)

A smarter evaluator would use sequential testing: run matches until a clear signal emerges or a maximum budget is reached. This would reduce wasteful evaluation of clear winners/losers and increase precision for borderline cases.

### 3.3 What the Metrics Actually Say

The existing `BonesPromotionEvaluationMetrics` record captures:
- Candidate and incumbent win rates
- Score differential delta
- Standard error and 95% confidence interval

Yet the promotion decision ignores the confidence interval entirely. The evaluator computes `StandardError` and `ConfidenceInterval95` at lines 142-143 and passes them into the metrics record, but `passesWinRate` and `passesScoreDelta` are purely threshold-based with no regard for statistical significance. If the CI overlaps zero (which it does for +0.05 at N=20), the system should be agnostic, not decisive.

**Budget tracking bug:** The `ComputePlannedIterationGameBudget()` in `BonesLearningLoopHost.cs` line 192-194 reserves `GameCount + 1 + EvaluationMatchCount` games, but the evaluator runs `EvaluationMatchCount × 2` games (candidate + incumbent each get their own 20 matches). The evaluator then calls `RecordGames(matchCount × 2)` at evaluator line 159, which adds 40 games to a counter that already reserved 20. The effective budget accounting is inconsistent — the system consumes 60 budget units for what should be 40 matches of work.

---

## 4. Enhancement Quality

### 4.1 What the LLM Sees: Single-Match Context

Source: `Wip.Bones.Agents/Enhance/BonesEnhanceStrategyAgent.cs`, lines 82-92:

```csharp
var allMatchHistories = await scopedKnowledgeStore.LoadMatchHistoryTranscripts(
    request.PlayerId, cancellationToken);

var matchHistories = request.MatchGameId is null
    ? allMatchHistories
    : allMatchHistories
        .Where(history => history.GameId == request.MatchGameId)
        .ToArray();
```

When `MatchGameId` is set (which it is in the co-learning workflow — it's set to the single play match that just completed), the enhancer filters to **exactly one** match. The `matchHistoriesLoaded` counter in the execution log confirms: every session shows `matchHistoriesLoaded=1`.

This is the most damaging design choice in the entire system. Here is what the LLM receives:

1. **The prior strategy script** — complete C# source of what it wrote before.
2. **One game's match history transcript** — the play-by-play of a single match.
3. **The outcome summary** — "Win" or "Loss" with score differential.

And it is asked to "refine" the strategy.

**Example of what the LLM cannot distinguish:**
- Was a loss caused by a genuine heuristic flaw, or by unlucky tile draws?
- Did the strategy improve from last iteration (it can't see prior match outcomes)?
- Is a particular `ChooseMove` branch consistently exploited by opponents?
- Did the strategy win because it was good, or because an opponent played poorly?

With N=1, every outcome is overfitted. The LLM is essentially doing random hill-climbing on a single data point.

### 4.2 What the LLM Doesn't See

1. **Prior iteration outcomes.** The enhancement has no access to how the strategy performed in previous iterations. It cannot detect trends (improving, plateauing, oscillating).

2. **Opponent strategies.** The enhancement prompt contains no information about what opponents were playing. It cannot counter-adapt.

3. **Multiple match transcripts.** Despite the code technically supporting `matchHistories.Count > 1` (the prompt builder iterates over multiple histories), the workflow always scopes to a single `MatchGameId`.

4. **The candidate strategy's evaluation results.** When a strategy is rejected in promotion evaluation, the 20-match evaluation data (which is far richer than a single game) is never fed back to the enhancer. The LLM cannot learn from the comprehensive evaluation that just happened.

5. **Compilation failures.** On retry, the LLM receives compiler diagnostics but not examples of what _did_ compile in the past. No positive examples are provided.

6. **Strategy library state.** The LLM doesn't know what strategies other seats are using, what has been promoted, or what dead ends have been explored.

### 4.3 The Enhancement Prompt Itself

Source: `Wip.Bones.Agents/Enhance/BonesEnhancePromptBuilder.cs`, lines 44-47:

```
You are refining Block Dominoes tactics for seat {seat} by revising a C# strategy script.
Output exactly one compilable C# source file...
Preserve incumbent ChooseMove logic that correlates with wins; revise heuristics where
outcomes were losses or score differential was negative.
```

This guidance is simultaneously too vague and too constraining:

- **Too vague:** "Preserve effective heuristics" — but with one game, the LLM has no way to know which heuristics were effective.
- **Too constraining:** "Revise heuristics where outcomes were losses" — this treats every loss as evidence of a heuristic flaw, which is statistically wrong for a stochastic game.
- **No meta-cognition:** The LLM cannot say "my last change made things worse, let me revert it." It has no memory of what it changed between versions.

---

## 5. Incentive Structure

### 5.1 Why the System Lacks Incentives to Go Forward

The learning loop is structured as a linear pipeline: Observe → Ponder → Play → Enhance → (evaluate → promote/reject). This structure has no intrinsic pressure toward improvement beyond what the LLM can infer from a single match. Several design choices actively work against progress:

**1. The Ponder Dead Zone (Seats 2-4)**

Seats 2-4 have Markdown library entries. Markdown is non-viable per `IsLibraryEntryViable()` (`BonesCoLearningPonderAgent.cs` line 164: `if (best.Kind != BonesStrategyKind.Script) return false`). Every iteration, these seats must re-Ponder — author a fresh C# script from observation transcripts. But the observation transcripts are from fixed bootstrap games, not from ongoing play. So seats 2-4 are repeatedly writing new strategies from the same stale observations, with no carry-over.

If the Ponder step produces a script that doesn't compile or doesn't win enough, it gets rejected, the seat stays Markdown, and next iteration Ponder starts again from zero. This is a **collapse trap**: a seat that falls behind cannot catch up because it keeps restarting from bootstrap observations while seat 1 (which has a compilable Script) gets to accumulate match history.

**2. Single-Match Reinforcement**

The enhance stage provides one data point to the LLM. Even if the LLM were perfect, it cannot learn from a single sample. The theoretical minimum for any statistical learning in a 4-player game with stochastic tile draws is on the order of dozens of games. The system provides one.

**3. No Process Rewards**

The only feedback is binary outcome (Win/Loss) on a single game. There is no reward for:
- Improving score even in a loss (+4.15 score delta gets zero credit)
- Discovering a novel heuristic
- Creating a script that compiles successfully
- Having a consistent strategy (low variance)

In reinforcement learning terms, this is a sparse-reward problem where the reward is observed once per iteration and is corrupted by massive stochastic noise.

**4. Promotion Gate as a Bottleneck**

The AND-gate means improvement in one dimension (score) is worthless without improvement in the other (win rate). For a strategy that trades off winning probability for scoring margin (a legitimate tactic in multi-round learning), this gate is impassable. The system punishes strategies that figure out how to score well but haven't yet figured out how to close games.

**5. No Exploration Memory**

The system has no memory of what strategies have been tried and rejected. The `Rejected` status is stored on artifacts but never queried during Ponder or Enhance. The LLM can (and likely does) re-author the same flawed approach multiple times because it cannot see the rejection graveyard.

### 5.2 Root Causes Summary

| Cause | Mechanism | Consequence |
|-------|-----------|-------------|
| AND-gate promotion | `passesWinRate && passesScoreDelta` | Rejects +4.15 score improvements |
| Single-match enhance | `matchHistories` filtered to one `MatchGameId` | LLM learns from N=1 |
| Markdown non-viability | `Kind != Script` → must re-Ponder | Seats 2-4 restart every iteration |
| No cross-iteration memory | No artifact bridging sessions | Each iteration is amnesic |
| No rejection analysis | Rejected strategies never inspected | Dead ends repeated |
| Sparse binary reward | Only Win/Loss feedback | No gradient for improvement |

---

## 6. Gap Analysis vs Continual Harness

### 6.1 Systematic Comparison

| Continual Harness Capability | Bones Status | Gap Severity |
|------------------------------|-------------|--------------|
| Reset-free online adaptation | Partial — runs in a loop without episode resets, but each iteration's session is isolated with no carry-over | High |
| Online process-reward co-learning | **Missing.** Only outcome reward (Win/Loss). No reward shaping, no intermediate credit assignment. | Critical |
| Sub-agent creation & delegation | **Missing.** Monolithic C# scripts. No skill decomposition, no sub-task delegation. | Medium |
| Skill creation & revision | **Missing.** Strategies are monolithic. No `BlockingHeuristic`, `ScoringHeuristic`, `EndgameHeuristic` decomposition. | High |
| Online prompt optimization | **Missing.** Enhancement prompt is static. The system never rewrites its own meta-instructions. | Medium |
| Long-context memory | **Missing.** Each enhancement sees exactly 1 match. No history accumulation across iterations. | Critical |
| Bootstrapped continual runs | **Partial.** `ResumeFromBest` loads library on restart, but only for strategy content — not for memories, skills, or meta-knowledge. | Medium |
| Curriculum/difficulty progression | **Missing.** All games are identical 4-player matches to 8 points. No easier introductory games, no harder variants. | Low |
| Rejection avoidance | **Missing.** No memory of failed strategies, no "don't do this again" signal. | High |

### 6.2 Where Bones Is Closest

Bones does have a few Continual Harness-aligned properties:
- **Multi-agent co-learning:** 4 seats learning simultaneously with opponent-relative evaluation (`OpponentActiveStrategies` in evaluator).
- **Strategy library with ranking:** `BonesStrategyLibraryRanking` with win rate → score differential → kind → recency sort order.
- **Compile-retry loop:** Up to 3 retries with compiler diagnostics fed back to the LLM (`maxCompileRetries = 3` in enhance agent).

These are valid foundations. The problem is they sit on top of a broken incentive structure.

### 6.3 The Critical Missing Piece: Memory

In Continual Harness, long-context memory "unsticks blocked routes." Bones has no equivalent. When seat 1 was rejected with a +4.15 score improvement, the system forgot this immediately. Next iteration, seat 1 will enhance from its (worse) incumbent and may make the same trade-off again — or, equally bad, may avoid the trade-off and produce a strategy that neither wins nor scores well.

A memory system would:
1. Record that "+4.15 score, -0.05 win rate" was explored and rejected.
2. On the next iteration, tell the LLM: "Your predecessor improved scoring dramatically but slightly reduced win probability. Try to preserve the scoring while fixing the closing-game heuristic."
3. If the pattern repeats (always high score, lower win rate), flag this as a systematic bias in the LLM's strategy generation and adjust the enhancement prompt.

---

## 7. Priority Enhancement Opportunities

### Tier 1: Immediate (Fix the Incentive Problem)

These changes address the four root-cause issues blocking progress and can be implemented within the existing architecture.

**T1.1 — OR-Gate or Composite Promotion Score**

Change `BonesStrategyPromotionEvaluator.cs` line 150 from AND to a composite:

```csharp
// Replace AND with a weighted composite that can rescue high-score strategies
var compositeScore = (winRateImprovement / 0.05) * 0.5
                   + (scoreDifferentialDelta / 1.0) * 0.5;
var outcome = compositeScore >= 0.0 || (passesWinRate && passesScoreDelta)
    ? BonesPromotionDecisionOutcome.Promoted
    : BonesPromotionDecisionOutcome.Rejected;
```

Or simpler: change AND to OR with a minimum bar on the weaker dimension:

```csharp
var outcome = (passesWinRate || passesScoreDelta)
           && winRateImprovement >= -0.10  // don't promote if win rate tanks
           && scoreDifferentialDelta >= -2  // don't promote if score tanks
    ? BonesPromotionDecisionOutcome.Promoted
    : BonesPromotionDecisionOutcome.Rejected;
```

The specific formula should be tunable. The key principle: a strategy that crushes on one metric and is neutral on the other should be promoted, not rejected.

**T1.2 — Multi-Match Enhancement Context**

In `BonesEnhanceStrategyAgent.cs` lines 82-92, change the match history loading to include all available histories for the player in this session:

```csharp
// Replace single-match filtering with multi-match context
var matchHistories = allMatchHistories;  // was: filtered to request.MatchGameId
```

If concern about context window size, use a sliding window of the last N matches (suggest N=5-10) with a weighted summary of older matches. The single-match design is the single biggest blocker to LLM learning — fixing this alone would produce the largest quality improvement of any change.

**T1.3 — Allow Markdown Promotion (Fix the Dead Zone)**

In `BonesCoLearningPonderAgent.cs` `IsLibraryEntryViable()`, allow Markdown entries that have sufficient match history:

```csharp
private bool IsLibraryEntryViable(BonesPlayerId playerId)
{
    // ...
    // Allow Markdown entries with significant match history to be viable
    if (best.Kind == BonesStrategyKind.Markdown && best.Effectiveness.MatchesPlayed >= 10)
        return true;
    // ...
}
```

Even better: after Ponder succeeds for a seat, persist the Script to the library immediately so the seat doesn't revert to Markdown on restart.

**T1.4 — Rejection Archaeology**

Before the enhance step, load the last N rejected strategies for this seat and include a summary in the enhancement prompt:

```
Previously rejected strategies:
- v3: +4.15 score, -0.05 win rate. Rejected for insufficient win rate improvement.
  Suggestion: preserve scoring heuristics, improve endgame closing.
- v2: -2.1 score, -0.15 win rate. Rejected for both dimensions.
  Avoid this approach.
```

This would prevent the LLM from cycling through the same dead ends.

### Tier 2: Near-Term (Build on Tier 1)

These require more architectural work but build naturally on Tier 1 fixes.

**T2.1 — Sequential Evaluation Testing**

Replace the fixed 20-match evaluation with a sequential probability ratio test (SPRT) or similar adaptive stopping rule. Run matches until:
- The composite score is confidently above/below threshold (p < 0.05), or
- A maximum budget (e.g., 50 matches) is reached.

This would reduce the evaluation budget for clear winners/losers while increasing precision for borderline cases.

**T2.2 — Cross-Iteration Strategy Memory**

Persist a "strategy lineage" across sessions: a chain showing what changed from version to version, the reason for change, and the outcome. Load this as context in the enhance prompt:

```
Strategy lineage for seat 1:
v1 (initial): Win rate 0.35, Avg score 8.5
v2: Added blocking heuristic → Win 0.30, Score 12.65. REJECTED (score up, win down).
v3: Current incumbent. Target: keep scoring, improve win rate.
```

This transforms the learning from a Markov process (only current state matters) to a history-aware process.

**T2.3 — Process Rewards**

Add intermediate reward signals beyond Win/Loss:
- **Score delta reward:** Even in a loss, reward the strategy for scoring more than the incumbent did.
- **Compilation reward:** Small positive signal for producing a compilable script (reduces Ponder churn).
- **Diversity reward:** Penalize strategies that are near-duplicates of previously rejected ones.

These can be added as fields in `BonesEnhancePromptBuilder` without changing the architecture.

**T2.4 — Opponent-Aware Enhancement**

The enhancement prompt (`BonesEnhancePromptBuilder`) should include summaries of opponent strategies. When the LLM knows "Seat 3 was playing an aggressive blocking strategy," it can adapt its own strategy to counter specific opponents rather than optimizing against an anonymous field.

**T2.5 — Fix the Budget Double-Count**

Fix `BonesLearningLoopHost.ComputePlannedIterationGameBudget()` to use `EvaluationMatchCount * 2` instead of `EvaluationMatchCount`, and remove the `RecordGames` call in the evaluator (or vice versa — pick one accounting path and be consistent).

### Tier 3: Ambitious (Continual Harness-Level)

These are roadmap items that would bring Bones to the frontier of continual co-learning.

**T3.1 — Skill Decomposition**

Replace monolithic `IBonesPlayerSlot.ChooseMove()` with composable skills:
- `TileEvaluationHeuristic` — scores individual tiles for playability
- `BlockingHeuristic` — decides when to block vs. score
- `EndgameHeuristic` — adapts behavior when close to target score
- `OpponentModel` — predicts opponent hand states

Each skill is independently authored by the LLM and composed at play time. Skills can be separately enhanced, promoted, and recombined. This would dramatically increase the search space coverage of the learning loop.

**T3.2 — Online Prompt Optimization**

Let the enhancement LLM also suggest improvements to the enhancement prompt itself. For example: if the LLM consistently produces strategies that score well but lose on close games, the meta-optimizer could add a specific instruction to the enhance system prompt: "Prioritize improving endgame win probability even at moderate cost to scoring."

**T3.3 — Long-Context Memory Architecture**

Build a dedicated memory store (separate from the artifact store) that accumulates:
- Strategy lineages with diffs
- Match outcome statistics per strategy version
- Discovered game patterns ("holding the double-6 to last is correlated with wins")
- Failed approaches with reasons

This memory persists across sessions and is loaded as context for both Ponder and Enhance. When the LLM asks "what should I try?" the memory answers "here's what worked, here's what didn't, here's what you haven't tried."

**T3.4 — Sub-Agent Delegation**

Allow the strategy to delegate sub-decisions to specialized sub-agents. For example, when the game reaches a state where the strategy is uncertain (new territory), it could call a "deep-think" sub-agent that has more context or a different LLM, rather than making a heuristic guess.

**T3.5 — Curriculum Learning**

Start with simpler variants of Dominoes (fewer tiles, lower target score, fewer players) to let strategies learn basic heuristics before graduating to full 4-player games. This would stabilize early learning and reduce the "Ponder dead zone" where seats 2-4 never get off the ground.

---

## 8. Risk Assessment: What Happens If Nothing Changes

### 8.1 Near-Term Trajectory (Next 10 Iterations)

- **Seat 1** will continue the oscillating pattern: produce a strategy, get rejected for slightly lower win rate (despite score improvement), revert to incumbent. The incumbent (0.35 win rate) is the ceiling — no strategy can be promoted through the AND-gate because any change will, by chance, reduce win rate in at least one of the two 20-match samples.
- **Seats 2-4** will remain in the Ponder dead zone, re-authoring from bootstrap observations each iteration. With 99 strategies compiled already across ~20 Ponder attempts per seat, we should expect more compilations, but the promotion rate (1 success in 5 iterations) suggests even compilable scripts rarely survive the gate.
- **The effective learning rate** will approach zero — strategies will bounce against the promotion gate without accumulating real improvement.

### 8.2 Medium-Term (Next 50 Iterations)

- **Budget exhaustion.** At ~600 games per iteration (4 sessions × ~150 games), the system will hit any reasonable budget cap before meaningful learning can occur. The evaluation budget alone burns 160 games per iteration (4 sessions × 40 eval matches) on a statistically inadequate 20-match test.
- **Strategy convergence.** Without cross-iteration memory, strategies will converge to whatever heuristics the LLM defaults to when asked to "write a Dominoes strategy." This is a local optimum determined by the LLM's pre-training, not by actual game performance.
- **Library rot.** The library will accumulate 4 entries with minimal churn. Promotions will be rare (maybe 1 every 10-20 iterations), and most will be noise rather than genuine improvement.

### 8.3 Long-Term (Production Deployment)

- **Cost-to-improvement ratio becomes untenable.** Each incremental strategy improvement costs thousands of evaluation games. The system is effectively doing random search with an LLM as the proposal distribution and a broken acceptance criterion.
- **The system teaches us nothing about Dominoes.** The strategies that survive the AND-gate will be those that happened to win 2-3 more games in a 20-match sample — indistinguishable from noise. The genuinely better strategies (like the +4.15 score improvement) are discarded.
- **Competitive disadvantage.** Any human player who watches 20 games can identify and counter the learned strategies. The system cannot adapt to a changing opponent meta because it has no opponent awareness and no memory of what worked against specific play styles.

### 8.4 The Silver Lining

The system is not fundamentally broken — it has the right architecture (multi-agent co-learning, compile-retry loop, structured evaluation). The problems are concentrated in three specific areas:

1. The promotion gate (one Boolean expression)
2. The enhancement context (one filter clause)
3. The missing memory (architecture gap)

Fixing #1 and #2 requires changing fewer than 20 lines of code. Fixing #3 requires a new subsystem but the interfaces already exist (artifact store, knowledge store, session lifecycles). The path from "stuck" to "learning" is unusually short for a system that has accumulated 2,814 games of data.

---

## Appendix: Key Code Locations

| Component | File | Key Lines |
|-----------|------|-----------|
| Promotion AND-gate | `BonesStrategyPromotionEvaluator.cs` | 147-152 |
| Promotion thresholds | `BonesKnowledgeTypes.cs` (BonesStrategyPromotionOptions) | ~289-293 |
| Enhancement single-match filter | `BonesEnhanceStrategyAgent.cs` | 82-92 |
| Enhancement prompt builder | `BonesEnhancePromptBuilder.cs` | 1-224 |
| Markdown viability check | `BonesCoLearningPonderAgent.cs` | 155-167 |
| Budget calculation | `BonesLearningLoopHost.cs` | 192-194 |
| Budget reservation | `BonesLearningLoopHost.cs` | ~118-122 |
| Evaluation match count ×2 | `BonesStrategyPromotionEvaluator.cs` | 89 |
| Library ranking (Script > Markdown) | `BonesStrategyLibraryRanking.cs` | 57-62 |
| Co-learning orchestrator (Ponder skip) | `BonesCoLearningOrchestrator.cs` | 155-167 |
