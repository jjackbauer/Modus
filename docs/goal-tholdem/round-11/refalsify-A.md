# Round 11 — falsifier A (C3, C5, C6)

- **Repository**: `C:\Users\ricar\dev\Modus`, branch `WIP`, HEAD `f1a26bb320531a28e159da4ee6d47275ff49a1c2`.
- **Date**: 2026-11-06.
- **Working tree under test**: the whole `WIP/Wip.Tholdem*` delivery is untracked (`git status` shows `?? WIP/Wip.Tholdem*`); the tree below is the delivery being assessed. `docs/tholdem/` and `docs/goal-tholdem/` are also untracked.
- **Build used**: `WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe` (`Wip.Tholdem.Host.dll` 2026-11-06 21:53:15; `Wip.Tholdem.Agents.dll` beside it 22:11:46, rebuilt after the last source edit — a comment at 22:10:06; every behaviour-bearing Tholdem source is 21:25 or older). Test assembly `WIP\Wip.Tholdem.Tests\bin\Release\net10.0\Wip.Tholdem.Tests.dll` (21:53, after the newest test source at 21:50).
- **Local limits honoured**: `dotnet test` not used; the supplementary runner was used over the compiled test assembly; no `%TEMP%` scratch (all scratch under `.tmp\refalsify-A`); the viewer was queried at `http://127.0.0.1:5099` and not restarted.

## Verdict summary

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| C3 | Falsified | The agent-facing surface itself is clean: `IPokerAgent`/`AgentTurnContext`/`PlayerObservation` carry no session seed, deck order or opponent hole cards; no file or compiled identifier in `WIP/Wip.Tholdem.Agents` mentions `SpectatorSnapshot`/`TholdemWebHost`/`LiveMatch`; the project graph is Agents → {Abstractions, Runtime, Tholdem} only. But the suite's own capture prints `turn seed 3386938122580612636` and `engine hand seed 13149409904026330690`; a 0..10,000 search over the public `SeedDerivation.DeriveTurnSeed` recovered session seed **42** in 2.0 s, and `TurnProbe(13149409904026330690)` printed `Qs Tc 4s Jd 8h 8s 8d Th 6c`, exactly the recorded `--seed 42` deal (`holeCardsSeat0 Qs,4s`, `holeCardsSeat1 Tc,Jd`). The same attack recovered seed **7** (turn seed 6286447237295211984, 1.57 s) and `TurnProbe(16308862071395657443)` printed `Ks Qs 9s 4h 2h 4c Js Qc Kc`, reproducing the recorded hole cards **and** the recorded board `2h 4c Js Qc Kc`. | An `IPokerAgent` implementation at hand 1, seat 0, action 0 receives `TurnSeed` plus hand/seat/action index; it enumerates `s = 0..10,000`, calls the public `SeedDerivation.DeriveTurnSeed(s, 1, 0, 0, 0)`, finds `s = 42` (or `7`), then calls `SeedDerivation.Derive` and `SeededDeck.Create` and rebuilds the recorded deal. Direct `SeededDeck.Create(TurnSeed)` (`Qh Kc Ts Tc 2h 2c Ks 4d 3h`) and `Derive(TurnSeed, 1, 0, 0, 0)` (`6c 3s 6d As Jd 4d 6s 7d Ah`) do fail; the search does not. | The third clause — “an attempt to reconstruct the deck from everything an IPokerAgent receives at a turn fails to reproduce the recorded hole cards or board” — is false for the documented low-entropy seeds. The first two clauses (no direct exposure; spectator type not reachable from the Agents project) hold. |
| C5 | Supported | Product sources under `WIP/Wip.Tholdem.Host`, `WIP/Wip.Tholdem.Agents` and `WIP/Wip.Tholdem.Web` contain no `HttpClient`, `WebRequest`, `WebClient`, `Socket`, `TcpClient`, `Dns` or `http(s)://` code; the only outbound HTTP client implementation in the built closure is `Wip.Runtime`’s DeepSeek provider and it has **no caller** in the Tholdem product (`grep` over Host/Web/Agents/Tholdem/Runtime/Abstractions found only the provider’s own type definitions). `THOLDEM_LIVE_MODEL` and `DEEPSEEK_API_KEY` are unset in this session; `LiveModelGateTests` pass in the 179/179 suite. The running viewer owns exactly one socket: `127.0.0.1:5099 ... Listen` (no UDP, no remote endpoint). A 1,500-hand Release-CLI run completed (metrics printed) while 2 `Get-NetTCPConnection`/`Get-NetUDPEndpoint` polls observed **0** TCP and **0** UDP endpoints for the process. No wallet/payment/account/login/betting/sportsbook/cashier/telemetry token exists in the six built projects (only the poker word “betting street” in `Street.cs`). No Tholdem `.nupkg` exists under `artifacts/`; `git log --all -- WIP/Wip.Tholdem*` is empty; the CI workflows only build/test; the Tholdem docs contain only negations of release/publish/deploy. | Searched the product and built closure for network/payment/betting APIs; watched the CLI process’s TCP/UDP endpoints for the whole run; inspected the viewer process’s sockets; searched workflows and scripts for `dotnet publish`, `dotnet pack`, `nuget push`, signing and deploy; inspected the pre-existing `.tmp\falsify-c\pack` and `.tmp\falsify-c\publish` scratch output. | No outbound call, external-betting/account integration, publish, signing or deployment performed by this delivery was found. The `.tmp\falsify-c` package/publish output is reviewer scratch under the git-ignored `.tmp/`, is not part of the delivery, and is covered by the claim’s “ordinary .NET packability is not itself a violation” carve-out; no release is claimed. |
| C6 | Falsified | The executable test/doc checks pass: supplementary runner `TOTAL 179 PASSED 179 FAILED 0 SKIPPED 0` exit 0; the sources contain 179 `[Fact]`, 0 `[Theory]`, 0 `Skip=` assignments in 50 files; `docs/plan/test-waivers.json` has `Wip.Tholdem.Tests: 179` and no Tholdem failure or `knownAborted` entry; the documented commands reproduce (`--help`, `--version`, `play --seed 777 --hands 2`, `train --iterations 1 --seed 7`, `train --iterations 3 --seed 7`, `evaluate --seeds 4`, `evaluate --seeds 24`, `promote` refusals, `replay`, `retention --status`, and the viewer HTTP endpoints); every JSON example in `docs/tholdem` is byte-equal to the generated file (lengths 333, 288, 279, 370, 59, 335, 321). **However**, `docs/tholdem/viewer.md:106-107` states “neither the session seed nor the deck can be reconstructed from it”, and the C3 counterexample reconstructs the session seed and the deck for the documented seed-42 and seed-7 sessions. | The C3 seed-search reconstruction applied to `docs/tholdem/viewer.md:100-107`; plus the documented command runs listed in 6.2 compared with the docs. | The test-integrity and waiver sub-claims hold, and all concrete examples reproduce; the absolute “every factual claim … matches current code behaviour” conjunct fails on the viewer.md non-reconstructibility sentence. |

