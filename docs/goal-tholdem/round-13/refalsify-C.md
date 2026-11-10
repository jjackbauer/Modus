# Round 13 — C refalsification: hidden information and test/doc integrity

- **Repository**: `C:\Users\ricar\dev\Modus`, branch `WIP`, current tree (2026-11-07).
- **Scope**: claims C3 and C6 only. No product, test, doc or configuration file was edited; the
  only file written is this report. Scratch output lives under `.tmp\refalsify-c-13`.
- **Build/runner**: `Wip.Tholdem.Host.exe` and `Wip.Tholdem.Tests.dll` from the Release build;
  supplementary runner `dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release
  -- <test dll>`. `dotnet test` was not used (documented Win32Exception (5) limitation). The
  `dotnet build Wip.slnx --configuration Release` run was re-executed and exited 0 with 0 errors and
  6 non-Tholdem warnings; the web-project build exited 0 with 0 warnings.

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| C3 | Supported | **(a)** Agent inputs are `PlayerObservation` (own hole cards, board and public state only) plus `AgentTurnContext` = `(HandNumber, Seat, ActionIndex)`. `TholdemRuntimeAgent` sends only the observation JSON to the model; the four tools are equity/pot-odds calculators and `HandHistoryTool`/`OpponentStatsTool`, which are seat-fixed; `SessionHandView` has board/own cards/actions/results only; `TableState.Seed/Deck/HoleCardsSeat0/HoleCardsSeat1` are `internal` with `InternalsVisibleTo` only `Wip.Tholdem.Tests`. The 194/194 suite includes `AgentInformationBoundaryTests` and `ActAsync_GivenObservation_ExpectedNoHiddenInformationInChatRequests`, which assert all 50 non-visible cards and the session-seed decimal are absent from every chat message. Session manifests and `hand-NNNNN.json` files contain seed/full deal but are operator replay artifacts written by `play`, exactly as the docs acknowledge; `SimulationReport`/`TrainingRun` expose the operator-supplied seed to the CLI, not to an agent. **(b)** `dotnet list reference` for `Wip.Tholdem.Agents` shows only `Wip.Abstractions`, `Wip.Runtime`, `Wip.Tholdem`; `Wip.Tholdem.Web` references `Agents` (one-way), has no `InternalsVisibleTo`, and the `Wip.Tholdem.Agents.dll` contains no `Wip.Tholdem.Web`, `SpectatorSnapshot`, `HoleCardsSeat0` or `HoleCardsSeat1` text. `SpectatorSnapshot` is defined only in `WIP/Wip.Tholdem.Web/Live/SpectatorSnapshot.cs` and produced only by `LiveMatch.Snapshot()` for `GET /api/live/{id}`. **(c)** Observed CLI: `play --seed 42 --hands 1` -> exit 2, `tholdem: seed 42 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos`; `play --seed 1099511627775` -> exit 2 with the same message shape; `play --seed 1099511627776 --hands 1` -> exit 0; `play --seed 42 --allow-weak-seed` -> exit 0 and session `tholdem-c3d4ff060ef47ae4`; `train --iterations 1 --seed 7` -> exit 2 with the `seed 7 ...` message, and the same command with `--allow-weak-seed` -> exit 0; `evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 1 --hands 1 --seed 42` -> exit 2 with the `seed 42 ...` message, and with `--allow-weak-seed` -> exit 0 with the evaluation written. Observed HTTP (current viewer on 5099, whose DLL hash equals the Release build): seeds 42, 1099511627775 and `"42"` without the field -> 400 `{"error":"weak-seed","minimum":1099511627776}`; seed 42 with `"allowWeakSeed": true` -> 201; seed 1099511627776 -> 201; seed `"18446744073709551615"` -> 201. Query `?allowWeakSeed=true`, an HTTP header, `allow_weak_seed`, and the string value `"true"` all leave the 400 in place. **(d)** `docs/tholdem/known-limits.md` states the precise threat model: no agent-facing exposure; a guessable seed is enumerable; guard rules; opting out is outside the guarantee. Its evidence sentence says candidate seeds `0..20000` against the `Qs,4s` hand-1 cards produced 11 matches, first `42`, and deck `Qs Tc 4s Jd 8h 8s 8d Th 6c`; I independently reimplemented `SeedDerivation.Derive`/`SeededDeck.Create`/Fisher-Yates in Node and obtained exactly 11 matches, first `42`, and that exact first-nine deck; the actual `play --seed 42 --hands 3 --allow-weak-seed` `hand-00001.json` has seat 0 `Qs,4s`, seat 1 `Tc,Jd`, board `8h,8s,8d,Th,6c`. A grep of all `docs/tholdem/*.md` for `invert|non-invert|reveals nothing|nothing an agent` finds only (i) the known-limits sentence saying it replaces earlier absolute wording, and (ii) scoped `no agent input contains a stored value to invert` sentences in `viewer.md`/`engine-rules.md`, each immediately followed by the deterministic-deal/guessable-seed caveat. | I tried to make an agent obtain hidden information: inspected every public type reachable from `IPokerAgent`/`TholdemRuntimeAgent`, ran the full hidden-information suite, and searched the Agents assembly for Web/spectator/deck identifiers; no path was found. I tried every operator entry point with a weak seed and no opt-in, the exact boundary (`2^40-1` refused, `2^40` accepted), and web bypasses (query string, header, alternate key, string `"true"`); nothing weak was accepted without the opt-in flag. I tried to reach `SpectatorSnapshot` from `Wip.Tholdem.Agents`; the project-reference graph and binary contain no such edge. I tried to find absolute non-invertibility wording; only the scoped statements above exist, and I re-derived the documented enumeration. One parser edge is worth recording: `--allow-weak-seed=false` is refused (exit 2, `option '--allow-weak-seed' does not take a value`), while `--allow-weak-seed false` is accepted because the CLI defines the opt-in as a standalone flag and `play` ignores the stray positional `false`; the opt-in token was supplied in that invocation, so this is a UX footgun, not acceptance without an opt-in. | All four subclaims hold on the current tree: the direct agent surface contains no hidden information, the spectator type is compile-time unreachable from the Agents project, every weak-seed entry point refuses without the flag/field, and the docs state and gate the residual enumeration risk instead of denying it. |
| C6 | Supported | **Suite**: `dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` exited 0 with `TOTAL 194 PASSED 194 FAILED 0 SKIPPED 0`. There are 194 `[Fact]` attributes in 51 test source files, 0 `[Theory]`, 0 `Skip=`, 0 `#if`/`#endif`, no `xunit.runner.json`/runsettings, no `<Compile Remove>`, all 44 test classes are public, and every test file contains assertions. **Waivers**: `docs/plan/test-waivers.json` parses; `expectedTotals["Wip.Tholdem.Tests"] = 194`; no `failures` or `knownAborted` entry names Tholdem (the only `tholdem` occurrence is that expected total). **Documented commands observed**: `--help` -> exit 0 with `--allow-weak-seed`, `1099511627776` and the documented seeds paragraph; `play --seed 777 --hands 2 --allow-weak-seed` -> session `tholdem-f275b4ee69be0557`, manifest `ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee`, and the generated `session.json`/`hand-00001.json` are byte-equal to the examples in `strategy-artifact-schema.md`; `play --seed 42 --hands 3 --allow-weak-seed` -> hand 1 `Qs,4s`/`Tc,Jd`/board `8h,8s,8d,Th,6c` as documented; `train --iterations 3 --seed 7 --allow-weak-seed --data-dir artifacts\tholdem-run` -> the exact three documented iteration lines and `final champion: tholdem-strategy-v1-f100e793`; `evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 24 --data-dir artifacts\tholdem-run` -> `decision: lcb-below-margin`, `verifier: verified (rejection-justified)`, exit 0; `promote ... --yes` -> `promotion refused: lcb-below-margin`, `verifier: rejection-justified`, exit 3; `promote` without `--yes` -> `promotion requires explicit confirmation; re-run with --yes` plus the refusal, exit 3; `evaluate ... --seeds 4` -> the documented CLI evaluation JSON (`4/4/0`) byte-equal; `replay` of the anchored hand -> exit 0 with the documented hand/action/stacks lines, and a copy without a sibling manifest -> exit 0 with `integrity: unanchored`; `retention --status --json` -> `{"schema":"tholdem.retention.v1",...}` exit 0, and a purge preview -> exit 3; `evaluate ... --min-seeds 1` -> exit 2 `unknown option '--min-seeds'`. All seven JSON examples in `strategy-artifact-schema.md` compare byte-for-byte equal to freshly generated files (including the one-iteration ledger/report/pointer and the seed-777 session/hand). **Viewer**: the current 5099 process runs the Release DLL (SHA-256 equals `WIP/Wip.Tholdem.Web/bin/Release/net10.0/Wip.Tholdem.Web.dll`), serves `app.js`/`index.html`/`viewer.css` byte-identical to the repo, returns `GET /` -> 302, the documented `/api/status` JSON, weak-seed 400, `allowWeakSeed:true` 201, strong seed 201, numeric-string seed 201, 501 hands -> 400 `invalid-hands`, 5001 ms -> 400 `invalid-delay`, unknown agent -> 400 `unknown-agent`, unknown live id -> 404; the documented web build and copy steps exited 0 and the copied DLL equals the Release DLL. | I searched for skipped, deleted or weakened Tholdem tests: no `Skip`/`Ignore`/`#if`/`[Trait]`/runner-filter/exclusion markers exist; the project is untracked in git, so there is no committed baseline against which a deletion could be diffed, but the current 194 facts exactly match the expected total, all are discovered and executed, and every test file has assertions. I looked for a Tholdem waiver and found none. I ran the documented commands and compared exact stdout/JSON; I also tried the adjacent cases (promote with/without `--yes`, evaluate seeds 4/24, replay anchored/unanchored, retention status/purge-preview, absence of `--min-seeds`, viewer bounds) and found no mismatch. I did not start the documented viewer command on 5099 because a current-build server already owns that port; I verified its DLL and assets are the current build and exercised every documented HTTP endpoint instead. The 388 ms enumeration timing is a machine measurement, not a behavioural claim; the substantive 11-match/first-42/deck facts were independently reproduced. | The executable test, waiver and documentation example checks all pass, and no skip, exclusion, waiver or exercising shortcut was found. The only unavailable check is a historical git diff for deletion, because the whole Tholdem test project is untracked; the current tree's count, expected total, compilation, and absence of skip/exclusion markers are consistent with the claim. |

