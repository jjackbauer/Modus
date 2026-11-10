# Round 3 — N-seat viewer, live API, replay and `play --seats`

**Status:** complete for the round objective. Final local supplementary suite is green at **273
tests** (257 before + 16 new executed cases), zero failures. `dotnet test` was **not** run (prohibited
by the task and unusable in this session); the compiled test assembly was executed with the
supplementary xUnit runner `.tmp/tholdem-verify/RunXunit`. CI was not triggered or observed.

## 1. Scope and environment

- Repository `C:\Users\ricar\dev\Modus`, branch `WIP`.
- Changed only allowed paths: `WIP/Wip.Tholdem.Web/`, `WIP/Wip.Tholdem.Host/`,
  `WIP/Wip.Tholdem.Tests/` (two new test files only), `docs/tholdem/{viewer,known-limits,README}.md`,
  `docs/plan/test-waivers.json` and this file.
- No engine, history, agents, learning-loop, simulator, `Wip.slnx`, architecture-test or `.github/`
  file was edited. No project was added or removed: the coverage check still observes **48**
  projects and the architecture assertion still expects 48.
- Scratch, sessions and logs live under
  `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/round3/`; `%TEMP%` was not used. The viewer was
  run only from a copied build output, never from `bin/Release`.

## 2. What changed

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Host/Commands/PlayCommand.cs` | `--seats` (2–6, default 2), `--agents` (exactly one name per seat, takes precedence over `--agent0`/`--agent1`), optional `--starting-stacks` (one per seat, each ≥ big blind 10). All invalid forms throw `CliUsageException` (exit 2) with a specific message. The session is still written by `MatchSimulator` (N agents, v2 records for 3+ seats). |
| `WIP/Wip.Tholdem.Host/TholdemCli.cs` | `play` accepts `seats`, `agents`, `starting-stacks`; usage text documents them. `train`/`evaluate` option sets, seeds and heads-up behaviour are unchanged. |
| `WIP/Wip.Tholdem.Web/Live/SpectatorSnapshot.cs` | `StartLiveMatchRequest` gains optional `seats`, `agents`, `startingStacks`. `SpectatorSnapshot` gains a trailing `Seats` list of `SpectatorSeat` (seat, agent, stack, holeCards, folded, allIn, handName, winner, dealer); all pre-existing members stay. `LiveMatchStarted` gains trailing `Seats`/`Agents`. |
| `WIP/Wip.Tholdem.Web/Live/LiveMatch.cs` | Generalised to 2–6 agents: `HoldemHandEngine.Start(..., seats, buttonSeat: (hand - 1) % seats, startingStacks)` and `Apply`, one action counter per seat, per-seat snapshot projection. It keeps the constructor's 2-seat semantics; the simulator's seed/button/rules are reused exactly. |
| `WIP/Wip.Tholdem.Web/Live/LiveMatchRegistry.cs` | `Start(seed, hands, delay, agents, startingStacks)` for 2–6 seats; the two-agent overload delegates. Id material is unchanged for two seats (names then sequence). |
| `WIP/Wip.Tholdem.Web/TholdemWebHost.cs` | `POST /api/live` validates/accepts `seats` (2–6), `agents` (exact count, known names) and `startingStacks` (exact count, ≥ big blind), keeping the seed/hands/delay guards and the two-seat `seat0Agent`/`seat1Agent` body. |
| `WIP/Wip.Tholdem.Web/HandNaming.cs` | New `ForSeats(board, IReadOnlyList<IReadOnlyList<Card>>)` and string overload; the two-seat methods are untouched. |
| `WIP/Wip.Tholdem.Web/Recording/SessionCatalog.cs` | Reads both v1 (`holeCardsSeat0/1`) and v2 (`holeCards`, `pots`, `startingStacks`) hand records. `SessionSummary`/`SessionDetail` gain `seats`/`startingStacks`; `HandSummary` gains per-seat `holeCards` and pot layers `pots`; `RecordedHand` carries per-seat hole cards, agents, starting stacks and pot layers, with `holeCardsSeat0/1`/`handNames` preserved. |
| `WIP/Wip.Tholdem.Web/Recording/ReplayTableRenderer.cs` | One `.replay-seat` panel per recorded seat (2–6), per-seat card faces/hand names/winners/dealer; board centre; the v2 pot layers are rendered as a labelled line; the settlement row is unchanged. |
| `WIP/Wip.Tholdem.Web/wwwroot/viewer/index.html` | `#seats` selector (2–6) in the live form; the static two-seat table is replaced by an inert `#seat-template` cloned once per snapshot seat; the board/action-log ids and markup are retained. |
| `WIP/Wip.Tholdem.Web/wwwroot/viewer/app.js` | `liveSeats(snapshot)` builds one panel per `snapshot.seats` entry (fallback to the legacy 2-seat fields when `seats` is absent); `replaySeats` does the same for recorded hands; `ensureSeatPanels` clones `#seat-template`; per-seat dealer/active/last-action/winner/folded/all-in badges; the action log is unchanged. All untrusted text is written with `textContent`; no `innerHTML`. |
| `WIP/Wip.Tholdem.Web/wwwroot/viewer/viewer.css` | Seat grid (2 columns for 2–4, 3 columns for 5–6 on wide screens, one column on phones), folded/all-in badges, replay pot layers. The 2-seat server-rendered replay remains visible-equivalent (layout container changed, content preserved). |
| `WIP/Wip.Tholdem.Tests/Host/PlaySeatsCommandTests.cs` | **new**, 10 executed cases. |
| `WIP/Wip.Tholdem.Tests/MultiwayViewerTests.cs` | **new**, 6 executed cases. |
| `docs/tholdem/viewer.md` | Seat selector, N-seat live request/response body, N-seat replay/detail; HTTP table updated. |
| `docs/tholdem/known-limits.md` | The engine/simulator/viewer/`play` support 2–6 seats; the learning loop and strategy evaluation remain heads-up. |
| `docs/tholdem/README.md` | Intro, CLI block and a `play --seats 4 --agents …` example. |
| `docs/plan/test-waivers.json` | `Wip.Tholdem.Tests` total 257 → **273**. |

