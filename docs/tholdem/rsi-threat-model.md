# Mini-RSI threat model — A1…A15

Scope: the UI-triggered training API (`WIP/Wip.Tholdem.Web/Training/**`), the bounded meta-loop
(`WIP/Wip.Tholdem.Agents/Learning/Meta*.cs`, `FrozenComponents.cs`, `ProposerGenome.cs`,
`LedgerChain.cs`) and their persistence under one data directory. The evaluator, promotion policy,
verifier and seed derivation are frozen and hash-pinned; the engine, simulator and history formats are
out of scope and untouched.

**Adversary model.** A local operator (or untrusted browser content) can call the loopback HTTP API,
write files in the data directory, supply a `strategyId`, a seed and a prompt template, and observe
every artifact. The adversarial strategy text is data. The adversary cannot sign builds, cannot edit
the running process's memory, and cannot change the frozen code without also editing the manifest
constant (A14 states the residual). No network, money, publish or model execution is in scope.

**Severity.** `P0` = a false promotion or a forged audit trail is accepted. `P1` = the statistics can
be gamed into a misleading verdict, or a budget/seed defense fails. `P2` = a quality, hygiene or
reproducibility gap with no direct false promotion.

The committed controls are in `WIP/Wip.Tholdem.Tests`. Mutation evidence (defense removed →
focused control observed failing; original restored and SHA-256 checked) is in
[`docs/goal-rsi/round-1/mutation-evidence.md`](../goal-rsi/round-1/mutation-evidence.md).

## A1 — Reward hacking against baseline agents

- **Severity:** P1.
- **Attack:** optimize the proxy (mean bb/100 against one fixed `TightAggressiveAgent`) rather than
  poker; the loop can report a "better" strategy that only exploits this baseline or the sampled
  deals.
- **Why the design is vulnerable:** the objective is a fixed-baseline held-out mean. There is no
  external benchmark, no multi-opponent panel and no real-money evaluation. A proposer that learns
  "beat this one bot" is not shown to be a better poker player.
- **Defense:** the promotion verdict never reads training reward, a self-reported score or a
  win-rate alone. It reads a paired held-out `EvaluationReport` and requires `LCB > margin` with
  `seeds >= minSeeds`; the verifier re-derives that decision from the report. The report, not the
  proposer, names the metric.
- **Control test:** there is no dedicated reward-hacking fixture because the attack is not a
  code-path bug: the existing `PromotionGateTests` and `TrainingPromoteApiTests` prove that only the
  held-out report and its frozen verifier can move the pointer; the training reward
  (`SessionAnalysis.BbPer100`) is never passed to `StatisticalPromotionPolicy`.
- **Residual:** **not fully preventable.** A single-baseline proxy can be over-fitted and is not a
  poker-strength claim. This is stated in `rsi.md §9`; the honest claim is "beats this baseline on
  these paired seeds", not "plays good poker".

## A2 — Evaluation leakage, seed mining, p-hacking across iterations

- **Severity:** P1.
- **Attack:** reuse the same base seed with different iteration counts/margins until a favourable
  report appears; reuse a training seed as an evaluation seed; make meta seeds overlap inner seeds.
- **Why the vulnerable surface exists:** the deal is deterministic from the seed, and the loop exposes
  a seed in every request. Without a register, the same deals could be re-rolled under different
  options.
- **Defense:**
  1. the persisted `training-seeds.json` register refuses a seed reused with a different options hash
     (`409 seed-reuse`);
  2. meta seeds use dedicated hand bases/streams (`20000/30000/40000/45000`, streams 7/11) and
     `MetaSeedDerivation.EpisodeSeedSet` throws if any meta and inner seed collide;
  3. evaluation seeds are derived inside the loop and never accepted from the request.
- **Control tests:** `TrainingJobApiTests.Post_GivenSeedReuseWithDifferentOptions_Expected409`;
  `MetaLoopTests.MetaSeedDerivation_GivenJobSeed_ExpectedDistinctDeterministicDomains`.
- **Residual:** a determined operator can use a fresh seed for every attempt; the register prevents
  *reuse*, not *many independent tries*. The per-job cost and the fixed criteria limit, but do not
  eliminate, multi-testing across seeds.

