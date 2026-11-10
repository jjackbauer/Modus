# Round 12 — falsifier A (C3, C6)

- **Repository**: `C:\Users\ricar\dev\Modus`, branch `WIP`, HEAD `f1a26bb`.
- **Date**: 2026-11-07.
- **Working tree under test**: `git status` shows the whole `WIP/Wip.Tholdem*` delivery and
  `docs/tholdem/` untracked. The tree below is the delivery being assessed.
- **Build used**: `WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe` and the Release
  DLLs beside it. The `Wip.Tholdem.dll` / `Wip.Tholdem.Agents.dll` in the host output are byte-identical
  (SHA-256) to the copies in each project's `bin\Release\net10.0`:
  `F340DC29F37AFDCAEB46EA284193B4933A13878007BFE40DB0A6B4EA54616ECF` and
  `390C1D691605F393813AD5CBC54D1A80B96148E9E8D831400621EA320BFF4724`; the attack therefore exercises
  the same code as the documented CLI.
- **Local limits honoured**: `dotnet test` was not used (VSTest is unusable here); the supplementary
  runner over the compiled test assembly was used; no `%TEMP%` scratch (all scratch under
  `.tmp/falsify12*`, which is git-ignored); the viewer at `http://127.0.0.1:5099` was queried, not
  restarted.

## Verdict summary

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| C3 | Falsified | The direct agent-facing members are clean: `AgentTurnContext` is exactly `(HandNumber, Seat, ActionIndex)`, `DeriveTurnSeed` is gone, `PlayerObservation` has no seed/deck/opponent fields, no file under `WIP/Wip.Tholdem.Agents` mentions `SpectatorSnapshot`/`TholdemWebHost`/`LiveMatch`, and the project graph is Agents -> {Abstractions, Runtime, Tholdem}. But the observation is a deterministic ciphertext of the deal: the session seed is recoverable by enumeration of the deck generator (section 3.2). | A Release probe replayed recorded hands with the product APIs, captured exactly the turn observation + context the seat receives, and enumerated session seeds 0..100000 against `SeededDeck.Create(SeedDerivation.Derive(candidate, hand, 0, 0, 0))`. It recovered session seed **1** from one flop turn, **42** from one turn, and **777** uniquely from two preflop turns of the documented `play --seed 777 --hands 2` session, then rebuilt the opponent hole cards and the recorded board from the recovered deck. | The third clause ("NO value available to an IPokerAgent at a turn can be inverted to the session seed or reproduce the recorded hole cards and board") is false. No seed value is needed: the hole cards and board that the API does expose are the oracle. The first two clauses (no direct exposure; spectator type unreachable from Agents) hold, but the claim is a conjunction, so it is falsified. |
| C6 | Falsified | The test/waiver sub-claims hold: supplementary runner `TOTAL 182 PASSED 182 FAILED 0 SKIPPED 0`; 182 `[Fact]`, 0 `[Theory]`, no `Skip=`/`#if` in test sources; `docs/plan/test-waivers.json` has `Wip.Tholdem.Tests: 182` and no Tholdem entry in `failures` or `knownAborted`. The documented commands reproduce, and all seven JSON examples in `strategy-artifact-schema.md` are byte-equal to the generated files (section 6.2). **However**, `docs/tholdem/viewer.md:104` says "nothing an agent receives can be inverted to the session seed or to the deck", and `docs/tholdem/engine-rules.md:174` says the observation "reveals nothing about the deck". The C3 counterexample demonstrates the opposite on the current build. | Ran `--version`, `--help`, `play --seed 777 --hands 2`, `train --iterations 1 --seed 7`, `train --iterations 3 --seed 7`, `evaluate --seeds 4`, `evaluate --margin -1000 --seeds 4`, both `promote` refusal paths, `replay` (anchored, unanchored and bad `--expected-hash`), `retention --status`, and the viewer HTTP endpoints; compared outputs and generated JSON with the docs; then applied the C3 enumeration to the documented seed-777 and seed-42 sessions. | The "every factual claim ... matches current behaviour" conjunct fails on the two quoted non-invertibility statements. The concrete examples and command outputs do match; the test-integrity and waiver sub-claims were not broken. |

## C3 — hidden information: what an agent receives and what can be rebuilt

### 3.1 The surface that did hold

Read from the tree, not assumed:

- `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs:10` — `public readonly record struct
  AgentTurnContext(int HandNumber, int Seat, int ActionIndex);`. Reflection over the compiled
  assembly returns exactly `ActionIndex:Int32, HandNumber:Int32, Seat:Int32` and no seed-valued
  member (also asserted by `AgentInformationBoundaryTests.AgentTurnContext_GivenReflection_...`).