### Contract details

- **CLI defaults.** `--seats` defaults to 2; without `--agents`, seats 0/1 keep `--agent0`
  (`tight-aggressive`) / `--agent1` (`random-legal`) and seats 2–5 default to `random-legal`.
  `--agents` always wins when both forms are present.
- **CLI validation messages** (all exit 2, empty stdout, no session directory):
  - `option '--seats' must be between 2 and 6; observed 7.`
  - `option '--agents' must name exactly 4 agents (one per seat); observed 3.`
  - `option '--agents' must use random-legal, tight-aggressive or pot-odds-equity; observed 'cheater'.`
  - `option '--starting-stacks' must list exactly 4 stacks (one per seat); observed 3.`
  - `option '--starting-stacks' must be at least 10 (the big blind); observed 5 at seat 2.`
- **HTTP validation.** `seats` outside 2–6 → 400 `invalid-seats`; `agents` count ≠ seats (including
  omitting `agents` for 3+ seats) → 400 `invalid-agents`; unknown agent name → 400 `unknown-agent`
  (the pre-existing error); starting-stack count/value mismatch → 400 `invalid-starting-stacks`.
  The seed, hands and delay guards run first and are unchanged.
- **Live snapshot compatibility.** `stacks`, `holeCardsSeat0`, `holeCardsSeat1`, `handNames`,
  `buttonSeat`, `actingSeat`, `lastActionSeat`, `actions`, `handHashes` and the settlement fields keep
  their names and meaning; `seats` is additive and has one entry per configured seat for every
  status (running, paused, complete, stopped, before the first hand).
- **Determinism.** `LiveMatch` derives hand `n` from `SeedDerivation.Derive(seed, n, 0, 0, 0)`, uses
  `buttonSeat = (n - 1) % seats`, the same `SimulationOptions` defaults and the same per-seat
  starting stacks as `MatchSimulator`, so the recorded hand bytes/hashes match exactly. The test
  compares two live runs and the simulator's written hand files for 4 seats at seed 42, 3 hands.
