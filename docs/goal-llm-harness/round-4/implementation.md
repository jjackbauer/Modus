# Round 4 — Ponder live-model support (WP-6)

Status: scratch Ponder host built and scripted smoke observed. The live 20-hand match was **not**
run in this round (orchestrator-owned); no live model call and no secret file were used.

## 1. Build and scripted smoke (observed)

Run from the repository root.

```powershell
dotnet build .tmp/goal-llm-harness/ponder/Ponder.csproj -c Release
dotnet run --project .tmp/goal-llm-harness/ponder/Ponder.csproj -c Release -- --scripted --seed 1099511927776 --hands 2 --out .tmp/goal-llm-harness/ponder/smoke
```

Observed build tail (exit 0):

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:17.34
```

Observed scripted-smoke stdout (exit 0; wrapper printed `exit=0`):

```text
schema=tholdem.ponder.session.v1
modelKind=scripted
seed=1099511927776
hands=2
requestedModel=scripted
servedModels=scripted
modelTurns=1
toolCalls=0
toolNamesUsed=
legalActions=1
modelActions=1
fallbacks=0
violations=0
errors=0
minLatencyMilliseconds=10
maxLatencyMilliseconds=10
promptTokens=0
completionTokens=0
transcriptLines=1
session=C:\Users\ricar\dev\Modus\.tmp\goal-llm-harness\ponder\smoke\ponder-session.json
transcript=C:\Users\ricar\dev\Modus\.tmp\goal-llm-harness\ponder\smoke\llm-transcripts.jsonl
```

Session JSON path: `.tmp/goal-llm-harness/ponder/smoke/ponder-session.json`.
Transcript line count: 1 (`.tmp/goal-llm-harness/ponder/smoke/llm-transcripts.jsonl`).

Direct read of the session file confirmed the label and the shape:

```text
modelKind=scripted schema=tholdem.ponder.session.v1 turns=1 calls=1 servedModels=scripted
turn={"hand":1,"seat":0,"actionKind":"Fold","amountTo":0,"actionSource":"model",
      "legalActionKinds":["Fold","Call","Raise","AllIn"],"minRaiseTo":20,"maxRaiseTo":1000,
      "observationSha256":"sha256:57a8b8cb14838e0392930018e20af130dc36d950d40cfce8b1a6361e36a72f50",
      "latencyMilliseconds":832,"violationsAfter":0,"error":null}
