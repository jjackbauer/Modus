# Falsification report — C (round 9)

- Repository: `C:\Users\ricar\dev\Modus`; branch `WIP`; date 2026-11-06.
- Role: independent falsifier; scope C1–C6.
- Method: read product/tests/docs; ran the documented supplementary xUnit runner, the built CLI, the existing `SimProbe` scratch probe (the `MatchSimulator` path), HTTP calls to the running viewer at `http://127.0.0.1:5099`, source scans and `git` checks. No product/test/doc/config edit; only this file and scratch outputs under `.tmp/falsify-c/`.
- Verdict legend: **Supported** = no counterexample found after the attempts below; **Falsified** = an observed contradiction.

## Verdict table

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| C1 | Supported | CLI `play --seed 42 --hands 20 --agent0 random-legal --agent1 random-legal` wrote 21 files under `sessions/tholdem-2bd1d99348a60b79` (20 `hand-*.json` + `session.json`); a second CLI run into another data directory produced the same 21 file names and SHA-256s. `SimProbe session` (calls `MatchSimulator.Run` directly) produced the same 21 byte-identical files and `manifest=fbbd5384f8d0b8a8c61264cad53bda29fd630142bd21bf230da8f52843016a58`. CLI seed 43 produced `tholdem-d232f2173bb641ad`, manifest `03c92e9621ab101ccb9f7cad4f18658ee99d2ca54b5eddd9afab8deded1839b3`, and a different `hand-00001.json`. CLI default agents, 5 hands, twice: 6 identical files and byte-identical report JSON. `SimulationReproducibilityTests` 2/2, `PlayCommandTests` 3/3; full suite 177/177. | Two separate CLI processes; CLI vs direct `MatchSimulator` writing session files; seed 42 vs seed 43; default agents twice. No byte difference was found. | Same seed and same named agents feed the same `SeedDerivation` and xorshift deck; `HandRecordCanonicalWriter` has a fixed key order and invariant numbers, and the simulator writes no clock/GUID. The observed hashes match across processes and across the two entry points. |
| C2 | Supported | `PromoteCommand.cs:26-31` re-derives the policy verdict and always calls `PromotionVerifier.Verify`; the pointer is written only at `PromoteCommand.cs:50`. `TrainingLoop.cs:125-132` verifies each iteration and writes `active-champion.json` only at `TrainingLoop.cs:156-160` when `verified && promoted`. `StrategyArtifactStore.WriteChampionPointer` is internal and its only caller is `PromoteCommand.cs:50`. CLI: a failing evaluation with `--yes` exited 3 with `promotion refused: lcb-below-margin` and `verifier: rejection-justified`, pointer hash unchanged; a verifying evaluation without `--yes` exited 3 with `promotion requires explicit confirmation; re-run with --yes`, pointer unchanged; the same verifying evaluation with `--yes` exited 0 and changed the pointer. `TrainingLoopTests` 7/7 (includes the always-promote negative control), `PromotionGateTests` 4/4; full suite 177/177. | Failing evaluation with `--yes`; verifying evaluation without `--yes`; verifying evaluation with `--yes`; source scan of Host/Web/Agents for `Process`, `HttpClient`, `git`, `publish`, `deploy`. No bypass of the verifier or of the confirmation gate was found. | Every evaluation-driven pointer change is behind `PromotionVerifier` and `--yes`. No auto-merge, publish or deploy code exists in Host/Web/Agents. A fresh data directory writes a version-1 starting-champion pointer (`TrainingLoop.cs:173-183`) with no evaluation; that is bootstrap initialization to the root champion, not promotion of a challenger (no incumbent/challenger pair exists yet), and the code/docs/tests describe it as the starting pointer. |
| C3 | **Falsified** | `AgentTurnContext.cs:8` declares `public readonly record struct AgentTurnContext(ulong SessionSeed, int HandNumber, int Seat, int ActionIndex)`. `IPokerAgent.cs:14-17` passes this context to every agent. `MatchSimulator.cs:68-72` constructs it with `options.Seed` (the session seed); `LiveMatch.cs:222` does the same with the match seed. `SeedDerivation.cs:9`, `HeadsUpHandEngine.cs:26` and `PlayerObservation.cs:56` are public. The boundary test `AgentInformationBoundaryTests.cs:48-50` forbids `\bSeed\b`, which does not match `SessionSeed`, and the suite is 177/177. Under `WIP/Wip.Tholdem.Agents`, `SpectatorSnapshot` and `Wip.Tholdem.Web` have 0 matches, and the viewer test scans all agent sources for spectator identifiers; that half holds. | Source-path trace of the agent-facing API plus the boundary test; the direct malicious-agent composition is inferred (see the C3 section) but was not compiled because source edits are forbidden. | The session seed is exposed through the agent-facing `AgentTurnContext`. With the public `SeedDerivation.Derive`, public `HeadsUpHandEngine.Start` and public `PlayerObservation.ForSeat`, an agent can derive the hand seed and read the other seat's hole cards. The claim is conjunctive, so the clean spectator half does not save it. |
| C4 | Supported | `TholdemRuntimeAgent.cs:105-106` truncates the observation JSON, `:144-148` caps the final message at 16 KB, `:150-160` parses/validates and falls back, `:213-304` parses, `:306-331` validates legality, `:337-356` falls back check→fold→call. `RuntimeToolInvoker.cs:57-59` rejects unknown tools, `:82` calls `PermissionGuard.Ensure`, `:100-103` returns `permission-denied`, `:182-190` truncates tool results. `TholdemRuntimeAgentTests` covers malformed output, illegal raise, injection in final output and memory, a denying evaluator, unknown tool, tiny observation and a 100 KB final message; targeted runner 11/11, `ToolContractTests` 2/2, `SessionHandStoreTests` 2/2, full suite 177/177. | Injected final message; illegal raise above maximum; malformed JSON; 100 KB final message; injection stored in memory; denying permission evaluator; unknown tool name; observation budget 200 chars; all exercised through the working-tree tests. No contradiction was found. | Tools are a fixed array passed to the executor; no model or hand-history text path mutates permission descriptors or the tool list. Parse/validate is deterministic against the observation, fallback is always checked against the observation, permission denial returns before execution, and both observation and tool-result text are bounded. |
| C5 | **Falsified** | The first two clauses hold: no product `HttpClient`/`Socket`/`Process` path (the only `System.Net` hit is `WIP/Wip.Tholdem.Web/Recording/ReplayTableRenderer.cs:2` for `WebUtility.HtmlEncode`), viewer assets have no external reference, `LiveModelGate.cs:13-31` is not called by product code, CLI `play` with the gate off exits 0, and no money/account integration exists (only `WIP/Wip.Tholdem/Engine/Street.cs:4` "betting street"). But a packaging/publish path does exist: `WIP/Wip.Tholdem.Web/Wip.Tholdem.Web.csproj:12` sets `CopyToPublishDirectory="PreserveNewest"`, all four Tholdem projects set `PackageId`, `Directory.Build.props` sets `IncludeSymbols`, `PackageVersion` and package metadata, and `dotnet pack WIP/Wip.Tholdem/Wip.Tholdem.csproj -c Release -o .tmp/falsify-c/pack --no-restore` exited 0 producing `Wip.Tholdem.0.0.2.nupkg` (27,756 B) and `.snupkg` (35,340 B); `dotnet publish WIP/Wip.Tholdem.Host/Wip.Tholdem.Host.csproj -c Release -o .tmp/falsify-c/publish --no-restore` exited 0. | Network/process scan, external-asset scan, gate-off CLI, and release/publish scan; then `dotnet pack` and `dotnet publish`, both of which exited 0 and produced local outputs. | The no-network and no-payment clauses are supported, but "no release, publish or deploy path exists" is false: the projects are configured as packable and the Web project carries publish metadata; the concrete `dotnet pack` and `dotnet publish` commands both succeed. That is a publish/packaging path even though it is local and no remote registry or deployment automation is wired. |
| C6 | **Falsified** | Suite: `TOTAL 177 PASSED 177 FAILED 0 SKIPPED 0`; 0 hits for `Skip=`, `[Fact(Skip`, `[Theory(Skip`, `Assert.True(true)` or `Assert.False(false)` in `WIP/Wip.Tholdem.Tests`. Waivers: `expectedTotals["Wip.Tholdem.Tests"]=177`; 0 Tholdem failure entries and 0 Tholdem aborted entries (27 failure entries overall, none Tholdem). Docs/code: `docs/tholdem/viewer.md:98` says "Agents receive only `PlayerObservation`" and `docs/tholdem/engine-rules.md:156` says "`PlayerObservation` is the only projection an agent receives", while `IPokerAgent.cs:14-17` passes `AgentTurnContext` and `AgentTurnContext.cs:8` exposes `SessionSeed`; `AgentInformationBoundaryTests.cs:48-50` does not catch it. Repo state: `git ls-files` over the five Tholdem project directories returns 0 tracked files; `git status --porcelain` shows every Tholdem project as `??`, while `docs/tholdem/known-limits.md:43` calls it "The committed suite". | Ran the suite, parsed the waivers, scanned tests for skips/weak patterns, compared the docs lines to the code, and checked git tracking. The docs/code contradiction and the zero-committed-tests state were found. | At least one claim in `docs/tholdem/*.md` contradicts the code, so C6's final clause is falsified. In addition, no Tholdem test is committed at all, so the docs' "committed suite" statement is false; the green suite is a working-tree artifact only. |

