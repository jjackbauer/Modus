# Final report — Tholdem model-in-the-loop harness (goal-llm-harness)

> **Outcome: complete.** All nine definition-of-done dimensions are Covered. D6 is resolved by the authorized parallel variant round: the `both-required` prompt produced **54 live tool calls** with recorded results and 61 legal actions, and the D6 test now passes against that session. The pre-registered LLM refinement arm produced a real model-driven run with **no verified promotion** (an honest no-promotion result). The deterministic mode, the five frozen components, the legacy hashes and the secret hygiene are unchanged. Every claim below traces to an `EVD` row in [evidence.md](evidence.md).

## What was delivered

| Work package | Delivery | Evidence |
|---|---|---|
| WP-1 | `TrainingLoop.Run` optional `IRefinementPolicy` seam; the default path constructs today's policy lazily | EVD-017, EVD-023 |
| WP-2 | `Wip.Tholdem.ModelHarness`: canonical transcript record/store, redaction, proposal parser/clamp, replay; the ten live-arm transcripts replay offline onto the persisted challengers | EVD-024, EVD-027, EVD-064 |
| WP-3 | `LlmPromptBuilder` (M-02 sections and bounds) and `LlmRefinementPolicy` (gate refusal, one repair, budget, failure recording) | EVD-025, EVD-026, EVD-028 |
| WP-4 | `LiveModelConfiguration` + allowlisted `.secrets/deepseek.env` loader; missing env refuses before any request | EVD-032, EVD-051 |
| WP-5 | Pre-registered LLM and deterministic arms through the frozen gate; scratch arm runner and verify mode | EVD-031 to EVD-035, EVD-046 |
| WP-6 | Live 20-hand Ponder matches over `TholdemRuntimeAgent`: pre-change, option A, and the authorized parallel variants; the winning `both-required` run made 54 tool calls with results, and the tool-invocation path is also proven offline with a scripted tool call | EVD-041 to EVD-044, EVD-062, EVD-072, EVD-074 |
| WP-7 | 22-test plan, behavior-proof gate, final regression, this report | EVD-045 to EVD-060 |

## The pre-registered experiment

Both arms started from the documented `0.70/0.70/0.12/1.0` demo champion in fresh directories, seed `1099511927776`, 10 iterations, 2000 training hands, 50 evaluation seeds, 20 hands per evaluation match, and the unchanged `EvaluationCriteria(MarginBbPer100: 5.0, MinSeeds: 24)`.

| Arm | Model calls | Promotions | Best LCB (bb/100) | Best mean (bb/100) | Refusal |
|---|---:|---:|---:|---:|---|
| `llm-refinement` | **10 live** | 0 | **1.6233195816931527** (iteration 6) | **50.03** (iteration 6) | `lcb-below-margin` |
| `deterministic-refinement` | 0 | 0 | **2.396597390544052** (iteration 2) | **7.7** (iteration 10) | `lcb-below-margin` |

**The pre-registered success rule — at least one verified promotion in the LLM arm — was not met.** The gate was not lowered, the arm was not rerun, and no deterministic arm was substituted.

What makes the LLM arm auditable rather than a story:

- Ten real requests, all requested `deepseek-chat` and served `deepseek-flash`, 6213 prompt + 800 completion tokens, 1117–2951 ms each, every transcript `outcome=ok`, no repair.
- Every persisted challenger equals the parsed model proposal (`candidateMatchesProposal=true` for all 10 in the verify output). The transcripts and the two pointer copies are mirrored byte-identically under [round-3/live-artifacts/](round-3/live-artifacts/) ([mirror manifest](live-artifacts-mirror.md)).
- Every verdict re-derived from the persisted evaluation file with `StatisticalPromotionPolicy` and `PromotionVerifier`; the report hash matches the file; the champion pointer is byte-identical before and after (`ba24279777d1c52c3219fef7e9184df0e15313d2a6cc28cfd2c96868358ad805`).
- One earlier invocation was refused with `live-model-disabled` before any request because the operator opt-in flag was missing; that attempt is preserved in [round-3/llm-arm-attempt0-refused.log](round-3/llm-arm-attempt0-refused.log).

## Why the model did not promote

The gate is reachable from this start. A post-hoc fixture hunt at a **different, test-only seed** (20261109) through the same loop shows that tight genomes promote:

