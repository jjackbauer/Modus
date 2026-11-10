# Round 9 adversarial falsification — Falsifier B

- **Repository**: `C:\Users\ricar\dev\Modus` (branch `WIP`)
- **Scope**: six delivery-wide claims C1–C6 (`docs/goal-tholdem/round-9` adversarial pass)
- **Method**: source reading, the documented supplementary runner over `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll`, the Release CLI, direct `MatchSimulator` via the existing `SimProbe` scratch binary, the running viewer at `http://127.0.0.1:5099`, and F# Interactive against the current Release assemblies (no product file was edited; scratch artifacts live under `.tmp/round9-*`)
- **Not used**: `dotnet test` (local VSTest aborts with Win32Exception (5), as documented in `docs/tholdem/known-limits.md`); `pwsh` (absent, Windows PowerShell used)

## Verdict table

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| C1 | Supported | `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:127-136` writes the canonical hand bytes and `:149-157` the manifest; `WIP/Wip.Tholdem.Host/Commands/PlayCommand.cs:37-48` calls the same `MatchSimulator.Run` and moves the staging directory. Observed: two CLI `play --seed 1234 --hands 40 --agent0 random-legal --agent1 random-legal` runs produced 41 files each, 0 differing SHA-256 hashes, `session.json` sha256 `77d3041f40a96c06e0109a68b0519323605168011e2ab05fc2483eacb376cbc7` both times; `SimProbe session ... 1234 40` (direct `MatchSimulator.Run`) produced the same 41 files, 0 byte differences; seed 1235 produced session `tholdem-6b25cd4f99aed53e` and manifest `2425f5015e88ebc2a5d1d3287f21a45366527b3f587f27a43c54aa2b4691bcd3`; two default-agent CLI runs at seed 777/25 hands produced 26 files each with 0 differences. | Ran the CLI twice with the same seed; ran the direct simulator probe with the same seed and compared every file; ran a second seed; reran the CLI with the default agent pair; searched for any other writer of session files. | The observed files are byte-identical for the same seed and agents across both entry points, and different seeds produce different manifests and hand files. No counterexample was found. |
| C2 | Supported | `WIP/Wip.Tholdem.Agents/Learning/PromotionVerifier.cs:33-58` re-derives both conditions; `TrainingLoop.cs:125-160` only writes the pointer for `verified && decision.Promoted`; `WIP/Wip.Tholdem.Host/Commands/PromoteCommand.cs:26-53` always calls the verifier and writes only after `verified && decision.Promoted && --yes`; `StrategyArtifactStore.WriteChampionPointer` (`Storage/StrategyArtifactStore.cs:98-106`) has exactly one product call site, `PromoteCommand.cs:50` (grep over `WIP`; the test helper is a separate method). Observed: a passing synthetic CLI evaluation with `promote` and no `--yes` exited 3 and left `active-champion.json` unchanged; the same with `--yes` exited 0 and rewrote it to the challenger; a real seed-7 evaluation (`LCB -0.33`, margin 5) with `--yes` exited 3 and left the pointer unchanged; `train --iterations 1 --seed 7` printed `promoted=false verified=true outcome=rejected` and the pointer stayed at v1; no `Process`, `ProcessStartInfo`, `git`, `HttpClient`, `Publish` or `Deploy` code exists under the Tholdem projects (grep). | Forged a passing `cli-…json` evaluation and ran `promote` without `--yes`; ran it with `--yes`; ran a real below-margin evaluation with `--yes`; searched every pointer writer and every process/git/publish path. | The verifier is on both persistence paths, a refused/absent confirmation cannot move the pointer, and there is no merge/publish/deploy code. The only pointer write that does not call the verifier is the documented fresh-directory bootstrap of the fixed version-1 champion (`TrainingLoop.cs:175-183`), which is not a challenger promotion and is asserted by `TrainingLoopTests.Run_GivenAlwaysPromotePolicy_...`; it does not let a challenger bypass the gate. |
| C3 | Falsified | `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs:8` is `public readonly record struct AgentTurnContext(ulong SessionSeed, int HandNumber, int Seat, int ActionIndex)` and `IPokerAgent.cs:14-17` passes it to every agent. `MatchSimulator.cs:49` derives the hand seed as `SeedDerivation.Derive(options.Seed, hand, 0, 0, 0)` and `:68-72` passes `options.Seed` (the session seed) to the agent; `Live/LiveMatch.cs:222` does the same. Using only public product APIs, with the session seed and hand number an agent receives, I reconstructed the deal: `SeedDerivation.Derive(1234UL, 1, 0, 0, 0UL)` = `13187247165317579491`, and `SeededDeck.Create` of that seed gives `seat0=9s 4c seat1=2c 5c`, `board=5h 6c Ac Ks Qd`, exactly the recorded `hand-00001.json` of the seed-1234 session. (Observed via `dotnet fsi -r:` against `Wip.Tholdem.dll` and `Wip.Tholdem.Agents.dll`.) The "spectator snapshot type is not reachable from `WIP/Wip.Tholdem.Agents`" sub-claim does hold: grep for `SpectatorSnapshot`/`Wip.Tholdem.Web` in that project returns no matches. | Wrote the cheat-agent data flow as a source path: `IPokerAgent.ActAsync` receives `context.SessionSeed` and `context.HandNumber`; `SeedDerivation.Derive` + `SeededDeck.Create` are public; `HeadsUpHandEngine.cs:98-100` deals `deck[0],deck[2]` to seat 0 and `deck[1],deck[3]` to seat 1 and `:540-552` deals the board from `deck[4..]`. Executed the same public calls to print the reconstructed hole cards and board and compared them to the recorded artifact. | The session seed is exposed in an agent-facing API and is sufficient to reconstruct both seats' hole cards and the board order. The spectator-snapshot sub-claim is true, but the compound claim is false. |
| C4 | Supported | `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:104-190` truncates the observation (`MaxObservationCharacters`, default 4000, marker `...[truncated N chars]`), `:213-331` rejects malformed/illegal actions and `:337-356` returns a legal check/fold/call fallback; `WIP/Wip.Runtime/Runtime/RuntimeToolInvoker.cs:57-116` rejects unknown tools, enforces permissions via `PermissionGuard.cs:13-43`, and truncates tool observations (`ToolInvocationOptions.MaxObservationCharacters`, default 8000); `AgentExecutor.cs:60` sends the caller-supplied `request.Tools` and `:78-123` dispatches tool calls by name only. Observed: the full suite passed `TOTAL 177 PASSED 177 FAILED 0 SKIPPED 0`, including `ActAsync_GivenDenyingPermissionEvaluator_ExpectedPermissionDeniedToolResultAndLegalAction`, `ActAsync_GivenInjectionInMemory_ExpectedUnchangedToolsAndPermissions`, `ActAsync_GivenMalformedModelOutput_ExpectedMalformedViolationAndLegalFallback`, `ActAsync_GivenRaiseAboveMaximum_...`, `ActAsync_GivenTinyObservationBudget_...`, `ActAsync_GivenHundredKilobyteFinalMessage_...`; `ToolContractTests` asserts the four fixed definitions/descriptors and `artifact.read` requirements. | Tried an out-of-range raise, malformed JSON, an unknown action, an unknown tool, a denying permission evaluator, an injected instruction in model output and in memory, a 100 KB final message and a tiny observation budget; read the fallback branches against the engine's legality projection. | Every attempted malformed/illegal action was rejected deterministically with a legal fallback, the tool/permission lists are caller-supplied data the model cannot mutate, denial is enforced by `PermissionGuard`, and both observation surfaces truncate. No counterexample was found. (The runtime agent is composed by the test pipeline, not by the CLI/viewer; the claim is about this code path.) |
| C5 | Supported | Grep over the Tholdem sources and viewer assets found no `HttpClient`, `WebRequest`, `Socket`, `TcpClient`, DNS or external URL; the only URL is the loopback constant `http://127.0.0.1:5099` (`WIP/Wip.Tholdem.Web/TholdemWebHost.cs:21`). The running viewer's `GET /api/status` returned `{"mode":"spectator",...,"offline":true}`; `wwwroot/viewer/index.html`/`viewer.css` reference only `viewer.css`, `app.js` and a `data:` favicon. A monitored Release `train --iterations 60 --seed 99` run showed 0 non-loopback TCP connections and 0 UDP endpoints in every sample (6 samples), and its redirected stdout reached `final champion: tholdem-strategy-v1-f100e793`. No wallet/payment/account/betting integration exists (grep). No Tholdem publish/deploy step exists in `.github/workflows`, `tools`, `scripts` or the Makefile (only `tools/run-wip-tests.ps1:38` lists the test project); no `dotnet pack`/`dotnet publish` step exists in any workflow. | Scanned for network APIs and external URLs; polled `Get-NetTCPConnection`/`Get-NetUDPEndpoint` for the CLI process during a long training run; scanned for money/account integrations; searched CI and tooling for publish/pack/deploy. | The built product has no outbound-network code and the observed execution opened no remote sockets; there is no real-money/account/betting integration and no repository release/publish/deploy automation. The generic SDK verbs `dotnet publish`/`dotnet pack` exist for any project (and `Wip.Tholdem.Web.csproj` has `CopyToPublishDirectory` for static assets), but no Tholdem-specific delivery path, package or deployment is wired or produced; the claim's delivery-path reading holds. |
| C6 | Falsified | `docs/tholdem/viewer.md:98-99` says "Agents receive only `PlayerObservation` … no seed, no deck, no opponent cards" and `docs/tholdem/engine-rules.md:156` says "`PlayerObservation` is the only projection an agent receives", but `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs:8` exposes `SessionSeed` to every `IPokerAgent` (`IPokerAgent.cs:14-17`), and C3 above shows the seed reconstructs both hole cards and the board. The committed boundary test cannot catch this: `WIP/Wip.Tholdem.Tests/AgentInformationBoundaryTests.cs:48-50` forbids `\b(TableState|HandRecord|HoleCardsSeat0|HoleCardsSeat1|Seed|Deck)\b`, `AgentTestSupport.cs:141-153` includes `AgentTurnContext.cs` in the scan, yet the regex has 0 matches in `SessionSeed` (observed `[regex]::Matches('SessionSeed','\bSeed\b').Count` = 0; the unbounded substring test returns 1). Also `docs/tholdem/viewer.md:79` marks `seat0Agent?`/`seat1Agent?` optional, but `POST /api/live` with `{"seed":1,"hands":1,"delayMs":0}` returned 400 `{"error":"unknown-agent","seat":0,"agent":null}` (`TholdemWebHost.cs:66-74`). The other C6 parts hold: `docs/plan/test-waivers.json` has 0 Tholdem failure/aborted entries (`expectedTotals['Wip.Tholdem.Tests']` = 177), 177 `[Fact]` methods exist, no `Skip=`/`#if`/`Assert.Skip` exists in the project, and the runner reported `SKIPPED 0`. | Read the docs' observation-boundary claims; searched for a code path that supplies the seed to an agent; ran the boundary test's own regex against the actual identifier; compared the documented `POST /api/live` optional fields with the HTTP response; enumerated the waiver register and all test attributes. | At least one claim in `docs/tholdem/*.md` (the agents-only-see-`PlayerObservation`/no-seed statement) does not match the code, and the committed boundary test is green precisely because its regex cannot match `SessionSeed`. The waiver/skip sub-claims are clean, but the compound C6 claim is false. |