## C1 — determinism (Supported)

**Implementation read.** `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:46-57` derives the hand seed with `SeedDerivation.Derive(options.Seed, hand, 0, 0, 0UL)`, sets `buttonSeat = options.AlternateButton ? (hand - 1) % 2 : 0`, and starts the hand. `MatchSimulator.cs:127-136` builds `HandRecord.From(state).ToCanonicalJson()`, hashes the UTF-8 bytes and writes `hand-NNNNN.json`. `MatchSimulator.cs:149-157` computes the session id (seed, hands and both agent names), builds `session.json`, hashes it and writes it. `WIP/Wip.Tholdem.Host/Commands/PlayCommand.cs:37-48` calls the same `MatchSimulator.Run` with `HistoryDirectory: staging`, then moves the directory to `sessions/<sessionId>`. `WIP/Wip.Tholdem/History/HandRecordCanonicalWriter.cs:11-69` writes a fixed key order; `:72-84` render integers and invariant-culture numbers; no clock, GUID or culture-dependent value is in the document.

**Observed.** Two independent CLI processes with `--seed 42 --hands 20 --agent0 random-legal --agent1 random-legal` into separate data directories wrote 21 files each; every relative file name and SHA-256 matched. `SimProbe session` (the direct `MatchSimulator.Run` path, not the CLI) wrote the same 21 byte-identical files and printed `manifest=fbbd5384f8d0b8a8c61264cad53bda29fd630142bd21bf230da8f52843016a58`; the CLI run printed the same manifest hash. CLI seed 43 printed `manifest sha256: 03c92e9621ab101ccb9f7cad4f18658ee99d2ca54b5eddd9afab8deded1839b3`, a different session id (`tholdem-d232f2173bb641ad`) and a different hand 1. Two runs of the CLI default agents (`tight-aggressive` vs `random-legal`, 5 hands) produced 6 identical files and byte-identical report JSON.

