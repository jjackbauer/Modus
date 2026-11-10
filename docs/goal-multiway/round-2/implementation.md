# Round 2 — N-seat simulator and runtime session store

**Status:** complete for the round objective; local supplementary suite green at **257 tests**
(246 before + 11 new executed cases), zero failures. `dotnet test` was **not** run (prohibited by the
task and unusable in this session); the compiled test assembly was executed with the supplementary
xUnit runner `.tmp/tholdem-verify/RunXunit`. CI was not triggered or observed.

## 1. Scope and environment

- Repository `C:\Users\ricar\dev\Modus`, branch `WIP`.
- Changed only allowed paths: `WIP/Wip.Tholdem.Agents/Simulation/`,
  `WIP/Wip.Tholdem.Agents/Runtime/`, `WIP/Wip.Tholdem.Tests/`,
  `docs/tholdem/strategy-artifact-schema.md`, `docs/plan/test-waivers.json` and this file.
- No engine, history, viewer, CLI, learning-loop, `Wip.slnx`, architecture-test or `.github/` file
  was edited. No project was added or removed: the architecture run still observes **48** projects.
- Scratch, sessions and logs live under
  `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/round2/`; `%TEMP%` was not used.
- The four-seat CLI-style session was written by the scratch probe
  `.tmp/tholdem-verify/MultiwaySessionProbe` (the CLI still has no `--seats` option — out of scope);
  the replay evidence below is the real `Wip.Tholdem.Host replay` command.