## A3 — Gate erosion (margin/minSeeds lowered, verifier bypassed, policy swapped)

- **Severity:** P0.
- **Attack:** pass `AlwaysPromotePolicy` or a custom policy, lower `margin`/`minSeeds`, or mutate the
  verifier so the loop promotes without evidence.
- **Why vulnerable:** the loop accepts an `IPromotionPolicy` for the negative control, and the
  criteria are request options.
- **Defense:** the loop always calls `PromotionVerifier` after the policy; an unjustified promotion is
  rewritten to `verification-failed` and no pointer moves. Request bounds keep `margin >= 0` and
  `minSeeds >= 2`. The promote endpoint rebuilds criteria from the job and re-runs policy +
  verifier; it never accepts a decision from the body. `AlwaysPromotePolicy` is the committed
  negative control.
- **Control test:** `ThreatControlTests.AlwaysPromoteControl_GivenReportBelowGate_ExpectedVerificationFailedAndNoPointerMove`
  (plus the pre-existing `PromotionGateTests`).
- **Mutation evidence:** making the verifier accept a promoted decision unconditionally turns this
  control (and the existing gate tests) red.

## A4 — Ledger tampering or rewriting

- **Severity:** P0.
- **Attack:** edit `ledger.json` (or `meta/meta-ledger.json`) to add a promotion, hide a rejection,
  or rewrite a metric.
- **Why vulnerable:** the training ledger is a file the loop rewrites on every run; an unanchored
  ledger has no way to notice an edit.
- **Defense:** every ledger entry carries `previousHash` and `entryHash` (see `LedgerChain`); the
  loop verifies the existing chain before appending, the promote endpoint verifies it before writing
  the pointer, and the meta ledger uses the same construction. The artifact/report files are
  content-addressed and the promote path recomputes the report.
- **Control tests:** `ThreatControlTests.Ledger_GivenTamperedEntry_ExpectedIntegrityFalseAndNextRunRefused`;
  `ThreatControlTests.MetaLedger_GivenTamperedEntry_ExpectedIntegrityFalseAndNextRunRefused`.
- **Residual:** a hash chain is tamper-**evident**, not tamper-proof. Anyone who can write the file
  can recompute the chain because the construction is public and unsigned. Truncating the tail is not
  detected without an external anchor. No signing key is delivered.

## A5 — Self-referential / echo-chamber evaluation

- **Severity:** P1.
- **Attack:** have the challenger be the champion (or behave identically), so the report is
  zero-variance and a naive `>=` gate could promote a no-op.
- **Why vulnerable:** "0 difference" can pass a non-strict comparison; an inert proposer can loop
  forever without changing the champion.
- **Defense:** (1) `StrategyGenome.BehaviorEquals` and the inert-proposal guard reject a numeric
  no-op before evaluation; (2) the meta-loop refuses when every candidate episode ends on the same
  champion genome as its incumbent episode (`champion-vs-itself`), before evaluator time; (3) the
  gate uses strict `LCB > margin` and requires `seeds >= minSeeds`, so a self-comparison (LCB 0,
  margin 0) fails. A5's proposer-level analogue is the `identical-proposer` refusal in the meta-loop.
- **Control tests:** `MetaLoopTests.Run_GivenIdenticalFinalChampions_ExpectedSelfComparisonRefusal`;
  `MetaLoopTests.Run_GivenIdenticalProposer_ExpectedInertRefusalBeforeAnyEpisode`;
  `ThreatControlTests.InertProposal_GivenGuard_ExpectedNoEvaluationFileAndInertOutcome`.
- **Residual:** two different genomes can still be behaviourally equivalent over the sampled deals;
  the guard catches parameter-identical proposals before evaluation, not "same actions on these
  deals" in general.

## A6 — Silent regression on a frozen benchmark after promotion (ratchet)

- **Severity:** P1.
- **Attack:** promote a proposer that wins the meta selection sample but loses on a fresh sample;
  the champion then regresses without anyone noticing.
- **Why vulnerable:** any selection over a finite seed set can overfit; the meta-loop's own
  comparison seeds are used for selection.