## Detail and observed output

### Method and limits

- Full suite: `dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll`. Final observed line: `TOTAL 177 PASSED 177 FAILED 0 SKIPPED 0` (exit code 0).
- `dotnet test` was not run; the documented local VSTest failure is reproduced in `docs/tholdem/known-limits.md:23-28` and the dispatch forbids it.
- Scratch directories: `.tmp/round9-cli`, `.tmp/round9-probe`, `.tmp/round9-c2`, `.tmp/round9-train`, `.tmp/round9-net2`, `.tmp/round9-cli-default`. No product, test, doc or configuration file was changed.

### C1 — determinism

Source path:

1. `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:49` derives the per-hand seed from the session seed; `:127-136` serializes the canonical record bytes and writes `hand-NNNNN.json`; `:149-157` writes `session.json`.
2. `WIP/Wip.Tholdem.Host/Commands/PlayCommand.cs:37-40` calls `MatchSimulator.Run` with the selected agents and a staging `HistoryDirectory`; `:42-48` moves the staging directory to `sessions/<sessionId>/`.
3. There is no second hand-history writer in the CLI.

Observed:

- Two CLI runs: `play --seed 1234 --hands 40 --agent0 random-legal --agent1 random-legal --data-dir .tmp\round9-cli\a` and `...\b`. Both printed `session: tholdem-2dbc6d629fbed3f4` and `manifest sha256: 77d3041f40a96c06e0109a68b0519323605168011e2ab05fc2483eacb376cbc7`; the two session directories each contained 41 files and the SHA-256 comparison reported `sameSeedFileDiffs=0`.
- Direct simulator: `.tmp/tholdem-verify/SimProbe/bin/Release/net10.0/SimProbe.exe session .tmp/round9-probe 1234 40` printed the per-hand hashes and the final `manifest=77d3041f40a96c06e0109a68b0519323605168011e2ab05fc2483eacb376cbc7`; comparing that directory to the CLI session directory reported 41 files each and `matchSimulatorVsCliDiffs=0`.
- Different seed: `play --seed 1235 --hands 40 ...` printed `session: tholdem-6b25cd4f99aed53e` and `manifest sha256: 2425f5015e88ebc2a5d1d3287f21a45366527b3f587f27a43c54aa2b4691bcd3`; `hand-00001.json` SHA-256 was `97f334fee11370cd2a4db57f9f34675e8d44253ad72da663adc3cdbe774040ad` for seed 1234 and `d0e0f3dff24f1e2b8bea2adcd6c9781fa8af9cad1f232e4200cbe433588fb03f` for seed 1235 (observed different).
- Default agents: two CLI runs at `--seed 777 --hands 25` produced 26 files each with 0 hash differences (`agents: tight-aggressive, random-legal`).