transcript iteration=1 correlationId=ponder-1 requestedModel=scripted servedModel=scripted
outcome=ok response={"action":"fold"}
```

The session file is UTF-8 without a BOM (first three bytes are not `EF BB BF`) and its last byte is
`0x7D` (no trailing newline). The transcript file is UTF-8 without a BOM and ends with the
single newline appended by `LlmTranscriptStore.AppendAsync`.

Only one acting turn occurred in the two-hand smoke: hand 1 ended when the scripted seat 0 folded to
the big blind (fold was legal; check was not — `legalActionKinds` above), and in hand 2 the baseline
folded preflop before seat 0 acted. This is the recorded match, not a wiring failure.

### Gate refusal (observed, no secret, no network, no match)

The live flag half of the gate is checked before the secret file is read
(`.tmp/goal-llm-harness/ponder/Program.cs:95-102`), and `LiveModelConfiguration.Resolve` remains the
authority for the key and base URL (`WIP/Wip.Tholdem.ModelHarness/LiveModelConfiguration.cs:40-72`).
The following negative test passed no `--secret-file` and made no network call:

```powershell
Remove-Item Env:THOLDEM_LIVE_MODEL -ErrorAction SilentlyContinue
dotnet run --project .tmp/goal-llm-harness/ponder/Ponder.csproj -c Release --no-build -- --live --seed 1099511927776 --hands 20 --model deepseek-chat --out .tmp/goal-llm-harness/ponder/gate-refusal
```

Observed:

```text
refusal=live-model-disabled
exit=2
sessionExists=False
outExists=False
```

The output directory was never created, so no session file could exist. This observes the
"live without the gate" failure rule; it does not exercise the key-missing branch through
`Resolve`, which is the same refusal code (`LiveModelConfiguration.cs:54-59`).

### Supplemental scripted wiring check (NOT live evidence)

A 20-hand scripted run was used only to exercise more turns and the prior-hand loading path:

```powershell
dotnet run --project .tmp/goal-llm-harness/ponder/Ponder.csproj -c Release --no-build -- --scripted --seed 1099511927776 --hands 20 --out .tmp/goal-llm-harness/ponder/smoke-multi
```

Observed (exit 0):

```text
modelKind=scripted
hands=20
modelTurns=19
toolCalls=0
legalActions=19
modelActions=19
fallbacks=0
violations=0
errors=0
transcriptLines=19
```

A separate inspection of the session and history directory observed
`turnHands=1,3,4,4,4,4,5,7,8,8,8,8,9,10,11,13,15,17,19` and `historyFiles=20`.
Hands 3+ required parsing the already-written `history/hand-*.json` files through
`HandRecordJson.Parse` and recording them in `SessionHandStore`
(`.tmp/goal-llm-harness/ponder/RecordingPokerAgent.cs:119-175`); a parse or record failure would
have aborted the match. This is scripted wiring evidence only and must never be cited as live
evidence.

## 2. Files created

| File | One-line purpose |
|---|---|
| `.tmp/goal-llm-harness/ponder/Ponder.csproj` | Scratch net10.0 console host referencing `Wip.Tholdem.Agents`, `Wip.Runtime`, `Wip.Tholdem.ModelHarness`, `Wip.Artifacts.Local`, plus DI 10.0.0; not in `Wip.slnx`. |
| `.tmp/goal-llm-harness/ponder/Program.cs` | Mode parsing, live gate refusal, unchanged-type composition, `MatchSimulator.Run` launch and summary printing. |
| `.tmp/goal-llm-harness/ponder/RecordingChatModel.cs` | `IChatModel` decorator recording every call and appending one canonical `LlmTranscript`. |
| `.tmp/goal-llm-harness/ponder/RecordingPokerAgent.cs` | `IPokerAgent` decorator loading completed hands into `SessionHandStore` and recording every turn. |
| `.tmp/goal-llm-harness/ponder/ScriptedChatModel.cs` | Local no-network legal fold/check double used only by `--scripted`. |
| `.tmp/goal-llm-harness/ponder/PonderSession.cs` | Session DTOs and the canonical UTF-8 (no BOM) `tholdem.ponder.session.v1` serializer. |
| `docs/goal-llm-harness/round-4/implementation.md` | This report. |

Composition is the existing pipeline unchanged: `PermissionEvaluator`, `SessionHandStore(2)`,
`EquityTool(2000)`, `PotOddsTool`, `HandHistoryTool(0, store)`,
`OpponentStatsTool(0, store)`, `WipArtifactStoreLocal(<out>/artifacts)`,
`RuntimeToolInvoker(provider, TholdemToolCatalog.CreateDescriptors())`,
`AgentExecutor(recordingModel, invoker)`, `ArtifactAgentMemory(store)`, and
`TholdemRuntimeAgent(executor, memory, new SessionId("tholdem-ponder-live"), <out>, options,
TholdemToolCatalog.CreateDefinitions())` — `.tmp/goal-llm-harness/ponder/Program.cs:157-200`.
The same pattern is the existing test composition at
`WIP/Wip.Tholdem.Tests/Runtime/RuntimeAgentTestSupport.cs:204-226`.

Session semantics used by the summary (`.tmp/goal-llm-harness/ponder/PonderSession.cs:85-155`):
`modelTurns` is the number of model calls; `toolCalls` is the sum of response tool calls;
`legalActions` is the number of recorded acting turns with a model or fallback action;
`modelActions` and `fallbacks` partition those turns by whether the runtime agent's violation count
grew during the turn (`RecordingPokerAgent.cs:100-101`); latency min/max are over model calls;
token totals are summed usage. The turn records additionally carry `minRaiseTo`, `maxRaiseTo`,
`observationSha256` and `error` to satisfy the per-turn recording requirement.

## 3. No WIP file changed

No file under `WIP/` was created, edited or deleted in this round. All writes targeted the six
files in `.tmp/goal-llm-harness/ponder/` and this document. `git diff --stat -- WIP` printed
nothing, and the frozen component mtimes remain before this session's start (for example
`TholdemRuntimeAgent.cs` 2026-11-06T23:11, `AgentExecutor.cs` 2026-11-05T05:56,
`ArtifactAgentMemory.cs` 2026-11-06T11:58, `LiveModelConfiguration.cs` 2026-11-09T12:14).
`dotnet build` did refresh the referenced projects' ignored `bin/`/`obj/` outputs; no WIP source
or test file was written. Pre-existing working-tree changes (including `Wip.slnx`) predate this
round and were not touched here.

## 4. Exact command the orchestrator must run for the live match

Run from the repository root in a process where the gate flag is set. The loader allowlists only
`DEEPSEEK_API_KEY` and `DEEPSEEK_BASE_URL` (`WIP/Wip.Tholdem.ModelHarness/LiveModelConfiguration.cs:107-111`),
so `THOLDEM_LIVE_MODEL=1` must be in the process environment, not the secret file.

```powershell
$env:THOLDEM_LIVE_MODEL = "1"
dotnet run --project .tmp/goal-llm-harness/ponder/Ponder.csproj -c Release -- --live --secret-file <path-to-secret-file> --seed 1099511927776 --hands 20 --model deepseek-chat --out .tmp/goal-llm-harness/ponder/live
```

The secret file is a `KEY=VALUE` file containing `DEEPSEEK_API_KEY=...` and optionally
`DEEPSEEK_BASE_URL=...`. The host prints only the loaded names
(`secretNames=DEEPSEEK_API_KEY[,DEEPSEEK_BASE_URL]`), never a value.

Expected artifacts:

- `.tmp/goal-llm-harness/ponder/live/ponder-session.json` — `modelKind=live`, 20 hands,
  `servedModels` from the DeepSeek responses, calls with request/response/usage/latency.
- `.tmp/goal-llm-harness/ponder/live/llm-transcripts.jsonl` — one canonical line per model call.
- `history/hand-00001.json` … `hand-00020.json` and `history/session.json` from `MatchSimulator`.
- `artifacts/tholdem-ponder-live/` — memory and tool-call artifacts from the local store.

Use a **fresh** `--out` for the live run: `LlmTranscriptStore.AppendAsync` appends to the
transcript file (`WIP/Wip.Tholdem.ModelHarness/LlmTranscriptStore.cs:38-46`) and the memory session
persists under the same directory, so reusing an output directory mixes runs. Do not reuse
`.tmp/goal-llm-harness/ponder/smoke`; it is scripted evidence (`modelKind=scripted`).

## 5. What I could not make work / did not observe

- **No live match was run.** Per the round instruction, no live model call was made and no secret
  file was passed. Live DeepSeek latency, usage, served-model ids, tool calls and exception mapping
  are therefore unobserved; the orchestrator's run is the first live evidence.
- **The scripted double does not call tools.** It answers only legal fold/check JSON
  (`ScriptedChatModel.cs:8-14,42-84`), so the smoke shows `toolCalls=0`. The tool composition is
  copied from `RuntimeAgentTestSupport.cs:204-226` and compiles, but actual tool invocation remains
  to be observed live.
- **Streaming is not recorded.** `AgentExecutor.ExecuteAsync` uses `CompleteAsync`
  (`WIP/Wip.Runtime/Runtime/AgentExecutor.cs:54-74`), and `RecordingChatModel.StreamAsync` is a
  transparent delegation (`RecordingChatModel.cs:88-99`). A future streaming host must add stream
  recording.
- **No partial session file on abort.** The session JSON is written only after
  `MatchSimulator.Run` returns (`Program.cs:197-212`). A model exception is caught by
  `AgentExecutor` and becomes the agent's documented fallback, so it does not abort
  (`AgentExecutor.cs:69-73`; `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:133-137`), but an
  unexpected abort (for example an unparseable prior-hand file) leaves the transcript file and no
  session file. I did not add partial-session recovery.
- **The required two-hand smoke yields one acting turn** for the reasons above, so its
  `modelTurns=1` should not be read as a 20-hand-turn count. The supplemental scripted run gives 19
  turns; neither is live evidence.