**Attempted counterexamples.** Cross-process determinism (two CLI processes), cross-entry-point determinism (CLI vs direct `MatchSimulator` session files), and seed sensitivity (42 vs 43). No difference was observed. The test classes `SimulationReproducibilityTests` (2/2) and `PlayCommandTests` (3/3) also passed in the 177/177 run.

**Inference.** The observed equality is expected from the fixed writer and seed-only derivation; the evidence is still observed bytes and hashes, not a proof over all agents.

## C2 — promotion gate (Supported)

**Implementation read.** `WIP/Wip.Tholdem.Agents/Learning/PromotionVerifier.cs:30-58` re-derives `clearsMargin` and `enoughSeeds` from the report and refuses a promoted decision that fails either (and an unjustified rejection). `TrainingLoop.cs:125-132` runs the verifier for every iteration; `:132` sets `promoted = verified && decision.Promoted`; the pointer is written only at `:156-160`. `WIP/Wip.Tholdem.Host/Commands/PromoteCommand.cs:21-50` obtains confirmation, re-derives the decision with `StatisticalPromotionPolicy`, always runs `PromotionVerifier.Verify` at `:27-31`, and writes the pointer only at `:50` after `verified && decision.Promoted && confirmed`. `WIP/Wip.Tholdem.Host/Storage/StrategyArtifactStore.cs:98-106` is the only other pointer writer; it is `internal` and is called only from `PromoteCommand.cs:50` (source scan of Host call sites).

**Observed CLI paths** (data directory copied from `.tmp/round8-cli-final3`; all writes were in `.tmp/falsify-c/c2`):

```text
promote --challenger tholdem-strategy-v2-0ceaa8fd --yes   (existing failing evaluation)
promotion refused: lcb-below-margin
verifier: rejection-justified
exit=3
pointer unchanged: True

promote --challenger tholdem-strategy-v2-0ceaa8fd         (same failing evaluation, no --yes)
promotion requires explicit confirmation; re-run with --yes
promotion refused: lcb-below-margin
verifier: rejection-justified
exit=3
pointer unchanged: True
```

A fresh evaluation was then produced by the CLI itself with `--margin -100000 --seeds 24 --hands 2`; it printed `decision: promoted` and `verifier: verified (verified)`. Promoting that verifying evaluation without `--yes` still exited 3 with only the confirmation message and did not change the pointer; adding `--yes` exited 0, printed `promoted: tholdem-strategy-v2-0ceaa8fd replaces tholdem-strategy-v1-f100e793`, and changed `active-champion.json`. The negative control `TrainingLoopTests.Run_GivenAlwaysPromotePolicy_ExpectedVerificationFailureAndUnchangedChampion` (`WIP/Wip.Tholdem.Tests/Learning/TrainingLoopTests.cs:11-58`) passed 7/7 in the class run; `PromotionGateTests` passed 4/4.

