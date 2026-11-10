# Round 12 adversarial refalsification - Falsifier B

- Repository: `C:\Users\ricar\dev\Modus`, branch `WIP`, current working tree.
- Scope: C3 (hidden information) and C6 (test and doc integrity).
- Method: current-tree source reading; a fresh `dotnet build Wip.slnx --configuration Release`; the
  documented supplementary runner over the rebuilt `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll`;
  the Release CLI; the running viewer on `http://127.0.0.1:5099`; and an in-memory C# enumerator
  compiled with `Add-Type` (workspace TEMP under `.tmp/round12-falsify-B/temp`, never `%TEMP%`) that
  calls the same public derivation formulas as the product.
- Not used: `dotnet test` (documented local VSTest `Win32Exception (5)` limitation); `pwsh` (Windows
  PowerShell 5.1 was used).
- Not edited: no product, test, doc or configuration file. The only file written by this review is
  this report; CLI scratch output lives under `.tmp/round12-falsify-B/` (git-ignored).

## Verdict table

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| C3 | Falsified | On the agent surface the sub-claims hold: `AgentTurnContext` is `(int HandNumber, int Seat, int ActionIndex)` (`WIP/Wip.Tholdem.Agents/AgentTurnContext.cs:10`); `PlayerObservation` has no seed/deck/opponent-card member (`WIP/Wip.Tholdem/History/PlayerObservation.cs:22-54,90-140`); the hand-history view has no such member (`WIP/Wip.Tholdem.Agents/Runtime/SessionHandView.cs:8-16`); no file under `WIP/Wip.Tholdem.Agents` (source, bin or obj) contains `SpectatorSnapshot` or `Wip.Tholdem.Web`, the `.csproj` references only Abstractions/Runtime/Tholdem, and `Wip.Tholdem.Agents.deps.json` has no Web assembly. But the third clause fails: `playerObservation.ForSeat` copies `state.Board` and the seat's own cards verbatim (`PlayerObservation.cs:74-75`), and a recorded hand's turn therefore exposes enough cards to test candidate session seeds. In `hand-00018` (recorded by `play --seed 42 --hands 20`: own `Ac 6d`, board `5s 2h 3s 7h As`, river action seat 0) enumeration of candidate session seeds `0..100000` with the public `SeedDerivation.Derive(sessionSeed, hand, 0, 0, 0)` (`SeedDerivation.cs:21-35`) plus `SeededDeck.Create` and checking deck positions `0,2,4..8` returned exactly one hit: `42 :: 1411498873484073348 :: Ac 2s 6d Js 5s 2h 3s 7h As` in 665 ms. `hand-00018.json` records `"seed":1411498873484073348`, `holeCardsSeat0 ["Ac","6d"]`, `holeCardsSeat1 ["2s","Js"]`, board `["5s","2h","3s","7h","As"]`; the recovered session seed 42 derives exactly that hand seed and deck, whose deck[1]/deck[3] are the other seat's recorded hole cards. | Built the capture of everything an `IPokerAgent` receives at the river turn of `hand-00018`: observation hand number 18, own cards `Ac 6d`, board `5s 2h 3s 7h As`, plus public state, and turn context `(18, 0, 3)`. Enumerated candidate session seeds with the product's own public derivation and shuffle, compared only against those seven observed cards (the recorded file's `seed` field was not used as input), then rebuilt the deck from the recovered seed and compared it with the recorded hand file. Result: one hit (42) and an exact match on the other seat's cards and the board. Corroboration: the flop-only observation (five cards) also returned only 42 in 873 ms; and for the documented `play --seed 777 --hands 2` session, only the own cards an agent sees across the two hands (seat 0 `4c Js`, `6s 4s`) returned only 777 in 607 ms (seat 1's four cards likewise returned only 777 in 596 ms), whose derived decks reproduce the recorded `Tc Kd` and `7d Ts`. | The unqualified third clause is false for the documented/default low-entropy seeds this repo itself uses (42 is the viewer form default at `WIP/Wip.Tholdem.Web/wwwroot/viewer/index.html:31`; 7 is the runbook seed; 777 is the session example). Removing the `TurnSeed` value did not remove the public deck derivation: `SeedDerivation.Derive` is public, `SeededDeck.Create` is public, and both assemblies are already referenced by the agent project, so an adversarial agent can enumerate candidate session seeds and validate them against its own observation without reimplementing the shuffle. The round-12 boundary test only replays the deleted one-way-digest attack (`AgentInformationBoundaryTests.cs:199-275`) and never attempts the deck enumeration, so green tests do not establish the property. |
| C6 | Falsified | Concrete examples reproduce exactly: `play --seed 777 --hands 2` prints `session: tholdem-f275b4ee69be0557`, `manifest sha256: ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee`; the written `session.json` and `hand-00001.json` are byte-for-byte the examples at `docs/tholdem/strategy-artifact-schema.md:125` and `:135`. `train --iterations 1 --seed 7` prints exactly `iteration=1 ... meanBbPer100=0.00 lcb=0.00 seeds=24` and the version-1 artifact, ledger row, training report and champion pointer are byte-for-byte the examples at `:31`, `:61`, `:84`, `:105`. `train --iterations 3 --seed 7` prints the documented iteration 2 and 3 lines (`1.67/-0.76`, `-1.25/-3.40`) and final champion `tholdem-strategy-v1-f100e793`. `evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 4` writes exactly the CLI evaluation document at `:92` and prints the documented decision/verifier lines; `--seeds 24` prints `decision: lcb-below-margin` / `verifier: verified (rejection-justified)`. `promote` without and with `--yes` prints the documented refusal lines, exits 3 and leaves the pointer at `{"strategyId":"tholdem-strategy-v1-f100e793","iteration":0}`. `replay` of an anchored hand exits 0; a copy without a sibling manifest prints `integrity: unanchored` and exits 0; a copy with one appended byte exits 5 with the hash-mismatch message; a missing path exits 4. Viewer: `GET /` is 302 with `Location=/viewer/index.html`; `GET /api/status` is exactly `{"mode":"spectator","dataDirectory":"...","sessions":2,"strategies":4,"liveMatches":0,"offline":true}`; `/api/sessions`, `/api/strategies`, `/api/ledger`, `/api/champion` answer; `POST /api/live` with an unknown agent is 400 `{"error":"unknown-agent",...}`, a valid body is 201 and the snapshot carries both seats' hole cards. Test/waiver checks: 50 test source files contain 182 `[Fact]` attributes, 0 `[Theory]`, 0 parameterized `[Fact(...)]`, 0 `Skip` tokens, 0 `Assert.Skip`; the rebuilt suite is `TOTAL 182 PASSED 182 FAILED 0 SKIPPED 0` (exit 0); `docs/plan/test-waivers.json` adds only `expectedTotals["Wip.Tholdem.Tests"] = 182` (single-line git diff) and has no Tholdem `failures`/`knownAborted` entry. Three factual claims do not match: (1) `docs/tholdem/training-promotion-runbook.md:23` says "`StrategyArtifact.Create` persists the challenger", but `StrategyArtifact.Create` (`WIP/Wip.Tholdem.Agents/Learning/StrategyArtifact.cs:28-37`) is a pure factory with no I/O; the persistence is `TrainingLoop.cs:137` `WriteUtf8(strategyDirectory, challenger.Id + ".json", ...)`, executed after the evaluation, so both the operation and its place in the numbered sequence are wrong. (2) `docs/tholdem/viewer.md:104` says "so nothing an agent receives can be inverted to the session seed or to the deck"; the C3 enumeration refutes that (the turn observation recovers seed 42 and the recorded deal). (3) `docs/tholdem/strategy-artifact-schema.md:117-120` says `MatchSimulator.Run` with a history directory writes `<data>/sessions/<sessionId>/session.json` and `hand-NNNNN.json`, but `MatchSimulator.Run` writes both into `SimulationOptions.HistoryDirectory` directly (`MatchSimulator.cs:39-40,131-135,155`; `HistoryIntegrityTests.cs:34` reads `<directory>/session.json`); only `PlayCommand` stages `sessions/.inprogress` and moves it to `sessions/<sessionId>` (`PlayCommand.cs:26-48`). | Ran at least eight documented commands and compared observed output with the docs: `--help`/`--version`, `play --seed 777 --hands 2`, `train --iterations 1 --seed 7`, `train --iterations 3 --seed 7`, `evaluate --seeds 4`, `evaluate --seeds 24`, `promote` with and without `--yes`, `replay` (anchored, unanchored, byte-appended, missing), a usage error and the removed `--min-seeds` option, plus the viewer endpoints above. Separately, traced each numbered runbook step and the two schema/session statements to the implementing files. | The compound C6 claim fails on its third clause: the concrete JSON examples and command outputs match, and the skip/waiver bookkeeping is clean as far as observable, but at least one factual claim (`StrategyArtifact.Create` persists the challenger) is simply false, a second (`nothing an agent receives can be inverted`) is refuted by the C3 counterexample, and the MatchSimulator session path statement names a directory layout the simulator does not produce. `docs/tholdem/*.md` therefore does not match current behaviour. |

## C3 - how the reconstruction was done

### What an agent receives (current tree)

- `WIP/Wip.Tholdem.Agents/IPokerAgent.cs:13-20` is the whole agent entry point:
  `ActAsync(PlayerObservation observation, AgentTurnContext context, CancellationToken)`.
- `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs:10`:
  `public readonly record struct AgentTurnContext(int HandNumber, int Seat, int ActionIndex);`
  There is no seed, digest or string in the record.
- `WIP/Wip.Tholdem/History/PlayerObservation.cs:22-54` exposes hand number, street, button seat,
  acting seat, board, own hole cards only, stacks, pot, commitments, amount to call, min/max
  raise-to, legal action kinds, completion and winners; `ToCanonicalJson` (`:90-140`) lists exactly
  those fields. `PlayerObservation.ForSeat` copies `state.Board` and the seat's own cards at
  `:74-75`.
- Runtime tool results are also projected: `SessionHandView` (`Runtime/SessionHandView.cs:8-16`)
  has own cards only and no seed/deck; `SessionHandStore.ToView` replays a record and projects it
  through `PlayerObservation.ForSeat` (`Runtime/SessionHandStore.cs:138-160`); `HandHistoryTool`
  fixes the seat at construction and bounds the limit; `OpponentStatsTool` returns aggregate
  statistics (`SeatStatistics`); `EquityTool`/`PotOddsTool` compute from model-supplied public
  inputs only.
- Spectator sub-claim: a byte scan of every file under `WIP/Wip.Tholdem.Agents` (including `bin`
  and `obj`) for `SpectatorSnapshot` or `Wip.Tholdem.Web` found zero matches; the only hidden-name
  strings in the tree are `HoleCardsSeat0`/`HoleCardsSeat1` inside the copied
  `Wip.Tholdem.Agents/bin/.../Wip.Tholdem.dll` (the domain assembly's `internal` `TableState`
  members, not a public agent surface). The project references are only
  `Wip.Abstractions`, `Wip.Runtime` and `Wip.Tholdem`, and `Wip.Tholdem.Agents.deps.json` lists no
  Web assembly. This sub-claim holds.

### The turn used

`play --seed 42 --hands 20 --data-dir .tmp/round12-falsify-B/play42` recorded session
`tholdem-06795fbeacfe528b`. `hand-00018.json` is:

```json
{"handNumber":18,"seed":1411498873484073348,"smallBlind":5,"bigBlind":10,"startingStack":1000,"buttonSeat":1,"holeCardsSeat0":["Ac","6d"],"holeCardsSeat1":["2s","Js"],"board":["5s","2h","3s","7h","As"],"actions":[{"street":0,"seat":1,"kind":"Raise","amount":37,"potAfter":47},{"street":0,"seat":0,"kind":"Call","amount":0,"potAfter":74},{"street":1,"seat":0,"kind":"Check","amount":0,"potAfter":74},{"street":1,"seat":1,"kind":"Check","amount":0,"potAfter":74},{"street":2,"seat":0,"kind":"Check","amount":0,"potAfter":74},{"street":2,"seat":1,"kind":"Check","amount":0,"potAfter":74},{"street":3,"seat":0,"kind":"Check","amount":0,"potAfter":74},{"street":3,"seat":1,"kind":"Check","amount":0,"potAfter":0}],"showdown":true,"winners":[1],"potAwarded":74,"finalStacks":[963,1037]}
```

At the river turn (seat 0, action index 3) the observation the agent receives has
`HandNumber = 18`, `OwnHoleCards = [Ac, 6d]` and `Board = [5s, 2h, 3s, 7h, As]`: the first two are
`state.HoleCardsSeat0` and the last five are `state.Board`, copied verbatim by
`PlayerObservation.ForSeat` (`PlayerObservation.cs:74-75`). The context is
`AgentTurnContext(18, 0, 3)`. Seven of the nine first deck cards are therefore agent-visible; the
two it cannot see are `deck[1]` and `deck[3]` (the opponent's cards).

### The enumeration

The engine derivation and deck shuffle are public and unchanged:

- `SeedDerivation.Derive(ulong sessionSeed, int handNumber, int seat, int actionIndex, ulong stream)`
  (`WIP/Wip.Tholdem.Agents/SeedDerivation.cs:21-35`) is the splitmix-style mix over
  `sessionSeed ^ constants(hand, seat, action) ^ stream`;
- `MatchSimulator.Run` (`WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:49`) derives hand `n`
  as `Derive(options.Seed, n, 0, 0, 0)` and `HeadsUpHandEngine.Start` builds the deck with
  `SeededDeck.Create(handSeed)` (Fisher-Yates over xorshift64*; `HeadsUpHandEngine.cs:69,98-100`);
- the deal is `deck[0],deck[2]` / `deck[1],deck[3]`, board `deck[4..8]`
  (`docs/tholdem/engine-rules.md:29-32`, and the code at `HeadsUpHandEngine.cs:98-100`).

The attacker does not need to reimplement any of that: `Wip.Tholdem.Agents` already references
`Wip.Tholdem`, so an agent can call `SeedDerivation.Derive` and `SeededDeck.Create` directly.
The enumerator used by this review is an independent C# implementation of the same two public
formulas. For every candidate `c` in `0..100000` it computes `h = Derive(c, 18, 0, 0, 0)`, shuffles,
and requires `deck[0] = Ac`, `deck[2] = 6d`, `deck[4..8] = 5s 2h 3s 7h As`. Observed output:

```text
sanity Derive(42,18,0,0,0)=1411498873484073348
sanity Derive(777,1,0,0,0)=9754104818518126180
deck(1411498873484073348)=Ac 2s 6d Js 5s 2h 3s 7h As
candidates 0..100000 hand 18 elapsed_ms=665
HITS:
42|1411498873484073348|Ac 2s 6d Js 5s 2h 3s 7h As
```

The single candidate is the session seed 42. Its derived deck is exactly the recorded deck:
`deck[1],deck[3] = 2s, Js` are the recorded opponent hole cards and `deck[4..8]` is the recorded
board. With the recovered session seed the agent can derive every hand of the session, so the
recorded hole cards and board of every hand are reproduced. The recorded hand file's `seed` field
happens to equal the derived hand seed `1411498873484073348`, but it was not used as an input to
the enumeration; it is quoted only as an after-the-fact check.

### Corroborating runs

```text
hand18 flop (5 observed cards) candidates 0..100000 -> [42]  elapsed_ms=873
documented seed-777 session, seat0 own cards in hands 1+2 -> [777]  elapsed_ms=607
documented seed-777 session, seat1 own cards in hands 1+2 -> [777]  elapsed_ms=596
deck for 777 hand2 = 6s 7d 4s Ts Tc Jd Kc Jc 4c
777 hand1 seed=9754104818518126180 deck=4c Tc Js Kd 2s 2h Jd Qs Ks
```

The last two lines reproduce `hand-00001.json` (`4c Js` vs `Tc Kd`) and `hand-00002.json`
(`6s 4s` vs `7d Ts`) of the documented `play --seed 777 --hands 2` session. A single flop
observation is already sufficient to uniquely identify the session seed.

### Why this falsifies the claim and not merely a test

The third clause is unqualified: *no* value available to an `IPokerAgent` at a turn can be inverted
to the session seed *or* reproduce the recorded hole cards and board. The observation is such a
value, the derivation is public, the documented/default seeds are tiny, and the reconstruction was
observed. The round-12 change removed the `TurnSeed` value; it did not change `Derive`, did not
key it with a secret, and did not make the agent observation insufficient to validate candidates.
The round-12 boundary test checks only the deleted digest formula against agent-visible numbers, so
the suite can stay green while this attack still succeeds. The first two sub-claims (no direct seed
field on the agent surface; spectator type unreachable from the agent project) do hold.

## C6 - commands run and the mismatches

### Documented commands observed (selection)

```text
$ Wip.Tholdem.Host.exe play --seed 777 --hands 2 --data-dir .tmp/round12-falsify-B/play777
session: tholdem-f275b4ee69be0557
seed: 777  hands: 2  manifest sha256: ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee
... (metrics table)
exit 0
session.json : {"schema":"tholdem.session.v1","sessionId":"tholdem-f275b4ee69be0557","seed":777,"hands":2,...,"handHashes":["571049a9883fcc96d5ea0b85771030d63f66fe0169306d6a16f6bb39b7a8019b","92aa3db0e586fd3b9b31bb1d45777d507aa8f931b7b912aca1c14964ace547f3"]}
hand-00001  : {"handNumber":1,"seed":9754104818518126180,...,"holeCardsSeat0":["4c","Js"],"holeCardsSeat1":["Tc","Kd"],"board":[],"actions":[{"street":0,"seat":0,"kind":"Fold","amount":0,"potAfter":0}],"showdown":false,"winners":[1],"potAwarded":15,"finalStacks":[995,1005]}
```

Both files match `docs/tholdem/strategy-artifact-schema.md:125` and `:135` exactly; the session id
formula also verifies (`SHA-256("777\n2\ntight-aggressive\nrandom-legal")[:16]` = `f275b4ee69be0557`).

```text
$ train --iterations 1 --seed 7 --data-dir .tmp/round12-falsify-B/train1
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-b155b98f promoted=false verified=true outcome=rejected meanBbPer100=0.00 lcb=0.00 seeds=24
final champion: tholdem-strategy-v1-f100e793
```

The version-1 artifact, `ledger.json`, the training evaluation report and `active-champion.json`
were byte-for-byte the examples at `strategy-artifact-schema.md:31`, `:61`, `:84`, `:105`
(`"winRate":0.5`, `"challengerWins":24,"incumbentWins":24,"ties":0`, pointer `iteration:0`).

```text
$ train --iterations 3 --seed 7 --data-dir .tmp/round12-falsify-B/train3
iteration=2 ... challenger=tholdem-strategy-v2-e1ba56f7 ... meanBbPer100=1.67 lcb=-0.76 seeds=24
iteration=3 ... challenger=tholdem-strategy-v2-ea96d95d ... meanBbPer100=-1.25 lcb=-3.40 seeds=24
final champion: tholdem-strategy-v1-f100e793
```

Matches `training-promotion-runbook.md:32-35`.

```text
$ evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 4 --data-dir .tmp/round12-falsify-B/train1
evaluation: ...\evaluations\cli-tholdem-strategy-v2-b155b98f-vs-tholdem-strategy-v1-f100e793.json
challenger: ... incumbent: ... seeds: 4  hands per match: 10
mean bb/100: 0.00  LCB: 0.00  margin: 5.00
decision: lcb-below-margin
verifier: verified (rejection-justified)
```

The written document is exactly `strategy-artifact-schema.md:92` (`"seeds":4,...,"challengerWins":2,"incumbentWins":2,"ties":4`).

```text
$ promote --challenger tholdem-strategy-v2-b155b98f --data-dir .tmp/round12-falsify-B/train1
promotion requires explicit confirmation; re-run with --yes
promotion refused: lcb-below-margin
verifier: rejection-justified
exit 3
$ promote --challenger tholdem-strategy-v2-b155b98f --yes --data-dir ...
promotion refused: lcb-below-margin
verifier: rejection-justified
exit 3
pointer unchanged: {"strategyId":"tholdem-strategy-v1-f100e793","iteration":0}
```

```text
$ replay ...\hand-00018.json            -> full table, exit 0
$ replay <copy without session.json>    -> ... integrity: unanchored, exit 0
$ replay <copy with one trailing space> -> "replay hash mismatch: file sha256 6436... does not equal record hash 5710...", exit 5
$ replay <missing path>                 -> "hand record '...' does not exist.", exit 4
$ play (no --seed)                      -> "option '--seed' is required.", exit 2
$ evaluate --min-seeds 1                -> "unknown option '--min-seeds'", exit 2
```

### Test and waiver integrity

- 50 test source files (excluding `bin`/`obj`) contain 182 `[Fact]` attributes, 0 `[Theory]`,
  0 parameterized `[Fact(...)]`, 0 `Skip` tokens, 0 `Assert.Skip`, 0 `#if` and 0 `Explicit`.
- Supplementary runner over the rebuilt Release test assembly:
  `TOTAL 182 PASSED 182 FAILED 0 SKIPPED 0`, exit 0.
- `docs/plan/test-waivers.json`: the only change in the working tree is the single expected-total
  line `"Wip.Tholdem.Tests": 182` (git diff shows exactly that addition); the only occurrence of
  the word `Tholdem` in the file is that line, so there is no Tholdem `failures` or `knownAborted`
  waiver.
- The Tholdem test tree is untracked (`git status` shows `?? WIP/Wip.Tholdem.Tests/`), so a
  historical deletion/weakening diff is not available; the observable skip/waiver/assertion checks
  are clean and are reported as such, not as proof about history.

### Concrete mismatch 1 - the runbook names a pure factory as the persister

`docs/tholdem/training-promotion-runbook.md:23`:

```text
4. `StrategyArtifact.Create` persists the challenger;
```

`WIP/Wip.Tholdem.Agents/Learning/StrategyArtifact.cs:28-37`: `Create` clamps the genome, computes
the version, content hash and id, and returns `new StrategyArtifact(...)`. It performs no file,
stream or other I/O. The actual persistence is
`WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:137`:
`WriteUtf8(strategyDirectory, challenger.Id + ".json", challenger.ToCanonicalJson());` - and that
line runs after the evaluation and decision, not before them. The numbered step therefore names
the wrong operation and places it at the wrong point of the sequence.

### Concrete mismatch 2 - the viewer doc's "nothing can be inverted" claim

`docs/tholdem/viewer.md:103-104`:

```text
The context has no seed of any width, no digest and no other secret-derived value, so nothing an
agent receives can be inverted to the session seed or to the deck.
```

The C3 counterexample refutes the conclusion: a turn observation (seven cards) recovers session
seed 42 and the recorded deal. `docs/tholdem/engine-rules.md:163-167` states the narrower, true
version ("nothing an agent receives *from it* [the context] can be inverted"), so the two docs are
also inconsistent with each other.

### Concrete mismatch 3 - the session path attributed to MatchSimulator

`docs/tholdem/strategy-artifact-schema.md:117-120`:

```text
`play` (and `MatchSimulator.Run` with a history directory) writes
`<data>/sessions/<sessionId>/session.json` and `hand-NNNNN.json` (`WIP/Wip.Tholdem.Agents/
Simulation/MatchSimulator.cs`).
```

`MatchSimulator.Run` writes `hand-NNNNN.json` and `session.json` directly into
`options.HistoryDirectory` (`WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:39-40,131-135,155`);
it never creates a `sessions/<sessionId>` directory. The suite itself relies on the direct layout
(`HistoryIntegrityTests.cs:34` reads `Path.Combine(directory, "session.json")`). Only `play` stages
that directory as `sessions/.inprogress` and moves it to `sessions/<sessionId>`
(`WIP/Wip.Tholdem.Host/Commands/PlayCommand.cs:26-48`). The parenthetical attribute is therefore
not what the referenced code does.

## Limits of this review

- `dotnet test` was not run (documented local VSTest `Win32Exception (5)` limitation); the
  supplementary runner over the same compiled assembly was used, as documented.
- CI was not observed (no `gh` CLI/token in this environment).
- The browser-facing JavaScript contracts were checked by reading `app.js`/`index.html` and by the
  server-rendered/HTTP surface; no headless-browser execution was performed in this round.
- The Tholdem tests are untracked in git, so no historical diff can establish whether a test was
  deleted or its assertions changed before this round; only current-tree evidence is reported.