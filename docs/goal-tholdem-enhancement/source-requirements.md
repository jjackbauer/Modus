# Modus / WiP Texas Hold'em — Enhancement Requirements (working copy)

> Transcribed from the operator-provided specification delivered 2026-11-10, which is the
> authoritative source for this goal. Requirement IDs are stable. Acceptance conditions are
> reproduced for the tickets that are executed; the full acceptance text for every row was supplied
> with the source specification.
>
> **Reference:** `jjackbauer/Modus`, branch `WIP`, commit `11eca9e83602b8ae485c815788d1e8b3df271a5e`.
> **Target:** Four independent, individually scored agents playing no-limit Texas Hold'em, while
> preserving the existing two-to-six-seat engine.

## 1. Executive decision and scope

**Delivery objective (verbatim):** A challenger may be called *strategically improved* only if its
measured payoff is better on a pre-registered, four-player, seat-rotated, held-out evaluation **and**
it clears regression guards against diverse, unseen opponents. WiP may be called *better at
learning* only if its proposal process outperforms equal-budget, non-LLM controls over multiple
starting strategies and evaluation tasks.

### Scope boundaries

- **In scope:** deterministic local simulation, cash-game-style independent hands, four independent
  seats, side pots, public/private observation boundaries, data-only policy evolution, LLM proposal
  workflows, multi-opponent evaluation, evidence artifacts, operator review, reproducible
  experiments, and optional 2–6-seat playback.
- **Preserve:** the existing engine and two-seat compatibility, deterministic seeds and canonical
  history, local-first execution, explicit permissions, frozen independent verification, no
  execution of arbitrary model-authored strategy code, and no automatic merge/deployment.
- **Out of scope for first release:** complete multiway Nash/GTO solving, real-money poker, online
  matchmaking, real players, GPU training of model weights, production gambling services, and proof
  that an LLM is universally capable of strategic reasoning.
- **Mode choice:** default evaluation consists of independent 100-big-blind cash-game hands with
  stacks reset between hands, matching the current simulator. Persistent-stack tournaments are a
  later separate benchmark, never silently mixed with cash-game metrics.

### Normative conventions

- **P0 / MUST:** blocks a credible four-player strategic-learning claim.
- **P1 / SHOULD:** required before a strong comparative research claim or broadly usable benchmark.
- **P2 / MAY:** usability, scaling, and research depth after the core is correct.

## 2. Critical traceability map

| Observed weakness | Primary implementation | Requirement groups |
|---|---|---|
| Fixed heuristic hand strength mistaken for equity | `Baselines/HandStrength.cs`, `Learning/StrategyAgent.cs` | EQ, POL, OBS |
| 1-opponent uniform Monte Carlo used in multiway games | `Equity/MonteCarloEquity.cs`, `Runtime/EquityTool.cs` | EQ, OPP |
| Global thresholds, minimum-raise sizing plateau | `Learning/StrategyGenome.cs`, `Learning/StrategyAgent.cs` | POL, MUT |
| Training and promotion still heads-up | `Learning/TrainingLoop.cs`, `Learning/MatchEvaluator.cs` | SIM, EVA |
| Misleading post-hoc failure signals, unmeasured VPIP=0 | `Refinement/FailureSignatureExtractor.cs`, `Learning/SessionAnalysis.cs` | DIA, OBS |
| No demonstrated incremental value from the LLM | `ModelHarness/LlmRefinementPolicy.cs`, archived Ponder experiments | LLM, EVA |
| Inert parameter differences treated as changes | `StrategyGenome.BehaviorEquals`, `StrategyProposer`, `RefinementGuard` | MUT, EVA |
| Cross-player opponent information is aggregated | `Runtime/OpponentStatsTool.cs` | OBS, OPP |
| Replay evidence is not generalization evidence | `Simulation/MatchSimulator.cs`, reports/ledger | EVA, ART |

## 3. Benchmark definition and simulation semantics

