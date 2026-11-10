# Round 13 — Falsifier B: C3 (hidden information) and C6 (test/doc integrity)

Repository `C:\Users\ricar\dev\Modus`, branch `WIP`, HEAD `f1a26bb`; the Tholdem tree is untracked. Findings were re-derived from the current tree; no product, test, doc or configuration file was edited (only this report and tool-generated artifacts under `.tmp` and the build/test output directories).

Method: Release build of `Wip.slnx`; native CLI `WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe`; supplementary xUnit runner `.tmp/tholdem-verify/RunXunit` over `WIP\Wip.Tholdem.Tests\bin\Release\net10.0\Wip.Tholdem.Tests.dll`; the viewer at `http://127.0.0.1:5099`; `SeedSearchProbe` in `.tmp/tholdem-verify`.

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| C3 | Supported | (a) `IPokerAgent.ActAsync` receives only `PlayerObservation` + `AgentTurnContext`; the context is exactly `(HandNumber, Seat, ActionIndex)`; the observation JSON has no seed/deck/opponent cards; the runtime surface is exactly four tools and unknown tools are rejected; `SessionHandView` is the single-seat projection. Session manifests/hand files contain the seed/deal but no agent tool reads them. (b) `Wip.Tholdem.Agents.csproj` references only Abstractions/Runtime/Tholdem; no `Wip.Tholdem.Web`/`SpectatorSnapshot` in source or the compiled Agents dll; `SpectatorSnapshot` exists only in Web and Web references Agents (reverse). (c) `play`/`train`/`evaluate` with seed 42 and 1099511627775: exit 2 and the exact minimum message; seed 1099511627776: exit 0; weak seed with `--allow-weak-seed`: exit 0. `POST /api/live` seed 42/0/1099511627775/string "42"/`{}`: HTTP 400 `{"error":"weak-seed","minimum":1099511627776}`; seed 1099511627776: 201; seed 42 + `allowWeakSeed:true`: 201. (d) `known-limits.md` states enumeration recovers the deal and opt-out is outside the guarantee; no absolute non-invertibility claim remains; 0..20000 seed search reproduced 11 matches, first 42, exact deck. | Tried a weak seed through every entry point without and with the opt-in, at the boundary, as a string, with `allowWeakSeed:false`, and with a missing seed/body; tried to find an agent tool/path to the session seed, hand files, deck or opponent hole cards; inspected the assembly/project graph and dynamic-loading usage for the spectator type; read every docs/tholdem statement for absolute wording; reproduced the documented seed-recovery attack. | All enumerated exposure paths and guards behaved as claimed; the remaining weak-seed inversion risk is documented and gated, not denied. |
| C6 | Supported | Full suite twice (before and after the Release rebuild): `TOTAL 194 PASSED 194 FAILED 0 SKIPPED 0`, exit 0; 194 Fact attributes, zero Theory/InlineData, zero `Skip`, zero `#if`, 1651 `Assert.` occurrences. `docs/plan/test-waivers.json` mentions Tholdem only as `"Wip.Tholdem.Tests": 194` under `expectedTotals`; no Tholdem failure/abort waiver. All documented test names exist. Documented commands matched exactly: `dotnet build Wip.slnx --configuration Release` (Build succeeded, 0 errors, 36.03 s, exit 0) and `--help`; `play --seed 777 --hands 2 --allow-weak-seed` (session id, both hand hashes, hand-1 JSON all byte-identical to the schema doc); `train --iterations 3 --seed 7 --allow-weak-seed` (all four output lines identical to the runbook); `evaluate ... --seeds 4` (exact CLI evaluation JSON); `promote` refusal (exit 3), `replay` (exit 0 / exit 5 on hash mismatch), `retention --status`, `play --seed 42 --hands 3 --allow-weak-seed` (Qs,4s hand-1 exact); all documented viewer endpoints and HTTP guards. | Searched for a skipped, deleted, weakened or vacuous test; tried to make every concrete docs/tholdem command differ from its text; tried boundary seeds, string seeds, empty bodies, missing/unknown agents, invalid hands/delay; checked the waiver register, run script, verifier and CI wiring; tried the documented threat-model attack. | Every concrete docs/tholdem command/example tested matched observed output; all 194 tests execute with zero skips and match the waiver register's expected total; the documented residual risk is stated, not contradicted. |

## C3 — detail

