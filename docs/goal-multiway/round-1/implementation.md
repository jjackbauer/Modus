# Round 1 — Multiway engine, side pots, N-seat observation and history v2

**Status:** complete for the round objective; local supplementary suite green at **241 tests**
(194 pre-existing + 47 new), zero failures. `dotnet test` was not run (VSTest is unusable in this
session); the compiled test assembly was executed with the supplementary xUnit runner
`.tmp/tholdem-verify/RunXunit`. CI was not observed.

## 1. Scope and environment

- Repository: `C:\Users\ricar\dev\Modus`, branch `WIP`. Only the allowed paths were changed:
  `WIP/Wip.Tholdem/Engine/`, `WIP/Wip.Tholdem/History/`, `WIP/Wip.Tholdem.Tests/`,
  `docs/tholdem/engine-rules.md`, `docs/plan/test-waivers.json` and this file.
- No project was added or removed; `Wip.slnx` and the architecture-test project count stay at
  **48**. `Wip.Tholdem.Agents`, `.Web`, `.Host`, the learning loop and `.github/` were not edited.
- Scratch output lives under `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/`; `%TEMP%` was not
  used. The two small probe programs used for evidence live under `.tmp/tholdem-verify/`
  (`ObservationProbe`, `MultiwayHandProbe`).

## 2. API shape and design decisions

- **`TableState` was generalized in place; the name was kept.** The contract allowed either
  `HoldemTableState` or keeping `TableState`; keeping the name keeps the out-of-scope Host/Web/Agents
  call sites source-compatible. `TableState` now exposes `SeatCount`, `SmallBlindSeat`,
  `BigBlindSeat`, `TotalCommitted`, `Folded`, `AllIn`, `Pots` and a public `AwardedPot`, alongside
  the existing members. `PotAward(long Amount, IReadOnlyList<int> Winners)` is a public record in
  `Wip.Tholdem.Engine`.
- **Hidden state remains internal**: `Seed`, `Deck`, per-seat `HoleCards` (with
  `HoleCardsSeat0`/`HoleCardsSeat1` compatibility projections), `DeckPosition`, `ActedMask` and
  `ActionLog`.
- **`HeadsUpHandEngine` is now a thin 2-seat wrapper** over `HoldemHandEngine` for `Start`,
  `GetLegalActions`, `Apply` and `Replay`. The pinned `SplitPot(pot, buttonSeat)` helper is
  retained.
- **`PotAward` layers are not merged.** Each showdown contribution layer is recorded as its own
  award (the contract allowed merging only if stated and tested; not merging keeps the layer
  arithmetic independently checkable by the brute-force property test). `PotAward.Winners` is always
  ascending; a fold-win records one award for the whole pot.
- **Uncalled excess is settled as the top pot layer.** There is no early refund path: when the
  highest contribution level has a single eligible seat, the whole layer is awarded to it. Therefore
  the chip-conservation invariant holds with `returned == 0`:
  `sum(Stacks) + Pot == SeatCount * StartingStack` after every `Apply` (and
  `Stacks[0] + Stacks[1] + Pot == 2 * StartingStack` for two seats).
- **`PlayerObservation`** now has `SeatCount`, `Seat` and `Seats` (`IReadOnlyList<SeatPublicState>`
  with `Seat`, `Stack`, `StreetCommitment`, `Folded`, `AllIn`). For three or more seats these three
  members are appended to the canonical JSON; for two seats the JSON is byte-identical to the
  pre-change form and none of them is emitted. The legacy scalar legality members
  (`AmountToCall`, `MinRaiseTo`, `MaxRaiseTo`, `LegalActionKinds`) remain the **acting seat's**
  projection for every seat count, exactly as in v1; per-seat stacks/commitments/flags are in
  `Seats`. `OpponentStack`/`OpponentStreetCommitment` are the other seat for two seats and the sum
  over all other seats for three or more.
- **`HandRecord`** keeps its original 14 constructor members (so v1 construction call sites still
  compile) and adds init members `SeatCount` (default 2), per-seat `HoleCards` and `Pots`. The v1
  writer is used only when `SeatCount == 2`; v2 is used for 3–6 seats. `HandRecordJson.Parse`
  detects v2 by the presence of `"seats"` and otherwise runs the unchanged v1 parser.

## 3. Files changed

Product (all inside the allowed scope):

