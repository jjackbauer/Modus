# Live model comparison — old provider vs new runtime path (2026-11-07)

- **Question**: does the production "new path" behave like the old provider path against the **real**
  DeepSeek API, not a scripted fake?
- **Answer**: it did **not** at first — the live API rejected the new path with HTTP 400 because of
  the dotted tool name. The adapter was fixed and the comparison was re-run: both paths now complete
  and both generated scripts compile, but the new path uses the compile tool and therefore costs
  several times more tokens/latency.
- **Credential**: the machine's configured `DEEPSEEK_API_KEY` (read from the local credential store,
  never printed, never written to the repo or to any log/artifact in this report).
- **Endpoint/model**: `https://api.deepseek.com`, `deepseek-flash`, temperature default.
- **Method**: one live run per path per round, two rounds each, same session layout, same seat (2),
  same observation transcript, same prompt text, same DeepSeek model. Old path =
  `BonesPonderAgent` legacy ctor -> `DeepSeekBonesStrategyAuthoringProvider` -> `DeepSeekModelProvider`.
  New path = `BonesPonderAgent` runtime ctor -> `BonesAgentAdapter` -> `AgentExecutor` ->
  `RuntimeToolInvoker` -> `BonesStageChatModel` -> `DeepSeekChatModel`. The harness captured every
  HTTP request/response body (no credentials are in the bodies) and the resulting compiled scripts.

## 1. Pre-fix result (first live run)

| Round | Old path | New path |
|---|---|---|
| 1 | OK, 2 calls, 29.7 s, script 4,817 chars | **FAILED after 1 call, 0.9 s**: `HTTP 400` |
| 2 | OK, 1 call, 5.1 s, script 3,505 chars | **FAILED after 1 call, 0.6 s**: `HTTP 400` |

Both requests carried the same prompt (`promptEqual=True`), the same `model` (`deepseek-flash`), and
the same thinking setting difference (old sends `{"type":"disabled"}`, new sends nothing). The only
new element was the tool definition. The API response body was exact:

```json
{"error":{"message":"Invalid 'tools[0].function.name': string does not match pattern. Expected a string that matches the pattern '^[a-zA-Z0-9_-]+$'. (request_id: ...)","type":"invalid_request_error","param":null,"code":"invalid_request_error"}}
```

Root cause: WIP capability ids are dotted (`tool.bones.strategy-compile`, and the shell's
`tool.repo.read_file`, …), while the provider's function-name grammar accepts only
`[A-Za-z0-9_-]`. The offline/mocked tests never validated the wire grammar, so the migration was
green in CI and broken against the real API. This is exactly the class of defect the live comparison
was requested for.

## 2. Fix

Provider-specific name mangling now lives in the adapter (`DeepSeekChatModel`), not in the domain:

- outgoing `tools[].function.name` and assistant `tool_calls[].function.name` are sanitized:
  invalid characters become `_`, empty names become `tool`, length capped at 64;
- the mapping is deterministic per request (sorted originals, numeric suffix on collision, for
  example `a.b` → `a_b`, `a_b` → `a_b_2`);
- incoming tool-call names are mapped back to the canonical dotted id before the agent sees them, so
  `RuntimeToolInvoker` still resolves `tool.bones.strategy-compile` / `tool.repo.read_file`;
- the model-visible observation (`ToolResult.Content`) is unchanged by this mapping.

Tests: `DeepSeekChatModelTests` updated to assert wire names are provider-safe and response names are
restored, plus a new collision test (`a.b`/`a_b` → `a_b`/`a_b_2`, response `a_b_2` restored to
`a_b`). The live regression test
`DeepSeekChatModelLiveTests.CompleteAsync_GivenLiveApiKeyAndDottedToolName_ExpectedAcceptedRequestAndRestoredToolName`
runs only when `DEEPSEEK_API_KEY` is set (offline gate returns immediately without it). Both live
tests passed with the real key after the fix.

## 3. Post-fix result (second live run)

| Round | Old path | New path |
|---|---|---|
| 1 | OK, 1 call, 19.8 s, 1,000+868 = 1,868 tokens, script 3,598 chars | OK, **3 calls**, 63.5 s, 10,374+1,285 = 11,659 tokens, script 5,134 chars |
| 2 | OK, 1 call, 5.6 s, 1,000+746 = 1,746 tokens, script 3,194 chars | OK, **2 calls**, 67.6 s, 11,772+1,690 = 13,462 tokens, script 6,536 chars |

- Both paths returned a `Script`/`Active` strategy and both generated scripts compiled; the prompt
  on the first call was byte-identical between paths (`promptEqual=True`); the model id was
  `deepseek-flash` on both.
- `scriptsEqual=False` (different SHA-256): expected — the model is stochastic and the two paths make
  a different number of calls. The structural contract (compiles, implements `IBonesPlayerSlot`,
  `ChooseMove`) is the parity criterion; byte equality would be an invalid expectation for a live
  model.
- The new path's extra calls are **tool calls**: the model chose to call the compile tool (1–2 times)
  and the executor fed the compiler diagnostics back before the final answer. That is the feature
  working live — and the reason for the cost difference.
- Trade-off to record: the new path used **~6–7× the tokens** and **~3–12× the wall time** for the
  same result, because each tool call round-trips the full script through the prompt. If this cost
  matters in production, narrow the tool description/usage policy or accept it as the price of live
  self-validation.

## 4. Evidence

| Artifact | Path |
|---|---|
| Pre-fix live log | `.tmp/live-compare/live.log` |
| Pre-fix detail (second run, output-path failure after the 400s) | `.tmp/live-compare/live2.log` |
| Post-fix live log | `.tmp/live-compare/live3.log` |
| Post-fix transcript (request/response bodies, usage, generated scripts) | `.tmp/live-compare-out/run-bf0eaff6dce54c34ae542304ca70b169/` |
| Live adapter tests (keyed) | `.tmp/live-compare/live-tests2.log`, `live-tests3.log` |
| Adapter unit tests (offline) | `.tmp/live-compare/adapter-tests.log` |

The harness project is `.tmp/live-compare/LiveCompare.csproj` (gitignored; ad-hoc verification tool,
not part of the shipped test suite).

## 5. Honest limits

- One model (`deepseek-flash`), two rounds per path, one prompt; this is a smoke comparison, not a
  statistical evaluation.
- The API key was read from the local credential store for this run only; it is not in the repo, the
  logs, the transcript or this report.
- The comparison covers Ponder. Enhance shares the same adapter/tool path and the same fix; a live
  Enhance comparison would multiply the cost, so it was not run.
