# Round 12 — refalsify C (claims C3 and C6)

Current tree: branch `WIP`, Release build `dotnet build Wip.slnx --configuration Release` (exit 0; 6 warnings, 0 errors). Every result below is from this tree; prior-round conclusions were not used as evidence.

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| C3 | Falsified | The static clauses hold: `SpectatorSnapshot` has no textual occurrence under `WIP/Wip.Tholdem.Agents`; that project references only `Wip.Abstractions`, `Wip.Runtime` and `Wip.Tholdem`; `AgentTurnContext` is exactly `(int HandNumber, int Seat, int ActionIndex)`; `HandHistoryTool` returns the seed-free `SessionHandView`. The inversion clause fails. A fresh session made with `play --seed 42 --hands 6 --agent0 random-legal --agent1 random-legal --data-dir .tmp/refalsify-c/data/play42` recorded hand 3 as board `Ah 7c 4c 7s Kd`, seat 0 `2h Ts`, seat 1 `8h 2d`, hand seed `12993712030378080011`. At seat 1's flop turn the agent receives only own hole cards `8h 2d`, board `Ah 7c 4c`, hand number 3 and public turn coordinates. A pure enumeration over candidate session seeds 0..1,000,000 (handSeed = `SeedDerivation.Derive(candidate,3,0,0,0)`, deck = `SeededDeck.Create(handSeed)`, match `deck[1]=8h`, `deck[3]=2d`, `deck[4..6]=Ah 7c 4c`) returned exactly candidate `42` in 18,354 ms. Rebuilding from 42 gave handSeed `12993712030378080011` and deck[0..8] `2h 8h Ts 2d Ah 7c 4c 7s Kd`, exactly the recorded file. The documented `play --seed 777 --hands 2` session was also recovered from the two turns' own cards (`4c Js`, `6s 4s`) over 0..1,000,000: candidate `777` in 18,996 ms; derived deals `4c Tc Js Kd` and `6s 7d 4s Ts` match both recorded hands. No `session.json` seed and no hand-file seed was read by the enumeration. | Took everything an agent receives at seat 1's flop turn of a recorded hand (hand number 3, seat 1, own cards `8h 2d`, public board `Ah 7c 4c`) and enumerated small session seeds; recovered 42 and rebuilt the entire deck, including the other seat's hole cards and the full board. Repeated for the documented seed-777 session from two turns' own hole cards; recovered 777 and rebuilt both deals. | The observation is a deterministic function of the session seed. With a user-chosen small seed (the documented examples are 42 and 777), the agent-visible cards are enough to enumerate the session seed and reproduce the recorded hole cards and board. Recovering 42/777 demonstrates that the claim's "NO value ... can be inverted" clause is false; the direct-field and project-reference checks pass but do not save the claim. |
| C6 | Falsified | The test, waiver and example checks pass. `dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` printed `TOTAL 182 PASSED 182 FAILED 0 SKIPPED 0` (exit 0); there are 182 `[Fact]` methods and no `Skip=` / `Skip` test attribute in the project; `docs/plan/test-waivers.json` has no `Wip.Tholdem.Tests` entry in `failures` or `knownAborted` (only `expectedTotals` 182); `tools/run-wip-tests.ps1` includes the project without a filter. Documented outputs match: `play --seed 777 --hands 2` gives session `tholdem-f275b4ee69be0557`, manifest `ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee`, hand hashes `571049a9...` and `92aa3db0...`, and hand-00001 exactly matches the doc JSON; `train --iterations 3 --seed 7` prints the documented three iteration lines and final `tholdem-strategy-v1-f100e793`, and the ledger, training report, strategy v1, and champion pointer match the doc JSON; `evaluate --seeds 4` writes the documented `2/2/4` JSON and leaves the champion unchanged; `promote` without and with `--yes` exits 3 with the documented messages; `replay` of hand 3 and hand 1 exits 0 with matching stacks and anchors; help and exit codes 2/3/4/5 match; the 5099 viewer serves `/` (302), `/api/status`, `/viewer/`, and `/viewer/table`; a fresh current-build viewer on 5101 gives live 777 hand hashes identical to the CLI session, identical hashes at delay 0 and 40, working pause/resume, 400 `unknown-agent`, and 400 `invalid-hands` for hands 501. The docs claim fails: `docs/tholdem/viewer.md` lines 100-112 state "nothing an agent receives can be inverted to the session seed or to the deck", and `docs/tholdem/engine-rules.md` line 166-167 scopes its version to the context ("from it") and line 174 speaks of the agent seed, so the failing broad sentence is `viewer.md` line 104. The C3 counterexample shows a turn's `PlayerObservation` alone (own hole cards plus flop) recovers seed 42 and the deck. In addition, the suite's designated cheat-path test `AgentInputs_GivenRecordedSeed42Hand1Enumeration_ExpectedNoCandidateSeedMatchAndDifferentDeck` enumerates only old `LegacyTurnDigest` values, never candidate decks, so it stays green while the deck-matching enumeration succeeds; that guard is weaker than the property it is named for. | Ran the documented commands and a fresh current-build viewer; all documented examples matched. Then applied the C3 enumeration to the docs' stated guarantee and recovered seed 42 from the agent-received flop observation, contradicting "nothing an agent receives can be inverted". | C6 is a conjunction. The test/waiver/example conjuncts hold, but the docs conjunct does not: `docs/tholdem/viewer.md` makes a factual claim that the current behaviour contradicts, and the only test presented as the cheat-path guard does not exercise the actual deck-matching attack. Therefore C6 is not fully true. |