**No auto-merge/publish/deploy.** A source scan over `WIP/Wip.Tholdem.Host`, `WIP/Wip.Tholdem.Web` and `WIP/Wip.Tholdem.Agents` found no `Process`, `Process.Start`, `HttpClient`, `WebRequest`, `Socket`, `TcpClient`, `git `, `nuget`, `Publish` or `Deploy` code path; the only writes are local session/strategy/evaluation/ledger/pointer files. The same scan over `.github/workflows` found no publish, push or deploy step for Tholdem.

**Boundary considered.** `TrainingLoop.LoadChampion` (`WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:173-183`) writes a version-1 `active-champion.json` on a fresh directory before any iteration. This is a bootstrap pointer to the root strategy, not a promotion of a challenger: there is no incumbent/challenger evaluation, no `PromotionDecision`, and the operation is explicitly documented as the starting champion (`docs/tholdem/training-promotion-runbook.md:36-38`). I did not count it as a promotion bypass; the evaluation-driven writers are correctly gated.

## C3 — hidden information (Falsified)

**Observed code surface.** `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs:8`:

```csharp
public readonly record struct AgentTurnContext(ulong SessionSeed, int HandNumber, int Seat, int ActionIndex);
```

`WIP/Wip.Tholdem.Agents/IPokerAgent.cs:14-17` is the agent contract:

```csharp
ValueTask<PokerAction> ActAsync(
    PlayerObservation observation,
    AgentTurnContext context,
    CancellationToken cancellationToken);
```

`WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:68-72` constructs the context with `options.Seed` — the same session seed that `MatchSimulator.cs:49` uses to derive the hand seed; `WIP/Wip.Tholdem.Web/Live/LiveMatch.cs:222` does the same with the live-match seed. Both `SeedDerivation.Derive` (`WIP/Wip.Tholdem.Agents/SeedDerivation.cs:9`) and `HeadsUpHandEngine.Start` (`WIP/Wip.Tholdem/Engine/HeadsUpHandEngine.cs:26`) are public; `PlayerObservation.ForSeat` (`WIP/Wip.Tholdem/History/PlayerObservation.cs:56`) is public and its `OwnHoleCards` is the requested seat's two cards.

**Boundary test is blind to the field.** `WIP/Wip.Tholdem.Tests/AgentInformationBoundaryTests.cs:43-64` scans the agent files with the regex at `:48-50`:

```csharp
@"\b(TableState|HandRecord|HoleCardsSeat0|HoleCardsSeat1|Seed|Deck)\b"
```

`\bSeed\b` does not match `SessionSeed` (the `S` follows a word character, so there is no word boundary), and the full suite is 177/177. The runtime test `ActAsync_GivenObservation_ExpectedNoHiddenInformationInChatRequests` (`WIP/Wip.Tholdem.Tests/Runtime/TholdemRuntimeAgentTests.cs:160-215`) checks only the chat request/messages, not the `AgentTurnContext` the agent implementation receives.

**Inferred exploit composition.** An `IPokerAgent` can read `context.SessionSeed` and `context.HandNumber`; `SeedDerivation.Derive(sessionSeed, hand, 0, 0, 0)` is the exact expression `MatchSimulator` uses for the hand seed; `HeadsUpHandEngine.Start(handSeed, hand, ...)` returns the `TableState`; and `PlayerObservation.ForSeat(state, 1 - seat).OwnHoleCards` returns the other seat's hole cards. That is a concrete public-API composition, but I did not compile a malicious agent or add a probe because the task forbids editing anything but this report. The source lines above are the observed evidence; the composition is inferred.

**What holds.** `SpectatorSnapshot` and `Wip.Tholdem.Web` have 0 references under `WIP/Wip.Tholdem.Agents`; `TholdemWebViewerTests.AgentSources_GivenSpectatorBoundaryScan...` (`WIP/Wip.Tholdem.Tests/TholdemWebViewerTests.cs:552-578`) scans all agent sources for `SpectatorSnapshot`, `TholdemWebHost` and `LiveMatch` and passes. `POST /api/live` on the running viewer returned 201 and a snapshot with `holeCardsSeat0: {2h, Ts}` and `holeCardsSeat1: {8h, 2d}` — the spectator projection intentionally shows both seats; it is not the agent path. The claim is conjunctive, so C3 is Falsified by the seed exposure even though the spectator half is clean.