| ID | Pri | Requirement |
|---|---|---|
| SIM-001 | P0 | Make **four independent players** the default learning, evaluation, and promotion environment; retain 2–6 seats for engine and UI regression. |
| SIM-002 | P0 | Introduce a versioned `GameConfiguration` covering seat count, blinds, stack policy, initial stacks, ante, button rotation, action cap, side-pot rules, and evaluation mode. |
| SIM-003 | P0 | Explicitly distinguish independent-hand cash evaluation from persistent-stack tournaments. |
| SIM-004 | P0 | Preserve full four-player action order, blind/button rotation, all-in re-opening rules, side pots, refund semantics, and chip conservation. |
| SIM-005 | P0 | Expose decision-time pot eligibility and effective callable amounts, especially when some players are all-in. |
| SIM-006 | P0 | Ensure a hand cannot reveal a future board, undealt cards, or another player's private cards to any seat's decision policy or tools. |
| SIM-007 | P1 | Allow heterogeneous stacks and seat-order permutations without changing the player identity or strategy under test. |
| SIM-008 | P1 | Keep exact two-seat historical replay compatibility while versioning new four-player learning report schemas. |
| SIM-009 | P1 | Define deterministic timeout, invalid-action, and model-failure consequences distinct from legitimate poker decisions. |

## 4. Decision-time observation and feature correctness

| ID | Pri | Requirement |
|---|---|---|
| OBS-001 | P0 | Build an immutable, versioned `DecisionContext` from only information available at the exact action boundary. |
| OBS-002 | P0 | Include own seat, button relative position, street, active/folded/all-in counts, and individual public state for every opponent. |
| OBS-003 | P0 | Include own and per-opponent effective stacks, amount to call, legal min/max targets, current pot and eligible side-pot exposures. |
| OBS-004 | P0 | Include a normalized action history with actor seat, street, action, amount-to, and resulting public pot/commitment. |
| OBS-005 | P0 | Record all truly unavailable statistics as `null` / `NotMeasured`, not zero. Fix `SessionAnalysis.Vpip` before passing it to a model. |
| OBS-006 | P1 | Compute actual VPIP, PFR, three-bet, fold-to-raise, aggression, showdown participation, and action counts from observed histories, with denominators. |
| OBS-007 | P1 | Add board texture, pairedness, suit distribution, connectivity, draw status, and estimated nut/relative-strength features. |
| OBS-008 | P1 | Maintain per-seat public opponent profiles; do not collapse all opponents into a single aggregate. |
| OBS-009 | P1 | Keep operator-only full-hand data separate from any agent/tool transcript and from model-visible refinement input. |
| OBS-010 | P2 | Support features derived from bounded temporal histories and explicit confidence/age/decay for opponent estimates. |

## 5. Equity, pot odds, and uncertainty

| ID | Pri | Requirement |
|---|---|---|
| EQ-001 | P0 | Do **not** compare `HandStrength.Estimate` directly to pot odds as though it were showdown equity. Separate `HandCategoryScore` from `EquityEstimate`. |
| EQ-002 | P0 | Extend Monte Carlo equity from one uniform opponent to the exact number of live eligible opponents (1–5). |
| EQ-003 | P0 | Estimate winning probability, tying probability, and **expected pot share**, not merely heads-up win+half-tie. |
| EQ-004 | P0 | Condition sampled opponent hands on range weights derived from public position/action history; keep uniform ranges as a labeled control. |
| EQ-005 | P0 | Calculate call EV using actual contributions, contested pots, side-pot eligibility, and possible future costs, rather than one unqualified pot-odds scalar. |
| EQ-006 | P0 | Model uncertainty around Monte Carlo estimates and forbid confidence-free decisions near action thresholds. |
| EQ-007 | P1 | Support exact showdown equity on the river and deterministic exhaustive/reference cases for turns where feasible. |
| EQ-008 | P1 | Add blocker/card-removal-aware range sampling without replacement across *all* opponents. |
| EQ-009 | P1 | Expose effective sample count, method, range-model version, seed domain, and compute budget with every equity result. |
| EQ-010 | P1 | Separate raw showdown equity from realized equity, implied odds, fold equity, and opponent response assumptions. |
| EQ-011 | P2 | Offer stratified/variance-reduced sampling and adaptive sample allocation on strategically close decisions. |

## 6. Strategy representation and action policy