No counterexample found. Verdict: **Supported**.

### C2 — promotion gate

Source path:

1. `WIP/Wip.Tholdem.Agents/Learning/PromotionVerifier.cs:33-58` re-derives `LCB > margin` and `seeds >= minSeeds` from the report and criteria.
2. `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:125-132` runs the verifier on every iteration; `:156-160` writes `active-champion.json` only when `verified && decision.Promoted`.
3. `WIP/Wip.Tholdem.Host/Commands/PromoteCommand.cs:26-53` recomputes the policy decision, always calls `PromotionVerifier.Verify`, and writes the pointer only for `verified && decision.Promoted && --yes`.
4. `grep` for `WriteChampionPointer|ChampionFileName|active-champion` across `WIP` found the only product writer at `PromoteCommand.cs:50`; `TrainingLoop` writes the pointer at `:159` (verified promotion) and `:181` (fresh-directory bootstrap of the fixed v1 default).

Observed:

- A passing synthetic CLI evaluation (`LCB 40`, seeds 30, margin 5): `promote --challenger tholdem-strategy-v2-aaaaaaaa --incumbent tholdem-strategy-v1-bbbbbbbb` printed `promotion requires explicit confirmation; re-run with --yes`, exited 3, and `active-champion.json` was unchanged. The same with `--yes` printed `promoted: ...` and rewrote the pointer to the challenger.
- Real flow: `train --iterations 1 --seed 7 --data-dir .tmp\round9-train` printed `iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-0ceaa8fd promoted=false verified=true outcome=rejected meanBbPer100=-19.38 lcb=-59.50 seeds=24` and left `{"strategyId":"tholdem-strategy-v1-f100e793","iteration":0}`. `evaluate --challenger tholdem-strategy-v2-0ceaa8fd --seeds 4` printed `mean bb/100: 2.50  LCB: -0.33  margin: 5.00`, `decision: lcb-below-margin`, `verifier: verified (rejection-justified)`; `promote --challenger ... --yes` then printed `promotion refused: lcb-below-margin` / `verifier: rejection-justified` and exited 3, pointer unchanged.
- The always-promote control is exercised by `PromotionGateTests.Verify_GivenRederivedCriterion_ExpectedControlsRejectedAndPassesAccepted` and `TrainingLoopTests.Run_GivenAlwaysPromotePolicy_ExpectedVerificationFailureAndUnchangedChampion`, both green in the 177/177 run.
- No `Process`, `ProcessStartInfo`, `git `, `HttpClient`, `Publish` or `Deploy` code exists in the Tholdem projects (grep). `.github/workflows/tests-ci.yml` contains only `dotnet build`/`dotnet test` for Tholdem (lines 34, 53, 70, 133, 139, 187, 193); no pack/publish/deploy step.