| File | Change |
|---|---|
| `WIP/Wip.Tholdem/Engine/TableState.cs` | generalized in place; `PotAward`; new per-seat public members; hidden members internal |
| `WIP/Wip.Tholdem/Engine/HoldemHandEngine.cs` | **new** general 2–6 seat engine (`Start`, `GetLegalActions`, `Apply`, `Replay`, internal `ResolveShowdown`) |
| `WIP/Wip.Tholdem/Engine/HeadsUpHandEngine.cs` | rewritten as a 2-seat delegation wrapper; `SplitPot` retained |
| `WIP/Wip.Tholdem/History/HandRecord.cs` | `SeatCount`, `HoleCards`, `Pots`; `From` projects per-seat holes and pots |
| `WIP/Wip.Tholdem/History/HandRecordCanonicalWriter.cs` | unchanged v1 writer behind a 2-seat branch; new fixed-order v2 writer |
| `WIP/Wip.Tholdem/History/HandRecordJson.cs` | v2 detection and validation; v1 parser unchanged |
| `WIP/Wip.Tholdem/History/PlayerObservation.cs` | N-seat projection; `SeatPublicState`; 2-seat JSON preserved exactly |

`PokerAction`, `LegalActionSet`, `ActionKind`, `Street` and the enums were not changed.

Tests:

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Tests/MultiwayTestSupport.cs` | new helpers: deterministic random play, crafted river states, brute-force pot calculator, award/tie helper |
| `WIP/Wip.Tholdem.Tests/MultiwayHoldemEngineTests.cs` | new: 2-seat equivalence, rotation/order, per-seat legality, hand-crafted side pots, property test (36 executed cases) |
| `WIP/Wip.Tholdem.Tests/MultiwayObservationTests.cs` | new: exact 2-seat JSON strings, 3/6-seat boundary, folded-seat public state, seat-range validation (5 executed cases) |
| `WIP/Wip.Tholdem.Tests/MultiwayHistoryTests.cs` | new: v1 hash pin, v2 round-trip/replay, 4-seat fold v2 record, all recorded v1 files swept (6 executed cases) |
| `WIP/Wip.Tholdem.Tests/PlayerObservationTests.cs` | **the only existing test changed**: the pinned public-property list gained `Seat`, `SeatCount` and `Seats` |

Why the one existing test changed: the round objective requires `PlayerObservation` to expose the
requesting seat, the seat count and per-seat public state; that test is a reflection pin of the
public property set, so its expected list genuinely changed. No expected value of any behavioural
test was altered and no test was skipped, weakened or deleted.

## 4. Test-first evidence (red)

The new tests were written before the engine/history/observation changes. The test project did not
compile against the pre-change code; the captured compile log contains 40 `error CS` diagnostics and
no successful build. Excerpt:

```text
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayTestSupport.cs(21,12): error CS0103: The name 'HoldemHandEngine' does not exist in the current context
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayHoldemEngineTests.cs(17,27): error CS0103: The name 'HoldemHandEngine' does not exist in the current context
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayHistoryTests.cs(19,32): error CS1061: 'HandRecord' does not contain a definition for 'SeatCount' ...
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayTestSupport.cs(120,13): error CS0117: 'TableState' does not contain a definition for 'SeatCount'
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayTestSupport.cs(139,32): error CS0246: The type or namespace name 'PotAward' could not be found ...
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\MultiwayObservationTests.cs(65,45): error CS1061: 'PlayerObservation' does not contain a definition for 'SeatCount' ...
```

(Full log: `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/scratch/round1-red-build.txt`.)

## 5. What was implemented

- `HoldemHandEngine.Start(seed, seats, handNumber = 1, startingStack = 1000, smallBlind = 5,
  bigBlind = 10, buttonSeat = 0)` validates `2 <= seats <= 6`, `buttonSeat` in range, positive
  stack/blinds, `smallBlind <= bigBlind` and `startingStack >= bigBlind`.
- Dealing: seat `s` gets `deck[s]` and `deck[seats + s]`; `DeckPosition = 2*seats`; the board is
  `deck[2*seats .. 2*seats+5)`. For two seats this is the recorded deal.
- Blinds and order: button posts SB, next seat BB; preflop first non-folded/non-all-in seat after
  BB; postflop first after the button.
- Round completion: every non-folded, non-all-in seat acted since the last aggression and every
  non-folded seat is at `CurrentBet` or all-in. A closed round resets commitments, advances the
  street and deals the next board cards (or runs out the board when at most one seat still has
  chips).
- Folds: the last unfolded seat wins immediately (no showdown). Showdown: layers from ascending
  distinct `TotalCommitted` levels, layer amount
  `(level - previous) * count(committed >= level)`, best `HandRank` among non-folded eligible
  seats, ties split with odd chips going one at a time clockwise from the button; folded seats
  contribute and are never eligible.
- `Replay` verifies street, seat, pot-after, board, every seat's hole cards, final stacks, winners,
  showdown and awarded pot, plus the pot layers for 3+ seats.

## 6. Mutation evidence (odd-chip rule)

The odd-chip distribution was temporarily changed from "first eligible seats clockwise from the
button" to "the button seat receives the remainder" (`awards[state.ButtonSeat] += remainder`). The
side-pot class (`MultiwayHoldemEngineTests`, 36 executed cases) was run with:

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll MultiwayHoldemEngineTests
```