## C3 details

### Session recorded with the documented command

```text
command: WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe play --seed 777 --hands 2 --data-dir .tmp\refalsify-c\data\play777
observed:
  session: tholdem-f275b4ee69be0557
  seed: 777  hands: 2  manifest sha256: ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee
  tight-aggressive             2    -75.00     -124.00 .. -26.00      0.00          0.00 .. 0.66        0.00      0.00
  random-legal                 2     75.00       26.00 .. 124.00      1.00          0.34 .. 1.00        1.00      0.00
  exit=0
```

The recorded manifest hand hashes are `571049a9883fcc96d5ea0b85771030d63f66fe0169306d6a16f6bb39b7a8019b` and `92aa3db0e586fd3b9b31bb1d45777d507aa8f931b7b912aca1c14964ace547f3`; both match `docs/tholdem/strategy-artifact-schema.md`. Hand 1 is the exact JSON shown in that document (`4c Js` vs `Tc Kd`, fold, pot 15, final stacks 995/1005).

### The inversion

`play --seed 42 --hands 6 --agent0 random-legal --agent1 random-legal` (documented seed/agents, fresh data directory) recorded this hand 3:

```json
{"handNumber":3,"seed":12993712030378080011,"smallBlind":5,"bigBlind":10,"startingStack":1000,"buttonSeat":0,"holeCardsSeat0":["2h","Ts"],"holeCardsSeat1":["8h","2d"],"board":["Ah","7c","4c","7s","Kd"],"actions":[{"street":0,"seat":0,"kind":"Call","amount":0,"potAfter":20},{"street":0,"seat":1,"kind":"Raise","amount":387,"potAfter":397},{"street":0,"seat":0,"kind":"Call","amount":0,"potAfter":774},{"street":1,"seat":1,"kind":"AllIn","amount":0,"potAfter":1387},{"street":1,"seat":0,"kind":"Call","amount":0,"potAfter":0}],"showdown":true,"winners":[0],"potAwarded":2000,"finalStacks":[2000,0]}
```

At seat 1's flop turn the engine's `PlayerObservation` contains hand number 3, acting seat 1, board `Ah 7c 4c`, own hole cards `8h 2d`, and public stack/pot/legal-action values. The enumeration used only hand number 3, seat 1, those two own hole cards and the three flop cards:

```text
candidate session seeds 0..1,000,000; recovered: 42
elapsed_ms=18354
seed=42 handSeed=12993712030378080011 deck0..8=2h 8h Ts 2d Ah 7c 4c 7s Kd
  predicted seat0=2h,Ts seat1=8h,2d board=Ah,7c,4c,7s,Kd
```