### (a) Agent surface does not expose the deal

- `WIP/Wip.Tholdem.Agents/IPokerAgent.cs` defines the only agent method: `ActAsync(PlayerObservation observation, AgentTurnContext context, CancellationToken)`. `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs` is `public readonly record struct AgentTurnContext(int HandNumber, int Seat, int ActionIndex)` — three public ints, no seed/digest/string.
- `WIP/Wip.Tholdem/History/PlayerObservation.cs` exposes hand number, street, button seat, acting seat, board, own hole cards, own/opponent stacks, pot, commitments, amount to call, min/max raise, legal action kinds, completion flag and winners. `ToCanonicalJson()` emits exactly those fields. There is no seed, deck order, undealt-card list or opponent-hole-card member.
- `TholdemToolCatalog.CreateDefinitions()/CreateDescriptors()` lists exactly four tools: equity-calculator, pot-odds, hand-history-query, opponent-statistics. `RuntimeToolInvoker.InvokeAsync` returns `unknown-tool` for any name outside the registered descriptors; there is no file-read tool.
- `SessionHandStore.BuildFor(seat, limit)` replays a `HandRecord` through `HeadsUpHandEngine.Replay` and projects with `PlayerObservation.ForSeat`; `SessionHandView` has no member for the seed, deck or opponent cards. `SessionHandStoreTests.BuildFor_GivenThreePlayedHands_ExpectedNewestLastViewsWithoutHiddenInformation` (which passed in the 194/194 run) additionally asserts that each opponent card string, the decimal seed and the word "deck" are absent from the serialized view.
- Artifact check: `play --seed 42 --hands 3 --allow-weak-seed --data-dir .tmp\refalsify-b\weak42` wrote `session.json` with `"seed":42` and `hand-00001.json` with both seats' hole cards. Those are operator replay artifacts; no agent-facing tool exposes a path to them or reads the filesystem. `TholdemRuntimeAgent` receives only the observation plus `worktreePath`, and `worktreePath` is used only inside `CapabilityContext`; none of the four tools reads the filesystem. `RuntimeSourceBoundaryTests` scans the runtime sources for `TableState`, `SpectatorSnapshot`, `HoleCardsSeat0`, `HoleCardsSeat1`, `Deck` and passed.
- Closest reachable surface: `PlayerObservation.ForSeat(TableState state, int seat)` is public and could project either seat if a caller held a `TableState`. No agent method, tool result or context supplies a `TableState`; `TableState.Seed`, `Deck`, `HoleCardsSeat0`, `HoleCardsSeat1`, `DeckPosition` are `internal`, and `Wip.Tholdem.csproj` grants `InternalsVisibleTo` only to `Wip.Tholdem.Tests`. `SessionHandStore.Record(HandRecord)` is public but has no getter, and the agent is not given store access; its only outputs are `SessionHandView` projections.
- The runtime agent derives randomness from `SeedDerivation.DeriveAgentSeed(observation.ToCanonicalJson(), context.HandNumber, context.Seat, context.ActionIndex, stream)`; the context itself is ignored by `ActAsync` (`_ = context;`).

### (b) Spectator snapshot is unreachable from `WIP/Wip.Tholdem.Agents`

- `Wip.Tholdem.Agents.csproj` has project references only to `Wip.Abstractions`, `Wip.Runtime`, `Wip.Tholdem`. `dotnet list WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj reference --include-transitive` printed exactly those three.
- `Wip.Tholdem.Web.csproj` references `Wip.Tholdem` and `Wip.Tholdem.Agents`; a Web reference from Agents would be a project cycle. `SpectatorSnapshot` is defined only in `WIP/Wip.Tholdem.Web/Live/SpectatorSnapshot.cs` (namespace `Wip.Tholdem.Web.Live`) and is constructed only by `LiveMatch.Snapshot()`.
- A source scan of `WIP/Wip.Tholdem.Agents` found zero occurrences of `Wip.Tholdem.Web` or `SpectatorSnapshot`. A string scan of the compiled `Wip.Tholdem.Agents.dll` returned `SpectatorSnapshot=False`, `HoleCardsSeat0=False`, `HoleCardsSeat1=False`; `TableState=True` only because `Simulation/MatchSimulator.cs` uses it internally to run the deal, not in any agent input.
- There is no `Assembly.Load`/reflection-based route: the only `GetType()` hit in Agents is `exception.GetType().Name`. `RuntimeSourceBoundaryTests` explicitly bans `SpectatorSnapshot` and the deck identifiers in the runtime sources and passed in the suite.