## C3 — hidden information: what an agent receives and what can be rebuilt

### 3.1 Enumerated agent-facing surface (read, not assumed)

- `WIP/Wip.Tholdem.Agents/IPokerAgent.cs` — `ActAsync(PlayerObservation, AgentTurnContext, CancellationToken)` only. The XML comment claims the context’s `TurnSeed` “cannot be inverted to a session identifier or to the shuffled card order”.
- `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs:9` — `readonly record struct AgentTurnContext(ulong TurnSeed, int HandNumber, int Seat, int ActionIndex)`; no session-seed member.
- `WIP/Wip.Tholdem/History/PlayerObservation.cs:22-54` — hand number, street, button seat, acting seat, board, **own** hole cards, both stacks, pot, both street commitments, call/min-raise/max-raise, legal kinds, completion flag, winners. No seed, no deck, no undealt cards, no opponent cards.
- Runtime tools (`Runtime/TholdemToolCatalog.cs`): `equity-calculator`, `pot-odds`, `hand-history-query`, `opponent-statistics`. The equity tool is pure Monte Carlo against a uniform random opponent (`Equity/MonteCarloEquity.cs`); the hand-history tool projects each record through `PlayerObservation.ForSeat` (`Runtime/SessionHandStore.cs:138-160`); the opponent-stats tool returns aggregate VPIP/PFR/AF/showdown only (`Runtime/OpponentStatsTool.cs`). None reads a seed or the other seat’s cards.
- `Runtime/SessionHandView.cs` — hand number, board, **own** hole cards, textual action log, winners, showdown, pot, net chips. No seed, deck or opponent cards.
- Artifact caveat: the CLI’s `sessions/<id>/session.json` does contain the session `seed`, and `hand-NNNNN.json` contains the per-hand `seed` and **both** hole cards (`HandRecord.From`). These are spectator/replay artifacts written by the CLI, not reachable through `IPokerAgent` or any of its tools (see 3.3); the claim’s “agent-facing” qualifier is what keeps this from being a direct exposure.