The predicted deal equals `hand-00003.json` exactly. The same approach on the documented seed-777 session (using only the two turns' own cards, hand 1 `4c Js` and hand 2 `6s 4s`) recovered 777 in 18,996 ms and predicted `4c Tc Js Kd` for hand 1 and `6s 7d 4s Ts` for hand 2, again matching the recorded files.

## C6 details

### Tests and waivers

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
...
TOTAL 182 PASSED 182 FAILED 0 SKIPPED 0
exit=0
```

`WIP/Wip.Tholdem.Tests` contains 182 `[Fact]` methods, no `[Theory]`/`InlineData`, no `Skip=` attribute, no `xunit.runner.json`/runsettings filter. `docs/plan/test-waivers.json` has only `expectedTotals["Wip.Tholdem.Tests"] = 182`; neither `failures` nor `knownAborted` names a Tholdem project. `tools/run-wip-tests.ps1` appends `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj` to the project list.

### Documented commands and observed output

```text
train --iterations 3 --seed 7 (fresh data dir):
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-b155b98f promoted=false verified=true outcome=rejected meanBbPer100=0.00 lcb=0.00 seeds=24
iteration=2 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-e1ba56f7 promoted=false verified=true outcome=rejected meanBbPer100=1.67 lcb=-0.76 seeds=24
iteration=3 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-ea96d95d promoted=false verified=true outcome=rejected meanBbPer100=-1.25 lcb=-3.40 seeds=24
final champion: tholdem-strategy-v1-f100e793
exit=0
```

This matches `docs/tholdem/training-promotion-runbook.md` line for line. The resulting `ledger.json`, `evaluations/tholdem-strategy-v2-b155b98f-vs-tholdem-strategy-v1-f100e793.json`, `strategies/tholdem-strategy-v1-f100e793.json`, and `active-champion.json` match the JSON examples in `docs/tholdem/strategy-artifact-schema.md` exactly (including `winRate:0.5`, `challengerWins:24`, `incumbentWins:24`, `ties:0`, and `{"strategyId":"tholdem-strategy-v1-f100e793","iteration":0}`). `evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 4` wrote the documented CLI document with `challengerWins:2`, `incumbentWins:2`, `ties:4` and did not change the champion. `promote` without `--yes` printed `promotion requires explicit confirmation; re-run with --yes` and `promotion refused: lcb-below-margin`, exit 3; with `--yes` it printed `promotion refused: lcb-below-margin` / `verifier: rejection-justified`, exit 3, champion unchanged. `replay` of hand 3 and hand 1 exited 0 and printed the recorded board/actions/stacks; hand 3's file hash equals `session.json` `handHashes[2]` `0cce40e8a065ba5a6957fd8b51314fd1f00c94e7813d05e6b373886172a5aeca`. Help, `play`, `train`, `evaluate`, `retention`, and `--version` outputs and exit codes 0/2/3/4/5 match the README/runbook.

The viewer at 5099 served `/` -> 302 `Location: /viewer/index.html`, `/viewer/` 200, `/api/status` `{"mode":"spectator",...,"offline":true}`, `/api/sessions` with `skipped:0`, and `/viewer/table?...` 200 `text/html`. Because the 5099 process started at 18:55, before the 23:31 Release build, its live-match code is stale; a copy of the current build run on 5101 under `.tmp/refalsify-c/viewer-current` produced live 777 hand hashes identical to the CLI session `571049a9...`/`92aa3db0...`, identical hashes at delay 0 and delay 40, working pause/resume, 400 `unknown-agent` for a missing agent, and 400 `invalid-hands` for 501 hands. The 5099 static/API claims match the docs; the live claims were verified against the current build rather than the stale process.

### Why C6 still fails

`docs/tholdem/viewer.md` line 104 asserts, as a fact about the current agent surface, that "nothing an agent receives can be inverted to the session seed or to the deck". The C3 enumeration recovered session seed 42 from one turn's `PlayerObservation` and rebuilt the deck, so that sentence does not match current behaviour. `docs/tholdem/engine-rules.md` line 166-167 correctly scopes its version to the context ("from it"), and line 174 is about the `DeriveAgentSeed` value; the broad failing statement is the `viewer.md` sentence. Read as context-only, that sentence would be true; as written, "nothing an agent receives" is unrestricted, and the document presents it as an agent-surface guarantee. C3 remains false under either reading because the observation is the agent's input. The suite's only test named for this cheat path does not attempt the deck-matching attack: it computes `LegacyTurnDigest(candidate)` for candidate 0..100000 and checks only whether that numeric value occurs among agent-visible numeric tokens; it never derives a candidate deck or compares cards. The test therefore stays green while the actual inversion succeeds. This is a doc and guard-integrity failure even though the count/skip/waiver and example checks pass.