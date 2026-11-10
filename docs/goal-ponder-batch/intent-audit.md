# Intent audit — aggressive Ponder+Enhance batch

**User request (verbatim):** "okay, do it more agressively now and more times. at least 10 times this level of experiment run. /stretch-goal-harness"

**Counts:** KEPT 7 · REPLACED 1 · REMOVED 0.

| Mechanism | Verdict | Proposal location | Citation | Consequence |
|---|---|---|---|---|
| Search-augmented Ponder+Enhance run ("this level") | KEPT | goal.md:9, 15, 26; preregistration.md:20-32 | experiments-3.md:3 (three-round hybrid: live 6-seat Ponder, LLM seed, deterministic grid, frozen gate); goal.md:26 (each run 3 rounds × 6 seats × 20 hands) | The prior run's full hybrid shape is the per-run template; ten copies are planned. |
| Live 6-seat Ponder match every round | KEPT | goal.md:9, 15, 26; preregistration.md:25, 28 | experiments-3.md:3, 39-47; RoundRunner.cs:279-289 (seat 0 live runtime agent, seats 1-5 tight-aggressive); Program.cs:270-285 | The model-driven Ponder surface is retained in every round of every run. |
| LLM seed proposal from the champion's 6-seat measurement | KEPT | goal.md:9, 15, 26 (analysisSource=genome); preregistration.md:29 | experiments-3.md:3, 47; RoundRunner.cs:83-116 (genome measurement feeds LlmRefinementPolicy; the live proposal seeds the search) | The refine step remains live-model-driven; the deterministic genome measurement is its input, as in the prior run. |
| Deterministic grid search on seeds disjoint from the gate | KEPT | goal.md:9, 26, 35; preregistration.md:30 | experiments-3.md:35-37; RoundRunner.cs:311-361 (7000+round search base; 9000+round gate base) | Search stays deterministic and off the evaluation sample; only its breadth changes. |
| Frozen gate at 24 seeds on untouched seeds | KEPT | goal.md:26, 34; preregistration.md:31 | experiments-3.md:20, 37; RoundRunner.cs:130-143; MatchEvaluator.cs:13 (SeedHandBase=9000) | The promotion decision stays on the frozen 24-seed gate; no gate weakening. |
| "At least 10 times" (more times) | KEPT | goal.md:16, 26; preregistration.md:7-18 | experiments-3.md:3 (one run); goal.md:26 (≥10 independent runs); journal.md:18 (ten runs, sequential) | Ten independent runs at ten pre-registered seeds satisfy the count; failures are reported, not hidden. |
| "More aggressively" | REPLACED BY wider deterministic grid (100→≥300 candidates at 50 search seeds); live Ponder stays 3 rounds × 20 hands; LLM seed one per round | goal.md:17, 26; preregistration.md:30 | experiments-3.md:35 (prior run: 100 candidates at 50 seeds, 3 rounds); goal.md:26 (new run: 3 rounds × 20 hands, 50 seeds, ≥300 candidates) | Only the offline search breadth is more aggressive; the live model-driven run is unchanged. If the user meant more live Ponder/LLM work, that escalation is not delivered. |
| /stretch-goal-harness | KEPT | goal.md:18, 28-30; journal.md:1-44; evidence.md:1-7; state.md:1-26; deliverables.md:1-5 | goal.md:28-30 (batch report, invariants, honesty); journal.md:18 (pre-registration), 33-40 (risk); state.md:12 (G0 intent audit) | Harness state, evidence, gates and intent audit are present; the proposal's sequential batch is an execution choice, not a removal. |

## Question

Does "more aggressively" mean the proposed wider deterministic grid (100→≥300 candidates at 50 search seeds, with live Ponder still 3 rounds × 20 hands), or does it require the live model-driven run itself to be more aggressive (more rounds, more hands, or more search seeds)?