### 3.2 Direct attempts (these fail, as the current test asserts)

Observed from the 179-test run (console output captured in `.tmp/refalsify-A/full-suite.log`):

```text
recorded deck prefix : Qs Tc 4s Jd 8h 8s 8d Th 6c
context seed prefix  : 6c 3s 6d As Jd 4d 6s 7d Ah
replayed deal        : seat0=6c,6d seat1=3s,As board=Jd 4d 6s 7d Ah
turn seed            : 3386938122580612636
engine hand seed     : 13149409904026330690
```

- `SeededDeck.Create(TurnSeed)` with `TurnSeed = 3386938122580612636` → `Qh Kc Ts Tc 2h 2c Ks 4d 3h` (different deal).
- `SeedDerivation.Derive(TurnSeed, 1, 0, 0, 0)` = `16723055204109422328` → `6c 3s 6d As Jd 4d 6s 7d Ah` (different deal).
- Therefore the two “obvious” inversion attempts fail, exactly as `AgentInformationBoundaryTests.TurnSeed_GivenSessionSeed42Hand1_ExpectedDeckAndRecordedCardsDiffer` asserts.

### 3.3 The search attempt (succeeds)

The `TurnSeed` is a deterministic hash of the session seed, and the session seed is a user-supplied small integer in every documented path (`--seed 42`, `--seed 7`, `--seed 777`, viewer seed field). The public `SeedDerivation.DeriveTurnSeed` is callable by any agent implementation.

1. The suite’s own capture supplies the real first-turn value for the recorded session-42 hand: `3386938122580612636`. Independently recomputing `SHA-256("tholdem-turn-seed|42|1|0|0|0")`’s first 8 big-endian bytes gave the same value.
2. Searching candidate session seeds `s = 0..10,000` with the public derivation:
   `turn seed 3386938122580612636` → unique match `42` (2,000 ms for 10,001 candidates, 0.2 ms/candidate); `turn seed 6286447237295211984` (seed 7) → unique match `7` (1,569 ms). The 0..200 run matched `42` in 505 ms.
3. The recorded `--seed 42 --hands 1` artifact (`play42`, Release CLI, random-legal vs random-legal) contains:
   `"seed":13149409904026330690`, `"holeCardsSeat0":["Qs","4s"]`, `"holeCardsSeat1":["Tc","Jd"]`.
   `TurnProbe 13149409904026330690` prints `Qs Tc 4s Jd 8h 8s 8d Th 6c`; deck positions 0/2 and 1/3 are exactly the recorded hole cards.
4. The recorded `--seed 7 --hands 1` artifact (`play7`) contains:
   `"seed":16308862071395657443`, `"holeCardsSeat0":["Ks","9s"]`, `"holeCardsSeat1":["Qs","4h"]`, `"board":["2h","4c","Js","Qc","Kc"]`.
   `TurnProbe 16308862071395657443` prints `Ks Qs 9s 4h 2h 4c Js Qc Kc`; that is the recorded hole cards at 0/2 and 1/3 and the recorded board at 4..8 — a full reproduction, not only a hole-card coincidence.
5. The hand seed in each artifact is exactly what the product’s own `MatchSimulator` computed from the session seed (`SeedDerivation.Derive(options.Seed, hand, 0, 0, 0)`, `MatchSimulator.cs:49`); no external derivation formula was substituted.

Conclusion: the context value is “opaque” only against direct inversion, not against a search over the seed space actually used by the product. For the documented seeds the deck — including the opponent’s cards and the whole board — is reconstructible from what the agent receives.

### 3.4 Spectator-snapshot reachability

- No file under `WIP/Wip.Tholdem.Agents` contains `SpectatorSnapshot`, `TholdemWebHost` or `LiveMatch` (grep of all `.cs`).
- The compiled `Wip.Tholdem.Agents.dll` contains none of those strings either.
- `Wip.Tholdem.Agents.csproj` references only `Wip.Abstractions`, `Wip.Runtime` and `Wip.Tholdem`; none of those (transitively) references `Wip.Tholdem.Web`; the only project referencing the Web project is `WIP/Wip.Tholdem.Tests`.
- The suite’s `TholdemWebViewerTests.AgentSources_GivenSpectatorBoundaryScan_ExpectedNoWebOrLiveIdentifiers` scans every `.cs` under `WIP/Wip.Tholdem.Agents` and passes.
- The spectator view is real and does expose both seats’ cards over `GET /api/live/{id}` (live test: snapshot contained `holeCardsSeat0:["Qs","4s"]`, `holeCardsSeat1:["Tc","Jd"]`), but it is built inside the web host from per-seat `PlayerObservation` projections (`LiveMatch.cs:87-94`) and is never passed to an `IPokerAgent`.