The only verifier-free pointer write is the documented fresh-directory bootstrap (`TrainingLoop.cs:175-183`, asserted by `TrainingLoopTests`), which materializes the fixed version-1 champion; it cannot be used to promote a challenger and is not a challenger path. No counterexample to the challenger gate was found. Verdict: **Supported**.

### C3 — hidden information

The claim is compound; the spectator-snapshot part is true, the seed part is false.

Observed source facts:

- `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs:8`: `public readonly record struct AgentTurnContext(ulong SessionSeed, int HandNumber, int Seat, int ActionIndex);`
- `WIP/Wip.Tholdem.Agents/IPokerAgent.cs:14-17`: every agent's `ActAsync(PlayerObservation observation, AgentTurnContext context, ...)` receives it.
- `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:68-72`: `new AgentTurnContext(options.Seed, hand, actingSeat, actionCounts[actingSeat])` — the session seed.
- `WIP/Wip.Tholdem.Web/Live/LiveMatch.cs:222`: `new AgentTurnContext(Seed, hand, seat, actionCounts[seat])` — the session seed.
- `WIP/Wip.Tholdem.Agents/SeedDerivation.cs:9-23` and `WIP/Wip.Tholdem/Cards/SeededDeck.cs:16-37` are public, deterministic, and depend only on those values.
- `WIP/Wip.Tholdem/Engine/HeadsUpHandEngine.cs:98-100`: seat 0 gets `deck[0]`, `deck[2]`; seat 1 gets `deck[1]`, `deck[3]`; `:540-552` deals the board from `deck[4..]`.