## 2. What changed

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Simulation/SimulationOptions.cs` | trailing optional `IReadOnlyList<long>? StartingStacks = null` (copied on construction). Default: every seat uses `StartingStack`. |
| `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs` | new `Run(IReadOnlyList<IPokerAgent>, SimulationOptions, CancellationToken)` for 2–6 agents; the two-agent overload now delegates to it; per-seat action counts/aggression/calls/showdown participation; session id over every seat name; v2-aware manifest; per-seat metrics against each seat's own starting stack; report shape unchanged. |
| `WIP/Wip.Tholdem.Agents/Simulation/MetricsCalculator.cs` | only the seat guard changed (`0 or 1` → `0..finalStacks.Count-1`). Wilson/normal intervals, net chips, aggression factor and showdown rate formulas are byte-for-byte the same code. |
| `WIP/Wip.Tholdem.Agents/Simulation/SessionManifest.cs` | **new** canonical manifest read model. Missing `seats` parses as the recorded two-seat default and `startingStack` is repeated per seat, so old manifests remain readable. |
| `WIP/Wip.Tholdem.Agents/Runtime/SessionHandStore.cs` | optional `seats` constructor (default 2) and `SeatCount`; 2–6-seat `BuildFor` using `record.HoleCards[seat]`; net chips use `record.StartingStacks[seat]`; a folded seat is not credited with a showdown; hand-weighted multi-seat statistics aggregate (`BuildStatisticsFor(IReadOnlyList<int>)`) from raw counters. |
| `WIP/Wip.Tholdem.Agents/Runtime/OpponentStatsTool.cs` | aggregates every seat other than the requesting seat; `Opponents` (ascending) added to the result; `Seat` is the single opponent for a two-seat table and `-1` for several opponents. |
| `WIP/Wip.Tholdem.Agents/Runtime/HandHistoryTool.cs` | seat validation now accepts 0..5; record shape unchanged. |
| `WIP/Wip.Tholdem.Tests/MultiwaySimulationTests.cs` | **new**, 9 executed cases (oracle, 3/4/6-seat determinism, 6-seat zero-sum, controlled metrics, 4-seat illegal action, 4-seat session files, 6-seat hidden information). |
| `WIP/Wip.Tholdem.Tests/Runtime/MultiwayRuntimeSessionTests.cs` | **new**, 2 facts (N-seat `BuildFor`, hand-weighted opponent statistics). |
| `docs/tholdem/strategy-artifact-schema.md` | session-id rule over all seats, `seats`/`startingStacks` manifest layout, v2 hand example, per-seat report note. |
| `docs/plan/test-waivers.json` | `Wip.Tholdem.Tests` total 246 → **257**. |

### Contract details

- **Hand loop.** Hand `n` uses `SeedDerivation.Derive(options.Seed, n, 0, 0, 0)`,
  `buttonSeat = AlternateButton ? (n - 1) % seats : 0`, and
  `HoldemHandEngine.Start(handSeed, seats, handNumber: n, options.StartingStack,
  options.SmallBlind, options.BigBlind, buttonSeat, startingStacks)`.
- **Per turn.** `AgentTurnContext(hand, actingSeat, actionCounts[actingSeat])` — public turn
  coordinates only, no seed. The observation is `PlayerObservation.ForSeat(state, actingSeat)`.
- **Two-agent delegation.** `Run(seat0, seat1, options, ct)` calls
  `Run(new[] { seat0, seat1 }, options, ct)` after its original null checks. Nothing about the
  two-seat engine path, seed derivation, name normalization, session-id material or writer order
  changed.
- **Per-seat tracking.** `HandOutcome` keeps its recorded shape: final stacks, winners, showdown,
  aggressive actions, calls and showdown participants are one value per seat.
  `ShowdownParticipants[seat] = ShowdownReached && !Folded[seat]`; for two seats a showdown implies
  both seats are live, so the recorded `[true, true]` / `[false, false]` behaviour is unchanged.
- **Metrics.** `MetricsCalculator.Compute(name, seat, outcomes, seatStartingStack, bigBlind)` per
  seat. Net chips and bb/100 therefore use that seat's own stack; per-seat `netChips` sum to exactly
  zero because the engine settles every pot layer.
- **Manifest layout.** The v1/v2 split mirrors `HandRecordCanonicalWriter`: a **two-seat session
  with equal starting stacks** writes exactly the pre-change manifest (no `seats`, no
  `startingStacks`). Every other session inserts `"seats":<n>` after `"hands"`, inserts
  `"startingStacks":[…]` after `"startingStack"`, and its `"startingStack"` is the first seat's
  effective stack. `startingStack` comes from the engine-normalized `state.StartingStacks`, so it is
  identical to `options.StartingStack` in every pre-existing case.
- **Report.** `tholdem.report.v1` keeps its recorded member order and adds nothing; it naturally
  carries one `metrics` object per seat, each with its `seat` index. The manifest hash embedded in
  it is the hash of the new seat-aware manifest for multiway sessions.
- **Runtime hidden information.** `SessionHandStore.BuildFor` replays the record through
  `HoldemHandEngine.Replay` and projects it through `PlayerObservation.ForSeat`; the view type still
  has no member for the seed, the deck or another seat's cards. `OpponentStatsTool` only ever
  returns aggregate rates.
- **Opponent aggregation.** Hands are summed; VPIP/PFR/showdown are recomputed from summed counters
  (hand-weighted); the aggression factor keeps the recorded `aggressive / max(1, calls)` formula on
  summed counters. A folded seat is excluded from the showdown count.

## 3. Test-first evidence (red)

`MultiwaySimulationTests.cs` was created first with the 4-seat determinism fact and the 6-seat
zero-sum fact (both required by the task) against the pre-change API. The test project did not
compile:

```text
$ dotnet build WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj -c Release --nologo
...\MultiwaySimulationTests.cs(33,46): error CS7036: There is no argument given that corresponds to the required parameter 'options' of 'MatchSimulator.Run(IPokerAgent, IPokerAgent, SimulationOptions, CancellationToken)'
...\MultiwaySimulationTests.cs(36,47): error CS7036: There is no argument given that corresponds to the required parameter 'options' of 'MatchSimulator.Run(IPokerAgent, IPokerAgent, SimulationOptions, CancellationToken)'
...\MultiwaySimulationTests.cs(39,50): error CS7036: There is no argument given that corresponds to the required parameter 'options' of 'MatchSimulator.Run(IPokerAgent, IPokerAgent, SimulationOptions, CancellationToken)'
...\MultiwaySimulationTests.cs(89,21): error CS1739: The best overload for 'SimulationOptions' does not have a parameter named 'StartingStacks'
    0 Warning(s)
    4 Error(s)
