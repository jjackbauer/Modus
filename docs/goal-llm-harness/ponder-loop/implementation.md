# Implementation — live 6-seat Ponder + Enhance loop (scratch)

Scratch host: `.tmp/goal-llm-harness/loop/` (project `Loop.csproj` + `Program`). Scope: new files under that directory only, plus this document. No WIP product file, no existing test and no Ponder host file was edited; the Ponder host's `RecordingChatModel.cs`, `RecordingPokerAgent.cs` and `PonderSession.cs` are linked into the loop assembly with `<Compile Include="..\ponder\..." Link="Shared\..." />`.

## What it is

One round = one measured Ponder match, one live refiner call, one frozen-gate evaluation:

1. **Ponder** — the real pipeline (runtime agent at seat 0, `TightAggressiveAgent` baselines at seats 1..N-1) plays `hands` hands at `seats` seats through `MatchSimulator.Run` with `HistoryDirectory = <out>/round-r/history` and `roundSeed = SeedDerivation.Derive(seed, r, 0, 0, 0)`. Every model call is recorded by the linked `RecordingChatModel` into `<out>/round-r/llm-transcripts.jsonl`, every turn by `RecordingPokerAgent`, and the canonical session document is written to `<out>/round-r/ponder-session.json`.
2. **Enhance** — the round's `hand-*.json` are parsed with `HandRecordJson.Parse`, reduced by `FailureSignatureExtractor.Extract`, and the seat-0 metrics become a `SessionAnalysis` (Vpip 0, not measured). A per-round `LlmRefinementPolicy` over the **raw** model (never the recorder) proposes one genome from `RefinementContext(champion, analysis, signatures, r)` and its transcript goes to `<out>/round-r/refiner/llm-transcripts.jsonl`; the last record is re-parsed with `LlmProposalParser` for the model's `reason`.
3. **Gate** — `MatchEvaluator` (frozen) evaluates candidate vs champion heads-up at `eval-seeds` x `eval-hands` from `SeedDerivation.Derive(seed, 9000 + r, 0, 0, 0)`; `StatisticalPromotionPolicy` decides and `PromotionVerifier` re-derives the verdict. `promoted = verified && decision.Promoted`.
4. **Persist** — canonical evaluation report in `<out>/evaluations/`, candidate artifact in `<out>/strategies/`, and on promotion the champion is rewritten in `<out>/refiner-data/active-champion.json` plus `<out>/refiner-data/strategies/<id>.json`.

The start state is prepared before round 1 exactly as pre-registered: artifact `0.70/0.70/0.12/1.0` at iteration 0 and the training loop's own pointer format `{"strategyId":...,"iteration":0}`, so the refiner's lineage reader resolves a real chain.

A phase that fails leaves the rest of the round null: `LlmRefinementRefusalException` (or any transport/shape failure) is recorded as `error` and stops the loop, and the report is still written. No candidate is fabricated.

## Build

~~~powershell
dotnet build .tmp/goal-llm-harness/loop/Loop.csproj -c Release
~~~

Observed (final build):

~~~text
  Loop -> C:\Users\ricar\dev\Modus\.tmp\goal-llm-harness\loop\bin\Release\net10.0\Loop.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
~~~

## Scripted smoke (wiring only, no network, no secret)

~~~powershell
dotnet run --project .tmp/goal-llm-harness/loop/Loop.csproj -c Release -- --scripted --rounds 2 --seats 2 --hands 2 --eval-seeds 2 --eval-hands 2 --out .tmp/goal-llm-harness/loop/smoke
~~~

Observed stdout (exit 0):

~~~text
startChampion=tholdem-strategy-v1-c9d1a24b genome=0.70/0.70/0.12/1.0 dataDirectory=C:\Users\ricar\dev\Modus\.tmp\goal-llm-harness\loop\smoke\refiner-data
round=1 seats=2 hands=2 roundSeed=13032400259741297018 ponderTurns=5 toolCalls=0 toolNames=- legalActions=5 errors=0 incumbent=tholdem-strategy-v1-c9d1a24b candidate=tholdem-strategy-v2-03017818 rationale=llm-proposal gateSeeds=2 gateMean=6.25 gateLcb=-30.4993 verdict=lcb-below-margin verified=true promoted=false champion=tholdem-strategy-v1-c9d1a24b
round=2 seats=2 hands=2 roundSeed=2312279730489829546 ponderTurns=1 toolCalls=0 toolNames=- legalActions=1 errors=0 incumbent=tholdem-strategy-v1-c9d1a24b candidate=tholdem-strategy-v2-17790bb2 rationale=llm-proposal gateSeeds=2 gateMean=0 gateLcb=-48.9991 verdict=lcb-below-margin verified=true promoted=false champion=tholdem-strategy-v1-c9d1a24b
modelKind=scripted
completedRounds=2
finalChampion=tholdem-strategy-v1-c9d1a24b
reportJson=C:\Users\ricar\dev\Modus\.tmp\goal-llm-harness\loop\smoke\strategies-report.json
reportMarkdown=C:\Users\ricar\dev\Modus\.tmp\goal-llm-harness\loop\smoke\strategies-report.md
~~~