## C3 — detail

### (a) Agent-facing surface

`IPokerAgent.ActAsync` takes only `WIP/Wip.Tholdem/History/PlayerObservation.cs` and
`WIP/Wip.Tholdem.Agents/AgentTurnContext.cs`. `AgentTurnContext` is exactly
`(int HandNumber, int Seat, int ActionIndex)`. `PlayerObservation.ToCanonicalJson()` emits hand
number, street, button seat, acting seat, board, own hole cards, own/opponent stacks, pot,
commitments, amount to call, min/max raise-to, legal action kinds, completion and winners. It has no
seed, deck order, undealt cards or opponent hole cards.

The runtime agent (`Runtime/TholdemRuntimeAgent.cs`) sends the observation JSON and the public
hand/seat text to the model. Its four tools (`Runtime/TholdemToolCatalog.cs`) are:
`equity-calculator` and `pot-odds` calculators, `hand-history-query` (fixed seat, returns
`SessionHandView`: board, **own** hole cards, action log, winners, showdown, pot, net chips), and
`opponent-statistics` (aggregate VPIP/PFR/aggression/showdown only). `SessionHandStore.ToView`
replays the record and projects through `PlayerObservation.ForSeat`; it does not return the stored
`HandRecord`.

The engine's `TableState.Seed/Deck/HoleCardsSeat0/HoleCardsSeat1/DeckPosition/ActedMask/AwardedPot/
ActionLog` are `internal`; `Wip.Tholdem.csproj` grants `InternalsVisibleTo` only to
`Wip.Tholdem.Tests`. `Wip.Tholdem.Agents` is not a friend.

The full suite passes the dedicated boundary facts, including
`ActAsync_GivenObservation_ExpectedNoHiddenInformationInChatRequests`, which checks that none of the
50 cards outside the acting seat's view and the seed decimal appear in any chat message, and
`AgentInformationBoundaryTests.AgentSources_...`, which scans the executable agent sources for
hidden-information identifiers.

Operator-facing artifacts are explicitly out of scope in the doc: `session.json` and
`hand-NNNNN.json` contain the seed and full deal, but they are written by `play` for replay, not
passed to an agent. `SimulationReport.Seed`/`TrainingRun.Options.Seed` are returned to the CLI
operator, not to an agent; `SeedDerivation.Derive(ulong sessionSeed, ...)` takes the seed as an input.

### (b) Spectator type reachability

`dotnet list WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj reference` prints only
`Wip.Abstractions`, `Wip.Runtime`, `Wip.Tholdem`. `Wip.Tholdem.Web` prints `Wip.Tholdem` and
`Wip.Tholdem.Agents`; the reference is one-way. `Wip.Tholdem.Web` has no `InternalsVisibleTo` and no
type-forward to `SpectatorSnapshot`. A byte search of the Release
`Wip.Tholdem.Agents.dll` contains no `Wip.Tholdem.Web`, `SpectatorSnapshot`, `HoleCardsSeat0` or
`HoleCardsSeat1` text (it does contain `TableState`, used internally by `MatchSimulator`/
`SessionHandStore`, which is not the spectator type). `SpectatorSnapshot` is defined only in
`WIP/Wip.Tholdem.Web/Live/SpectatorSnapshot.cs` and produced only in
`WIP/Wip.Tholdem.Web/Live/LiveMatch.cs` for the spectator endpoint.

### (c) Entry-point observations (full command lines)

```text
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe play --seed 42 --hands 1
  exit=2  stderr: tholdem: seed 42 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos
WIP\...\Wip.Tholdem.Host.exe play --seed 1099511627775 --hands 1
  exit=2  stderr: tholdem: seed 1099511627775 is below the minimum 1099511627776; ...
WIP\...\Wip.Tholdem.Host.exe play --seed 1099511627776 --hands 1
  exit=0  session: tholdem-88f28b70086c22cf
WIP\...\Wip.Tholdem.Host.exe play --seed 42 --hands 1 --allow-weak-seed
  exit=0  session: tholdem-c3d4ff060ef47ae4
WIP\...\Wip.Tholdem.Host.exe train --iterations 1 --seed 7
  exit=2  stderr: tholdem: seed 7 is below the minimum 1099511627776; ...
WIP\...\Wip.Tholdem.Host.exe train --iterations 1 --seed 7 --allow-weak-seed
  exit=0  iteration=1 champion=tholdem-strategy-v1-f100e793 ...
WIP\...\Wip.Tholdem.Host.exe evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 1 --hands 1 --seed 42
  exit=2  stderr: tholdem: seed 42 is below the minimum ...
WIP\...\Wip.Tholdem.Host.exe evaluate ... --allow-weak-seed
  exit=0  evaluation: ...\cli-tholdem-strategy-v2-b155b98f-vs-tholdem-strategy-v1-f100e793.json