red_build_exit=1
```

(Full log: `scratch/round2-red-build.txt`.) The remaining seven executed cases were added after this
red state was recorded, before the product changes were finalized.

## 4. Tests added (11 executed cases, zero skips)

`WIP/Wip.Tholdem.Tests/MultiwaySimulationTests.cs`:

1. `Run_GivenTwoAgentsSeed42_ExpectedUnchangedSessionIdManifestHashAndHandBytes` — pins session
   `tholdem-06795fbeacfe528b`, manifest `87f90413…`, hand-1 `709c0816…` and hand-20 `bef6917b…`; the
   manifest has no `seats`/`startingStacks` member and `SessionManifest.Parse` reads it as two seats
   with `[1000, 1000]`.
2. `Run_GivenMultiwayAgentsAndSameSeed_ExpectedByteIdenticalFilesAndManifestHash(3|4|6)` — two
   directories are byte-identical (12 hands + `session.json`); a different seed changes the session
   id, manifest hash and hand bytes; every recorded `buttonSeat` equals `(n - 1) % seats`.
3. `Run_GivenSixSeatsOneHundredHandsWithPerSeatStacks_ExpectedZeroSumPerSeatAndPerHandStackTotals` —
   100 hands with stacks `[1000,900,800,700,600,500]`: per-seat net chips sum to exactly zero, every
   record's starting stacks equal the input and every record's final stacks sum to 4900; each seat's
   reported net chips equal the sum of its recorded `final - starting` deltas.
4. `Run_GivenControlledThreeSeatTable_ExpectedLiteralWinsShowdownsAndAggression` — always-all-in
   seat 0 vs always-call seat 1 vs always-fold seat 2, 12 hands: literal wins `7 / 6 / 0`, showdown
   rates `1.0 / 1.0 / 0.0`, aggression factors `12.0 / 0.0 / 0.0`; the single split pot is counted
   by reading the hand files (`tiedHands == 1`) and wins sum to `12 + 1`.
5. `Run_GivenIllegalActionAtFourSeats_ExpectedExceptionNamingAgentAndHandWithoutFallback` — the
   illegal agent at seat 2 acts first in hand 1; `IllegalAgentActionException` names
   `four-seat-illegal-agent`, hand `1`, action `RaiseTo(1)` and the directory stays empty.
6. `Run_GivenFourSeatsAndPerSeatStacks_ExpectedV2FilesSeatAwareManifestAndReplay` — five hands with
   stacks `[1000,800,600,400]`: six files, manifest contains `"seats":4` and
   `"startingStacks":[1000,800,600,400]`, four agent names, the session id equals the independently
   recomputed all-seat name hash; every hand parses, canonical-round-trips, hash-verifies against the
   manifest and replays to the recorded final stacks; report net chips equal the recorded deltas.
7. `Run_GivenSixSeats_ExpectedEverySeatObservationHidesSeedAndForeignHoleCards` — every observation
   of every seat of a six-seat hand: no hand/session seed text, no `seed`/`deck` token, no foreign
   hole card, no card outside the final board plus that seat's own hole cards, and the own cards are
   present.

`WIP/Wip.Tholdem.Tests/Runtime/MultiwayRuntimeSessionTests.cs`:

8. `BuildFor_GivenFourSeatHand_ExpectedOnlyOwnHoleCardsAndNoHiddenInformation` — a recorded 4-seat
   hand; `BuildFor(1, 10)` returns seat 1's own hole cards, board, winners, pot and
   `finalStacks[1] - startingStacks[1]`; the serialized view contains no other seat's cards, no
   seed and no deck token.
9. `OpponentStatsTool_GivenFourSeatHands_ExpectedHandWeightedAggregateOfOtherSeats` — two recorded
   4-seat hands (seat 0 raise/bet, seat 1 folds, seat 2 calls, seat 3 folds):
   `OpponentStatsTool(1)` reports `Opponents=[0,2,3]`, `Seat=-1`, hands `6`, VPIP `2/3`, PFR `1/3`,
   aggression `0.8` and showdown `2/3`, cross-checked against a recount of the recorded actions
   (`aggressive == 8`, `calls == 10`); `BuildStatisticsFor(1)` (the hero) is all zeros and
   `HandHistoryTool(2)` returns newest-last views.

No test uses a stub simulator or a skipped fact; the only custom agents are the deliberately
controlled baselines in the metrics/illegal-action facts.

## 5. Mutation evidence (button rotation)

The original simulator was saved and hashed before the mutation:

```text
original_sha256=2f82f78ca7091494e4437ec0099f3e030901aff96582db4be785bb67aef64478
```

Mutation applied to `MatchSimulator.Run`: `var buttonSeat = options.AlternateButton ? (hand - 1) % seats : 0;`
became `var buttonSeat = 0;` (same file, same encoding). A normal rebuild succeeded, then the new
simulator test class ran:

```text
$ dotnet build WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj -c Release --nologo
    0 Warning(s)
    0 Error(s)
