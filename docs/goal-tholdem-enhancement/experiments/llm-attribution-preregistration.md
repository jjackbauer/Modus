# LLM attribution pre-registration (item 3b) — pending budget

**Status:** not run. CAP-001 found a configured live model; no model budget or approval has been
supplied, so this is a pre-registration, not evidence.

## Question

Does an LLM proposal process beat equal-budget non-LLM controls across multiple starting strategies
and evaluation tasks, clearing the same four-seat gate?

## Design

- **Tasks**: the four starting genomes from `experiments/learning-ablation-v1` (one per evaluation
  task). Each task gets exactly **4 candidate proposals** (equal budget).
- **Arms**: LLM (live DeepSeek), Random, Grid, Greedy, NoSearch. The four non-LLM arms reuse the
  existing `SearchAblation` runner and its evaluation protocol.
- **LLM protocol**: one request per proposal, no repair retries, temperature default, prompt built by
  `LlmPromptBuilder` from the same observation schema; the response must parse into a genome through
  `LlmGenomeParser`; a parse failure is recorded as an unsafe/missing proposal, not retried.
- **Budget**: 4 tasks x 4 proposals = **16 requests**; max output tokens 512 per request; no model
  call outside this set. Estimated cost is provider-dependent and must be approved before running.
- **Evaluation**: the same selection/holdout protocol as the search ablation; promotions only through
  `PopulationPromotionGate` with an anchor and the independent verifier.
- **Outcome rule**: `benefit` only if the LLM arm has >=2 verified tasks, 0 unsafe tasks, and a
  strictly better verified count than **each** non-LLM arm. Otherwise `no-benefit`.

## Capability

- `CAP-001`: `.secrets/deepseek.env` has `DEEPSEEK_API_KEY` and `DEEPSEEK_BASE_URL`; the live path
  requires `THOLDEM_LIVE_MODEL=1`. The key is not in the process environment by default.
- No model call has been made. No LLM claim exists.

## Gate before running

1. Operator supplies an explicit budget/approval for 16 requests.
2. The task set, prompt, parser and outcome rule are hashed and written before the first call.
3. Scoring is recomputed by the verifier, not trusted from the LLM response.

Without 1-3, item 3b stays unclaimed (`RISK-006`).