- **Defense:** a **fresh holdout** derived from disjoint streams (episode 40000/stream 11, comparison
  45000/stream 11) is evaluated after the meta gate and before eligibility; the candidate is refused
  unless `fresh LCB >= -margin` (non-inferiority ratchet). Strategy promotions are additionally
  checked against the active champion at promote time (`champion-changed` refuses a stale rollback).
- **Control test:** `MetaLoopTests.Run_GivenCandidateWinsOnMetaSeedsButLosesFreshHoldout_ExpectedRefusal`.
- **Residual:** the fresh holdout is still finite and re-used for future meta-runs unless the job seed
  changes; a long campaign can overfit the whole public seed space. The interval, not a claim of
  certainty, is the defense.

## A7 — Inert or degenerate proposals

- **Severity:** P2.
- **Attack:** submit a proposer/strategy whose only change is prompt text or a parameter that cannot
  change an action, burning evaluation budget and creating meaningless artifacts.
- **Why vulnerable:** the genome has a prompt field the agent never reads; a naive proposer can
  return an identical genome.
- **Defense:** `ProposalGuards.RejectBehaviorallyInert` compares the four numeric parameters before
  the evaluation phase; the iteration is recorded as `inert-proposal` with a null report and the
  ledger row carries that outcome. The meta-loop rejects a proposer whose
  `MutationStep`/`ParameterSelectionPolicy`/`EvaluationBudgetShare` are unchanged, before any
  episode. The proposer itself falls back to a threshold flip when clamping would absorb its move.
- **Control test:** `ThreatControlTests.InertProposal_GivenGuard_ExpectedNoEvaluationFileAndInertOutcome`
  (guard on → no evaluation file; same proposer without the guard → evaluation file). See also
  `MetaLoopTests.Run_GivenIdenticalProposer_...`.
- **Residual:** behaviourally inert proposals (different parameters, identical actions on the sampled
  deals) are only detected statistically by the zero-effect report; the 0.000 mean observed with the
  shipped champion is recorded in `rsi.md §9`.

## A8 — UI bypass (promote without server verification)

- **Severity:** P0.
- **Attack:** call the promote endpoint without confirmation, with a hand-written body claiming
  success, or after editing the stored report; treat the browser dialog as authorization.
- **Why vulnerable:** any authorization that exists only in JavaScript is advisory.
- **Defense:** the endpoint requires `confirm:true`, then independently re-reads the report, checks
  canonical form and ids, compares its hash with the in-memory completion record, recomputes it from
  the persisted artifacts and seeds with the frozen evaluator, re-runs the policy and verifier,
  verifies the ledger chain and refuses a stale champion. The UI button is only rendered for a
  completed, promotable job and uses `window.confirm`, but the server does not trust it.
- **Control tests:** `TrainingPromoteApiTests.Promote_GivenNoConfirm_Expected409ConfirmationRequired`;
  `...Promote_GivenTamperedReport_Expected409ServerReverification`;
  `...Promote_GivenReportForAnotherChallenger_Expected409JobMismatch`;
  `...Promote_GivenUnverifiedJob_Expected409NotPromotable`.
- **Mutation evidence:** the campaign removes the `confirm` check (row A8) and the first control
  goes red. The tamper/mismatch controls additionally depend on the canonical/hash/recompute steps;
  those steps are asserted directly by the four control tests, but the campaign did not mutate each
  of them separately.

## A9 — Prompt injection through strategy text or history into the LLM agent

- **Severity:** P1.
- **Attack:** put instructions in `StrategyGenome.PromptText` or `ProposerGenome.PromptTemplate` that
  a downstream LLM agent might obey; or make the learning loop send text to a model.
- **Why vulnerable:** the product contains an LLM runtime agent elsewhere; a future integration could
  treat the stored prompt as instructions.
- **Defense:** in the learning layer the prompt is **data only**. `StrategyAgent` never reads
  `PromptText`; `PromptTemplateRenderer` performs literal substitution only; `ProposerGenome`
  sanitises control characters and caps length at 2000; `LearningSourceSafetyTests` scans every
  learning source for `Process`, `HttpClient`, `Assembly.Load`, `Compile`, `CSharpScript` and
  `CodeDom`; no model call exists on the training path. The meta-loop rejects a prompt-only candidate
  before evaluation.