and failed:

```text
FAIL MultiwayHoldemEngineTests.Apply_GivenThreeHundredRandomMultiwayHands_ExpectedConservationTerminationAndMatchingBruteForcePots: EqualException
Expected: 1003
Actual:   1002
FAIL MultiwayHoldemEngineTests.ResolveShowdown_GivenConsecutiveLayersWithSameWinners_ExpectedSeparatePotAwards: EqualException
Expected: [12, 13, 0]
Actual:   [13, 12, 0]
FAIL MultiwayHoldemEngineTests.ResolveShowdown_GivenSplitSidePotWithOddChip_ExpectedOddChipToFirstEligibleLeftOfButton: EqualException
Expected: [6, 15, 0, 14]
Actual:   [9, 13, 0, 13]
TOTAL 36 PASSED 33 FAILED 3 SKIPPED 0
```

Restored from a saved copy and hash-verified:

```text
restored_sha256=904f93377a12a9b80d5c302e55babb6195adee5a6a399d27703d75fa12f60093
matches_original=True
```

Then a hash-verified forced `--no-incremental` rebuild and a full re-run:

```text
engine_sha256=904f93377a12a9b80d5c302e55babb6195adee5a6a399d27703d75fa12f60093
matches_original=True
Build succeeded.
    0 Error(s)
TOTAL 241 PASSED 241 FAILED 0 SKIPPED 0
restored_run_exit=0
```

(Logs: `scratch/round1-mutation-run.txt`, `scratch/round1-restored-run.txt`.)

## 7. CLI oracle and replays (observed)

Baseline before the change and after the change are identical.

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe play --seed 42 --hands 20 --allow-weak-seed --data-dir <scratch>
```

```text
session: tholdem-06795fbeacfe528b
seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
tight-aggressive            20  -1883.00   -3511.94 .. -254.06      0.15          0.05 .. 0.36        0.10      0.20
random-legal                20   1883.00     254.06 .. 3511.94      0.85          0.64 .. 0.95        3.25      0.20
exit: 0
```

Old recorded v1 hand (manifest-anchored):

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe replay artifacts/tholdem/sessions/probe-777/hand-00001.json
```

```text
hand: 1
board: -
seat 0: 4c Js
seat 1: Tc Kd
actions:
  preflop seat=0 kind=Fold amount=0 potAfter=0
showdown: false
winners: 1
pot awarded: 15
final stacks: 995 / 1005
replayed stacks: 995 / 1005  pot: 0
exit: 0
```

New four-seat v2 hand (generated deterministically by
`.tmp/tholdem-verify/MultiwayHandProbe`, canonical JSON hash
`b3bc72aa08029fd8260e88fc898c2f6c882d5d23d0f32dcdb2598a00b11a24a1`):

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe replay <scratch>\multiway-4seat-hand.json
```

```text
hand: 1
board: 8d 4d 8c Qh 6d
seat 0: 9c 7c
seat 1: 6h 4s
actions:
  preflop seat=3 kind=Raise amount=20 potAfter=35
  preflop seat=0 kind=AllIn amount=0 potAfter=1035
  preflop seat=1 kind=Call amount=0 potAfter=2030
  preflop seat=2 kind=Call amount=0 potAfter=3020
  preflop seat=3 kind=AllIn amount=0 potAfter=0
showdown: true
winners: 1
pot awarded: 4000
final stacks: 0 / 4000 / 0 / 0
replayed stacks: 0 / 4000 / 0 / 0  pot: 0
integrity: unanchored
exit: 0
```

## 8. Verification (observed output)

All commands were run from the repository root. `dotnet test` was **not** run.

### 8.1 Test build

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:18.11
build_exit=0
```