This clause holds; the falsification of C3 is the reconstruction clause, not spectator reachability.

## C5 — offline safety: attempts and observations

1. **Static scan of the product** (`.cs` under `WIP/Wip.Tholdem.Host`, `WIP/Wip.Tholdem.Agents`, `WIP/Wip.Tholdem.Web`): no `HttpClient`, `WebRequest`, `WebClient`, `Socket`, `TcpClient`, `Dns`, `Process.Start`, `Assembly.Load`, `http://` or `https://` outside the `LiveModelGate` environment-variable names.
2. **Built closure**: `Wip.Runtime` is the only project with HTTP code (`DeepSeekChatModel`, `DeepSeekModelProvider`, `DeepSeekChatModelServiceCollectionExtensions`). A grep of Host/Web/Agents/Tholdem/Runtime/Abstractions finds those types only at their definitions — no `AddDeepSeekChatModel` caller, no constructor call, no `TholdemRuntimeAgent` construction in product code (only `RuntimeAgentTestSupport` in tests). The live gate (`Runtime/LiveModelGate.cs`) only reads `THOLDEM_LIVE_MODEL`/`DEEPSEEK_API_KEY`; `grep LiveModelGate` shows its only callers are `LiveModelGateTests`. Thus the product cannot reach the one HTTP client in its dependency closure.
3. **Gate state**: `THOLDEM_LIVE_MODEL` is empty and `DEEPSEEK_API_KEY` is unset in this session; `LiveModelGateTests.IsEnabled_GivenEnvironmentMatrix_...` passes in the 179/179 run, including the disabled cases.
4. **Runtime socket observation (viewer)**: the running viewer process (PID 22960, `dotnet Wip.Tholdem.Web.dll` from the copied `.tmp/viewer-run4` output) has exactly one TCP endpoint, `127.0.0.1:5099 0.0.0.0:0 Listen`, and no UDP endpoints.
5. **Runtime socket observation (CLI)**: `Wip.Tholdem.Host.exe play --seed 12345 --hands 1500` completed and printed its metrics table; polling `Get-NetTCPConnection`/`Get-NetUDPEndpoint` for the process during the run observed 0 TCP and 0 UDP endpoints (2 polls; the earlier 4,000-hand run likewise observed 0/0 over 9 polls).
6. **Real-money/account/betting scan**: zero matches for `wallet|payment|stripe|paypal|account|login|signin|betting|sportsbook|cashier|deposit|withdraw|gambl|real money|odds api|telemetry|analytics` in the six built projects; the single `betting` hit is the poker term in `WIP/Wip.Tholdem/Engine/Street.cs:4`.
7. **Publish/sign/deploy**: no `dotnet publish`, `dotnet pack`, `nuget push`, `gh release`, signing or deploy step in `.github/workflows/*`, `Makefile` or `tools/scripts` for Tholdem; `git log --all -- WIP/Wip.Tholdem*` is empty (the delivery has never been committed); no Tholdem `.nupkg` under `artifacts/`; `Directory.Build.props` is ordinary package metadata (`PackageVersion 0.0.2`, symbols), which the claim explicitly excludes; the pre-existing `scripts/pack-plugin-upload.ps1` / `upload-plugin-via-api.ps1` sign/upload a different (plugin) subsystem and are not invoked by any Tholdem path.
8. **Release claims**: `docs/tholdem/*.md` contains only negations (“No publish, signing or deployment…”, “No release is claimed”, “no release, publish or deploy is claimed or performed”); `docs/goal-tholdem/*` has the same negation in the goal/state.
9. **Scratch found**: `.tmp/falsify-c/pack/Wip.Tholdem.0.0.2.nupkg` + `.snupkg` and `.tmp/falsify-c/publish/*` exist from a prior reviewer. `.tmp/` is git-ignored (`.gitignore` line `.tmp/`), they are not in `artifacts/`, and the docs say package output produced during review was scratch and not delivered. Under the claim’s own carve-out for ordinary .NET packability this is not a violation of “what this delivery performs”.

Result: no outbound call and no external money/account/betting integration was found; no publish, signing or deployment by this delivery was found; no release is claimed. C5 holds.

## C6 — test and doc integrity: what ran and what it showed

### 6.1 Tests are executed, not skipped