- **Control test:** `ThreatControlTests.StrategyAgent_GivenPromptInjectionText_ExpectedBehaviouralEquivalence`
  (prompt-only difference evaluates to exactly zero and the learning sources contain no execution or
  network path), plus `MetaLoopTests.PromptTemplateRenderer_...` and the pre-existing
  `LearningSourceSafetyTests`.
- **Residual:** **not fully preventable as a product claim.** If a future version feeds the strategy
  prompt to the runtime LLM agent, injection becomes live. The current defense is architectural
  absence, which the test pins; it cannot make arbitrary future text safe.

## A10 — Runaway compute, DoS, cancellation leaving partial state

- **Severity:** P1.
- **Attack:** request the maximum bounds repeatedly, or cancel at an inconvenient moment to leave a
  half-written evaluation or a promoted pointer.
- **Why vulnerable:** a synchronous learning loop behind an HTTP API can pin a CPU; cancellation that
  is only checked at the start of an iteration can promote after the operator asked to stop.
- **Defense:** one job per process; bounded request options; a single background consumer; meta
  budgets (`maxInnerIterations`, `maxEvaluationsPerEpisode`, wall clock); cancellation is checked at
  every phase boundary and before any persist, so a cancelled iteration writes no artifact and cannot
  promote. A meta-loop promotion is never automatic; on cancellation the pointer is unchanged.
- **Control tests:** `MetaLoopTests.Run_GivenInnerBudgetTooSmall_ExpectedBudgetExceptionAndPointerUnchanged`;
  `MetaLoopTests.Run_GivenCancellationAfterFirstEpisode_ExpectedNoPointerChange`;
  `TrainingJobApiTests.Delete_GivenRunningJob_ExpectedCancelledWithoutChampionChange`;
  `TrainingJobApiTests.Post_GivenSecondJobWhileOneRuns_Expected409`.
- **Residual:** a single long evaluation call (up to 500 seeds × 50 hands × 2 matches) is not
  interrupted mid-call; cancellation takes effect at the next phase. The wall-clock budget is checked
  between phases, not inside the simulator.

## A11 — Weak-seed inference amplification during long training runs

- **Severity:** P2.
- **Attack:** use a guessable seed and a long run so an observer can enumerate the deal from its own
  cards and predict/exploit subsequent hands.
- **Why vulnerable:** every hand's deal is deterministic from the base seed; more observed hands
  narrows the candidate set.
- **Defense:** the existing `2^40` minimum seed guard is preserved for `POST /api/training` and
  `POST /api/rsi`; opting into a weak seed is explicit and **caps the run at 5 iterations** (training)
  or **2 meta-iterations** (mini-RSI); the job snapshot exposes `weakSeed:true`.
- **Control test:** `TrainingJobApiTests.Post_GivenWeakSeedWithoutOptInOrOverTheCap_Expected400`.
- **Residual:** the guard is a threshold, not a proof; an opted-in demo remains inferable. The same
  documented limitation as `docs/tholdem/known-limits.md`.

## A12 — Reproducibility drift

- **Severity:** P2.
- **Attack:** make the same options produce different artifacts (clock in an id, culture-dependent
  formatting, ordering drift), so a verdict cannot be reproduced or audited.
- **Why vulnerable:** jobs carry an acceptance timestamp; artifact writers could accidentally include
  it; JSON number formatting is culture-sensitive by default.
- **Defense:** artifacts are canonical JSON with invariant `R`/`G` formatting and no clock; the
  options hash and job id are clock-free; the ledger chain is deterministic; tests run the same
  options in two fresh data directories and compare SHA-256 for every JSON file.
- **Control test:** `TrainingJobApiTests.Post_GivenSameOptionsInTwoDirectories_ExpectedIdenticalArtifactHashes`.
- **Mutation evidence:** injecting a clock value into a persisted artifact makes that test red.
- **Residual:** the accepted-at timestamp and the seed register are intentionally not reproducible;
  they are operational metadata, excluded from the artifact comparison.

## A13 — Metric shopping

- **Severity:** P1.
- **Attack:** after seeing results, choose a favourable metric (win rate, mean, upper bound) or
  redefine the decision.