mutation_build_exit=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll MultiwaySimulationTests
FAIL ...Run_GivenMultiwayAgentsAndSameSeed_ExpectedByteIdenticalFilesAndManifestHash(3): EqualException:
Expected: 1
Actual:   0
FAIL ...Run_GivenMultiwayAgentsAndSameSeed_ExpectedByteIdenticalFilesAndManifestHash(4): EqualException:
Expected: 1
Actual:   0
FAIL ...Run_GivenMultiwayAgentsAndSameSeed_ExpectedByteIdenticalFilesAndManifestHash(6): EqualException:
Expected: 1
Actual:   0
FAIL ...Run_GivenTwoAgentsSeed42_ExpectedUnchangedSessionIdManifestHashAndHandBytes: EqualException:
Expected: "87f90413e929d99fff9c833f9768cda3da22e4939"···
Actual:   "38a555d1cf6c689f07a9020487a213fb97a66aa01"···
TOTAL 9 PASSED 5 FAILED 4 SKIPPED 0
mutation_run_exit=1
```

(The three determinism facts fail on the documented `buttonSeat` sequence; the two-seat oracle also
fails because heads-up alternation is part of the recorded contract. At least one N-seat
determinism/metrics fact failed, as required.)

The original file was restored from the saved copy and hash-verified, then the whole test project was
rebuilt with `--no-incremental` and the full suite re-ran:

```text
restored_sha256=2f82f78ca7091494e4437ec0099f3e030901aff96582db4be785bb67aef64478
matches_original=True

$ dotnet build WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj -c Release --no-incremental --nologo
    2 Warning(s)
    0 Error(s)
restored_build_exit=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 257 PASSED 257 FAILED 0 SKIPPED 0
restored_run_exit=0
```

(The two warnings are the pre-existing `Wip.Runtime` CA2024 warnings emitted by the forced rebuild of
its dependencies; no Tholdem project warning.)

## 6. Verification (observed output)

All commands were run from the repository root. `dotnet test` was **not** run.

### 6.1 Test project build

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo
Build succeeded.
    0 Warning(s)
    0 Error(s)
build_exit=0
```

### 6.2 Full Tholdem suite (supplementary runner, final tree)

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 257 PASSED 257 FAILED 0 SKIPPED 0
final_run_exit=0
```

**Exact test count: 257** = 246 pre-existing + 11 new executed cases. The recorded v1 canonical hash
`c953464b…`, the 30-file recorded-v1 replay sweep and every pre-existing two-seat test stay green.

### 6.3 Solution build

```text
$ dotnet build Wip.slnx --configuration Release
    125 Warning(s)
    0 Error(s)
Time Elapsed 00:01:29.43
solution_build_exit=0
```

The warnings are the pre-existing solution warnings (the heads-up baseline reported 126–127); no
Tholdem project produced one.

### 6.4 Architecture

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
architecture_exit=0
```

`ArchitectureReferenceTests.cs:16` still asserts exactly **48** projects.

### 6.5 Solution coverage

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

### 6.6 HarnessSync

```text
$ dotnet run --project tools/HarnessSync -- --check
harnesssync_exit=0
```

(no output; all text files remain UTF-8 without BOM)