## C4 — untrusted data (Supported)

**Implementation read.** `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:104-106` renders the observation JSON and truncates it to `MaxObservationCharacters`; `:144-148` rejects a final message longer than 16 KB as malformed; `:150-160` parses and validates the final action; `:213-304` parses exactly one action object with a bounded `amountTo`; `:306-331` re-checks `LegalActionKinds` and the `MinRaiseTo..MaxRaiseTo` range; `:337-356` falls back to a legal check, then fold, then call. `WIP/Wip.Runtime/Runtime/RuntimeToolInvoker.cs:57-59` returns `unknown-tool` for an unregistered name, `:82` calls `PermissionGuard.Ensure` before execution, `:100-103` returns `permission-denied` when the evaluator denies, and `:182-190` truncates tool-result text at `ToolInvocationOptions.MaxObservationCharacters` (default 8,000). `WIP/Wip.Tholdem.Agents/Runtime/TholdemToolCatalog.cs:68-78` declares the two session-read tools with `artifact.read`.

**Observed tests.** `dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll TholdemRuntimeAgentTests` printed `TOTAL 11 PASSED 11 FAILED 0 SKIPPED 0`. The class covers: an illegal raise above maximum -> `illegal-action:` violation and legal fold; malformed output -> `malformed:` and legal check/fold; injection text in the final message -> rejected and fallback; injection stored in memory -> the tool list in the second request is unchanged (`equity-calculator`, `pot-odds`, `hand-history-query`, `opponent-statistics`) and the permission strings are unchanged; denying permission evaluator -> `permission-denied:` and `unknown-tool:`; 200-character observation budget -> truncated task; 100 KB final message -> malformed fallback. `ToolContractTests` 2/2 and `SessionHandStoreTests` 2/2 passed; full suite 177/177.

**Attempted counterexamples.** Injections in model output and memory, malformed JSON, an out-of-range raise, a 100 KB final message, an unknown tool, a denied `artifact.read`, and a tiny observation budget. None changed the registered tool list or permission descriptors; each malformed/illegal action produced the documented deterministic fallback. The hand-history surface (`SessionHandView`) has no seed, deck or opponent-hole-card member (`WIP/Wip.Tholdem.Agents/Runtime/SessionHandView.cs:8-16`), and `SessionHandStore.ToView` projects through `PlayerObservation.ForSeat` (`WIP/Wip.Tholdem.Agents/Runtime/SessionHandStore.cs:138-160`). The hand-history text itself is data-only: `RuntimeToolInvoker` serializes the result (`WIP/Wip.Runtime/Runtime/RuntimeToolInvoker.cs:167-180`), and `AgentExecutor` appends it as a `ChatMessage.Tool` (`WIP/Wip.Runtime/Runtime/AgentExecutor.cs:85-90`) without interpreting it as tool or permission configuration.

**Inference.** The permission descriptors are static data created by `TholdemToolCatalog.CreateDescriptors()`; no code path reads model output or hand-history text to mutate them. The only "tool list" sent to the model is the fixed `_tools` array copied in the constructor (`TholdemRuntimeAgent.cs:75-76`), and the executor passes `request.Tools` unchanged (`WIP/Wip.Runtime/Runtime/AgentExecutor.cs:59-62`). This is read from the code; the tests above exercise the observable behavior.

## C5 — offline safety (Falsified)

**Observed scans.**

```text
product_network_or_process_hits=1
WIP\Wip.Tholdem.Web\Recording\ReplayTableRenderer.cs:2: using System.Net;
money_or_account_hits=1
WIP\Wip.Tholdem\Engine\Street.cs:4: /// The betting street a hand is currently on. ...
external_asset_hits=0
```

The single `System.Net` hit is `WebUtility.HtmlEncode` in the replay renderer, not a network client. The three viewer assets (`index.html` 9,359 B, `app.js` 36,900 B, `viewer.css` 25,174 B) contain no external `http://`, `https://`, `//cdn` or `@import url` references. `WIP/Wip.Tholdem.Agents/Runtime/LiveModelGate.cs:13-31` returns disabled unless `THOLDEM_LIVE_MODEL` is `1`/`true` and `DEEPSEEK_API_KEY` is non-empty; a scan of `WIP/Wip.Tholdem*` product sources found no caller of `LiveModelGate` (only `WIP/Wip.Tholdem.Tests/Runtime/LiveModelGateTests.cs`), so the product does not construct a live model at all. Two CLI `play` runs with `THOLDEM_LIVE_MODEL` and `DEEPSEEK_API_KEY` cleared exited 0 (`play_exit_gate_off=0`). `GET http://127.0.0.1:5099/api/status` returned `{"mode":"spectator",...,"offline":true}`; the viewer test `ViewerAssets_GivenOfflineViewerDirectory_ExpectedNoExternalReferencesAndRelativeIndexAssets` (`WIP/Wip.Tholdem.Tests/TholdemWebViewerTests.cs:504-550`) also passes.