| ID | Pri | Requirement |
|---|---|---|
| POL-001 | P0 | Replace four global thresholds as the sole policy with a versioned, data-only structured strategy of independently mutable components. |
| POL-002 | P0 | Use separate preflop decisions for unopened pots, facing a limp/open, facing a three-bet, and defending blinds. |
| POL-003 | P0 | Support a 169-class preflop hand representation, suitedness, pair rank, position, opener position, and effective stack depth. |
| POL-004 | P0 | Use distinct postflop policy branches for flop, turn, river, initiative, opponent aggression, and multiway participation. |
| POL-005 | P0 | Support fold, check, call, bet, raise and all-in with validated expected-value or explicit policy reasons; never default to call/fold solely on a category score. |
| POL-006 | P0 | Replace `MinRaiseTo * multiplier` with legal **pot-relative** (and optionally effective-stack-relative) sizing targets. |
| POL-007 | P0 | Remove semantically flat mutation ranges or canonicalize them to the same effective policy. |
| POL-008 | P0 | Use street-, position-, and opponent-aware bluffing with fold-equity and blocker considerations; keep pure random bluff frequency as a control. |
| POL-009 | P1 | Support mixed strategies with deterministic, reproducible action sampling and declared probability distributions. |
| POL-010 | P1 | Separate value betting, thin value, protection, semibluff/draw betting, and pure bluff action categories. |
| POL-011 | P1 | Represent exploitative deviations from a stable baseline policy explicitly and cap unsafe over-adaptation. |
| POL-012 | P1 | Allow constrained street-specific skill composition while ensuring it changes *executed* decisions rather than only metadata. |
| POL-013 | P1 | Remove ignored numeric fields from effective strategy identity whenever `Skills` overrides them, or define explicit precedence and canonical composition. |
| POL-014 | P1 | Add interpretable policy action traces: feature values, candidate action values, action probabilities, and binding constraints. |
| POL-015 | P2 | Support multiple policy families (heuristic, range-table, compact decision tree, distilled model) behind the same typed action interface. |

## 7. Failure attribution, diagnostics, and training signals

| ID | Pri | Requirement |
|---|---|---|
| DIA-001 | P0 | Make failure labels **decision-time causal hypotheses**, not labels assigned solely from final made hand or terminal win/loss. |
| DIA-002 | P0 | Replace `missed-value` based only on final hand category/no aggression with a bounded counterfactual estimate of bet EV versus check EV. |
| DIA-003 | P0 | Replace `call-with-weak-hand` based on showdown loss with a decision-time call-EV or calibrated regret estimate. |
| DIA-004 | P0 | Count folds facing the *actual outstanding aggressive action*, not merely when the immediately previous action was an opponent raise. |
| DIA-005 | P0 | Keep zero observations, zero failures, and unmeasured quantities as separate states. |
| DIA-006 | P1 | Emit the denominator, data source, confidence interval, and eligible-sample criterion for each failure signal. |
| DIA-007 | P1 | Separate diagnostic frequency from estimated economic impact and rank candidates by expected recoverable EV. |
| DIA-008 | P1 | Attribute diagnostics to seat, street, opponent archetype, effective stacks, and strategy version. |
| DIA-009 | P1 | Keep counterfactual/private-card analysis in trusted offline learning only; never present future information as if known at action time. |
| DIA-010 | P2 | Provide adjudicable example hands and counterexamples for every signature, with a stable taxonomy and version. |

## 8. Multiway strategy evaluation and statistical promotion

| ID | Pri | Requirement |
|---|---|---|
| EVA-001 | P0 | Implement an N-seat `PopulationMatchEvaluator`, initially targeting four seats, independent of the proposer. |
| EVA-002 | P0 | Compare challenger against incumbent by replacing **exactly one focal seat** at a time while freezing the other opponents, configs and dealt cards. |
| EVA-003 | P0 | Rotate the focal policy through all four positions and rotate/permutate opponent positions in balanced blocks. |
| EVA-004 | P0 | Use common-random-number paired decks and documented disjoint random domains for dealing, policy mixing, opponent sampling and equity estimation. |
| EVA-005 | P0 | Express primary performance as **paired net chip EV in bb/100**, computed from per-hand differences and a documented per-seat baseline. |
| EVA-006 | P0 | Compute confidence bounds at the independent *seed block* / opponent-pool unit rather than treating correlated seats and hands as independent trials. |
| EVA-007 | P0 | Separate proposal/training, search/selection, promotion holdout, and final audit test sets, including **unseen opponent populations**, not only unseen deck seeds. |
| EVA-008 | P0 | Establish a pre-registered minimum meaningful improvement (bb/100), confidence level and statistical power before any candidate search. |
| EVA-009 | P0 | Require a positive held-out gain over incumbent **and** no material regression against a designated anchor pool. |
| EVA-010 | P0 | Make the promotion decision independently recomputable from persisted code/policy versions, seeds, opponent pool and canonical results. |
| EVA-011 | P0 | Preserve a legitimate `no-improvement` result; never promote simply to show progress. |
| EVA-012 | P1 | Introduce an evaluation-budget/power controller that increases sample size for near-threshold candidates, with an explicit stopping rule. |
| EVA-013 | P1 | Account for candidate multiplicity and repeated/optional-stopping searches by using an alpha-spending, confidence-sequence, or separately protected final-test design. |
| EVA-014 | P1 | Report EV dispersion, lower-tail losses, large-pot exposure, and robustness across depth/position variants separately from mean payoff. |
| EVA-015 | P1 | Include 2-, 3-, 4-, and 6-seat transfer evaluations as separate secondary benchmarks, without letting them substitute for the four-seat gate. |
| EVA-016 | P1 | Record actual population-game results and not only candidate-versus-one-incumbent heads-up matches. |
| EVA-017 | P1 | Detect and report paired seed/seat leakage, reused selection data, and changes to the holdout after inspection. |
| EVA-018 | P2 | Measure approximate exploitability using a bounded empirical best response / counter-strategy suite; do not mislabel it exact multiway Nash exploitability. |