| Genome | 24 seeds × 20 hands | 50 seeds × 20 hands |
|---|---|---|
| `0.80/0.40/0.10/1.0` | **promoted**, LCB 10.87 | promoted, LCB 13.16 |
| `0.90/0.20/0.08/1.0` | promoted, LCB 15.59 | promoted, LCB 23.00 |
| model-like `0.60/0.55/0.18/1.15` | rejected, LCB −92.21 | rejected, LCB −34.50 |

The ten model proposals instead *loosened* the incumbent (raise 0.55–0.62, call 0.45–0.62) in response to a `fold-to-raise` signal. The honest reading: at this prompt and this model, the refiner's strategic direction was the limiting factor, not an impossible gate. The fixture is test infrastructure, not a second arm; the pre-registered arm result stands.

## The Ponder runs and the D6 resolution

| Field | Measured |
|---|---|
| Hands | 20 |
| Model turns | **33** |
| Legal model actions | **33** (0 fallbacks, 0 violations, 0 errors) |
| **Tool calls** | **0** |
| Served model | `deepseek-flash` |
| Latency | 773–1788 ms per call |
| Tokens | 43,547 prompt / 175 completion |
| Transcript | 33 canonical records, hash-validated; durable copies under [round-4/live-artifacts/](round-4/live-artifacts/) |

**Pre-change run (unchanged agent).** The recorded session and its 33 canonical transcripts are mirrored byte-identically under [round-4/live-artifacts/](round-4/live-artifacts/). The model played a varied, multi-street match — 6 folds, 18 checks, 7 calls, 1 bet and 1 raise across preflop, flop, turn and river — and still never asked a tool. (An earlier draft called this a fold-only policy; the recorded action tally falsified that claim, and the final report carries the measured mix.) A separate live capability probe shows the missing tool call is not a transport limitation: with the four catalog tools captured on the wire, one request returned exactly one tool call, `pot-odds {"pot":100,"amountToCall":25}`. The runtime agent also forwards the catalog tools on every turn. The fixed `TholdemRuntimeAgent` system prompt ("Reply with exactly one JSON object and no other text") plus the one-action task yields direct answers with no tool turn.

### Authorized option A and the second live round

On 2026-11-09 the operator authorized option A and a second live round. One invitation sentence was added to the fixed `TholdemRuntimeAgent.SystemPromptText` (before hash `29673953…`, after `a7dcfe6b…`); the tools, permissions, executor, memory, transport, refiner and frozen gate were untouched. The fresh seed `1099512027776` was verified unused and the run was pre-registered before execution ([round-10/ponder-rerun-preregistration.md](round-10/ponder-rerun-preregistration.md)).

| Field | Second run (authorized option A) |
|---|---|
| Hands | 20 |
| Model turns | **39** |
| Legal model actions | **39** (Fold 12 / Check 21 / Call 5 / Bet 1 / Raise 1) |
| **Tool calls** | **0** |
| Served model | `deepseek-flash` |
| Latency / tokens | 766–1486 ms per call; 52,954 prompt / 200 completion |
| Streets / hole pairs | 18 / 9 / 9 / 3; 16 distinct pairs |
| Prompt check | the first request's system message contains "You may call at most one of the provided tools" |

The invitation reached the model and it still answered directly on every turn. **Option A alone does not close D6.** The D6 test now reads this authorized run and remains red; the pre-change run is preserved as the baseline.

### Parallel variants and the D6 resolution

The operator then authorized **all remaining approaches in parallel** ("try all the approaches in parallel, winner wins"): three prompt modes on `deepseek-chat` and one cross-model variant, at the same fresh seed `1099512127776`, 20 hands, one attempt each, with a winner rule frozen before execution ([round-11/variant-preregistration.md](round-11/variant-preregistration.md)). The four variants ran concurrently:

| Variant | Mode | Requested model | Turns | Tool calls | Tools | Errors |
|---|---|---|---:|---:|---|---:|
| V1 `task` | task | `deepseek-chat` | 55 | 0 | — | 0 |
| V2 `both` | both | `deepseek-chat` | 53 | 0 | — | 0 |
| **V3 `both-required`** | **both-required** | **`deepseek-chat`** | **61** | **54** | **equity-calculator (44), pot-odds (10)** | **0** |
| V4 `reasoner-both` | both | `deepseek-reasoner` | 57 | 0 | — | 0 |