Executed reconstruction (F# Interactive against the current Release assemblies):

```text
cmd /c 'echo let s = Wip.Tholdem.Agents.SeedDerivation.Derive(1234UL, 1, 0, 0, 0UL) in let d = Wip.Tholdem.Cards.SeededDeck.Create s in printfn "handSeed=%d seat0=%O %O seat1=%O %O" s d.[0] d.[2] d.[1] d.[3] | dotnet fsi -r:C:/Users/ricar/dev/Modus/WIP/Wip.Tholdem/bin/Release/net10.0/Wip.Tholdem.dll -r:C:/Users/ricar/dev/Modus/WIP/Wip.Tholdem.Agents/bin/Release/net10.0/Wip.Tholdem.Agents.dll'
```

Observed output:

```text
handSeed=13187247165317579491 seat0=9s 4c seat1=2c 5c
```

The recorded `.tmp/round9-cli/a/sessions/tholdem-2dbc6d629fbed3f4/hand-00001.json` contains:

```json
{"handNumber":1,"seed":13187247165317579491,...,"holeCardsSeat0":["9s","4c"],"holeCardsSeat1":["2c","5c"],"board":["5h","6c","Ac","Ks","Qd"],...}
```

and the second F# call printed `hole=9s 2c 4c 5c board=5h 6c Ac Ks Qd` — the opponent's hole cards and the full board, reconstructed from the seed the agent receives.

The spectator part: `grep -R "SpectatorSnapshot|Wip.Tholdem.Web" WIP/Wip.Tholdem.Agents` returned no matches, and the project does not reference the Web project. The claim is nevertheless false because the seed sub-claim fails. Verdict: **Falsified**.

### C4 — untrusted data

Source path:

- `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:104-105` canonicalizes and truncates the observation; `:181-190` appends `...[truncated N chars]`.
- `:144-148` rejects a final message over 16 KiB; `:213-331` parses exactly one action object and validates its kind and raise range; `:337-356` returns check/fold/call, each checked against `LegalActionKinds`.
- `WIP/Wip.Runtime/Runtime/RuntimeToolInvoker.cs:57-60` rejects an unknown tool; `:80-103` calls `PermissionGuard.Ensure` and returns `permission-denied: ...`; `:182-191` truncates the tool observation (default 8000).
- `WIP/Wip.Runtime/Runtime/PermissionGuard.cs:21-42` evaluates every descriptor permission and throws `PermissionDeniedException` on denial.
- `WIP/Wip.Runtime/Runtime/AgentExecutor.cs:60` sends `request.Tools`; `:78-90` executes returned tool calls by name through the invoker. There is no model-controlled mutation of the tools list.

Attempts and observed results (all green in the 177/177 suite):

- `ActAsync_GivenRaiseAboveMaximum_ExpectedIllegalActionViolationAndLegalFallback` — out-of-range raise → `illegal-action:` and legal fold.
- `ActAsync_GivenMalformedModelOutput_ExpectedMalformedViolationAndLegalFallback` — prose/invalid action → `malformed:` and legal fold/check.
- `ActAsync_GivenDenyingPermissionEvaluator_ExpectedPermissionDeniedToolResultAndLegalAction` — `hand-history-query` denied with `permission-denied:`, unknown `delete-the-table` with `unknown-tool:`, then legal call.
- `ActAsync_GivenInjectionInModelOutput_ExpectedIllegalActionRejected` and `ActAsync_GivenInjectionInMemory_ExpectedUnchangedToolsAndPermissions` — injection text is carried as data; the two requests have the same four tools and the same descriptor permissions.
- `ActAsync_GivenTinyObservationBudget_ExpectedBoundedTruncatedTask` — 200-character budget yields the `...[truncated N chars]` marker and a bounded task (the test asserts `N = canonical.Length - 200`).
- `ActAsync_GivenHundredKilobyteFinalMessage_ExpectedMalformedFallback` — 100 KB final message rejected/deterministically falls back.
- `ToolContractTests` fixes the four tool definitions and the `artifact.read` requirements.

No counterexample found. The runtime agent is composed by the test pipeline (no CLI/viewer composition), which is a scope note, not a violation of the claim. Verdict: **Supported**.

### C5 — offline safety

Observed:

- Source/asset scan: `grep -R "HttpClient|WebRequest|Socket|TcpClient|https?://"` over `WIP/Wip.Tholdem*` sources and viewer assets found only `TholdemWebHost.cs:21` (`http://127.0.0.1:5099`). `index.html` references `viewer.css`, `app.js` and a `data:` favicon only.
- `GET http://127.0.0.1:5099/api/status` returned `{"mode":"spectator","dataDirectory":"C:\\Users\\ricar\\dev\\Modus\\artifacts\\tholdem","sessions":2,"strategies":4,"liveMatches":0,"offline":true}`.
- Network monitor: a Release `train --iterations 60 --seed 99` run was polled for its lifetime (`Get-NetTCPConnection -OwningProcess`, `Get-NetUDPEndpoint`); observed `tcpRemote=0 udpEndpoints=0` across 6 samples, and the redirected stdout printed all 60 iteration lines plus `final champion: tholdem-strategy-v1-f100e793`.
- Real-money/account scan: no wallet/payment/account/betting integration in the Tholdem sources; the only uses of "betting" are the poker `Street`/pot-odds domain text.
- Delivery scan: no `dotnet pack`/`dotnet publish`/`gh release`/deploy step in `.github/workflows`, `tools`, `scripts` or the Makefile for Tholdem; `tools/run-wip-tests.ps1:38` only lists `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj`. The `Wip.Tholdem.Web.csproj` `CopyToPublishDirectory` applies only to its static viewer assets.

The generic SDK `dotnet publish`/`dotnet pack` verbs exist for any project, but no Tholdem release, package or deployment is wired or produced. No counterexample to the intended offline/delivery claim was found. Verdict: **Supported**.

### C6 — test and docs integrity

Observed test/waiver facts:

- `TOTAL 177 PASSED 177 FAILED 0 SKIPPED 0` from the documented supplementary runner.
- 177 `[Fact]` attributes in `WIP/Wip.Tholdem.Tests`; no `Skip=`, `Assert.Skip`, `#if`/`#endif`.
- `docs/plan/test-waivers.json`: `expectedTotals["Wip.Tholdem.Tests"] = 177`, `tholdemFailures = 0`, `tholdemAborted = 0`.
- `tools/run-wip-tests.ps1:38` includes the Tholdem test project; the security CI steps at `.github/workflows/tests-ci.yml:139` and `:193` list the runtime-agent test classes.

Observed docs mismatch:

- `docs/tholdem/viewer.md:98-99`: "Agents receive only `PlayerObservation` … no seed, no deck, no opponent cards." `docs/tholdem/engine-rules.md:156`: "`PlayerObservation` is the only projection an agent receives."
- The code passes `AgentTurnContext` with `SessionSeed` to every `IPokerAgent` (`AgentTurnContext.cs:8`, `IPokerAgent.cs:14-17`), and the C3 reconstruction above shows the seed yields both seats' cards and the board. The docs statement therefore does not match the code.
- The committed test `AgentInformationBoundaryTests` (`:42-64`) scans `AgentTurnContext.cs` (`AgentTestSupport.cs:141-153`), but its forbidden regex `\b(TableState|HandRecord|HoleCardsSeat0|HoleCardsSeat1|Seed|Deck)\b` has 0 matches in `SessionSeed` (observed `[regex]::Matches('SessionSeed','\bSeed\b').Count` = `0`), so the suite is green while the boundary it names is broken.
- Secondary contract mismatch: `docs/tholdem/viewer.md:79` marks `seat0Agent?`/`seat1Agent?` optional, but `POST /api/live` with `{"seed":1,"hands":1,"delayMs":0}` returned `400` with body `{"error":"unknown-agent","seat":0,"agent":null}` (`TholdemWebHost.cs:66-74`; `LiveAgentCatalog.cs:20-36`).

The waiver and skip sub-claims are clean, but the claim that the `docs/tholdem/*.md` claims match the code is false, and the suite's boundary test does not detect the mismatch. Verdict: **Falsified**.

## Summary

- C1 Supported, C2 Supported, C3 Falsified, C4 Supported, C5 Supported, C6 Falsified.
- Root cause shared by C3/C6: `AgentTurnContext` carries the session seed to every `IPokerAgent`, and `SeedDerivation` + `SeededDeck` are public and deterministic, so an agent can reconstruct both seats' hole cards and the board order. The docs and the committed boundary test both claim/assume otherwise.
- No product, test or configuration file was modified in producing this report; the only documentation file created is this report, and all generated run artifacts are under `.tmp/round9-*`.