- **Replay compatibility.** A v1 two-seat hand has no `pots` member, so its `pots` list is empty; a
  v2 hand (3+ seats or unequal two-seat stacks) exposes its layers. Every pre-existing two-seat
  replay test stays green; the two-seat renderer now uses the same N-panel container with exactly two
  panels.

## 3. Test-first evidence (red)

Both new test files were written first against the pre-change API (they compile because they use only
the public CLI strings, the HTTP JSON and the existing simulator API). The test project built:

```text
$ dotnet build WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj -c Release --nologo
Build succeeded.
    0 Warning(s)
    0 Error(s)
red_build_exit=0
```

The new HTTP cases (real Kestrel, copied data directory) all failed:

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll MultiwayViewerTests
FAIL MultiwayViewerTests.LiveMatchCreation_GivenInvalidSeatsAgentsOrStacks_ExpectedContracted400Errors: EqualException: Assert.Equal() Failure: Strings differ
Expected: "invalid-seats"
Actual:   "unknown-agent"
FAIL MultiwayViewerTests.LiveMatchHashes_GivenFourSeats_ExpectedDeterministicMatchAndSimulatorHashEquality: EqualException: Assert.Equal() Failure: Values differ
FAIL MultiwayViewerTests.LiveMatch_GivenFourSeats_ExpectedFourSeatSnapshotsWithOwnHoleCardsAndLegacyFields: EqualException: Assert.Equal() Failure: Values differ
Expected: Created
Actual:   BadRequest
FAIL MultiwayViewerTests.LiveMatch_GivenSixSeats_ExpectedSixSeatSnapshotWithEveryOwnHoleCards: EqualException: Assert.Equal() Failure: Values differ
Expected: Created
Actual:   BadRequest
FAIL MultiwayViewerTests.RecordedSession_GivenFourSeatHistory_ExpectedNSeatDetailPotLayersAndReplayTable: KeyNotFoundException: The given key was not present in the dictionary.
FAIL MultiwayViewerTests.ViewerAssets_GivenMultiwayTable_ExpectedSeatSelectorSnapshotSeatsAndNoExternalUrls: TrueException: The live form must keep its #seats selector.
TOTAL 6 PASSED 0 FAILED 6 SKIPPED 0
red_http_exit=1
```

The new CLI cases (8 failed, 2 passed — the unchanged two-seat oracle and the existing
`--agent0`/`--agent1` form):

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll PlaySeatsCommandTests
FAIL PlaySeatsCommandTests.Run_GivenFourSeatsAgentsSeed42Hands3_ExpectedExitZeroAndFourSeatSession: EqualException: Assert.Equal() Failure: Values differ
Expected: 0
Actual:   2
FAIL PlaySeatsCommandTests.Run_GivenSeatsOutsideTwoToSix_ExpectedUsageErrorWithSeatMessage(1): ContainsException: Assert.Contains() Failure: Sub-string not found
String:    "tholdem: unknown option '--seats'.\r\n\r\ntho"···
Not found: "between 2 and 6"
FAIL PlaySeatsCommandTests.Run_GivenSeatsOutsideTwoToSix_ExpectedUsageErrorWithSeatMessage(7): ContainsException: Assert.Contains() Failure: Sub-string not found
FAIL PlaySeatsCommandTests.Run_GivenAgentsCountMismatch_ExpectedUsageErrorWithCountMessage: ContainsException: Assert.Contains() Failure: Sub-string not found
FAIL PlaySeatsCommandTests.Run_GivenUnknownAgentName_ExpectedUsageErrorWithNameMessage: ContainsException: Assert.Contains() Failure: Sub-string not found
FAIL PlaySeatsCommandTests.Run_GivenStartingStacksCountMismatch_ExpectedUsageErrorWithCountMessage: ContainsException: Assert.Contains() Failure: Sub-string not found
FAIL PlaySeatsCommandTests.Run_GivenStartingStackBelowBigBlind_ExpectedUsageErrorWithValueMessage: ContainsException: Assert.Contains() Failure: Sub-string not found
FAIL PlaySeatsCommandTests.Run_GivenAgentsAndAgentOptions_ExpectedAgentsTakesPrecedence: EqualException: Assert.Equal() Failure: Values differ
TOTAL 10 PASSED 2 FAILED 8 SKIPPED 0
red_cli_exit=1
```