### 6.7 Two-seat CLI oracle (byte-identical)

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe play --seed 42 --hands 20 --allow-weak-seed --data-dir <scratch>\oracle-final
```

```text
session: tholdem-06795fbeacfe528b
seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
agent                    hands    bb/100       95% CI (bb/100)  win rate     95% CI (win rate)  aggression  showdown
tight-aggressive            20  -1883.00   -3511.94 .. -254.06      0.15          0.05 .. 0.36        0.10      0.20
random-legal                20   1883.00     254.06 .. 3511.94      0.85          0.64 .. 0.95        3.25      0.20
cli_exit=0
baseline_byte_mismatches=0
manifest_sha256=87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
```

The pre-change baseline (`scratch/round2/oracle-baseline-data/`) was captured before any edit; all
21 files (20 hands + `session.json`) are byte-identical after the change (`0` mismatches).

### 6.8 Four-seat session: file list, seat-aware manifest, replay

```text
$ dotnet run --project .tmp/tholdem-verify/MultiwaySessionProbe -c Release -- <scratch>\four-seat-session-final
session: tholdem-1ae40dc5dbb84f25
seed: 4242  hands: 5  manifest sha256: 441c503235945b009582c89539facc1b3372b9308c811cf4630e057f6c27cd32
seat probe-seat-0: hands=5 wins=5 netChips=-1525 showdownRate=0.8
seat probe-seat-1: hands=5 wins=3 netChips=590 showdownRate=0.6
seat probe-seat-2: hands=5 wins=2 netChips=545 showdownRate=0.6
seat probe-seat-3: hands=5 wins=1 netChips=390 showdownRate=0.6
probe_exit=0
```

(Net chips `-1525 + 590 + 545 + 390 = 0`.)

File list (6 files):

```text
hand-00001.json
hand-00002.json
hand-00003.json
hand-00004.json
hand-00005.json
session.json
```

`session.json`:

```json
{"schema":"tholdem.session.v1","sessionId":"tholdem-1ae40dc5dbb84f25","seed":4242,"hands":5,"seats":4,"startingStack":1000,"startingStacks":[1000,800,600,400],"smallBlind":5,"bigBlind":10,"agents":["probe-seat-0","probe-seat-1","probe-seat-2","probe-seat-3"],"handHashes":["127489d0acbb740aab233d7c387ace4a1c73952f1b737756361ced620a890fb7","befdff87fb3a8bbe9fea3ff7d644c407906e2949508bedc6487b64a458935c3a","57c51ad454649b7fcba8c021d64ffc5e3f274aa0b2af1684800e74c838181434","02fc2bc25e84134219992947abf9a298e819e2ad03e5ee365b6faef70c217112","618ea14b8d16018129768e254c6b175afca5b044d8e6419b6da80a30ec957c3c"]}
```

`hand-00001.json` (v2):

```json
{"schema":"tholdem.hand.v2","handNumber":1,"seed":7927181507143236163,"smallBlind":5,"bigBlind":10,"startingStack":1000,"startingStacks":[1000,800,600,400],"buttonSeat":0,"seats":4,"holeCards":[["Ks","Tc"],["Td","Ac"],["Jd","5s"],["Jc","Jh"]],"board":["4h","8c","5h","6d","Js"],"actions":[{"street":0,"seat":2,"kind":"AllIn","amount":0,"potAfter":615},{"street":0,"seat":3,"kind":"AllIn","amount":0,"potAfter":1015},{"street":0,"seat":0,"kind":"Call","amount":0,"potAfter":1610},{"street":0,"seat":1,"kind":"Call","amount":0,"potAfter":2200},{"street":1,"seat":1,"kind":"AllIn","amount":0,"potAfter":2400},{"street":1,"seat":0,"kind":"Raise","amount":400,"potAfter":0}],"pots":[{"amount":1600,"winners":[3]},{"amount":600,"winners":[2]},{"amount":400,"winners":[1]},{"amount":200,"winners":[0]}],"showdown":true,"winners":[0,1,2,3],"potAwarded":2800,"finalStacks":[200,400,600,1600]}
```

Replay of one hand through the real CLI (the file sits beside `session.json`, so the manifest
anchors it — no `integrity: unanchored` line):

```text
$ WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe replay <scratch>\four-seat-session-final\hand-00002.json
hand: 2
board: 5c 5h 5s Td 8h
seat 0: Ad Kd
seat 1: 5d 7s
actions:
  preflop seat=3 kind=Fold amount=0 potAfter=15
  preflop seat=0 kind=Call amount=0 potAfter=25
  preflop seat=1 kind=Raise amount=448 potAfter=468
  preflop seat=2 kind=AllIn amount=0 potAfter=1058
  preflop seat=0 kind=AllIn amount=0 potAfter=2048
  preflop seat=1 kind=AllIn amount=0 potAfter=0