**Release/publish/deploy path — false.** No `.github/workflows` job and no product source calls `nuget push`, `docker push`, `kubectl` or a deploy step, and no remote registry/deployment target is wired. But the repository does configure a packaging/publish path: `WIP/Wip.Tholdem.Web/Wip.Tholdem.Web.csproj:12` sets `CopyToPublishDirectory="PreserveNewest"` for the viewer assets; all four Tholdem projects set `PackageId`; `Directory.Build.props` sets `IncludeSymbols`, `SymbolPackageFormat`, `PackageVersion` and license/repository metadata. The concrete commands below both exited 0.

**Attempted counterexample.**

```text
dotnet pack WIP/Wip.Tholdem/Wip.Tholdem.csproj -c Release -o .tmp/falsify-c/pack --no-restore
EXIT=0; Wip.Tholdem.0.0.2.nupkg (27,756 B), Wip.Tholdem.0.0.2.snupkg (35,340 B)

dotnet publish WIP/Wip.Tholdem.Host/Wip.Tholdem.Host.csproj -c Release -o .tmp/falsify-c/publish --no-restore
EXIT=0; Wip.Tholdem.Host -> .tmp/falsify-c/publish/
```

The outputs are local scratch files, and the journal's mitigation note (`docs/goal-tholdem/journal.md:468`) mentions a `dotnet publish` directory only as a place to run the local viewer without locking `bin/Release`. That does not make the command absent: a NuGet package and a publish output are a release/publish path, and the package/publish metadata is in the project files. The first two C5 clauses are supported; the third is falsified.

**No real-money/account integration.** No payment, wallet, account, deposit, casino or external-betting API exists in the product; the only hit for the search terms is the word "betting" in a `Street` doc comment. Chips are plain integers in the engine and simulator.

## C6 — test and docs integrity (Falsified)

**Test suite.** The documented supplementary runner printed `TOTAL 177 PASSED 177 FAILED 0 SKIPPED 0`. A scan of `WIP/Wip.Tholdem.Tests/**/*.cs` for `Skip=`, `[Fact(Skip`, `[Theory(Skip`, `Assert.True(true)` and `Assert.False(false)` returned `skip_or_weak_hits=0`.

**Waivers.** Parsing `docs/plan/test-waivers.json` gave `expectedTotals["Wip.Tholdem.Tests"]=177`, `failure_count=27`, `tholdem_failure_waivers=0` and `tholdem_aborted_waivers=0`. The register therefore contains no Tholdem failure waiver; this sub-claim holds.

**Docs vs code.** `docs/tholdem/viewer.md:98` says:

```text
Agents receive only `PlayerObservation` (`WIP/Wip.Tholdem/History/PlayerObservation.cs`), which has
```

`docs/tholdem/engine-rules.md:156` says:

```text
`PlayerObservation` is the only projection an agent receives. It contains: hand number, street,
```

The code disagrees: `WIP/Wip.Tholdem.Agents/IPokerAgent.cs:14-17` passes `PlayerObservation` **and** `AgentTurnContext`; `AgentTurnContext.cs:8` has a public `SessionSeed`. `IPokerAgent.cs:7-8` even calls both "hidden-information safe projections". The boundary test's `\bSeed\b` regex (`AgentInformationBoundaryTests.cs:48-50`) does not catch `SessionSeed`, so the 177/177 suite is green while the C3 invariant is false. This is a direct docs-vs-code contradiction and is sufficient to falsify C6's final clause.

**Committed tests.** `git ls-files` over `WIP/Wip.Tholdem`, `WIP/Wip.Tholdem.Agents`, `WIP/Wip.Tholdem.Host`, `WIP/Wip.Tholdem.Web` and `WIP/Wip.Tholdem.Tests` returned `tracked_tholdem_files=0`. `git status --porcelain` lists all five project directories as `?? WIP/Wip.Tholdem*/`. Nevertheless `docs/tholdem/known-limits.md:43` says "The committed suite drives the real HTTP surface ...". No Tholdem test is committed to git, so the green 177/177 result is a working-tree artifact, not a committed-suite result. If the first C6 clause is read literally, it is vacuous (there is no committed Tholdem test to skip); the docs' "committed suite" statement is false regardless.

**Attempted counterexample.** Ran the suite, parsed the waiver register, scanned the tests for skip/weak patterns, compared each docs line to the code, and checked git tracking. The docs/code contradiction and the zero-tracked-files state were observed.

