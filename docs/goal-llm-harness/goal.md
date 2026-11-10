# Goal — Tholdem model-in-the-loop harness

> **One-sentence outcome**: In LLM mode a real DeepSeek model drives every Tholdem refinement iteration through `TrainingLoop.Run` and the unchanged frozen gate, the existing `TholdemRuntimeAgent` plays a live 20-hand Ponder match with its four fixed tools, and both surfaces are recorded in canonical transcripts, while the deterministic mode stays byte-identical (394/394 baseline tests, legacy 7/7 hashes, five frozen component hashes unchanged).

- **Status**: Round 0 contract. Pre-registration frozen before any arm runs.
- **Contract**: `harness/requirements/Wip.Tholdem.ModelHarness.md` (M-01…M-15, WP-1…WP-7, 22-test plan, D1–D9).
- **Substrate**: `harness/requirements/Wip.Tholdem.ContinualHarness.md`; its LLM-exclusion sections are superseded here. The deterministic R-01–R-12 work is the offline control, not the deliverable.
- **Prior facts reused, not rebuilt**: `docs/goal-tholdem-harness/evidence.md`, `docs/goal-tholdem-floor/`, `docs/goal-promotion/goal.md`.

This is **not** a deterministic experiment. A deterministic-only refinement run presented as the LLM arm is a hard failure of this goal.

---

## Definition of done (falsifiable: command + expected observable)

| # | Dimension | Command / check | Expected observable |
|---|---|---|---|
| D1 | Seam and deterministic identity | `RunXunit <Wip.Tholdem.Tests.dll>`; `dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train --seed 1099511627776 --iterations 2 --data-dir <fresh>` twice | TOTAL = baseline+new, 0 FAILED, 0 SKIPPED; a focused test proves a supplied `IRefinementPolicy` is used; the default path produces 7/7 legacy JSON hashes equal to `docs/goal-tholdem-harness/round-0/baseline-hashes.txt` |
| D2 | Canonical transcript and replay | transcript tests; read the persisted `.jsonl`/`.json` files | one canonical record per model call with schema, requested+served model, prompt hash, response hash, usage, latency, outcome; replay reproduces the recorded candidate with **zero** model calls |
| D3 | Real model proposal, parse, clamp, one repair | focused policy tests + the live run transcript | valid JSON → exact genome; out-of-range → clamped with original+clamped recorded; malformed → exactly one repair request, then a recorded failure and no evaluation; live transcript has non-empty response, served model, usage and latency |
| D4 | Live gate, secret loader, redaction | missing-env test with a counting handler; secret loader test; key-prefix grep over repo artifacts | missing `THOLDEM_LIVE_MODEL`/`DEEPSEEK_API_KEY` → stable refusal with **zero** HTTP requests; `.secrets/deepseek.env` is gitignored and ACL-restricted and is read into the child environment only; no artifact contains the key prefix |
| D5 | LLM arm through the frozen gate | the pre-registered LLM arm run; re-derive the verdict from the persisted report | report persisted under the arm data dir; `StatisticalPromotionPolicy` + `PromotionVerifier` re-derive the same verdict; `active-champion.json` bytes recorded before/after; ≥1 verified promotion, or the exact best LCB/mean and the refusal reported |
| D6 | Live Ponder match | one 20-hand live match with the real model and the four fixed tools | transcript has ≥1 real model turn, ≥1 tool call with its result, ≥1 legal action, latency and errors recorded; missing key refuses before any match |
| D7 | Deterministic mode and frozen components unchanged | full suite; legacy CLI twice; hash the five frozen sources | suite green; 7/7 legacy hashes; five frozen hashes equal `docs/goal-llm-harness/round-0/frozen-baseline.txt` |
| D8 | Injection, budget, cancellation, failure | 22-test plan rows 4, 9, 10, 16 and their focused runs | injection text stays data and changes no tool/permission; over-budget refuses; cancellation leaves the pointer byte-identical; a failed model call records the error and never promotes |
| D9 | Final report | read `docs/goal-llm-harness/final-report.md` | states what the model did, what it cost (calls/tokens/latency) and whether it promoted; no key and no deterministic substitute |

D1–D9 are the requirements document's dimensions; every row must reach `Covered` at High confidence before the goal is complete.

---

## Hard gates

| Gate | Requirement | Failure means |
|---|---|---|
| HG1 | LLM mode off by default; deterministic mode byte-identical (artifacts and hashes) | the delivery is rejected |
| HG2 | 394/394 baseline tests preserved plus every new test green; legacy 7/7 hashes | the delivery is rejected |
| HG3 | Five frozen components unchanged (`EvaluationCriteria`, `PromotionPolicy`, `PromotionVerifier`, `MatchEvaluator`, `SeedDerivation`); default margin 5 bb/100, `MinSeeds` 24; no gate edits | the delivery is rejected |
| HG4 | Every LLM iteration has a real request/response transcript with requested + served model, prompt/response hashes, usage, latency and outcome; a scripted model is a unit-test double only | the LLM claim is Unverified |
| HG5 | The operator key never appears in the repo, goal files, tests, prompts, transcripts or logs; it is loaded from `.secrets/deepseek.env` into a child process only | the delivery is rejected |
| HG6 | No model-weight training, no `run_code`, no dynamic permissions or sub-agents, no model-authored code execution | the delivery is rejected |
| HG7 | The model has no promotion authority: only the unchanged gate and verifier move the pointer | the delivery is rejected |
| HG8 | No release, publish, deploy or CI-green claim | the report overclaims |