showdown: true
winners: 0, 1
pot awarded: 2400
final stacks: 200 / 2200 / 0 / 400
replayed stacks: 200 / 2200 / 0 / 400  pot: 0
replay_exit=0
```

`hand-00001.json` hash `127489d0…` equals `handHashes[0]`; the session id, manifest hash and every
hand hash were reproduced identically when the probe was re-run after the final solution build.

### 6.9 Legacy manifests without `seats`

The oracle fact above parses the recorded seed-42 manifest (which has no `seats` member) with the
product's `SessionManifest.Parse`: it observes `Seats == 2`, `StartingStacks == [1000, 1000]` and
both recorded agent names. That fact is green inside the 257-test run.

## 7. Existing tests changed and why

**None.** Every pre-existing test file is byte-for-byte unchanged, including the two-seat pinned
canonical hash, the 30-file recorded-v1 sweep, `PlayerObservation` JSON pins, metrics formulas,
learning-loop tests and the CLI/runtime tests. The two-agent public behaviour is unchanged:

- The two-agent overload delegates to the general overload with the same seed, button rotation,
  names, file names and writer order.
- `MetricsCalculator` formulas are untouched; only the seat range check widened.
- `SessionHandStore.BuildStatisticsFor` for two seats behaves identically (a two-seat showdown
  implies no folded seat, so the new folded filter changes nothing there).
- `OpponentStatsResult.Seat` for a two-seat store is the same opponent seat as before; `Opponents`
  is a new init-only member.

## 8. Compatibility evidence

- **Oracle:** session `tholdem-06795fbeacfe528b`, manifest
  `87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618`; hand-1
  `709c0816b59b18c7514198901d51ac0e0af050f02b27795a4c27521908324fc5`, hand-20
  `bef6917b919453b13f041dc8a56e2cfc7d91bc9c26a48fe4798c630a706a411f`.
- **All bytes:** the pre-change CLI baseline directory and the post-change run have identical file
  lists and `0` byte mismatches across all 21 files.
- **Recorded v1 hashes:** the pinned `c953464b…` two-seat hand hash and the recorded-v1 replay sweep
  are green in the 257 run; no history writer changed.
- **Determinism:** 3, 4 and 6 seats are byte-identical for the same seed and agents, and a different
  seed changes the session id, manifest hash and hand bytes.

## 9. Limits / what was not done

- **`dotnet test` was not run** (prohibited by the task and unusable here); local evidence is the
  supplementary runner over the compiled assembly. CI was not triggered or observed, so no CI green
  is claimed.
- Engine, hand history, viewer, CLI `--seats`, learning loop, `Wip.slnx`, architecture tests and
  `.github/` were out of scope and are unchanged. The four-seat session is produced by a scratch
  probe; only the replay is the real CLI.
- `OpponentStatsResult.Seat` is `-1` when more than one opponent is aggregated;
  `OpponentStatsResult.Opponents` lists the seats. This is an additive result member, documented in
  the tool's XML comment; the two-seat behaviour is preserved.
- `SessionHandStore` assumes one seat count per session (a session-wide table). A store built from
  hand records of mixed seat counts would resolve `SeatCount` to the largest count and reject
  out-of-range views loudly rather than silently skipping them.
- The two-seat `startingStack`/manifest/report scalar remains byte-identical; for a multiway session
  the scalar is the first seat's effective stack, and the authoritative per-seat values are
  `startingStacks`.

## 10. Test count bookkeeping

`docs/plan/test-waivers.json` records `"Wip.Tholdem.Tests": 257` (was `246`; +11 new executed
cases). The other project totals are untouched.

Artifacts (all under `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/round2/`):
`round2-red-build.txt`, `round2-first-green-build.txt`, `round2-first-green-run.txt`,
`round2-full-run1.txt`, `round2-full-run2.txt`, `round2-multiway-run1.txt`,
`round2-mutation-original-sha.txt`, `round2-mutation-build.txt`, `round2-mutation-run.txt`,
`MatchSimulator.cs.orig`, `round2-restored-build.txt`, `round2-restored-run.txt`,
`round2-cli-oracle.txt`, `round2-cli-oracle-final.txt`, `round2-solution-build.txt`,
`round2-architecture.txt`, `round2-coverage.txt`, `round2-harnesssync.txt`,
`round2-four-seat-probe-final.txt`, `round2-four-seat-replay-final.txt`,
`round2-final-full-run.txt`, `oracle-baseline-data/`, `oracle-final/`,
`four-seat-session-final/`.
