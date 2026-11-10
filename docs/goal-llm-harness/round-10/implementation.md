# Round 10 — Authorized option A and second live Ponder round

> The user authorized option A and a second live round: "go for A, and do a second live round please" (2026-11-09). The pre-registration was written before the run: [ponder-rerun-preregistration.md](ponder-rerun-preregistration.md). **Result: the authorized prompt change was delivered to the model and the second live match still made 0 tool calls.** D6 remains Partial; no rerun.

## The change (option A only)

`WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs`, `SystemPromptText`: one invitation added before the reply-schema line.

| | SHA-256 |
|---|---|
| Agent source before the change | `296739531bba2b8b80f736782bb2e0a3ffa540b4e8374c6fe528c7ed67903252` |
| Agent source after the change | `a7dcfe6b7fe3d9317de788cc3574c3d8d168424ae71a218122022356f5ecb6e9` |

No other product change: the four tools, permissions, `AgentExecutor`, `ArtifactAgentMemory`, transport, refiner and the five frozen gate components are untouched. The deterministic artifacts are unaffected.

## Pre-registration (frozen before execution)

- Fresh seed **`1099512027776`** — verified unused (`grep` in `docs` = 0, `.tmp` = 0).
- 20 hands, requested `deepseek-chat`, the four fixed tools, one attempt, output `.tmp/goal-llm-harness/ponder/live2/`.
- Success: at least one **model-authored** tool call with a recorded result and a legal action.
- Failure: zero tool calls; the null is reported and there is no rerun.

## Second live run — observed

Command: `Ponder.exe --live --secret-file .secrets/deepseek.env --seed 1099512027776 --hands 20 --model deepseek-chat --out .tmp/goal-llm-harness/ponder/live2` with `THOLDEM_LIVE_MODEL=1`.

| Field | Value |
|---|---|
| Hands | 20 |
| Model turns | **39** |
| Legal model actions | **39** (0 fallbacks, 0 violations, 0 errors) |
| **Tool calls** | **0** |
| Served model | `deepseek-flash` |
| Latency | 766–1486 ms per call |
| Tokens | 52,954 prompt / 200 completion |
| Transcripts | 39 canonical records |
| Action mix | Fold 12 / Check 21 / Call 5 / Bet 1 / Raise 1 |
| Streets | 18 / 9 / 9 / 3 |
| Distinct hole pairs | 16 |
| Prompt check | the first request's system message **contains** "You may call at most one of the provided tools" |

The invitation reached the model (verified in the recorded request) and the model still answered directly on every turn. The second run is more active (39 turns, 16 hands with decisions) and still makes no tool call.

## Tests and evidence

- `PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall` now reads `live2`; it fails with `summary.toolCalls=0`. The suite is `TOTAL 444 PASSED 443 FAILED 1 SKIPPED 0`; the one red is this deliberate gate.
- The pre-change run (`live`, 33 turns, 0 tool calls) is preserved; `PonderBehaviorTests` and `FinalEvidenceTests` continue to pin it as the pre-change baseline.
- The second run's session and transcripts are at `.tmp/goal-llm-harness/ponder/live2/`.

## Conclusion

Option A alone does not make the model call a tool on this surface. The remaining options are B (invite the tool in the task text instead of the system prompt), A+B, or C (a bounded `MaxToolCallsPerTurn` with the executor offering tools until the budget is spent). Without one of those, D6 stays Partial and the red gate stays red. No key appears in this report; the secret was loaded into the child process only.
