# Parallel variant results — winner

> Authorized by the direct instruction "try all the approaches in parallel, winner wins." Pre-registration: [variant-preregistration.md](variant-preregistration.md). All four variants ran **concurrently** at the same fresh seed `1099512127776`, 20 hands, one attempt each. The winner rule was frozen before execution.

## Results

| Variant | Tool-prompt mode | Requested model | Turns | Model calls | **Tool calls** | Tools | Legal actions | Errors | Tokens (p/c) |
|---|---|---|---|---:|---:|---:|---|---:|---:|---|
| V1 `task` | `task` | `deepseek-chat` | 55 | 55 | **0** | — | 55 | 0 | 77,429 / 305 |
| V2 `both` | `both` | `deepseek-chat` | 53 | 53 | **0** | — | 53 | 0 | 76,612 / 305 |
| **V3 `both-required`** | **`both-required`** | **`deepseek-chat`** | **61** | **106** | **54** | **equity-calculator (44), pot-odds (10)** | **61** | **0** | **159,964 / 5,026** |
| V4 `reasoner-both` | `both` | `deepseek-reasoner` | 57 | 57 | **0** | — | 57 | 0 | 82,462 / 320 |

Baselines: pre-change `live` (33 turns, 0 tool calls), option-A `live2` (39 turns, 0 tool calls with the invitation present).

## Winner

**V3 `both-required`** qualifies with 54 model-authored tool calls, 54 recorded tool results, and 61 legal model actions. It is the only qualifier, so the tie-breaks were not needed.

- Action mix: Check 35 / Call 10 / Bet 9 / Fold 5 / Raise 2 (61 turns).
- 106 model calls, 0 fallbacks, 0 violations, 0 errors.
- The task text carries the mandatory invitation ("You MUST call at least one of the provided tools..."); the system prompt carries the optional one.
- Sample turn: the model called `equity-calculator` (holeCards 4d 6h, samples 10000 → the tool's 2000-sample cap) and `pot-odds` (pot 20, amountToCall 5), received the results (`{"equity":0.3815,...}`), and then acted. The model sometimes issued two tool calls in one turn despite the "at most one" system wording; the executor allows it and the requirement only needs ≥1.
- V4 requested `deepseek-reasoner` but was served `deepseek-flash` (recorded as requested/served; no cross-model claim), and made 0 tool calls.

## Decision

Adopt `BothRequired` as the default tool-prompt mode and point the D6 test at V3's session. The change is a product default in `TholdemAgentOptions` plus the Ponder host default; the four tools, permissions, executor, memory and transport are unchanged, and the five frozen components and the deterministic mode are untouched.

## Artifacts

- Sessions/logs: `.tmp/goal-llm-harness/ponder/v-{task,both,required,reasoner}/`; logs `docs/goal-llm-harness/round-11/v-*.log`.
- Durable copies of the winner's session and 106 transcripts: `docs/goal-llm-harness/round-4/live-artifacts/ponder-session-winner.json`, `ponder-transcripts-winner.jsonl`.
- No key appears in any artifact.