## Observed vs inferred

**Directly observed.**

- The exact CLI stdout/exit codes, file names, file counts and SHA-256 equality reported in the C1 and C2 sections.
- `SimProbe` output for the direct `MatchSimulator` path and the matching manifest hash.
- The supplementary runner totals (`177/177`, `11/11`, `7/7`, `2/2`) and the absence of skip/weak patterns.
- The source lines cited for `AgentTurnContext`, `IPokerAgent`, `MatchSimulator`, `SeedDerivation`, `HeadsUpHandEngine`, `PlayerObservation`, `TholdemRuntimeAgent`, `RuntimeToolInvoker`, `PromotionVerifier`, `TrainingLoop`, `PromoteCommand` and `StrategyArtifactStore`.
- `git ls-files` = 0 tracked Tholdem files and `git status --porcelain` = `??` for all five Tholdem project directories.
- The waiver parse (`expectedTotals["Wip.Tholdem.Tests"]=177`, 0 Tholdem failure/aborted entries).
- The scan counts for network/process, external viewer assets, money/account terms and release automation.
- `GET http://127.0.0.1:5099/api/status` = `offline:true`; `POST /api/live` = 201 and a spectator snapshot with both seats' hole cards.
- `dotnet pack` and `dotnet publish` exit 0 with local scratch outputs.
- The docs lines quoted above.

**Inferred.**

- The C3 exploit composition: `AgentTurnContext.SessionSeed` + `SeedDerivation.Derive` + `HeadsUpHandEngine.Start` + `PlayerObservation.ForSeat` yields the other seat's hole cards. The public surface is observed; the exploit itself was not compiled because only this report may be edited.
- The C1 equality is attributable to seed-only derivation and the fixed canonical writer; the bytes are observed, the reason is inferred from the code.
- The C2 classification of the fresh-directory version-1 pointer as bootstrap initialization rather than a promotion decision; the write is observed, the classification is a semantic reading of "promotion".
- The C5 observation that `dotnet pack`/`dotnet publish` succeed is direct; whether a generic SDK command plus package/publish metadata counts as a "release, publish or deploy path" is an interpretation of the claim's wording. I treat it as a packaging/publish path because the project files explicitly configure `PackageId`/`CopyToPublishDirectory` and the commands succeed.

## Verification limits

- `dotnet test` was not run. The documented local limitation (Win32Exception (5) in VSTest) applies; the supplementary runner over the compiled test assembly is the documented substitute. CI was not triggered or observed.
- The live model was not called: no `DEEPSEEK_API_KEY` / `THOLDEM_LIVE_MODEL` live path was used; the gate-off CLI run is evidence only for the disabled configuration.
- No JavaScript was executed and no Playwright run was made; the viewer evidence is HTTP/static-response plus the running viewer at 5099. The review's browser-only claims are not re-verified here.
- No source, test, docs or configuration file was edited except this report. Scratch outputs are under `.tmp/falsify-c/`; nothing was written under `%TEMP%`.
- The C3 malicious-agent composition is inferred from public APIs; a runtime demonstration would require adding code, which this task forbids.
- `dotnet pack` / `dotnet publish` were run into `.tmp/falsify-c/`; they did not touch `bin/Release` or the viewer process.

## Selected command outputs

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 177 PASSED 177 FAILED 0 SKIPPED 0
exit code 0

... -- ... Wip.Tholdem.Tests.dll TholdemRuntimeAgentTests
TOTAL 11 PASSED 11 FAILED 0 SKIPPED 0

... -- ... Wip.Tholdem.Tests.dll TrainingLoopTests
TOTAL 7 PASSED 7 FAILED 0 SKIPPED 0

... -- ... Wip.Tholdem.Tests.dll SimulationReproducibilityTests
TOTAL 2 PASSED 2 FAILED 0 SKIPPED 0

... -- ... Wip.Tholdem.Tests.dll ToolContractTests
TOTAL 2 PASSED 2 FAILED 0 SKIPPED 0

... -- ... Wip.Tholdem.Tests.dll SessionHandStoreTests
TOTAL 2 PASSED 2 FAILED 0 SKIPPED 0

... -- ... Wip.Tholdem.Tests.dll PromotionGateTests
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