**Winner: V3 `both-required`** — 54 model-authored tool calls with 54 recorded tool results, 61 legal model actions (Check 35 / Call 10 / Bet 9 / Fold 5 / Raise 2), 106 model calls, 0 fallbacks, 0 violations, 0 errors, 159,964 prompt / 5,026 completion tokens. The mandatory task invitation was present. The optional variants (`task`, `both`) made 0 tool calls; V4 requested `deepseek-reasoner`, was served `deepseek-flash` (recorded as requested/served, no cross-model claim), and made 0.

`BothRequired` is now the default tool-prompt mode in `TholdemAgentOptions`, the Ponder host default, and the D6 test reads the winner's session. The four tools, permissions, executor, memory, transport, the five frozen components and the deterministic mode are unchanged. **D6 is Covered.** The two earlier runs remain as the measured baseline (33 and 39 turns, 0 tool calls each) and the authorization trail.

## Definition of done

| # | Dimension | Status | Evidence |
|---|---|---|---|
| D1 | Supplied policy used; deterministic default byte-identical | **Covered** | EVD-017, EVD-023, EVD-056 |
| D2 | Transcript per model call; replay without a model call | **Covered** | EVD-024, EVD-031, EVD-050, EVD-064 |
| D3 | Real proposal, parse/clamp, one repair, recorded failure | **Covered** | EVD-025, EVD-028 |
| D4 | Missing env refuses before HTTP; secret file; no key leak | **Covered** | EVD-016, EVD-032, EVD-036, EVD-059 |
| D5 | LLM arm through the frozen gate; report and pointer re-derived, or the no-promotion result reported | **Covered** (no promotion reported exactly) | EVD-031, EVD-033, EVD-046, EVD-058, EVD-069 |
| D6 | Live Ponder match with the four tools; actions/tool calls recorded | **Covered** — the authorized `both-required` variant made 54 live tool calls with results and 61 legal actions | EVD-041 to EVD-044, EVD-050, EVD-062, EVD-069, EVD-072, EVD-074, EVD-075 |
| D7 | Deterministic mode, legacy hashes, five frozen components unchanged | **Covered** | EVD-020, EVD-037, EVD-049, EVD-056, EVD-057 |
| D8 | Injection, budget, cancellation and failure without promotion | **Covered** | EVD-047, EVD-048, EVD-051, EVD-063 |
| D9 | Final report states what the model did, cost and promotion outcome, no key, no deterministic substitute | **Covered** | this report |

## Test plan (22 planned tests)

| # | Planned test | Implemented as | Status |
|---|---|---|---|
| 1–5 | policy valid/clamp/malformed/injection/missing-key | `LlmRefinementPolicyTests`, `LlmProposalParserTests`, `LlmPromptBuilderTests` | green |
| 6–7 | one call per iteration; frozen gate verdict | `TrainingLoopLlmPolicyTests` | green |
| 8 | verified pointer move / invalid leaves bytes | `PromotionPointerTests` | green |
| 9 | model failure, no promotion, recorded error | `ModelFailureTests` | green |
| 10 | cancellation, no pointer change, no persisted promotion | `CancellationTests` | green |
| 11 | replay without a model call | `LlmTranscriptTests` | green |
| 12 | redaction, no key | `LlmTranscriptTests` | green |
| 13 | requested and served model recorded | `LlmRefinementPolicyTests` | green |
| **14** | **live Ponder: ≥1 model turn, ≥1 tool call, ≥1 legal action** | `PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall` (reads the winner, v-required) | **green (54 tool calls)** |
| 15 | missing key refuses, no match | `PonderGuardTests` | green |
| 16 | injection cannot change permissions | `PonderGuardTests` | green |
| 17 | deterministic artifacts unchanged when LLM mode is off | `DeterministicOffTests` | green |
| 18 | Learning scan clean; harness outside it | `LearningIsolationTests` | green |
| 19–21 | behavior-proof compliance (real, metadata-only, API-focused) | `BehaviorProofComplianceTests` | green |
| 22 | secret file environment-only, no artifact leak | `LiveModelConfigurationTests` + EVD-016/EVD-059 | green |