### (c) Seed guard at every named entry point

CLI (native executable; the `tholdem:` line is the process's stderr, the exit code is the process exit code):

```text
play --seed 42 --hands 1
  -> exit 2, "tholdem: seed 42 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos"
play --seed 1099511627775 --hands 1
  -> exit 2, same message with 1099511627775
play --seed 0 --hands 1
  -> exit 2, same message with 0
train --iterations 1 --seed 42
  -> exit 2, same message
evaluate --challenger does-not-exist --seeds 1 --seed 42
  -> exit 2, same message (the seed guard runs before the artifact load)
play --seed 1099511627776 --hands 1
  -> exit 0, session tholdem-88f28b70086c22cf
play --seed 42 --hands 3 --allow-weak-seed
  -> exit 0, session tholdem-9bae4b03e89b5865
train --iterations 1 --seed 7 --allow-weak-seed
  -> exit 0, iteration=1 champion=tholdem-strategy-v1-f100e793 ... seeds=24
evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 1 --seed 7 --allow-weak-seed
  -> exit 0, decision: lcb-below-margin / verifier: verified (rejection-justified)
```

`POST /api/live` (JSON body; observed status and body):

```text
{"seed":42,...}                                  -> 400 {"error":"weak-seed","minimum":1099511627776}
{"seed":42,...,"allowWeakSeed":false}            -> 400 same
{"seed":1099511627775,...}                       -> 400 same
{"seed":0,...}                                   -> 400 same
{"seed":"42",...}                                -> 400 same
{}                                               -> 400 same
{"seed":1099511627776,...}                       -> 201 {"id":...,"seed":1099511627776,...}
{"seed":42,...,"allowWeakSeed":true}             -> 201 {"id":...,"seed":42,...}
```

Also observed the documented validation order/bodies: `hands:501` -> 400 `{"error":"invalid-hands","min":1,"max":500}`; `delayMs:5001` -> 400 `{"error":"invalid-delay","min":0,"max":5000}`; missing agent -> 400 `{"error":"unknown-agent","seat":0,"agent":null}`.

### (d) Documentation does not claim absolute non-invertibility

- `docs/tholdem/known-limits.md` §Threat model says the deal is deterministic from the seed, so "'the agent never sees the seed' is not the same statement as 'the deal is secret'"; §(b) says an adversary holding its own observation can enumerate candidates and reconstruct the deck, including undealt cards and the other seat's hole cards; §(c) states the minimum and messages; §(d) says opting out "is outside the secrecy guarantee" and that a weak seed is recoverable by enumeration.
- `docs/tholdem/engine-rules.md` and `viewer.md` state the non-inversion claim only for the agent surface ("no agent input contains a stored value to invert... That is a statement about the agent surface, not about the deal") and immediately state the enumeration risk. `known-limits.md` says its section "replaces any earlier absolute 'non-invertible' wording".
- Reproduction: `SeedSearchProbe Qs 4s 1 20000` printed `candidates=20001 matches=11 elapsed_ms=445`, `recovered=42,2865,2954,5700,6077,9955,11832,12050,12229,16184`, `deck=Qs Tc 4s Jd 8h 8s 8d Th 6c`, `opponent_hole=Tc,Jd`, `board=8h,8s,8d,Th,6c`. The generated `hand-00001.json` for `play --seed 42 --hands 3 --allow-weak-seed` contains `holeCardsSeat0:["Qs","4s"]`, `holeCardsSeat1:["Tc","Jd"]`, `board:["8h","8s","8d","Th","6c"]`. This is exactly the doc's 11 matches/first 42/deck; the doc's 388 ms is a round-13 timing, while our run took 445 ms (timing only; the count, first candidate and deck match).

## C6 — detail

### Test integrity

- Full suite, run twice (the second time after the Release rebuild), via `dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP\Wip.Tholdem.Tests\bin\Release\net10.0\Wip.Tholdem.Tests.dll`: both runs ended `TOTAL 194 PASSED 194 FAILED 0 SKIPPED 0` and exit 0.
- Source scan of `WIP/Wip.Tholdem.Tests`: 194 `[Fact]`/`[Theory]` attributes (194 Facts, 0 Theories, 0 InlineData/MemberData), 0 `Skip` attributes, 0 `#if`; 1651 `Assert.` occurrences; no file containing tests without an assertion. The only empty catches are cleanup/retry paths: `ViewerTestSupport` ignores file-delete failures, and `TholdemViewerRemediationTests` retries a paced hand when it completes before pause lands; neither swallows an assertion.
- `docs/plan/test-waivers.json`: the only Tholdem occurrence is `"Wip.Tholdem.Tests": 194` in `expectedTotals` (line 21). `knownAborted` has two non-Tholdem projects; `failures` has 27 non-Tholdem tests. `git diff` shows the only register change is adding that expected total (implementation.md records 182 -> 194).
- `tools/run-wip-tests.ps1` includes `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj`; `tools/verify-test-results.ps1` errors when a project's TRX total is below its expected total; the CI offline job builds `Wip.slnx` (which includes the five Tholdem projects) before the `--no-build` runs.
- All test names cited in `docs/tholdem` exist and passed, including `AgentInformationBoundaryTests.AgentInputs_GivenRecordedSeed42Hand1Enumeration_ExpectedNoCandidateSeedMatchAndDifferentDeck`, `AgentSources_GivenAgentFiles_ExpectedNoHiddenInformationIdentifiers`, `AgentTurnContext_GivenReflection_ExpectedNoSeedValuedMember`, `HeadsUpHandEngineTests.Apply_GivenFiveHundredSeedsAndRandomPolicy_ExpectedChipConservationAndCompletion`, `SplitPot_GivenOddPot_ExpectedExtraChipToNonButton`, `TrainingLoopTests.Run_GivenSameOptionsIntoTwoDirectories_ExpectedIdenticalArtifactsReportsLedgerAndChampion`, `PromotionGateTests.Verify_GivenRederivedCriterion_ExpectedControlsRejectedAndPassesAccepted` and `TrainingLoopTests.Run_GivenAlwaysPromotePolicy_ExpectedVerificationFailureAndUnchangedChampion`.

### Documented commands and examples, observed

1. README quick start:
   - `dotnet build Wip.slnx --configuration Release` -> `Build succeeded.` 0 errors, 6 warnings, 36.03 s, exit 0.
   - `WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe --help` -> `tholdem 0.1.0` plus the usage block; exit 0; the command/option lines match README's CLI block.
2. `play --seed 777 --hands 2 --allow-weak-seed` (schema example):
   - stdout began `session: tholdem-f275b4ee69be0557` / `seed: 777  hands: 2  manifest sha256: ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee`; exit 0.
   - `session.json` was exactly `{"schema":"tholdem.session.v1","sessionId":"tholdem-f275b4ee69be0557","seed":777,"hands":2,...,"handHashes":["571049a9883fcc96d5ea0b85771030d63f66fe0169306d6a16f6bb39b7a8019b","92aa3db0e586fd3b9b31bb1d45777d507aa8f931b7b912aca1c14964ace547f3"]}`.
   - `hand-00001.json` was exactly `{"handNumber":1,"seed":9754104818518126180,...,"holeCardsSeat0":["4c","Js"],"holeCardsSeat1":["Tc","Kd"],"board":[],...,"winners":[1],"potAwarded":15,"finalStacks":[995,1005]}`.
   Both match `strategy-artifact-schema.md` byte-for-byte.
3. `train --iterations 3 --seed 7 --allow-weak-seed --data-dir .tmp\refalsify-b\train7x3` -> exit 0 and exactly:
   ```text
   iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-b155b98f promoted=false verified=true outcome=rejected meanBbPer100=0.00 lcb=0.00 seeds=24
   iteration=2 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-e1ba56f7 promoted=false verified=true outcome=rejected meanBbPer100=1.67 lcb=-0.76 seeds=24
   iteration=3 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-ea96d95d promoted=false verified=true outcome=rejected meanBbPer100=-1.25 lcb=-3.40 seeds=24
   final champion: tholdem-strategy-v1-f100e793
   ```
   This is identical to `training-promotion-runbook.md` lines 38-41. The persisted `strategies/tholdem-strategy-v1-f100e793.json` and the first `ledger.json` entry also match the schema document.
4. `evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 4 --data-dir ...` wrote exactly the document's `cli-...json`: `{"schema":"tholdem.cli-evaluation.v1","criteria":{"marginBbPer100":5,"minSeeds":24},"report":{...,"seeds":4,...,"meanBbPer100":0,...,"challengerWins":4,"incumbentWins":4,"ties":0,"challengerNetChips":0}}`. The `--seeds 24` run printed `seeds: 24`, `decision: lcb-below-margin`, `verifier: verified (rejection-justified)`.
5. `promote --challenger tholdem-strategy-v2-b155b98f` (without `--yes`) -> exit 3, `promotion requires explicit confirmation; re-run with --yes`, `promotion refused: lcb-below-margin`, `verifier: rejection-justified`; with `--yes` -> exit 3, `promotion refused: lcb-below-margin`; `active-champion.json` remained `{"strategyId":"tholdem-strategy-v1-f100e793","iteration":0}`.
6. `replay <hand-00001.json>` printed the hand, board, actions, showdown/winners/stacks and replayed stacks; exit 0. With `--expected-hash <correct hash>` exit 0; with a 64-zero hash exit 5 and `replay hash mismatch`. `retention --status` printed `sessions: 1`, `hands: 3`, `bytes: 1514`, `oldest session: ...`; `--version` printed `0.1.0`.
7. `play --seed 42 --hands 3 --allow-weak-seed` generated hand-1 exactly as the threat model states: `holeCardsSeat0:["Qs","4s"]`, `holeCardsSeat1:["Tc","Jd"]`, `board:["8h","8s","8d","Th","6c"]`.
8. Viewer endpoints as documented: `GET /` -> 302 to `/viewer/index.html`; `/viewer/index.html` -> 200; `/api/status` -> 200 `{"mode":"spectator","dataDirectory":"...artifacts\\tholdem","sessions":4,...,"liveMatches":0,"offline":true}`; `/api/sessions` -> 200 with four sessions and `skipped:0`; session detail, hand JSON and `/viewer/table` -> 200; `/api/strategies`, `/api/ledger`, `/api/champion` -> 200; `DELETE /api/live/{id}` -> 204; `POST /api/live` guards as above. `app.js` derives a 64-bit seed from `crypto.getRandomValues` (with the documented constant fallback) and reads `#allow-weak-seed` into `allowWeakSeed`.

### Threat-model statements

The documented enumeration is reproducible (11 matches, first 42, exact deck/opponent cards/board) and the seed-42 hand-1 deal matches the doc. The guard message text and the HTTP weak-seed body are exactly the strings in `known-limits.md`/`SessionSeedPolicy`. The 388 ms figure is explicitly a round-13 measurement; our 445 ms differs only in timing, not in the substantive result.

### Break attempts that failed

- Weak seed through all four entry points, without and with opt-in, at/below the boundary; all refused or accepted exactly as claimed.
- String-form seed, empty body, `allowWeakSeed:false`, missing seed, missing agent, invalid hands/delay; all returned the documented 400 bodies.
- Searched for a skipped, deleted, weakened or vacuous test: none; count 194 equals the register's expected total and all documented test names exist.
- Searched for an agent path to `session.json`/hand files or `SpectatorSnapshot`: no file tool, no reference, no dynamic load.
- Searched for a docs/tholdem command/example mismatch: build/help, play 777, train 3, evaluate 4, promote, replay, retention, HTTP guards and the 42-deal all matched.
- Local `dotnet test` was not run: this machine's VSTest aborts with Win32Exception (5), documented in `known-limits.md` and `docs/testing/test-status.md`; the supplementary runner over the same compiled assembly was used. CI is not observable from this machine (documented; no GitHub CLI/token claim).

## Residual observations (outside the claim's stated conditions; not used to falsify)

- `docs/testing/test-status.md` still says the offline gate has 16 projects and its inventory omits `Wip.Tholdem.Tests`, while `tools/run-wip-tests.ps1` and `test-waivers.json` now include it (17). This is documentation drift outside `docs/tholdem`; it does not skip or waive a Tholdem test.
- `docs/goal-tholdem/round-13/implementation.md` says `Wip.slnx`/`.github` were not touched, but `git diff` shows both modified (plus the architecture test count/rule changes). This is a scope statement, not a command/example or a Tholdem test waiver.
- `docs/tholdem/viewer.md` says the viewer "never writes"; `TholdemWebHost.Create` calls `Directory.CreateDirectory(dataDirectory)` for the root, though no artifact file is written. Wording only.