```text
CLI play seed 42 hands 20 random-legal vs random-legal:
session: tholdem-2bd1d99348a60b79
seed: 42  hands: 20  manifest sha256: fbbd5384f8d0b8a8c61264cad53bda29fd630142bd21bf230da8f52843016a58
file comparison: cli1 files=21 cli2 files=21 sim files=21; cli1==cli2: True; cli1==sim: True

CLI play seed 43 hands 20:
session: tholdem-d232f2173bb641ad
seed: 43  hands: 20  manifest sha256: 03c92e9621ab101ccb9f7cad4f18658ee99d2ca54b5eddd9afab8deded1839b3
first hand differs: True

CLI play default agents seed 42 hands 5, twice:
files=6 identical=True; report_identical=True
```

```text
promote --challenger tholdem-strategy-v2-0ceaa8fd --yes   (failing evaluation)
promotion refused: lcb-below-margin
verifier: rejection-justified
exit=3; pointer unchanged: True

promote --challenger tholdem-strategy-v2-0ceaa8fd         (failing evaluation, no --yes)
promotion requires explicit confirmation; re-run with --yes
promotion refused: lcb-below-margin
verifier: rejection-justified
exit=3; pointer unchanged: True

evaluate --challenger tholdem-strategy-v2-0ceaa8fd --seeds 24 --hands 2 --margin -100000
mean bb/100: 1.04  LCB: -1.00  margin: -100000.00
decision: promoted
verifier: verified (verified)
exit=0

promote ... (verifying evaluation, no --yes)
promotion requires explicit confirmation; re-run with --yes
exit=3; pointer unchanged: True

promote ... --yes
promoted: tholdem-strategy-v2-0ceaa8fd replaces tholdem-strategy-v1-f100e793
exit=0; pointer changed: {"strategyId":"tholdem-strategy-v2-0ceaa8fd","iteration":0}
```

```text
AgentTurnContext.cs:8: public readonly record struct AgentTurnContext(ulong SessionSeed, int HandNumber, int Seat, int ActionIndex);
AgentInformationBoundaryTests.cs:48-50 regex forbids \bSeed\b; SessionSeed does not match.
Agents source scan for SpectatorSnapshot|Wip.Tholdem.Web: matches=0.
AgentInformationBoundaryTests + runtime agent hidden-info test: suite green (177/177).
```

```text
product_network_or_process_hits=1  (only ReplayTableRenderer.cs:2 using System.Net)
money_or_account_hits=1            (only Street.cs:4 "betting street")
external_asset_hits=0
play_exit=0; play_exit_gate_off=0
GET /api/status => {"mode":"spectator","dataDirectory":"C:\\Users\\ricar\\dev\\Modus\\artifacts\\tholdem","sessions":2,"strategies":4,"liveMatches":0,"offline":true}
dotnet pack ... EXIT=0; Wip.Tholdem.0.0.2.nupkg; Wip.Tholdem.0.0.2.snupkg
dotnet publish ... EXIT=0; output directory .tmp/falsify-c/publish/
```

```text
expected_tholdem=177
failure_count=27
tholdem_failure_waivers=0
tholdem_aborted_waivers=0
tracked_tholdem_files=0
skip_or_weak_hits=0
git status: ?? WIP/Wip.Tholdem/ ?? WIP/Wip.Tholdem.Agents/ ?? WIP/Wip.Tholdem.Host/ ?? WIP/Wip.Tholdem.Tests/ ?? WIP/Wip.Tholdem.Web/
docs/tholdem/viewer.md:98 "Agents receive only `PlayerObservation`"
docs/tholdem/engine-rules.md:156 "`PlayerObservation` is the only projection an agent receives"
docs/tholdem/known-limits.md:43 "The committed suite drives the real HTTP surface"
```

## Bottom line

- C1 **Supported**: same-seed bytes were identical across two CLI processes and the direct `MatchSimulator` session writer; different seeds changed the manifest.
- C2 **Supported**: both evaluation-driven champion-pointer writers run `PromotionVerifier`; `promote` refused without `--yes` even when the verifier passed, and refused a failing report with `--yes`.
- C3 **Falsified**: the agent-facing `AgentTurnContext` exposes `SessionSeed`, and the public derivation/engine/observation APIs make the other seat's hole cards reachable; the green boundary test does not catch it.
- C4 **Supported**: the working-tree security tests exercise malformed/illegal actions, injection, permission denial, unknown tools and truncation without contradiction.
- C5 **Falsified**: no network/payment path was found and the gate-off CLI is offline, but `PackageId`/`CopyToPublishDirectory` plus successful `dotnet pack`/`dotnet publish` show a packaging/publish path, contradicting "no release, publish or deploy path exists".
- C6 **Falsified**: `docs/tholdem/viewer.md:98` and `engine-rules.md:156` claim agents receive only `PlayerObservation`, but `IPokerAgent` passes `AgentTurnContext` with a public `SessionSeed`; additionally, no Tholdem test is tracked by git, contradicting the docs' "committed suite" wording.