- `WIP/Wip.Tholdem.Agents/SeedDerivation.cs` — only `Derive(ulong sessionSeed, int handNumber, int
  seat, int actionIndex, ulong stream)` and `DeriveAgentSeed(string canonicalObservation, ...)`.
  `DeriveTurnSeed` and its `tholdem-turn-seed` domain constant are absent (grep in the project: 0
  matches).
- `WIP/Wip.Tholdem/History/PlayerObservation.cs:22-54` — hand number, street, button seat, acting
  seat, board, **own** hole cards, both stacks, pot, both street commitments, call/min/max raise,
  legal action kinds, completion flag and winners. No seed, no deck, no undealt cards and no
  opponent hole cards. Its canonical JSON is produced at `ToCanonicalJson()`.
- `WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj` references only `Wip.Abstractions`,
  `Wip.Runtime` and `Wip.Tholdem`. No file under `WIP/Wip.Tholdem.Agents` contains
  `SpectatorSnapshot`, `TholdemWebHost` or `LiveMatch` (grep: 0 matches), and
  `TholdemWebViewerTests.AgentSources_GivenSpectatorBoundaryScan_ExpectedNoWebOrLiveIdentifiers`
  scans every `.cs` under that directory and passes in the 182-test run. The spectator projection is
  real (`WIP/Wip.Tholdem.Web/Live/SpectatorSnapshot.cs` shows both seats' cards) but is built inside
  the web host and never passed to an `IPokerAgent`.

So the direct-exposure part and the spectator-reachability part are true. The attack below does not
use either: it uses only `PlayerObservation` and the public deterministic deck generator.

### 3.2 The enumeration attack that breaks the inversion clause

Scratch probe: `.tmp/falsify12-attack` (Release, git-ignored). It references the same Release
`Wip.Tholdem.dll` / `Wip.Tholdem.Agents.dll` as the CLI (hashes above). For a recorded hand it steps
`HeadsUpHandEngine.Start(record.Seed, ...)` and `Apply(...)` exactly as `MatchSimulator` does,
captures the real `PlayerObservation` and `AgentTurnContext` for the chosen turn, and then, using
**only** the observation values (hand number, own hole cards, board) and the public functions
`SeedDerivation.Derive` and `SeededDeck.Create`, searches candidate session seeds.

The filter is the deal itself: for candidate `s`, `handSeed = Derive(s, hand, 0, 0, 0)`,
`deck = SeededDeck.Create(handSeed)`; the seat's own cards must equal `deck[0], deck[2]` (seat 0) or
`deck[1], deck[3]` (seat 1), and the observed board must equal `deck[4..]` in order. The recorded
hand's `seed` is used only to reproduce the live observation during the test; the search does not
read it. `SeedDerivation.Derive` and `SeededDeck` are public and live in the assemblies an
`IPokerAgent` already references, so an agent implementation can run exactly this search.

**Attack 1 — the documented `play --seed 777 --hands 2` session.** The command was run from the
Release CLI and produced `session: tholdem-f275b4ee69be0557`, manifest
`ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee`, exactly the documented values;
`session.json` and `hand-00001.json` are byte-equal to the docs (section 6.2). For the seat-0 agent:

```text
hand-00001.json: attack target turn[0]
  observation JSON : {"handNumber":1,...,"board":[],"ownHoleCards":["4c","Js"],...}
  recovered session seeds : 777, 1470, 4469, ... (42 candidates)
  contains actual session seed : True

hand-00002.json: attack target turn[1]
  observation JSON : {"handNumber":2,...,"board":[],"ownHoleCards":["6s","4s"],...}
  recovered session seeds : 144, 777, 3391, ... (35 candidates)
  contains actual session seed : True
```

The candidate sets from the two turns of the same agent intersect in exactly one value:

```text
hand1 candidates: 42
hand2 candidates: 35
intersection: 777
```

For that recovered seed, the rebuilt decks reproduce the recorded opponent cards:

```text
candidate 777: handSeed=9754104818518126180
  rebuilt other-seat hole cards: Tc Kd
  recorded other-seat hole cards: Tc Kd
  other-seat hole cards match  : True
  recorded board match         : True
  predicted undealt next card  : 2s

candidate 777: handSeed=3789501218103994239
  rebuilt other-seat hole cards: 7d Ts
  recorded other-seat hole cards: 7d Ts
  other-seat hole cards match  : True
  recorded board match         : True
  predicted undealt next card  : Tc
```

**Attack 2 — one turn is enough when a board exists.** With the same probe, on a `play --seed 42
--hands 5` session, hand 4, the seat-0 turn on the turn street receives
`{"handNumber":4,...,"board":["3s","Tc","4h","Ah"],"ownHoleCards":["Kd","Jh"],...}`. Enumerating
0..100000 finds exactly one candidate:

```text
candidate session seeds enumerated: 0..100000 (100001 values)
recovered session seeds        : 42
contains actual session seed    : True

candidate 42: handSeed=13635655920283988368
  rebuilt other-seat hole cards: Jd Ts
  recorded other-seat hole cards: Jd Ts
  other-seat hole cards match  : True
  rebuilt deck[4..8]           : 3s Tc 4h Ah 5h
  recorded board               : 3s Tc 4h Ah
  recorded board match         : True
  predicted undealt next card  : 5h
```

The agent has seen only the first four board cards, yet the recovery rebuilds the opponent's hole
cards and the next (undealt) river card. The same probe on a `play --seed 1 --hands 20
--agent0 random-legal --agent1 random-legal` session, hand 16, flop turn (board `Td Kd Kc`, own
`5d Qc`), recovered session seed `1` uniquely and rebuilt the full recorded board:

```text
recovered session seeds        : 1
candidate 1: handSeed=5505034750148504567
  rebuilt other-seat hole cards: 3c 2s
  recorded other-seat hole cards: 3c 2s
  other-seat hole cards match  : True
  rebuilt deck[4..8]           : Td Kd Kc 5c 8d
  recorded board               : Td Kd Kc 5c 8d
  recorded board match         : True
```

### 3.3 Why this falsifies C3

The claim's third clause is absolute: "NO value available to an IPokerAgent at a turn can be
inverted to the session seed or reproduce the recorded hole cards and board." The observation is an
agent-facing value; it is a deterministic function of the session seed through the public
`Derive`/`SeededDeck` functions; and enumeration of the low-entropy seed space used by every
documented command (42, 7, 777, and the seed 1 used here) recovers the seed and the entire deck. The
previous round's attack searched for a turn-seed digest; this round removed the digest, but the
observation still contains enough cards to make the search possible. The suite's replacement
boundary test only enumerates the *deleted* digest formula (`AgentInformationBoundaryTests.cs:220-249`)
and therefore cannot detect this deal-based attack.

## C6 — test and doc integrity

### 6.1 Tests and waivers: the integrity sub-claims held

- Supplementary runner over `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll`:
  `TOTAL 182 PASSED 182 FAILED 0 SKIPPED 0`, exit 0.
- Source scan: 50 test files, 182 `[Fact]`, 0 `[Theory]`, 0 `[InlineData]`/`[MemberData]`, no
  `Skip =` assignments and no `#if`/`#endif` in the test sources. All 182 facts executed in the run
  above.
- `docs/plan/test-waivers.json`: `expectedTotals["Wip.Tholdem.Tests"]` is `182`; the `failures`
  and `knownAborted` arrays contain no entry whose `project` is `Wip.Tholdem.Tests` (they contain
  only `Wip.Modus.Tests` / `Wip.Bones.*` entries). `tools/run-wip-tests.ps1` includes
  `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj`.
- The round-12 report says the old `TurnSeed_Given...` fact was replaced because its subject no
  longer exists; the current tree indeed has no such fact. I did not treat that alone as the
  decisive C6 failure because it is a subject-removal replacement, not a failing assertion that was
  relaxed. The decisive failure is in section 6.3.

### 6.2 Documented commands and examples: observed values match

All commands below were run from the repository root with the Release CLI; the observed output was
compared with `docs/tholdem/*.md`.

| command | observed result |
|---|---|
| `--version` | `0.1.0`, exit 0 |
| `--help` | the usage block in `README.md` (commands, defaults, exit codes), exit 0 |
| `play --seed 777 --hands 2 --data-dir .tmp/falsify12/play777` | `session: tholdem-f275b4ee69be0557`; `seed: 777  hands: 2  manifest sha256: ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee`; session and hand-00001 byte-equal to the schema doc |
| `train --iterations 1 --seed 7` | `iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-b155b98f promoted=false verified=true outcome=rejected meanBbPer100=0.00 lcb=0.00 seeds=24`; `final champion: tholdem-strategy-v1-f100e793` |
| `train --iterations 3 --seed 7` | the three iteration lines and final champion are exactly the runbook block (`e1ba56f7 1.67 -0.76`, `ea96d95d -1.25 -3.40`) |
| `train --iterations 1 --seed 7 --json` | `{"schema":"tholdem.cli-training.v1",...}` with the documented shape |
| `evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 4` | `decision: lcb-below-margin`; `verifier: verified (rejection-justified)`; generated `cli-...json` byte-equal to the schema doc (`2/2/4`, `seeds:4`) |
| `evaluate ... --seeds 4 --margin -1000` | `decision: insufficient-seeds`; `verifier: verified (rejection-justified)` — the documented "below minSeeds never promotes" behaviour |
| `promote ...` without `--yes` and with `--yes` on the non-promotable report | `promotion requires explicit confirmation; re-run with --yes`, `promotion refused: lcb-below-margin`, `verifier: rejection-justified`, exit 3, champion unchanged |
| `replay ...hand-00001.json` (with manifest) | hand/board/hole cards/actions/stacks printed, exit 0, no `integrity: unanchored` |
| `replay` in a directory without `session.json` | same output plus `integrity: unanchored`, exit 0 |
| `replay ... --expected-hash <correct>` / `<wrong>` | exit 0 / exit 5 with the mismatch message |
| `retention --status --data-dir ...` | `sessions: 1`, `hands: 2`, bytes and `oldest session:` line, exit 0 |
| viewer `GET /api/status` at 5099 | `{"mode":"spectator","dataDirectory":"...\\artifacts\\tholdem","sessions":2,"strategies":4,"liveMatches":0,"offline":true}` |
| viewer `GET /`, `/api/sessions`, `/api/strategies`, `/api/ledger`, `/api/champion` | `302` to `/viewer/index.html`, then `200` with the documented shapes |
| viewer `POST /api/live` with an unknown agent | `400 {"error":"unknown-agent","seat":0,"agent":"nope"}` |

Programmatic extraction of the seven fenced JSON examples from
`docs/tholdem/strategy-artifact-schema.md` and byte comparison with the generated files:

```text
v1 artifact: bytes=333 docBytes=333 byteEqual=True
ledger:      bytes=273 docBytes=273 byteEqual=True
training report: bytes=279 docBytes=279 byteEqual=True
cli eval:    bytes=370 docBytes=370 byteEqual=True
champion:    bytes=59  docBytes=59  byteEqual=True
session:     bytes=335 docBytes=335 byteEqual=True
hand:        bytes=321 docBytes=321 byteEqual=True
```

### 6.3 The docs' non-invertibility claim: what broke

Two concrete factual statements in the in-scope docs do not match current behaviour:

- `docs/tholdem/viewer.md:100-105` — "The context has no seed of any width, no digest and no other
  secret-derived value, so nothing an agent receives can be inverted to the session seed or to the
  deck."
- `docs/tholdem/engine-rules.md:163-174` — "... so nothing an agent receives from it can be inverted
  to the session seed or to the deck. ... The observation is the agent's input (its own hole cards
  plus public state), so its derived value is reproducible for an honest replay and reveals nothing
  about the deck."