(Full logs: `scratch/round3/round3-red-http.txt`, `round3-red-cli.txt`, `round3-red-build.txt`.)

After the implementation, the first green attempt exposed three defects in my **new test code**, all
fixed before the final green run (not product changes): the 4-seat CLI case counted four hand files
instead of the three it asked for; the "no agents for 4 seats" HTTP case accidentally omitted
`seats`, forming a valid two-seat body; and the replay panel count regex also matched
`replay-seats`/`replay-seat-count`. The corrected assertions were then green.

## 4. Tests added (16 executed cases, zero skips)

`WIP/Wip.Tholdem.Tests/Host/PlaySeatsCommandTests.cs` — **10 executed cases**:

- `Run_GivenFourSeatsAgentsSeed42Hands3_ExpectedExitZeroAndFourSeatSession` — exit 0, 3 hand files,
  manifest `"seats":4` and the four agent names, every hand `tholdem.hand.v2` with four
  `holeCards`/`finalStacks` entries, and all four agent rows in the metrics table.
- `Run_GivenTwoSeatsAndAgentOptions_ExpectedExistingAgentsStillWork` — `--agent0`/`--agent1` still
  name the two seats.
- `Run_GivenAgentsAndAgentOptions_ExpectedAgentsTakesPrecedence` — `--agents` wins over
  `--agent0`/`--agent1` at two seats.
- `Run_GivenSeatsOutsideTwoToSix_ExpectedUsageErrorWithSeatMessage(1)` and `(7)` — **two theory
  cases**: exit 2 with the `between 2 and 6` message and no session directory.
- `Run_GivenAgentsCountMismatch_ExpectedUsageErrorWithCountMessage` — exit 2, `exactly 4 agents`,
  `observed 3`.
- `Run_GivenUnknownAgentName_ExpectedUsageErrorWithNameMessage` — exit 2 naming the agent choices
  and `'cheater'`.
- `Run_GivenStartingStacksCountMismatch_ExpectedUsageErrorWithCountMessage` — exit 2,
  `exactly 4 stacks`, `observed 3`.
- `Run_GivenStartingStackBelowBigBlind_ExpectedUsageErrorWithValueMessage` — exit 2, `at least 10`,
  `observed 5`.
- `Run_GivenTwoSeatOracle_ExpectedUnchangedSessionIdAndManifestHash` — `play --seed 42 --hands 20
  --allow-weak-seed` prints session `tholdem-06795fbeacfe528b`, the session file hashes to
  `87f90413…` and there are 20 hand files.

`WIP/Wip.Tholdem.Tests/MultiwayViewerTests.cs` — **6 executed cases**:

- `LiveMatch_GivenFourSeats_ExpectedFourSeatSnapshotsWithOwnHoleCardsAndLegacyFields` — 201 has
  `seats:4` and the four agent names; every snapshot has four `seats` entries (also immediately
  after creation); each entry has its own two card codes, folded/allIn/handName/winner/dealer,
  one dealer, four stacks summing to 4000; `holeCardsSeat0/1` equal seats 0/1 and `handNames` has
  four entries. The final snapshot's per-seat hole cards equal the simulator's recorded `holeCards`
  for the same configuration.
- `LiveMatch_GivenSixSeats_ExpectedSixSeatSnapshotWithEveryOwnHoleCards` — six seats, six stacks
  summing to 6000, six hand names and every seat's own two cards.
- `LiveMatchCreation_GivenInvalidSeatsAgentsOrStacks_ExpectedContracted400Errors` — `seats` 1/7,
  agent count 3 of 4, a missing `agents` list for 4 seats, starting-stack count 3 and a stack below
  the big blind all return the contracted `invalid-seats`/`invalid-agents`/`invalid-starting-stacks`
  bodies; an unknown name returns `unknown-agent`; no live match is registered.
- `LiveMatchHashes_GivenFourSeats_ExpectedDeterministicMatchAndSimulatorHashEquality` — two live
  4-seat matches (seed 42, 3 hands, delay 0) have equal `handHashes`, and each hash equals the
  SHA-256 of the simulator's written `hand-NNNNN.json`.