---

## Pre-registration (frozen before any run)

| Item | Frozen value |
|---|---|
| Starting champion | `StrategyGenome(RaiseThreshold: 0.70, CallThreshold: 0.70, BluffFrequency: 0.12, SizingMultiplier: 1.0)`, version 1, iteration 0, the documented demo start; `active-champion.json` points at it before the run in each fresh arm directory |
| Seed | `1099511927776` (fresh: never consumed by a run; it was pre-registered by `docs/goal-promotion/goal.md:18`, an experiment that reached only its round-0 contract and executed no arm — see OBS-010) |
| Gate | default `EvaluationCriteria(MarginBbPer100: 5.0, MinSeeds: 24, Z: 1.959963984540054)`; unchanged files |
| Arms | `llm-refinement` (real model proposer via `LlmRefinementPolicy`; `HarnessRefinement=true`, `SkillRefinement=false` so `RefinementGuard` does not intercept model proposals), `deterministic-refinement` (control; `HarnessRefinement=true`, `SkillRefinement=false`, `SignalRateFloor=0`), optional `legacy` (no refinement) |
| Shared shape | same seed, same starting artifact, same budget, same evaluation seeds/hands, same frozen gate |
| Budget | 10 iterations, 2000 training hands, 50 evaluation seeds, 20 hands per match |
| Success | at least one **verified** promotion in the LLM arm; otherwise report the exact best LCB/mean and the refusal. The gate is never lowered and the run is never repeated for a favourable number |
| Ponder | one live 20-hand match with the real model and the four fixed `TholdemToolCatalog` tools; record model turns, tool calls, tool results, legal actions, latency and errors |
| Recorded under | `docs/goal-llm-harness/round-3/`, `round-4/`, transcripts in the arm data directories |

No parameter above may change after the first arm run. A change requires a `DEC` superseding this section and voids the run.

---

## Constraints

- **Source scope**: `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs` (one optional parameter and one branch), the new `WIP/Wip.Tholdem.ModelHarness/` project, new tests under `WIP/Wip.Tholdem.Tests/ModelHarness/`, `Wip.slnx` + the tests csproj reference, and scratch hosts under `.tmp/goal-llm-harness/`.
- **Forbidden edits**: the five frozen components, `FrozenComponents`, `TrainingOptions` defaults, any existing test assertion, `TholdemRuntimeAgent`, `TholdemToolCatalog`, `DeepSeekChatModel`, `DeepSeekModelProvider`.
- **Learning scan**: `Learning` stays free of `Process`/`HttpClient`/`Compile`/`Assembly.Load`/`CSharpScript`; the model policy lives outside it.
- **Key handling**: the operator key lives only in the gitignored, ACL-restricted `.secrets/deepseek.env`; it is loaded into a child process environment, never printed, echoed, copied, committed or pasted. Grep for a prefix, never the value.
- **Network**: only in explicit LLM mode after the live gate and key check; no network in the default test run.
- **No release/publish/CI claim**; `dotnet test` is unusable on this machine (Win32Exception 5), so all local evidence uses `.tmp/tholdem-verify/RunXunit`.

---

## Stop criteria

Done only when **all** hold:

1. D1–D9 `Covered` at High confidence.
2. All 22 planned tests green (17 offline/scripted + the live rows run as operator runs with recorded transcripts).
3. A real LLM refinement run and a real Ponder run recorded with non-empty model responses, served model, usage and latency.
4. Deterministic mode byte-identical: suite green, legacy 7/7, five frozen hashes unchanged.
5. Zero Falsified delivery claims, zero P0 open questions.
6. No key leak (prefix grep clean).

Round cap: **8**. A no-promotion LLM arm is a valid outcome if reported exactly; a missing transcript is not.

---

## Round plan (one narrow objective per round)

| Round | Objective | Exit criterion |
|---|---|---|
| 1 | WP-1 + WP-2: `TrainingLoop.Run` optional `IRefinementPolicy` seam; canonical transcript + replay | deterministic path byte-identical; supplied policy used; replay reproduces a candidate with zero model calls |
| 2 | WP-3: `LlmRefinementPolicy` in `ModelHarness` with prompt builder, JSON schema, clamp/one repair | valid/out-of-range/malformed/missing-key tests green; no model in `Learning` |
| 3 | WP-4 + WP-5: live gate/key/base-URL/redaction; the LLM arm through `TrainingLoop` and the frozen gate | real DeepSeek transcript; report hash, policy/verifier re-derivation, pointer bytes, promotion or honest no-promotion |
| 4 | WP-6: live Ponder match with the real model | ≥1 model turn, ≥1 tool call, ≥1 legal action, transcript |
| 5 | WP-7: full falsification pass, 22-test plan, deterministic regression, final report | goal complete |

---

## Directory map

- `docs/goal-llm-harness/{goal,journal,evidence,state,deliverables}.md` — harness memory
- `docs/goal-llm-harness/round-0/` — baseline snapshot (hashes, test count, frozen hashes)
- `docs/goal-llm-harness/round-N/` — per-round artifacts
- `.tmp/goal-llm-harness/` — scratch arm data, transcripts, runner binaries