The report the run wrote (`<out>/strategies-report.md`):

~~~text
# Ponder + Enhance loop - scripted

> Wiring smoke only: the model is a local double. This run is NOT live evidence.

- modelKind: scripted
- seed: 1099512227776
- rounds: 2 completed of 2 requested
- table: 2 seats, 2 hands per round
- gate: 2 seeds x 2 hands per round (heads-up, frozen evaluator)
- final champion: tholdem-strategy-v1-c9d1a24b (raise/call/bluff/sizing = 0.7/0.7/0.12/1)

| Round | Seats | Hands | Ponder turns | Tool calls | Incumbent | Candidate | Gate mean | Gate LCB | Verdict | Verified | Promoted | Champion after |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 2 | 2 | 5 | 0 | tholdem-strategy-v1-c9d1a24b (0.7/0.7/0.12/1) | tholdem-strategy-v2-03017818 (0.8/0.4/0.1/1) | 6.25 | -30.4993 | lcb-below-margin | true | false | tholdem-strategy-v1-c9d1a24b |
| 2 | 2 | 2 | 1 | 0 | tholdem-strategy-v1-c9d1a24b (0.7/0.7/0.12/1) | tholdem-strategy-v2-17790bb2 (0.8/0.4/0.1/1) | 0 | -48.9991 | lcb-below-margin | true | false | tholdem-strategy-v1-c9d1a24b |

## Arising strategies

1. round 1 - tholdem-strategy-v2-03017818 raise/call/bluff/sizing = 0.8/0.4/0.1/1; rationale llm-proposal; reason scripted smoke; gate mean 6.25, LCB -30.4993, lcb-below-margin, promoted false
2. round 2 - tholdem-strategy-v2-17790bb2 raise/call/bluff/sizing = 0.8/0.4/0.1/1; rationale llm-proposal; reason scripted smoke; gate mean 0, LCB -48.9991, lcb-below-margin, promoted false
~~~

`strategies-report.json` (canonical, UTF-8 without BOM) carries `modelKind:"scripted"`, the requested/completed round counts, `stoppedError`, the final champion, a `strategies` array (id, parent, artifact path, genome, rationale code, reason, promoted, gate mean/LCB) and every round's full record: seats, hands, roundSeed, session and transcript paths, model turns, tool calls, tool names, legal actions, model actions, fallbacks, violations, errors, analysis, the six named signals with counts/samples/rates/directions, incumbent, candidate, gate seeds/mean/LCB/upper bound/win counts/decision/verified/verifier/promoted, evaluation path, champion after and `error`/`errorDetail`.

Scripted mode details, stated plainly:

- `ScriptedHybridModel` answers by the fixed system prompt: `poker player` -> `{"action":"fold"}`, `strategy refiner` -> `{"raiseThreshold":0.8,"callThreshold":0.4,"bluffFrequency":0.1,"sizingMultiplier":1.0,"reason":"scripted smoke"}`; served model id is `scripted`. A request carrying neither prompt throws instead of guessing.
- The fold answer is exactly the specified smoke reply. When fold is not legal the runtime agent's documented fallback acts, which is why round 1 shows `modelActions=2 fallbacks=3 violations=3`; nothing was replaced by the loop.
- Round 2 shows one turn and one action because the scripted fold ends both hands immediately (alternating button: seat 0 never acted in hand 1).
- The live refinement policy refuses to be constructed without `THOLDEM_LIVE_MODEL` and a non-empty `DEEPSEEK_API_KEY`, so scripted mode passes an `environmentLookup` that returns literal `1` / `scripted-smoke-not-a-secret`. It reads no file and no process variable; it does not weaken the live gate, which stays process-environment-only in `--live`. This run is wiring evidence, never live evidence.

## Stop-path probe (observed, then removed)

A deliberately corrupt `<out>/refiner-data/harness/memory.json` (wrong content hash) was pre-seeded to make the refiner refuse after the Ponder match:

~~~powershell
$dir = '.tmp/goal-llm-harness/loop/smoke-stop'
New-Item -ItemType Directory -Force (Join-Path $dir 'refiner-data/harness') | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $dir 'refiner-data/harness/memory.json'), '{"schema":"tholdem.harness-memory.v1","contentHash":"deadbeef","entries":[]}', $utf8)
dotnet run --project .tmp/goal-llm-harness/loop/Loop.csproj -c Release -- --scripted --rounds 2 --seats 2 --hands 2 --eval-seeds 2 --eval-hands 2 --out $dir
~~~

Observed (exit 2):

~~~text
round=1 seats=2 hands=2 roundSeed=13032400259741297018 ponderTurns=5 toolCalls=0 toolNames=- legalActions=5 errors=0 incumbent=tholdem-strategy-v1-c9d1a24b candidate=- rationale=- gateSeeds=- gateMean=- gateLcb=- verdict=- verified=- promoted=- champion=- error=HarnessMemoryException
stopped=HarnessMemoryException: The harness memory '...\harness\memory.json' content hash does not match its entries (stored 'deadbeef', computed '4f53cda1...').
modelKind=scripted
completedRounds=0
finalChampion=tholdem-strategy-v1-c9d1a24b
reportJson=...\smoke-stop\strategies-report.json
reportMarkdown=...\smoke-stop\strategies-report.md
~~~

The report was written with `completedRounds:0`, `stoppedError:"HarnessMemoryException"`, the round's Ponder metrics and signals preserved, `candidate:null`, `gate:null`, and `strategies:[]`. No candidate was fabricated. The probe directory was deleted after capturing this output.

## Files created

| Path | Purpose |
|---|---|
| `.tmp/goal-llm-harness/loop/Loop.csproj` | scratch project; links the three Ponder host files, references the WIP assemblies |
| `.tmp/goal-llm-harness/loop/Program.cs` | argument parsing, live/scripted dispatch, start state, round loop, round lines, report paths |
| `.tmp/goal-llm-harness/loop/RoundRunner.cs` | one round: Ponder composition, analysis/signatures, refiner call, gate, persistence, error capture |
| `.tmp/goal-llm-harness/loop/LoopReport.cs` | canonical `strategies-report.json` and `strategies-report.md` |
| `.tmp/goal-llm-harness/loop/LoopRecords.cs` | `LoopSettings`, `RoundRecord`, `RoundEvaluation` |
| `.tmp/goal-llm-harness/loop/ScriptedHybridModel.cs` | scripted-only `IChatModel` double |
| `.tmp/goal-llm-harness/loop/Canonical.cs` | invariant canonical JSON primitives for the reports |
| `.tmp/goal-llm-harness/loop/smoke/...` | the scripted smoke outputs listed above (artifacts, strategies, evaluations, refiner-data, per-round sessions/transcripts/history) |
| `docs/goal-llm-harness/ponder-loop/implementation.md` | this document |

All created text files are UTF-8 without BOM (52 text files checked byte-wise: no BOM, no UTF-16). `git status --porcelain WIP` shows no tracked WIP modification from this work.

## Exact live command for the orchestrator

The caller must set `THOLDEM_LIVE_MODEL=1` in the process that runs the loop; the loop refuses before reading the secret file when it is absent. The secret file is a `KEY=VALUE` file read by `LlmSecretFile.LoadIntoEnvironment` (allowlisted names: `DEEPSEEK_API_KEY`, optional `DEEPSEEK_BASE_URL`); the loop prints names only.

~~~powershell
$env:THOLDEM_LIVE_MODEL = '1'
dotnet run --project .tmp/goal-llm-harness/loop/Loop.csproj -c Release -- --live --secret-file <path-to-secret-file> --seed 1099512227776 --rounds 4 --seats 6 --hands 20 --eval-seeds 24 --eval-hands 20 --out .tmp/goal-llm-harness/loop/live
~~~

Expected shape: four 6-seat Ponder rounds of 20 hands (LLM at seat 0, five `baseline-1..5` tight-aggressive agents), one live refiner proposal per round, and a 24-seed x 20-hand frozen gate per round. Exit 0 when all four rounds complete; exit 2 with a written report when a refusal or transport failure stops the loop.

## Not exercised here

- The live path (`--live`) was **not** run and no secret was read; the live command above is the orchestrator's to execute.
- The promotion write path (`active-champion.json` rewrite plus `refiner-data/strategies/<id>.json`) is implemented but not exercised: the scripted gate cannot promote (2 seeds, LCB far below the 5 bb/100 margin), and forcing it would require fabricating an evaluation. The four-round live run is where a promotion can occur.
- `HarnessMemoryStore` entries are read by the refiner but never written by this loop, so the refiner's memory section stays empty by design.