## 9. Opponent populations and generalization

| ID | Pri | Requirement |
|---|---|---|
| OPP-001 | P0 | Ship a versioned, frozen opponent pool with at least tight-passive, tight-aggressive, loose-aggressive, calling-station, and adversarial-raiser behaviors. |
| OPP-002 | P0 | Include multiple opponent compositions (e.g. three TAGs, mixed archetypes, three calling stations, past champions) in four-player evaluation. |
| OPP-003 | P0 | Hold out entire opponent identities/policies, not just new card sequences, for the generalization gate. |
| OPP-004 | P1 | Strengthen the equity-based baseline with opponent count/ranges so it can be a credible reference, while retaining the old heads-up baseline for regression. |
| OPP-005 | P1 | Include previous champion generations, negative controls and targeted counter-strategies in a diverse population archive. |
| OPP-006 | P1 | Prevent opponent pool collapse and self-play co-adaptation from becoming the sole evidence of progress. |
| OPP-007 | P1 | Distinguish robust policies from opponent-specific exploitative policies with tagged evaluation goals. |
| OPP-008 | P1 | Measure per-opponent inference calibration and per-seat opponent modeling separately from policy EV. |
| OPP-009 | P2 | Add a bounded adversarial opponent-searcher that tries to exploit the candidate before final promotion. |

## 10. Mutation, search, novelty and policy selection

| ID | Pri | Requirement |
|---|---|---|
| MUT-001 | P0 | Define **effective strategy identity** from actionable policy behavior, not just raw floating-point parameter equality. |
| MUT-002 | P0 | Detect ignored parameters when skills override legacy numeric fields; do not evaluate inert variants. |
| MUT-003 | P0 | Use a canonical probe corpus spanning preflop/postflop, position, pot size, four-player interaction and stack depth to measure action/probability divergence. |
| MUT-004 | P0 | Require mutation operators to respect typed constraints, semantic invariants, legality and declared policy coverage. |
| MUT-005 | P1 | Separate small hypothesis-testing edits from multi-parameter exploratory search; record which is used. |
| MUT-006 | P1 | Maintain multiple diverse candidate lineages rather than only one champion and its immediate challenger. |
| MUT-007 | P1 | Track novelty separately from fitness and never accept a candidate based solely on decision divergence. |
| MUT-008 | P1 | Support systematic baselines: random search, grid search, coordinate search and Bayesian/model-based proposals with identical evaluation contracts. |
| MUT-009 | P1 | Record every candidate considered, its derivation, budget, selection score and reason for rejection, including unselected grid points. |
| MUT-010 | P1 | Cache action-equivalent policies and exact evaluation results by canonical content hash and experiment configuration. |
| MUT-011 | P1 | Allow search over richer policy families while limiting complexity and protecting against degenerate high-dimensional overfit. |
| MUT-012 | P2 | Add targeted exploration driven by diagnosed EV regret and principled uncertainty rather than arbitrary rate-direction mappings. |

## 11. LLM proposer, Ponder/Enhance causality and attribution

