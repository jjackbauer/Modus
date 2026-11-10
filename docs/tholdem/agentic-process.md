# The agentic process — what the agents actually do

Short answer:

- **Pondering?** Only inside the model turn of the runtime agent — a bounded tool-using loop. The model's reasoning text is its own; it is **not stored, displayed or scored**. Offline runs use a scripted `IChatModel`, so no real pondering happens in CI.
- **Enhancing strategies?** Yes, but not by an LLM in this slice. The learning loop **analyzes metrics and mutates one strategy parameter at a time**, then promotes only through a paired, statistically gated evaluation that a verifier re-derives. It never generates or executes code.

![In-hand agent loop](diagrams/agent-in-hand.png)

## 1. In-hand: the runtime agent

Source: `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs`, `WIP/Wip.Tholdem.Runtime/Runtime/AgentExecutor.cs`.

1. **Observation.** The agent gets `PlayerObservation` — its own hole cards, the board, stacks, pot, legal actions, to-call and min/max raise. It never gets the seed, the deck or another seat's cards. The JSON is truncated to `MaxObservationCharacters` (default 4000).
2. **Prompt.** A fixed system prompt says observation and tool results are *untrusted data*, never instructions, and that tool permissions cannot change; it also fixes the reply schema.
3. **Model turn.** `IChatModel` may call one of four tools or answer. This is the only place "pondering" exists. `IAgentExecutor` bounds it with `MaxSteps`, `Timeout`, `MaxTotalTokens` and `MaxConsecutiveToolFailures`.
4. **Tools.** `RuntimeToolInvoker` resolves the tool, enforces permissions (`artifact.read` for history/statistics), applies a per-tool timeout, truncates results and returns deterministic errors for unknown tools or denied permissions. Tools: seeded equity, pot odds, session-scoped hand history (redacted), opponent statistics (all other seats).
5. **Memory.** `ArtifactAgentMemory` loads the last N messages and appends the turn and the final answer; content is secret-redacted and bounded.
6. **Validation.** The reply is parsed (first `{` to last `}`, 16 KB cap) and re-checked against the observation: the kind must be legal, and a bet/raise amount must be inside `[minRaiseTo, maxRaiseTo]`. Anything else records a violation and returns a legal fallback (`check → fold → call`).

What this is **not**: no chain-of-thought is persisted, no rationale is shown in the viewer, and no tool output can change the tool list or permissions. The offline tests drive a scripted model; the live DeepSeek path requires `THOLDEM_LIVE_MODEL=1` and a key and is not exercised here.

## 2. Between hands: the learning loop

Source: `WIP/Wip.Tholdem.Agents/Learning/*`.

![Learning loop](diagrams/learning-loop.png)

1. **Champion** — a `StrategyArtifact`: four parameters (`raiseThreshold`, `callThreshold`, `bluffFrequency`, `sizingMultiplier`), prompt text and a content-hashed id.
2. **Self-play** — seeded hands produce byte-identical histories.
3. **Analyze** — win rate, bb/100, aggression and showdown rate. VPIP is currently always 0 (documented limit).
4. **Propose** — `DeterministicStrategyProposer` nudges **one** parameter by ±0.02 based on those metrics. No LLM, no generated code.
5. **Evaluate** — paired held-out seeds, both seat orders, challenger vs incumbent on the same deals; mean bb/100 with a normal 95% interval.
6. **Gate** — promote only if `LCB > margin` (default 5 bb/100) **and** `seeds >= 24`.
7. **Verify** — `PromotionVerifier` re-derives the decision from the report alone, so an always-promote policy is refused by the pipeline, not just by one class.
8. **Persist** — `strategies/<id>.json`, `evaluations/*.json`, an accumulating `ledger.json`, and `active-champion.json` only when verified. No auto-merge, no deploy.

A real run (seed 7) shows the normal outcome: challenger `v2-b155b98f`, `mean bb/100 0.00`, `LCB 0.00` against margin 5 → **rejected**, champion unchanged.

## 3. What is verified, and what is not

Verified by tests and independent probes: legal-action validation and fallback, hidden-information boundary (no seed/deck/other cards), tool permission denial, unknown tools, prompt-injection rejection, truncation, budgets, the always-promote control failing, gate arithmetic, N-seat side pots and conservation, and 2-seat historical compatibility.

Known limits (see [known-limits.md](known-limits.md)): the live model path is environment-gated and unpowered here; the learning loop is heads-up only; proposals can be behaviourally inert (a 0.02 threshold change may alter no decision); VPIP is 0; and the deterministic deal lets an observer recover a *guessable* seed, which is why CLI/viewer entry points refuse seeds below 2^40 unless explicitly opted out.
