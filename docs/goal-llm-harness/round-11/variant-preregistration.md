# Pre-registration — parallel tool-prompt variants

> Direct human instruction on 2026-11-09: **"try all the approaches in parallel, winner wins."** This authorizes the task-text invitation (B), the combined prompt (A+B), a mandatory variant, and a cross-model variant, run concurrently against the same pre-registered seed. It does not authorize weakening the D6 test or rerunning a variant for a different draw.

## Fresh seed

- **Seed: `1099512127776`** — verified unused before the run (`grep` in `docs` = 0, `.tmp` = 0). Consumed seeds: `1099511627776`(+i·1000), `1099511727776`, `1099511827776`, `1099511927776`, `20261109`, `1099512027776`.
- **Same seed for every variant**, 20 hands, one attempt per variant, in parallel. Pairing the deals isolates the prompt/model difference.

## Variants (frozen before execution)

| # | Variant | Tool-prompt mode | Model | Output |
|---|---|---|---|---|
| V1 | `task` (B) | `task` — invitation in the task text only | `deepseek-chat` | `.tmp/goal-llm-harness/ponder/v-task` |
| V2 | `both` (A+B) | `both` — system + task invitation | `deepseek-chat` | `.tmp/goal-llm-harness/ponder/v-both` |
| V3 | `both-required` | `both-required` — system invitation + mandatory task call | `deepseek-chat` | `.tmp/goal-llm-harness/ponder/v-required` |
| V4 | `reasoner-both` | `both` | `deepseek-reasoner` (cross-model capability, M-12 recorded) | `.tmp/goal-llm-harness/ponder/v-reasoner` |

- C (a bounded `MaxToolCallsPerTurn`) is a guard, not an inducer, and is not a winner candidate in this round; it would be applied to the winner if the operator wants a hard bound.
- The pre-change run (`live`, 33 turns) and the option-A run (`live2`, 39 turns, invitation present) remain the baselines; both made 0 tool calls.

## Winner rule (frozen)

1. A variant qualifies if it records **≥1 model-authored tool call** with a recorded tool result and **≥1 legal model action**.
2. Among qualifiers, the winner is the one with the **most tool calls**; tie-break by **fewer total tokens**, then **fewer model turns**, then variant order V1<V2<V3<V4.
3. If no variant qualifies, there is no winner; D6 stays Partial and the null is reported.
4. The winning variant's prompt mode becomes the default only after a separate verification that the full suite still passes; the D6 test then reads the winner's session.
5. No variant is rerun for a different draw; a transport failure is recorded as that variant's failure, not silently retried into a win.

## Invariants

- The five frozen gate components, the deterministic mode and the LLM-refinement arm are untouched.
- No key is written to any artifact; the secret is loaded into each child process only.
- The tool catalog, permissions, executor and memory are unchanged; only the prompt mode (and, for V4, the requested model) varies.