```

Web, on the current 5099 host (DLL equals the Release build):

```text
POST /api/live {"seed":42,...}                              -> 400 {"error":"weak-seed","minimum":1099511627776}
POST /api/live {"seed":1099511627775,...}                   -> 400 {"error":"weak-seed","minimum":1099511627776}
POST /api/live {"seed":"42",...}                            -> 400 {"error":"weak-seed","minimum":1099511627776}
POST /api/live {"seed":42,...,"allowWeakSeed":true}         -> 201
POST /api/live {"seed":1099511627776,...}                   -> 201
POST /api/live {"seed":"18446744073709551615",...}          -> 201
POST /api/live?allowWeakSeed=true {"seed":42,...}           -> 400 weak-seed
POST /api/live with header allowWeakSeed:true {"seed":42,...} -> 400 weak-seed
POST /api/live {...,"allow_weak_seed":true}                 -> 400 weak-seed
POST /api/live {...,"allowWeakSeed":"true"}                 -> 400 (body binding rejected)
```

CLI parser edge: `--allow-weak-seed=false` is a usage error (exit 2, `option '--allow-weak-seed' does
not take a value`); `--allow-weak-seed false` is accepted because the flag is standalone and the
stray positional is ignored by `play` (the opt-in token was supplied; the inline `=false` form is
refused). No invocation without the flag token accepted a weak seed.

### (d) Documentation

`docs/tholdem/known-limits.md` (Threat model) says, in order: no agent API/artifact exposes the
seed/deck/undealt/opponent cards; a guessable seed lets an observer enumerate and reconstruct the
deck; the entry points require `>= 1,099,511,627,776`; and an operator who opts out is outside the
secrecy guarantee. It includes the `play --seed 42 --hands 3 --allow-weak-seed` enumeration evidence.

Independent reproduction of that evidence (Node BigInt implementation of the engine's
`SeedDerivation.Derive`, `Xorshift64Star`, and the `SeededDeck` Fisher-Yates, reading only the current
C# source):

```text
matches=11 first=42,2865,2954,5700,6077,9955
seed42-hand1-deal=Qs Tc 4s Jd 8h 8s 8d Th 6c
```

The actual `hand-00001.json` from the documented command matches
`holeCardsSeat0 ["Qs","4s"]`, `holeCardsSeat1 ["Tc","Jd"]`,
`board ["8h","8s","8d","Th","6c"]`.

## C6 — detail

### Tests and waivers

```text
TOTAL 194 PASSED 194 FAILED 0 SKIPPED 0
```

`SessionSeedGuardTests` has 10 facts covering the constant (`0`, `minimum-1`, `minimum`,
`ulong.MaxValue`), the three commands without the flag (exit 2 + exact message + no artifact) and
with the flag (exit 0 + artifact), and the strong-seed cases. The web guard test posts seed 42 without
the field and expects 400 `weak-seed`, with `allowWeakSeed:true` expects 201, the minimum seed expects
201, and a numeric-string seed expects 201.

`docs/plan/test-waivers.json` contains only `expectedTotals["Wip.Tholdem.Tests"] = 194`; there is no
Tholdem failure, abort, exclusion or skip entry. `.github/workflows/tests-ci.yml` adds a Tholdem
security step and `tools/run-wip-tests.ps1` adds the full Tholdem test project; neither excludes it.

### Documented commands run and observed

```text
dotnet build Wip.slnx --configuration Release
  Build succeeded. 0 Error(s), 6 Warning(s) (none Tholdem), exit 0