| ID | Pri | Requirement |
|---|---|---|
| LLM-001 | P0 | Define exactly which agent was measured: live Ponder policy, deterministic genome policy, or a composite. Never propose from one policy's performance while claiming another policy improved. |
| LLM-002 | P0 | Separate an LLM's **seed proposal** from a search-refined/selected strategy. |
| LLM-003 | P0 | Keep the LLM's rationale attached only to the actions/parameters it actually proposed. Require a new explanation or an explicit `search-selected` rationale for downstream changes. |
| LLM-004 | P0 | Require ablation against a no-LLM search with the same grid/compute budget. A search grid containing previously discovered winning regions must be labeled as prior-informed. |
| LLM-005 | P0 | Avoid sending fabricated metrics or tiny-sample rates as precise facts. Include nullability, sample count and uncertainty in the model context. |
| LLM-006 | P1 | Ask the model for a falsifiable *hypothesis*: targeted decision situation, intended action changes, expected EV effect, likely failure conditions, and confidence. |
| LLM-007 | P1 | Supply standardized action-level evidence and a compact regression digest, not just win rate, aggression factor and qualitative failure labels. |
| LLM-008 | P1 | Give the model a consistent, fully documented policy schema. The model must not invent unsupported `skills` fields. |
| LLM-009 | P1 | Keep model memory structured by candidate hash, measured result, opponent pool and tested hypothesis, avoiding free-text claims of past success. |
| LLM-010 | P1 | Measure model proposal quality by its counterfactual contribution to found EV, not by prompt fluency or how often it returns valid JSON. |
| LLM-011 | P1 | Track token count, model ID/version, temperature, repair count, wall-clock latency and tool budget for the entire Ponder/refine/search cycle. |
| LLM-012 | P1 | Explicitly distinguish online within-hand LLM play from offline strategy proposal and offline policy execution. |
| LLM-013 | P1 | Preserve strict JSON parsing, bounded repairs, tool allowlists, cancellation, no executable generated code, and adversarial prompt-injection tests. |
| LLM-014 | P2 | Add optional model ensembles or independent critics, but compare their marginal benefit against their marginal simulation/token costs. |

## 12. Meta-learning and autonomous harness improvement