- **Why vulnerable:** many metrics are available in a `SimulationReport`.
- **Defense:** the metric is pre-registered structurally: `EvaluationCriteria` fixes the z quantile;
  `StatisticalPromotionPolicy` decides only on `LowerBoundBbPer100` and `Seeds`;
  `PromotionVerifier` re-derives exactly those; the promote endpoint never reads a client metric; the
  job/ledger `decision` strings are policy reasons, not a choice. The meta hit-rate improvement and
  the fresh ratchet are constants in the request/job, not chosen after the fact.
- **Control test:** the gate tests (`PromotionGateTests`, `TrainingPromoteApiTests`) and the literal
  `MetaLoopTests.HitRateCalculator_GivenLiteralInputs_ExpectedExactNormalInterval`.
- **Residual:** **not fully preventable by a test.** A human can request a different margin, seed set
  or baseline and report only the favourable run; the seed register and the frozen formula raise the
  cost but cannot prevent selective reporting across jobs. The honest statement is in `rsi.md §9`.

## A14 — Frozen-component tamper (manifest mismatch)

- **Severity:** P0.
- **Attack:** change `EvaluationCriteria`, the policy, the verifier, the evaluator or the seed
  derivation so the gate is easier to clear, then run the loop.
- **Why vulnerable:** all are ordinary source files in the same assembly; source access is local.
- **Defense:** `FrozenComponents.Capture()` hashes canonical source text (when reachable) and a
  deterministic behaviour probe set; the pinned manifest is versioned (`tholdem-rsi-frozen-v1`,
  manifest `95e010703d8ef7cd4d7cc2fae4ca839f7dd80b4c99b7645c7ac3d111ca748a5d`); the meta-loop
  re-captures and refuses before every iteration and before every promote; the promote endpoint
  re-checks the live manifest against the document.
- **Control tests:** `ThreatControlTests.FrozenComponents_GivenLiveCapture_ExpectedPinnedMatchAndSourcePin`;
  `...GivenTamperedBehaviorHash_ExpectedMismatchReason`;
  `MetaLoopTests.Run_GivenFrozenManifestMismatch_ExpectedRefusedBeforeEpisodes`.
- **Residual:** an attacker who edits a frozen source **and** `FrozenComponents.Pinned` in the
  non-frozen manifest file, then rebuilds, defeats the check. There is no signature chain or external
  attestation in this delivery. The next step would be a signed build artifact; that is out of scope
  and stated here rather than implied.

## A15 — Meta-overfitting

- **Severity:** P1.
- **Attack:** a proposer that wins on the meta seeds used for selection but loses on fresh seeds.
- **Why vulnerable:** the meta comparison is a selection over a finite meta sample; the proposer
  space is small but the sample can be overfit.
- **Defense:** the fresh holdout and non-inferiority ratchet from A6, plus the pre-registered hit-rate
  interval and the same frozen gate. Eligibility is recomputed from the persisted document by
  `MetaDecisionVerifier` at promote time.
- **Control test:** `MetaLoopTests.Run_GivenCandidateWinsOnMetaSeedsButLosesFreshHoldout_ExpectedRefusal`.
- **Mutation evidence:** removing the fresh-holdout clause makes that control eligible (red).
- **Residual:** two-stage selection still consumes sample information; a sufficiently long campaign
  can overfit both stages. No claim of generalisation beyond the evaluated seed space is made.

## Mutation evidence (defense removed -> focused control fails)

Every row below was produced by `docs/goal-rsi/round-1/run-mutations.ps1`: the original file bytes
were saved, one defense was removed with a literal one-line edit, the test project was rebuilt in
Release, the focused control type was run, and the original bytes were restored and compared by
SHA-256. The full transcript is in
[`docs/goal-rsi/round-1/mutation-evidence.md`](../goal-rsi/round-1/mutation-evidence.md).