- `RecordedSession_GivenFourSeatHistory_ExpectedNSeatDetailPotLayersAndReplayTable` — a recorded
  4-seat, per-seat-stack session: the list summary has `seats:4`, four agents and
  `startingStacks:[1000,800,600,400]`; the detail has four agents, per-hand four `holeCards`
  (each two cards), four `finalStacks`, four `handNames` and non-empty pot layers whose amounts sum
  to `potAwarded`; `/viewer/table` has four `<section class="replay-seat">` panels, each seat's
  agent and both hole cards, the four hand names, the winner(s) and the settlement row.
- `ViewerAssets_GivenMultiwayTable_ExpectedSeatSelectorSnapshotSeatsAndNoExternalUrls` — `#seats`
  with options 2–6, `app.js` contains `snapshot.seats`, `liveSeats`, the legacy
  `holeCardsSeat0/1` fallback and no `innerHTML`; no `http://`, `https://` or `//cdn` in any viewer
  asset.

Total: 10 + 6 = **16 executed cases** (the two seat-count theory rows are two of the ten CLI cases).

No test uses a stub, skip or environment-dependent sleep beyond the established deterministic live
polling helper; all scratch data is created and deleted by the tests under the test output directory.

## 5. Mutation evidence

The original `LiveMatch.cs` was saved and hashed before the mutation:

```text
original_sha256=6224a513b0e186f6894f08e8e080cef8ab676719442ac5cdb61b665cc60fa8b3
```

Mutation applied in `LiveMatch.Snapshot`: `var seats = _agents.Length;` became `var seats = 2;`, so
the snapshot always reported two seat entries regardless of the configured table. A normal rebuild
succeeded, then the new HTTP class ran:

```text
$ dotnet build WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj -c Release --nologo
    0 Warning(s)
    0 Error(s)
mutation_build_exit=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll MultiwayViewerTests
FAIL MultiwayViewerTests.LiveMatch_GivenFourSeats_ExpectedFourSeatSnapshotsWithOwnHoleCardsAndLegacyFields: EqualException: Assert.Equal() Failure: Values differ
FAIL MultiwayViewerTests.LiveMatch_GivenSixSeats_ExpectedSixSeatSnapshotWithEveryOwnHoleCards: EqualException: Assert.Equal() Failure: Values differ
TOTAL 6 PASSED 4 FAILED 2 SKIPPED 0
mutation_run_exit=1
```

(As required, the 4-seat HTTP test failed; the 6-seat one failed too. The hash/determinism and
invalid-request cases still passed, as they do not depend on the snapshot length.)

The original file was restored from the saved copy and hash-verified, then the test project was
rebuilt with `--no-incremental` and the full suite re-ran:

```text
restored_sha256=6224a513b0e186f6894f08e8e080cef8ab676719442ac5cdb61b665cc60fa8b3
matches_original=True

$ dotnet build WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj -c Release --no-incremental --nologo
    2 Warning(s)
    0 Error(s)
restored_build_exit=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 273 PASSED 273 FAILED 0 SKIPPED 0
restored_run_exit=0
```

(The two warnings are the pre-existing `Wip.Runtime` CA2024 warnings from the forced rebuild of its
dependencies; no Tholdem project warning.)

## 6. Verification (observed output)

All commands were run from the repository root. `dotnet test` was **not** run.

### 6.1 Full Tholdem suite (supplementary runner, final tree)

```text
$ dotnet build WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj -c Release --nologo
    0 Warning(s)
    0 Error(s)
test_build_exit=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 273 PASSED 273 FAILED 0 SKIPPED 0
final_run_exit=0
```

**Exact test count: 273** = 257 pre-existing + 16 new executed cases.