**All 22 planned tests pass** (test 14 now reads the winner's 54 tool calls). Six additional evidence tests (inert prompt metadata, scripted tool path, wall-clock budget, live-arm replay, the pinned live Ponder action/street mix, and an integrated final-state test that pins every recorded artifact) are green and are not counted in the 22.

## Cost

| Surface | Calls | Prompt tokens | Completion tokens | Wall clock |
|---|---:|---:|---:|---:|
| Pre-flight smoke | 1 | 22 | 9 | 2.8 s |
| Prompt-shape probe | 1 | 371 | 63 | 3.0 s |
| LLM refinement arm | 10 | 6,213 | 800 | 153.6 s |
| Tool capability probe | 1 | not recorded | not recorded | — |
| Ponder match (pre-change) | 33 | 43,547 | 175 | — |
| Ponder match (authorized option A) | 39 | 52,954 | 200 | — |
| Variant `task` | 55 | 77,429 | 305 | — |
| Variant `both` | 53 | 76,612 | 305 | — |
| **Variant `both-required` (winner)** | **106** | **159,964** | **5,026** | — |
| Variant `reasoner-both` | 57 | 82,462 | 320 | — |
| Deterministic control | 0 | 0 | 0 | 133.2 s |
| **Total live model calls** | **46** | | | |

## Not claimed

- No poker-strength claim: 0 promotions in both pre-registered arms.
- No cross-model generalization; only `deepseek-chat` (served `deepseek-flash`) was exercised.
- No model-weight training, no `run_code`, no dynamic permissions, no release/publish/deploy and no CI-green claim.
- The fixture hunt is a test-seed diagnostic, not an arm result.
- Replay proves reproducibility against the recorded response, not across model versions.

## Stop criteria

| Criterion | State |
|---|---|
| D1–D9 Covered | **Met** — 9/9 |
| All 22 planned tests green | **Met** — 446/446, 0 failed, 0 skipped (test 14 reads the winner) |
| Real LLM refinement run recorded | **Met** — 10 live transcripts |
| Real Ponder run recorded | **Met** — six live runs: two prompt baselines (33/39 turns, 0 tool calls) and four variants; the winner made 54 tool calls |
| Deterministic unchanged | **Met** — 446-suite (446 pass, 0 failed), legacy 7/7, five frozen hashes |
| Zero Falsified delivery claims | **Met for delivery claims** — VAL-001…011; two hypotheses/characterizations were falsified and corrected (VAL-007 "fold-only"; VAL-010 "option A induces a tool call"), and neither is a D1–D9 delivery claim |
| Zero P0 open questions | **Met** — OQ-005 is resolved by the winning variant |
| No key leak | **Met** — 0 prefix hits, ACL and gitignore verified |

## Round-7/11 evidence additions

After the first final report, evidence was strengthened without changing the deterministic mode: the live-arm replay (D2 end-to-end), the wall-clock budget branch (M-09), the Ponder tool-invocation path with a scripted tool call (M-06 mechanism), a pinned live-session behavior test that falsified and corrected an earlier "fold-only" characterization, an integrated final-evidence test that pins the arm reports and sidecars, the verdict, both pointers, the live session and the five frozen hashes, and the selectable tool-prompt mode. The authorized parallel variant round then resolved D6: `both-required` made 54 live tool calls and is the default mode. The suite is **446 tests — 446 pass, 0 failed, 0 skipped**.

## D6 resolution and residual limits

D6 was the only unmet dimension. The operator authorized option A alone (0 tool calls in 39 turns), then all remaining approaches in parallel. The winning variant, **`both-required`**, produced **54 live tool calls** with 54 recorded results and 61 legal actions in a 20-hand match at seed `1099512127776`; the four-tool surface, permissions and executor were unchanged, and only the prompt surface varied. `BothRequired` is now the default tool-prompt mode, the Ponder host default, and the D6 test reads the winner and passes.

Residual limits, stated rather than hidden: no poker-strength claim (0 promotions in both pre-registered arms); the winner sometimes issued two tool calls in a single turn despite the "at most one" system wording, which is permitted by the executor and does not affect the requirement; V4 requested `deepseek-reasoner` but was served `deepseek-flash`, so no cross-model claim is made; replay is against recorded responses, not across model versions; the two pre-change runs (33 and 39 turns, 0 tool calls) remain the measured baseline. No test was skipped, weakened or deleted.

## Post-completion: 6-seat Ponder + Enhance loop

On the operator's follow-up request ("a reasonable number of round of ponder and enhance ... use the max number of seats available. all pondering"), four further **live 6-seat** rounds ran at a fresh seed `1099512227776`, each followed by a live refiner proposal and a 24×20 frozen-gate evaluation. The agent made **145 tool calls** in total with 0 errors; the four arising strategies were:

| Round | Candidate | raise/call/bluff/sizing | Gate LCB | Verdict |
|---|---|---|---:|---|
| 1 | `v2-068a6ead` | 0.55/0.60/0.18/1.25 | −40.494 | refused |
| 2 | `v2-5f1a121b` | 0.60/0.55/0.18/1.25 | −45.935 | refused |
| 3 | `v2-7d1c8d53` | 0.60/0.62/0.18/1.25 | −34.508 | refused |
| 4 | `v2-62d307d7` | 0.62/0.66/0.18/1.15 | −78.977 | refused |

The champion stayed **0.70/0.70/0.12/1.0**. Every proposal loosened the incumbent in response to a 6-seat session where seat 0 was passive; the heads-up gate rewards tightening, so all four were refused. The full exposure (genomes, the model's own reasons, per-round Ponder metrics, gate reports and hashes) is at [ponder-loop/results.md](ponder-loop/results.md).

A second series then tested the diagnosis: a corrected loop fed the refiner the **genome's own** 6-seat measurement and the gate-refusal memory; a gate-aware refiner was shown the gate rule and the prior LCBs; ten proposals were sampled at 50 seeds; and a deterministic search was run as the benchmark. The model still never cleared (best LCB **2.568** across 21 proposals; it repeated its highest-mean loosening genome). The search found the clearing region: **`0.95/0.20/0.10/1.0` clears at 24/50/100 seeds with LCB 21.16/23.93/15.61**, `0.90/0.30/0.10/1.0` clears at 13.08, and a local search from the LLM's best at raise 0.90 (`0.90/0.30/0.14/1.05`) clears at 19.06. The LLM's error is directional (it lowers the raise threshold; the gate rewards raising it). Full record: [ponder-loop/experiments-2.md](ponder-loop/experiments-2.md).

A third series ran the **search-augmented** loop: three live 6-seat rounds at seed `1099512627776`, the LLM proposing a seed, a 100-point grid refining it on a **disjoint** 50-seed set, and the frozen gate evaluating the winner on **untouched** 24 seeds. Round 1 **promoted `0.90/0.20/0.18/1.0`** (gate mean 33.59375, LCB **11.8854**, verified); the champion pointer moved to `tholdem-strategy-v2-6a108c2b`. Rounds 2–3 were refused against the new champion. The promoted genome clears at two fresh base seeds across 24/50/100 seeds (LCB 27.7–36.2). The LLM's seed was still the familiar loosening genome; the decisive raise-up/call-down move came from the search, i.e. the model is a useful seed, not the optimizer. The promotion is in the loop's scratch data directory; the product pointer is unchanged and adoption is the user's call. Full record: [ponder-loop/experiments-3.md](ponder-loop/experiments-3.md). The frozen gate, the components and the deterministic mode are untouched.

## Reproduce

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
powershell -NoProfile -ExecutionPolicy Bypass -File docs/goal-llm-harness/round-6/final-regression.ps1
# LLM arm (operator; loads the secret into the child process only)
$env:THOLDEM_LIVE_MODEL="1"
dotnet run --project .tmp/goal-llm-harness/arms/Arms.csproj -c Release -- --arm llm --seed 1099511927776 --iterations 10 --training-hands 2000 --eval-seeds 50 --eval-hands 20 --data-dir <fresh> --report <path> --secret-file .secrets/deepseek.env
# Ponder (operator; pre-change and authorized post-change runs use separate output directories)
dotnet run --project .tmp/goal-llm-harness/ponder/Ponder.csproj -c Release -- --live --secret-file .secrets/deepseek.env --seed 1099511927776 --hands 20 --model deepseek-chat --out .tmp/goal-llm-harness/ponder/live
dotnet run --project .tmp/goal-llm-harness/ponder/Ponder.csproj -c Release -- --live --secret-file .secrets/deepseek.env --seed 1099512027776 --hands 20 --model deepseek-chat --out .tmp/goal-llm-harness/ponder/live2
```

*No API key appears in this report or in any artifact it cites; the key is loaded only into the child process environment. D6 is resolved by the authorized parallel round, and the winner's session and 106 transcripts are mirrored under round-4/live-artifacts/ with hashes.*