WIP\...\Wip.Tholdem.Host.exe --help
  exit 0; usage contains "tholdem play ... --allow-weak-seed", "1099511627776", seeds paragraph
WIP\...\Wip.Tholdem.Host.exe play --seed 777 --hands 2 --allow-weak-seed
  exit 0; session: tholdem-f275b4ee69be0557; manifest sha256:
  ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee
WIP\...\Wip.Tholdem.Host.exe play --seed 42 --hands 3 --allow-weak-seed
  exit 0; session: tholdem-9bae4b03e89b5865; hand 1 = Qs,4s / Tc,Jd / 8h,8s,8d,Th,6c
WIP\...\Wip.Tholdem.Host.exe train --iterations 3 --seed 7 --allow-weak-seed --data-dir artifacts\tholdem-run
  iteration=1 ... b155b98f promoted=false verified=true outcome=rejected meanBbPer100=0.00 lcb=0.00 seeds=24
  iteration=2 ... e1ba56f7 ... meanBbPer100=1.67 lcb=-0.76
  iteration=3 ... ea96d95d ... meanBbPer100=-1.25 lcb=-3.40
  final champion: tholdem-strategy-v1-f100e793
WIP\...\Wip.Tholdem.Host.exe evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 24 --data-dir artifacts\tholdem-run
  decision: lcb-below-margin; verifier: verified (rejection-justified); exit 0
WIP\...\Wip.Tholdem.Host.exe promote --challenger tholdem-strategy-v2-b155b98f --yes --data-dir artifacts\tholdem-run
  promotion refused: lcb-below-margin; verifier: rejection-justified; exit 3