### 8.2 Full Tholdem suite (supplementary runner)

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
TOTAL 241 PASSED 241 FAILED 0 SKIPPED 0
runner_exit=0
```

**Exact test count: 241** (194 pre-existing + 47 new executed cases). The 2-seat v1 canonical-hash
pin (`c953464b065b991bcda5c550c3ff5167b7fef6ccbd417636e822cf1c58a842c5` for the seed-42
check-down) is green, as is the sweep over all **30** recorded `artifacts/tholdem/sessions`
`hand-*.json` files: each parses, its file SHA-256 equals its record hash, its canonical text
round-trips, and it replays to the recorded stacks/winners/showdown/award.

### 8.3 Solution build

```powershell
dotnet build Wip.slnx --configuration Release
```

```text
Build succeeded.
    127 Warning(s)
    0 Error(s)
Time Elapsed 00:01:25.67
solution_build_exit=0
```

The warnings are the pre-existing solution warnings (the same build reported 126–127 of them in the
heads-up baseline); no Tholdem project produced a warning.

### 8.4 Architecture runner and project count

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
```

```text
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
architecture_exit=0
```

`tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs` asserts `Assert.Equal(48, graph.Count)`
inside that green run; the project count is unchanged.

### 8.5 Solution coverage

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
```

```text
solution=Wip.slnx
wip_project_count=48
solution_wip_project_count=48
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
coverage_exit=0
```

### 8.6 HarnessSync

```powershell
dotnet run --project tools/HarnessSync -- --check
```

```text
harnesssync_exit=0
```

(no output; all text files UTF-8 without BOM)

## 9. Compatibility evidence

- **Two seats:** `HoldemHandEngine.Start(seed, 2, …)` is compared against
  `HeadsUpHandEngine.Start(seed, …)` for 100 seeds and after each of 20 scripted raise-war actions,
  including board positions, stacks, commitments, total commitments, button/blind seats, acting
  seat, pot/current bet/last raise, hidden deck/deck position/acted mask and the action log.
- **v1 history:** the v1 writer branch is exercised by the pinned canonical hash above, by the
  20-hand CLI manifest (`87f90413…`) and by the 30-file replay sweep; no v1 byte changed.
- **Observation:** the exact pre-change canonical JSON strings captured in this session are embedded
  as literals in `MultiwayObservationTests` and matched for start/seat 0, start/seat 1 and the flop;
  the JSON contains no `seats`, `seatCount` or `seat` member for two seats. The
  `DeriveAgentSeed` fixtures and boundary tests that hash the observation are green in the 241 run.

## 10. Limits / what was not done

- **`dotnet test` was not run** (VSTest testhost aborts with `Win32Exception (5)` in this session,
  carried limitation); local evidence is the supplementary runner over the compiled assembly. CI was
  not triggered or observed, so no CI green is claimed.
- **Hand-crafted side pots use the internal `ResolveShowdown`.** A single `Start` deals equal starting
  stacks to every seat, so a legally played hand cannot reach the goal's illustrative
  `1000/600/300` all-in totals: every all-in seat would have `TotalCommitted == startingStack` and
  any excess would be uncalled. The contract's hand-crafted contribution levels are therefore built
  with the test-visible internal init members and resolved through the same product resolver used by
  `RunOutBoard`. The public-API property test covers equal-stack hands with folded dead money (which
  does produce multiple layers) and matches the independent brute-force calculator.
- **`PlayerObservation` legacy scalar legality fields are the acting seat's projection** for every
  seat count (necessary for two-seat byte identity); a non-acting seat reads its own public
  stack/commitment from `Seats`. This is documented in `engine-rules.md`.
- **Two history tests read the local, git-ignored `artifacts/tholdem/sessions` tree** (30 recorded
  v1 hand files in this workspace): the single-file hash/replay pin and the all-files sweep. They are
  green here and are the required "recorded v1 hand" evidence, but a clean clone that has never run
  the CLI/simulator will not contain that tree; the canonical v1 shape is additionally pinned
  independently by the seed-42 hash test, which needs no artifacts.
- The three-or-more-seat simulator, agents, viewer and CLI `--seats` option are explicitly outside
  this round's scope and were not implemented or exercised. Only the existing 2-seat CLI was used
  for the oracle and replay evidence above.

## 11. Test count bookkeeping

`docs/plan/test-waivers.json` now records `"Wip.Tholdem.Tests": 241` (was `194`). The other project
totals are untouched.