### 6.2 Two-seat CLI oracle (byte-identical)

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe play --seed 42 --hands 20 --allow-weak-seed --data-dir <scratch>\oracle-final
```

```text
session: tholdem-06795fbeacfe528b
seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
agent                    hands    bb/100       95% CI (bb/100)  win rate     95% CI (win rate)  aggression  showdown
tight-aggressive            20  -1883.00   -3511.94 .. -254.06      0.15          0.05 .. 0.36        0.10      0.20
random-legal                20   1883.00     254.06 .. 3511.94      0.85          0.64 .. 0.95        3.25      0.20
oracle_exit=0
oracle_manifest_sha256=87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
oracle_session=tholdem-06795fbeacfe528b
```

The hand-1/hand-20 pinned hashes are also asserted by the pre-existing
`MultiwaySimulationTests` oracle fact, which is green in the 273 run.

### 6.3 Four-seat CLI run

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe play --seats 4 --agents random-legal,tight-aggressive,pot-odds-equity,random-legal --seed 42 --hands 3 --allow-weak-seed --data-dir <scratch>\four-seat-final
```

```text
session: tholdem-d56ac5f4637e44ef
seed: 42  hands: 3  manifest sha256: 792e9815db4b308bbf450e935619b8e81293046bcfca81b7621131755f82c542
agent                    hands    bb/100       95% CI (bb/100)  win rate     95% CI (win rate)  aggression  showdown
random-legal                 3    -16.67       -49.33 .. 16.00      0.00          0.00 .. 0.56        0.00      0.00
tight-aggressive             3  -6836.67 -10018.86 .. -3654.47      0.00          0.00 .. 0.56        0.00      0.33
pot-odds-equity              3   4840.00  -4744.39 .. 14424.39      0.33          0.06 .. 0.79        0.00      0.33
random-legal                 3   2013.33   -9992.49 .. 14019.16      0.67          0.21 .. 0.94        5.00      0.67
four_seat_exit=0
four_seat_session=tholdem-d56ac5f4637e44ef
four_seat_manifest_sha256=792e9815db4b308bbf450e935619b8e81293046bcfca81b7621131755f82c542
hand_files=3
```

`session.json` (seat-aware manifest) and `hand-00001.json` (v2) from that run:

```json
{"schema":"tholdem.session.v1","sessionId":"tholdem-d56ac5f4637e44ef","seed":42,"hands":3,"seats":4,"startingStack":1000,"startingStacks":[1000,1000,1000,1000],"smallBlind":5,"bigBlind":10,"agents":["random-legal","tight-aggressive","pot-odds-equity","random-legal"],"handHashes":["d39cf7727f7d5e6d632db9c3a086dede8b9caffdeec11c2ca24eb87375bff5ae","78e42e085ab93701153f3493c682e61187e5825f6eaeb7865803dcc11ebddf53","bc7b562ccddd9d3b2ddb88eac1c2c40e70e27f468d8f7b87ad3c3a60de19d5c0"]}

{"schema":"tholdem.hand.v2","handNumber":1,"seed":13149409904026330690,"smallBlind":5,"bigBlind":10,"startingStack":1000,"startingStacks":[1000,1000,1000,1000],"buttonSeat":0,"seats":4,"holeCards":[["Qs","8h"],["Tc","8s"],["4s","8d"],["Jd","Th"]],"board":["6c","9h","3d","Ks","6s"],"actions":[{"street":0,"seat":2,"kind":"Fold","amount":0,"potAfter":15},{"street":0,"seat":3,"kind":"Raise","amount":589,"potAfter":604},{"street":0,"seat":0,"kind":"Fold","amount":0,"potAfter":604},{"street":0,"seat":1,"kind":"Call","amount":0,"potAfter":1183},{"street":1,"seat":1,"kind":"Check","amount":0,"potAfter":1183},{"street":1,"seat":3,"kind":"Check","amount":0,"potAfter":1183},{"street":2,"seat":1,"kind":"Check","amount":0,"potAfter":1183},{"street":2,"seat":3,"kind":"Check","amount":0,"potAfter":1183},{"street":3,"seat":1,"kind":"Check","amount":0,"potAfter":1183},{"street":3,"seat":3,"kind":"Bet","amount":122,"potAfter":1305},{"street":3,"seat":1,"kind":"Fold","amount":0,"potAfter":0}],"pots":[{"amount":1305,"winners":[3]}],"showdown":false,"winners":[3],"potAwarded":1305,"finalStacks":[995,411,1000,1594]}
```

### 6.4 Real HTTP on a copied build