WIP\...\Wip.Tholdem.Host.exe promote --challenger tholdem-strategy-v2-b155b98f --data-dir artifacts\tholdem-run
  promotion requires explicit confirmation; re-run with --yes; promotion refused: lcb-below-margin; exit 3
WIP\...\Wip.Tholdem.Host.exe evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 4 --data-dir artifacts\tholdem-run
  exit 0; generated CLI evaluation JSON is byte-equal to the doc (challengerWins 4, incumbentWins 4, ties 0)
WIP\...\Wip.Tholdem.Host.exe replay artifacts\tholdem-run\sessions\tholdem-f275b4ee69be0557\hand-00001.json
  exit 0; hand/board/actions/stacks lines; no "unanchored"
WIP\...\Wip.Tholdem.Host.exe replay .tmp\refalsify-c-13\hand-copy\hand-00001.json
  exit 0; ...; integrity: unanchored
WIP\...\Wip.Tholdem.Host.exe retention --status --json --data-dir artifacts\tholdem-run
  exit 0; {"schema":"tholdem.retention.v1","sessions":1,"hands":2,"bytes":1043,...}
WIP\...\Wip.Tholdem.Host.exe retention --purge --older-than-days 1 --data-dir artifacts\tholdem-run
  exit 3; preview: 0 session(s) older than 1 day(s); re-run with --yes to delete
WIP\...\Wip.Tholdem.Host.exe evaluate --challenger ... --seeds 4 --min-seeds 1 --data-dir artifacts\tholdem-run
  exit 2; tholdem: unknown option '--min-seeds'.
dotnet build WIP/Wip.Tholdem.Web/Wip.Tholdem.Web.csproj --configuration Release
  Build succeeded. 0 Warning(s), 0 Error(s); documented Copy-Item target equals the Release DLL
```

All seven JSON examples in `docs/tholdem/strategy-artifact-schema.md` were compared as strings with
the generated files from a fresh one-iteration run and the documented play/evaluate commands:

```text
json-blocks=7
block1 (v1 strategy) equal=True
block2 (ledger one-entry array) equal=True
block3 (training report) equal=True
block4 (CLI evaluation) equal=True
block5 (active-champion pointer) equal=True
block6 (seed-777 session.json) equal=True
block7 (seed-777 hand-00001.json) equal=True
```

Viewer observations on `http://127.0.0.1:5099` (server DLL SHA-256
`2DAA393CA62EE59DB853C0F25D7DE905DEEEF3AA791649B7D1B59A2498EFA27A` equals the Release DLL; served
`app.js`/`index.html`/`viewer.css` hashes equal the repo files):

```text
GET /                                      -> 302 Location: /viewer/index.html
GET /api/status                            -> 200 {"mode":"spectator",...,"offline":true}
GET /viewer/index.html                     -> 200; value="1099511627776"; input id="allow-weak-seed"
POST /api/live seed 42 no opt-in           -> 400 {"error":"weak-seed","minimum":1099511627776}
POST /api/live seed 42 allowWeakSeed true  -> 201
POST /api/live seed 1099511627776          -> 201
POST /api/live hands 501                   -> 400 {"error":"invalid-hands","min":1,"max":500}
POST /api/live delay 5001                  -> 400 {"error":"invalid-delay","min":0,"max":5000}
POST /api/live unknown agent               -> 400 {"error":"unknown-agent","seat":0,"agent":"cheater"}
GET /api/live/nope                         -> 404
```

### Limits of this check

- The whole `WIP/Wip.Tholdem.Tests` project is untracked in git, so there is no committed baseline
  against which a test deletion could be diffed. The current tree has 194 facts, the 194-test expected
  total, 194 executed, no skip/exclusion markers, and no Tholdem waiver; no counterexample to the
  no-deletion part was found in the available tree.
- The documented viewer start command could not bind `127.0.0.1:5099` because a current-build viewer
  already owns it. The running server was verified to be the current Release DLL with byte-identical
  viewer assets, and every documented endpoint/response was exercised against it; the port conflict
  is environmental, not a doc/code mismatch.
- The 388 ms enumeration time in `known-limits.md` is a machine-specific observation; the substantive
  facts (11 matches, first 42, exact deck, actual hand cards) were independently reproduced.