- `dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` → exit 0, tail `TOTAL 179 PASSED 179 FAILED 0 SKIPPED 0`.
- Source count: 50 test files, 179 `[Fact]`, 0 `[Theory]`, 0 `[InlineData]`, 0 `[MemberData]`, 0 `Skip =` assignments. No `#if`/`#endif` in the test sources; every test file has assertions (assert counts: Equal 876, True 129, Contains 280, Throws 25, etc.; no fact without an `Assert`).
- `docs/plan/test-waivers.json`: `Wip.Tholdem.Tests: 179` in `expectedTotals`; the `knownAborted` and `failures` arrays contain no Tholdem entry.
- `.github/workflows/tests-ci.yml` adds a Tholdem security step and `tools/run-wip-tests.ps1` adds `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj` to the offline gate; neither excludes or weakens it.

### 6.2 Documented commands reproduced

| command | observed result |
|---|---|
| `Wip.Tholdem.Host.exe --help` | exit 0; exact usage block from README (23 lines) |
| `... --version` | exit 0; `0.1.0` |
| `... play --seed 777 --hands 2` | exit 0; `session: tholdem-f275b4ee69be0557`; manifest `32ff5488…`; tight-aggressive −75.00 / random-legal 75.00 rows exactly as the schema doc; `session.json` and `hand-00001.json` byte-equal to the doc (335/321 bytes) |
| `... train --iterations 1 --seed 7` | exit 0; iteration line and `final champion:` exactly as the schema doc; `ledger.json` (288), training evaluation (279), `strategy-v1` (333), `active-champion` (59) byte-equal to the docs |
| `... evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 4` | exit 0; decision `lcb-below-margin`, verifier `verified (rejection-justified)`; `cli-…json` (370) byte-equal to the doc; champion unchanged |
| `... train --iterations 3 --seed 7` | exit 0; the three iteration lines and final champion exactly the runbook block |
| `... evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 24` | exit 0; decision/verifier exactly the runbook |
| `... promote --challenger tholdem-strategy-v2-b155b98f --yes` | exit 3; `promotion refused: lcb-below-margin` / `verifier: rejection-justified`; champion unchanged |
| `... promote` without `--yes` | exit 3; `promotion requires explicit confirmation; re-run with --yes` then the refusal and verifier lines |
| `... replay hand-00001.json --data-dir …` | exit 0; hand/board/holes/actions/stacks printed; manifest anchor found (no `integrity: unanchored`), matching ReplayCommand’s documented behaviour |
| `... retention --status --data-dir …` | exit 0; sessions/hands/bytes/oldest-session line |
| viewer `GET /api/status` | 200 `{"mode":"spectator","dataDirectory":"…artifacts\\tholdem","sessions":2,"strategies":4,"liveMatches":0,"offline":true}` |
| viewer `GET /` | 302 `/viewer/index.html`; `GET /viewer/` 200 (9,359 B); `GET /api/sessions`, `/api/sessions/probe-777`, `/api/sessions/probe-777/hands/1`, `/viewer/table?…`, `/api/strategies`, `/api/ledger`, `/api/champion` all 200 with the documented shapes |
| viewer `POST /api/live` (missing/unknown agent) | 400 `{"error":"unknown-agent",…}`; invalid hands 400 `invalid-hands`; invalid delay 400 `invalid-delay`; valid body 201, snapshot 200 with both seats’ hole cards, pause/resume 200, DELETE 204, unknown id 404 |

### 6.3 The remaining doc fact

`docs/tholdem/viewer.md:100-107` (also the same assertion in `docs/tholdem/engine-rules.md:163-172` in the weaker “digest cannot be inverted” form) says:

> The context has no session-seed field, and neither the session seed nor the deck can be reconstructed from it.

The C3 evidence above reconstructs both the session seed and the deck from the context for the documented seed-42 and seed-7 sessions. The concrete examples all match; this absolute factual claim does not. C6’s third conjunct therefore fails.

## Not checked / limits of this pass

- CI runs (no `gh`/token; `.github` workflow execution not observable here) — the CI claims are source-only from my side.
- Historical “deleted test” evidence: the whole Tholdem project is untracked, so there is no committed baseline to diff against; I checked the current sources/counts/waivers and found no skip, no exclusion, and no vacuous fact.
- Browser DOM/JS execution (keyboard, responsive geometry) was not run; the viewer HTTP surface and static assets were checked, and the app.js handler/columns/poll-speed values were read.
- Full seed-space inversion beyond 10,000 was not attempted; the attack is already sufficient for the documented seeds and is linear in the seed value.
- `dotnet build` was not re-run; the executed Release binaries/tests are the existing build, which is newer than every behaviour-bearing source (only a comment is newer, and the Agents dll beside the Host was rebuilt after it).