The Web project output was copied to `scratch/round3/viewer-build-final/` and started with
`dotnet Wip.Tholdem.Web.dll --urls http://127.0.0.1:5207 --data-dir <scratch>/viewer-data-final`
(never from `bin/Release`); the `sessions/` directory held the recorded 4-seat session above.

```text
GET /api/status -> 200 (server pid 20716, copied build)
POST /api/live -> 201 {"id":"a07ee49ee4efa5f9","seed":42,"hands":3,"delayMs":0,"seat0Agent":"random-legal","seat1Agent":"tight-aggressive","seats":4,"agents":["random-legal","tight-aggressive","pot-odds-equity","random-legal"]}
GET /api/live/a07ee49ee4efa5f9 -> status=complete seats=4 handNumber=3 hashes=3
  seat=0 agent=random-legal stack=1000 holeCards=2h,Ah folded=True allIn=False handName=One Pair, Threes winner=False dealer=False
  seat=1 agent=tight-aggressive stack=538 holeCards=8h,7c folded=True allIn=False handName=One Pair, Threes winner=False dealer=False
  seat=2 agent=pot-odds-equity stack=2462 holeCards=Ts,4c folded=False allIn=True handName=One Pair, Threes winner=True dealer=True
  seat=3 agent=random-legal stack=0 holeCards=2d,7s folded=False allIn=True handName=One Pair, Threes winner=False dealer=False
  legacy stacks=1000,538,2462,0 holeCardsSeat0=2h,Ah holeCardsSeat1=8h,7c
GET /api/sessions -> sessions=1 sessionId=tholdem-d56ac5f4637e44ef seats=4 agents=random-legal,tight-aggressive,pot-odds-equity,random-legal startingStacks=1000,1000,1000,1000
GET /api/sessions/tholdem-d56ac5f4637e44ef -> seats=4 agents=random-legal,tight-aggressive,pot-odds-equity,random-legal hands=3 holeCardsPerHand=4 finalStacks=995,411,1000,1594 pots=1 handNames=4
GET /viewer/table?session=tholdem-d56ac5f4637e44ef&hand=1 -> 200 text/html; charset=utf-8 seatSections=4 bytes=8381
server_stopped=True
```

(The live request body contained `seats:4` and the four `agents` names; the server was stopped after
the transcript. The same session was also replayed through the server-rendered table, which contained
four `.replay-seat` sections.)

### 6.5 Solution build

```text
$ dotnet build Wip.slnx --configuration Release
...
    125 Warning(s)
    0 Error(s)
Time Elapsed 00:01:24.32
solution_build_exit=0
```

The warnings are the pre-existing solution warnings (the heads-up baseline reported 126–127); no
Tholdem project produced one.