| ID | Pri | Requirement |
|---|---|---|
| META-001 | P0 | Define three non-interchangeable objectives: **game competence** (policy EV), **learning competence** (improvement generated per budget), and **harness competence** (correct/reproducible/secure execution). |
| META-002 | P0 | Never let a candidate proposer modify or replace its own evaluation environment, verifier, baseline opponent definitions, or gate criteria. |
| META-003 | P0 | Evaluate proposer improvements across *multiple* starting policies and opponent pools, not only one incumbent trajectory. |
| META-004 | P0 | Require the meta-proposer to beat the old proposer at equal simulation budget, then clear fresh untouched tasks. |
| META-005 | P1 | Allow the meta-proposer to choose meaningful tactics (which failure to address, which operator, budget allocation and search neighborhood), not merely mutation step/parity. |
| META-006 | P1 | Distinguish frozen-learning episodes from live-opponent adaptation and measure each separately. |
| META-007 | P1 | Make lineage, distilled cases and failure memory available to the proposer only through a bounded typed interface; disallow memory as an unverified success signal. |
| META-008 | P1 | Prevent benchmark overfitting by rotating unseen starting strategies, unseen opponent pools and hidden test seeds at the meta level. |
| META-009 | P1 | Require a negative/control outcome for the meta-loop: `no-benefit`, `inert-proposer`, `self-comparison`, `budget-exceeded` or `unsafe-change`. |
| META-010 | P2 | Support a later cross-domain meta benchmark (Bones, four-player Hold'em, small planning puzzles) with the same orchestration/evidence contract. |

## 13. Reproducibility, privacy, safety and governance

| ID | Pri | Requirement |
|---|---|---|
| ART-001 | P0 | Version/hash game rules, evaluator, policy schema, strategy artifact, opponent pool, probe corpus, seed policy and code commit in every result. |
| ART-002 | P0 | Store selection/evaluation/holdout boundaries, seed-domain allocation, opponent IDs, seat permutations and sample counts in a canonical manifest. |
| ART-003 | P0 | Persist full candidate lineage, proposal origin, action fingerprint, evaluation reports, model transcript IDs and immutable promotion evidence. |
| ART-004 | P0 | Maintain an independent gate/verifier; introduce a versioned four-player v2 gate without mutating the historical heads-up frozen v1 gate. |
| ART-005 | P0 | Preserve local, data-only strategies and explicit permission boundaries; no generated source compilation, shell/network tool elevation or automatic deployment. |
| ART-006 | P0 | Require explicit operator approval for pointer promotions into any deployed/active production profile, while permitting isolated automatic promotions inside disposable experiments. |
| ART-007 | P0 | Ensure hidden-card/seed disclosure cannot enter model prompts, strategy artifacts, opponent profiles, or tool outputs. |
| ART-008 | P1 | Make seeded randomness auditable and purpose-separated; distinguish strong unpredictable secrets from deterministic reproducibility IDs. |
| ART-009 | P1 | Store retention policies separately for hand transcripts, model prompts and summary metrics, with safe redaction of secrets and local paths. |
| ART-010 | P1 | Use atomic writes and recovery protocols for ledger append, report write, pointer update and interrupted runs. |
| ART-011 | P1 | Keep signed/externally anchored audit evidence optional but distinguish it from a merely hash-chained, writable ledger. |
| ART-012 | P1 | Make external model/provider use opt-in, budget-limited and isolated from the deterministic evaluator and artifact store. |
| ART-013 | P2 | Export a portable run bundle: manifest, strategy definitions, minimal traces, reports, checksums and verification instructions. |

## 14. Runtime cost, scale, and observability

| ID | Pri | Requirement |
|---|---|---|
| RUN-001 | P0 | Set explicit per-turn and per-evaluation CPU, simulation, LLM and wall-clock budgets. |
| RUN-002 | P0 | Guarantee deterministic outcomes regardless of safe parallel scheduling by assigning random streams to stable sample and seat IDs. |
| RUN-003 | P1 | Profile the four-player equity engine for time, allocations and throughput, and set a measurable acceptance budget before large search. |
| RUN-004 | P1 | Cache pure equity and feature results by card/range/public-state hashes without leaking private/opponent information across scopes. |
| RUN-005 | P1 | Support cancellation, pause/resume and durable checkpoints at completed seed-block boundaries. |
| RUN-006 | P1 | Emit structured events for propose, diagnostic, evaluate, gate, verify, reject, promote and memory update. |
| RUN-007 | P1 | Track simulations, LLM calls, tokens, retries, time, and total normalized cost per successful strategic gain. |
| RUN-008 | P2 | Add batch/parallel evaluation and optional remote workers without granting remote agents access to protected holdout answers. |

## 15. Test strategy and adversarial proof

| ID | Pri | Requirement |
|---|---|---|
| TST-001 | P0 | Preserve and run all pre-existing Hold'em engine, deterministic-replay, multiway side-pot, and information-boundary tests. |
| TST-002 | P0 | Add exact preflop-combination enumeration tests (1,326 combos) and known-range strength/position fixtures. |
| TST-003 | P0 | Add independent equity oracles for heads-up, three-way, four-way, ties, blockers, turn/river and all-in side pots. |
| TST-004 | P0 | Add all-four-seat action legality, cross-seat identity, blind rotation, partial all-ins and chip-conservation property tests. |
| TST-005 | P0 | Add null/zero/unmeasured metric and mislabeled failure-signature counterexamples, including strong board-only hands and losing +EV calls. |
| TST-006 | P0 | Add no-op mutation tests for sizing plateaus, rounded amounts, ignored legacy fields and identical skill compositions. |
| TST-007 | P0 | Add paired four-player evaluator sanity cases: policy versus itself, dominant vs dominated controls, seat symmetry and confidence calibration. |
| TST-008 | P0 | Add gate-mutation controls: force promotion, edit evaluation report, swap opponent pool, reuse protected seeds, change frozen evaluator. |
| TST-009 | P1 | Add model-proposer provenance tests proving selected genome and reason cannot be confused with the raw LLM proposal. |
| TST-010 | P1 | Add full four-player offline integration: simulate → diagnose → propose → evaluate → reject/promote → replay → audit. |
| TST-011 | P1 | Add controlled false-positive experiments across many null candidates and repeated looks to validate statistical error control. |
| TST-012 | P1 | Add equal-budget end-to-end ablations for deterministic, random, LLM-only and LLM+search proposers. |
| TST-013 | P1 | Add cold-start, interrupted-run, cross-version artifact, missing-lineage and corrupted-memory tests. |
| TST-014 | P2 | Add strategy-transfer fixtures for 2-, 3- and 6-seat modes and tournament-depth variants. |

## 16. Experiment visibility and operator experience

| ID | Pri | Requirement |
|---|---|---|
| UX-001 | P1 | Show the four individual seat identities, private/public information boundary, action sequence, position and stack/pot state in the spectator viewer. |
| UX-002 | P1 | Provide a policy-comparison view with direct action-level differences and EV/uncertainty—not just four parameter values. |
| UX-003 | P1 | Display independent outcomes: game EV, win rate, decision divergence, proposer success, budget, gate verification and opponent breakdown. |
| UX-004 | P1 | Visualize champion genealogy and distinguish model-proposed, search-selected and manually seeded strategies. |
| UX-005 | P1 | Add operator controls for four-seat test matrix, fixed evaluation budget, holdout lock, cancellation and approval. |
| UX-006 | P2 | Provide CSV/JSON export for result matrices and reproducible CI/regret plots. |

## 17. Prioritized execution plan

### Milestone A — Freeze and validate the scientific baseline
**Deliver:** baseline snapshot, existing heads-up behavior preservation, four-seat benchmark contract, and protected evaluation manifest.
**Exit evidence:** baseline regression, code hash, four-seat configuration manifest, negative information-leakage tests, and an independently reviewed experiment protocol.

### Milestone B — Trustworthy four-player evaluation (first real P0 gate)
**Deliver:** multiway paired evaluator, complete seat rotation, heterogeneous baseline opponents, statistical intervals and independent verifier.
1. Introduce focal-seat challenger/incumbent replacement against three frozen opponents.
2. Run balanced four-seat permutations using paired deals and purpose-separated agent randomness.
3. Implement per-opponent/mixed-pool EV and confidence bounds using seed blocks.
4. Protect independent selection and final test sets; define multiplicity and sample stopping.
5. Add self-play null controls, negative policy controls and reproducible replay evidence.
**Exit evidence:** a known stronger scripted agent defeats a known dominated agent on pre-registered four-seat data; the same policy compared with itself reports zero difference; a forced-false promotion is refused. No LLM required.

### Milestone C — Meaningful poker policy and equity
**Deliver:** range/multiway equity, position-aware preflop policy, street-specific postflop policy, rational bet sizing, and action traces.
**Exit evidence:** meaningful improvements on pre-registered poker situations, measurable out-of-sample EV, and no performance claim based on the original overfolding champion alone.

### Milestone D — Diagnostically useful autonomous refinement
**Deliver:** causal/uncertain failure signals, action-level proposals, a diverse policy pool, and controlled non-LLM search.
**Exit evidence:** refinement beats at least one equal-budget deterministic/random control on untouched, repeated four-player tasks or honestly records a null result.

### Milestone E — LLM attribution and meta-learning
**Deliver:** LLM-only and LLM+search arms, truthful provenance, cross-task proposer promotion and maintained safety boundaries.
**Exit evidence:** repeated held-out improvement of the *proposal algorithm* over a strong no-LLM control, not merely one champion defeating a weaker incumbent. If not achieved, keep the LLM experimental.

### Milestone F — Productization of the benchmark
**Deliver:** usable four-player viewer, reproducible run bundles, profile/version management, retention, checkpoints and observability.

## 18. Objective success metrics and promotion contract

| Goal | Primary metric | Required check | What **does not** count |
|---|---|---|---|
| More competent poker policy | Paired four-player bb/100 difference | Held-out lower confidence bound above minimum practical effect, plus anchor-pool non-regression | Raw win rate, one lucky session, winning against the old overfolding bot |
| Better strategic generalization | Cross-opponent payoff and worst material regression | Opponents unseen during search, all seats rotated | New deck seeds against exactly the same exploitable bot |
| Better refinement | Verified EV improvement per proposal/simulation budget | Equal-budget random/grid/refinement ablation | Larger decision divergence or more policy versions |
| Added value from LLM | Hybrid or LLM-only incremental improvement over a no-LLM control | Same candidate/simulation budget and same untouched evaluation tasks | Grid search discovering a previously known winning region |
| Better meta-learning | Final policy EV and hit-rate gains across fresh starting tasks | Untouched meta-holdout, bounded costs, no degradation | Evolving a proposer step size without any useful promotions |
| Reliable harness | Replay/verification success, rejected unsafe changes, cost | Deterministic evidence and adversarial failure tests | A successful process exit with unverifiable or misleading artifacts |

**Evaluation protocol (minimum):**
1. Fix a primary four-seat population and an anchor population with diverse opponent policies.
2. Define independent seed blocks, balanced seat permutations, sample-size/power plan, minimal practical effect and primary uncertainty method *before* generating candidates.
3. Use training blocks for diagnosis and search blocks for candidate selection. Neither may access the final promotion block or held-out opponent policy identities.
4. Compare the chosen candidate with the incumbent by paired focal-seat replacement against identical frozen opponents. Aggregate chip differences per seed block, with a block-level confidence interval.
5. Require a positive primary lower confidence bound over the practical-effect margin, adequate effective sample size, and anchor-pool non-regression; correct for repeated candidate tests or reserve an untouched one-use final audit.
6. Independently recompute the complete report and verify strategy, evaluator, policy, opponent and seed manifests. Only then mark the candidate eligible.
7. Maintain an explicit `rejected / inconclusive / verified-improvement / unsafe / unverified` decision taxonomy.

## 19. Definition of done

A requirement is `Done` only when it has all of:
- A stable contract/schema and traceability to source changes.
- Unit/property tests, with negative controls where relevant.
- End-to-end evidence from real four-player simulator execution for behavioral claims.
- Deterministic source/manifest/seed hashes and a cold-run reproduction command.
- Demonstrated information/permissions isolation and no regression in legacy paths.
- An explicit result under its declared objective, including a truthful null/rejected outcome.
- Independent verification of any promotion, with no model-generated claim accepted as an oracle.

### Mandatory release claims
- **R1 — Correct four-player benchmark:** A reproducible and independently verified evaluator with multiway equity, safe observations, coherent opponent pools and statistical promotion.
- **R2 — Useful strategy search:** More expressive policies plus measurable improvement on unseen opponents; deterministic search baselines are reported.
- **R3 — LLM value demonstrated:** Equal-budget experiments show an incremental contribution beyond deterministic search. If not, do not advertise LLM-driven strategic learning.
- **R4 — Harness learning demonstrated:** The proposer itself improves over multiple independent tasks/starting strategies with lower-bound EV gains and bounded cost. If not, do not advertise recursive self-improvement.

## 20. Suggested first twelve implementation tickets

1. **[P0] Four-seat evaluation contract** — `GameConfiguration`, focal seat, independent-hand semantics (`SIM-001`–`SIM-005`).
2. **[P0] Observation truthfulness** — per-seat action/stack information, actual VPIP and missing values (`OBS-001`–`OBS-005`).
3. **[P0] Four-seat opponent pool** — five distinct frozen archetypes and mixture manifests (`OPP-001`–`OPP-003`).
4. **[P0] Paired multiway evaluator** — balanced positions and real bb/100 difference (`EVA-001`–`EVA-005`).
5. **[P0] Statistical v2 promotion gate** — block confidence, protected sets and independent verification (`EVA-006`–`EVA-011`, `ART-004`).
6. **[P0] Equity contract separation** — forbid hand-category score as equity; implement 4-way pot-share estimation (`EQ-001`–`EQ-005`).
7. **[P0] Policy semantic correctness** — position ranges, per-street decision policy and pot-relative sizing (`POL-001`–`POL-008`).
8. **[P0] Semantic no-op gate** — sizing plateau/skill override fixes and action fingerprints (`MUT-001`–`MUT-004`).
9. **[P0] Failure-label repairs** — decision-time EV, genuine fold-to-raise and missing denominators (`DIA-001`–`DIA-005`).
10. **[P0] Candidate attribution** — model seed vs search selected policy and immutable causal lineage (`LLM-001`–`LLM-005`).
11. **[P1] Search-ablation experiment** — randomized/grid/LLM/hybrid at equal budget (`MUT-008`, `LLM-010`, `TST-012`).
12. **[P1] Proposer-level generalization** — cross-task meta-holdout and guarded meta-promote (`META-001`–`META-009`).

## 21. Reference sources in the reviewed branch

- `WIP/Wip.Tholdem.Agents/Learning/StrategyAgent.cs`
- `WIP/Wip.Tholdem.Agents/Baselines/HandStrength.cs`
- `WIP/Wip.Tholdem.Agents/Equity/MonteCarloEquity.cs`
- `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs`
- `WIP/Wip.Tholdem.Agents/Learning/MatchEvaluator.cs`
- `WIP/Wip.Tholdem.Agents/Learning/Refinement/FailureSignatureExtractor.cs`
- `WIP/Wip.Tholdem.ModelHarness/LlmRefinementPolicy.cs`
- `docs/goal-llm-harness/ponder-loop/experiments-3.md`, `preregistration-c.md`
- `docs/goal-tholdem-harness/final-report.md`, `docs/tholdem/known-limits.md`