| A# | Defense removed | Focused control | Observed after removal | Restored |
|---|---|---|---|---|
| A2 | `TrainingJobRegistry` seed-reuse admission | `TrainingJobApiTests` | `Post_GivenSeedReuseWithDifferentOptions_Expected409` failed; `9 PASSED 1 FAILED` | SHA-256 match |
| A3 | `PromotionVerifier.Verify` margin/seed checks forced true | `ThreatControlTests` | `AlwaysPromoteControl_...` failed; `4 PASSED 5 FAILED` | match |
| A4 | `LedgerChain.Verify` returns success immediately | `ThreatControlTests` | `Ledger_GivenTamperedEntry_...` failed; `7 PASSED 2 FAILED` | match |
| A5 | `MetaLoop` self-comparison refusal disabled | `MetaLoopTests` | `Run_GivenIdenticalFinalChampions_...` failed; `11 PASSED 1 FAILED` | match |
| A6/A15 | `MetaLoop` fresh-holdout clause disabled | `MetaLoopTests` | `Run_GivenCandidateWinsOnMetaSeedsButLosesFreshHoldout_...` failed (decision not reproducible); `11 PASSED 1 FAILED` | match |
| A7 | `ProposalGuards` behavior check disabled | `ThreatControlTests` | `InertProposal_GivenGuard_...` failed (report not null); `8 PASSED 1 FAILED` | match |
| A8 | promote `confirm` check disabled | `TrainingPromoteApiTests` | `Promote_GivenNoConfirm_Expected409ConfirmationRequired` failed; `4 PASSED 1 FAILED` | match |
| A10 | `MetaBudget.ConsumeEpisode` throw disabled | `MetaLoopTests` | `Run_GivenInnerBudgetTooSmall_...` failed (no exception); `11 PASSED 1 FAILED` | match |
| A11 | weak-seed iteration cap disabled | `TrainingJobApiTests` | `Post_GivenWeakSeedWithoutOptInOrOverTheCap_Expected400` failed; `9 PASSED 1 FAILED` | match |
| A12 | clock value injected into ledger entries | `TrainingJobApiTests` | `Post_GivenSameOptionsInTwoDirectories_ExpectedIdenticalArtifactHashes` failed; `9 PASSED 1 FAILED` | match |
| A14 | `FrozenComponents.Matches` returns true immediately | `ThreatControlTests` | `FrozenComponents_GivenTamperedBehaviorHash_...` failed; `8 PASSED 1 FAILED` | match |

**A1, A9 and A13 have no mutation row** because their residual is not a removable code branch:
A1 is the proxy-vs-poker gap (no external benchmark exists); A9 is architectural absence that a
future integration could break (the current source/test pin is the evidence); A13 is selective
reporting across independent jobs, which no single test can prevent. Each is marked *not fully
preventable* above rather than claimed as solved.

## Verification snapshot for this delivery

- Supplementary runner over the compiled assembly after all mutation restores:
  `TOTAL 317 PASSED 317 FAILED 0 SKIPPED 0` (275 pre-existing + 42 new).
- Frozen manifest after all restores:
  `95e010703d8ef7cd4d7cc2fae4ca839f7dd80b4c99b7645c7ac3d111ca748a5d`, `sourcePinned=true`,
  all five source and behaviour hashes matching the pinned constant.
- Real Training-tab run: completed with `mean 1.05`, `LCB 0.321`, `promoted`; the browser promote
  step returned `Already promoted: tholdem-strategy-v2-f7a0e101 (server verified)`; three-entry
  hash-chained ledger verified (`head=45fe1998…`); champion report hash `7a44736a…`.
- Same options in two fresh data directories: identical `jobId`
  `train-3fe2cf5f297514bc`, identical `optionsHash`, and identical SHA-256 for all seven persisted
  JSON artifacts (`identical=True`).
## What this loop cannot honestly claim

1. **Poker strength.** The metric is a fixed-baseline proxy. A1 is not fully preventable.
2. **Discovery of skill from the shipped champion.** The observed default-proposer evaluations were
   exactly `0.000`; the recorded promotion demo starts from an explicit `0.70/0.70` artifact.
3. **Prompt quality.** The prompt template is never evaluated; prompt-only changes are rejected as
   inert before spending evaluation.
4. **Unbounded self-improvement.** The meta-loop is capped on every axis and changes one small
   proposer field per iteration.
5. **Tamper-proof audit.** The ledger hash chain is unsigned; A4's residual is a re-chainer.
6. **Frozen-code attestation.** A14's residual is source + constant tamper in a rebuilt binary.
7. **CI green.** CI is not observable from this machine; only the local supplementary runner is
   claimed.
8. **A general-purpose RSI system.** This is a bounded search over strategy data and a four-field
   proposer policy under a frozen statistics gate, not a general self-improving agent.