### 6.6 Architecture

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
architecture_exit=0
```

`ArchitectureReferenceTests.cs` still asserts exactly **48** projects.

### 6.7 Solution coverage

```text
$ powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
solution=Wip.slnx
wip_project_count=48
solution_wip_project_count=48
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
coverage_exit=0
```

### 6.8 HarnessSync

```text
$ dotnet run --project tools/HarnessSync -- --check
harnesssync_exit=0
```

(no output; all text files remain UTF-8 without BOM, including the two new test files and this
document)

## 7. Existing tests changed and why

**None.** No pre-existing test file was edited; the two new test files are additive. Every pre-existing
two-seat assertion (live snapshots, action log, pause/resume/stop, weak-seed guard, replay table
faces, settlement row, agent headers, idle table, responsive CSS, strategies/ledger, learning web
integration) is green unchanged. Public shape changes are additive:

- `SpectatorSnapshot.Seats`, `SpectatorSeat`, `StartLiveMatchRequest.Seats/Agents/StartingStacks`,
  `LiveMatchStarted.Seats/Agents`, `LiveMatch.SeatCount/Agents`,
  `LiveMatchRegistry.Start(..., IReadOnlyList<IPokerAgent>, ...)`.
- `SessionSummary.Seats/StartingStacks`, `SessionDetail.Seats/StartingStacks`,
  `HandSummary.HoleCards/Pots`, `RecordedHand.HoleCards/Seats/StartingStacks/Pots`; the legacy
  `RecordedHand.HoleCardsSeat0/1` and `HandNames` remain in the JSON.
- CLI options `--seats`, `--agents`, `--starting-stacks`; `train`/`evaluate` are untouched.

The two-seat visible replay layout now uses the same N-panel grid with two panels; the visible
content (cards, hand names, dealer, winner, settlement, action log) is equivalent.

## 8. Compatibility evidence

- **Oracle:** session `tholdem-06795fbeacfe528b`, manifest
  `87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618`; the pinned hand-1
  `709c0816…` and hand-20 `bef6917b…` hashes are asserted by the pre-existing simulator oracle fact,
  green in the 273 run.
- **CLI bytes:** the two-seat `play` path still goes through `MatchSimulator` with the same seed,
  button rotation, names, file names and writer order; the manifest hash observed above is identical
  to the heads-up evidence.
- **Live hashes:** the live worker's hand hashes equal the simulator's written files for the same
  4-seat configuration (the 4-seat hash test), and two identical live runs have equal hashes.
- **Replay:** all pre-existing two-seat replay tests are green; the catalog reads both hand schemas.

## 9. Limits / what was not done

- **`dotnet test` was not run** (prohibited by the task and unusable here); local evidence is the
  supplementary runner over the compiled assembly. CI was not triggered or observed, so no CI green
  is claimed.
- **The viewer's JavaScript was not executed in a browser as part of the suite.** The tests assert
  markup/CSS/`app.js` contracts over the real HTTP surface (the established pattern in this working
  tree); no new Playwright/browser run was performed. The seat grid, three-column 5/6 layout and
  one-column phone layout are CSS/markup assertions plus the server-rendered replay output.
- **The learning loop remains heads-up.** `train`, `evaluate` and `promote` still run two-agent
  matches; multiway strategies are not trained or promoted by this round. This is documented in
  `docs/tholdem/known-limits.md`, not changed.
- **v1 two-seat hands have no pot-layer list.** The canonical v1 form never recorded `pots`, so
  `/api/sessions/{id}` reports an empty `pots` list for those hands; the awarded total and winners
  are still present. v2 hands (3+ seats or unequal two-seat stacks) expose the layers.
- **3+ seat live bodies require `agents`.** A 3–6 seat request with only `seat0Agent`/`seat1Agent` is
  rejected with `invalid-agents` rather than silently duplicating two names.
- The default rooms for seats 2–5 in the viewer form alternate the two visible agent choices; the CLI
  supports any per-seat combination through `--agents`.
- `%TEMP%` was not used; all scratch is under the test output directory. The viewer was run from a
  copied output, never `bin/Release`.

## 10. Test count bookkeeping

`docs/plan/test-waivers.json` records `"Wip.Tholdem.Tests": 273` (was `257`; +16 new executed
cases). The other project totals are untouched; project count stays 48.

Artifacts (all under `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/round3/` unless noted):
`round3-red-build.txt`, `round3-red-http.txt`, `round3-red-cli.txt`,
`round3-first-green-build.txt`, `round3-http-run1.txt`, `round3-cli-run1.txt`,
`round3-http-run2.txt`, `round3-cli-run2.txt`, `round3-full-run1.txt`,
`round3-mutation-original-sha.txt`, `LiveMatch.cs.orig`, `round3-mutation-build.txt`,
`round3-mutation-run.txt`, `round3-mutation-restore.txt`, `round3-restored-build.txt`,
`round3-restored-run.txt`, `round3-final-full-run.txt`, `round3-final-full-run2.txt`,
`round3-cli-oracle.txt`,
`round3-cli-oracle-final.txt`, `round3-cli-four-seat.txt`, `round3-cli-four-seat-final.txt`,
`round3-http-start-4seat.json`, `round3-http-snapshot-4seat.json`, `round3-http-sessions.json`,
`round3-http-table-4seat.html`, `round3-http-final.txt`, `round3-http-final-snapshot.json`,
`round3-http-final-sessions.json`, `round3-http-final-detail.json`,
`round3-http-final-table.html`, `round3-solution-build.txt`, `round3-architecture.txt`,
`round3-coverage.txt`, `round3-harnesssync.txt`, `oracle-final/`, `four-seat-final/`,
`four-seat-final-session/`, `viewer-build-final/`, `viewer-data-final/`.