The C3 counterexample above is exactly the negation: from the seat's observation alone, enumeration
of 0..100000 recovered session seed `1` from one flop turn, session seed `42` from one turn, and
session seed `777` from two preflop turns of the documented `play --seed 777 --hands 2` session, and
rebuilt the recorded opponent hole cards and board. The observation object contains no seed *field*,
but the claim is about inversion, not fields, and the observation is invertible.

### 6.4 Result

The test-execution, waiver and concrete-example sub-claims are intact; the absolute "every factual
claim ... matches current behaviour" conjunct is not. C6 is therefore falsified, on the same
underlying defect that falsifies C3 (and on the two docs lines above rather than on a stale example).

## Not checked / limits

- CI execution and GitHub Actions results were not observed (the docs themselves say no CI result is
  claimed).
- Browser-only JavaScript, keyboard and responsive-geometry behaviour was not executed; only the
  HTTP surface at 5099 was queried.
- The live-model provider path was not exercised (no `DEEPSEEK_API_KEY`; the docs say the path is
  gated and untested without a key).
- `dotnet test` was not used (VSTest aborts on this machine); the supplementary runner is the test
  evidence, as in the previous rounds.
- `retention --purge` (destructive) was not run; only `retention --status` was observed.
- The attack is an API/artifact-level information-flow check; it does not attempt OS-level side
  channels (debugger, process memory, or an agent implementation deliberately reading the CLI's
  session directory outside the `IPokerAgent` contract).