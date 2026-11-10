# Falsify A — round 4 (multiway)

- **Falsifier**: A (independent probe; no product, test, doc or config file edited).
- **Tree**: `C:\Users\ricar\dev\Modus`, branch `WIP`, sampled 2026-11-07 (America/Sao_Paulo).
- **Probe**: `.tmp/tholdem-verify/FalsifyA` (scratch only). It references `WIP/Wip.Tholdem` and
  `WIP/Wip.Tholdem.Agents` and drives the public `HoldemHandEngine`, `MatchSimulator`, `HandRecord`
  and `HandRecordJson` surfaces. Its side-pot calculator is deliberately a *different* algorithm from
  the product: it peels the highest contribution level off first (top-down), while
  `ResolveShowdown` builds layers bottom-up.
- **Build**: `dotnet build .tmp\tholdem-verify\FalsifyA\FalsifyA.csproj -c Release` →
  `Build succeeded. 0 Warning(s) 0 Error(s)`.
- **Suite runner**: `dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP\Wip.Tholdem.Tests\bin\Release\net10.0\Wip.Tholdem.Tests.dll`.
- **Viewer**: `GET http://127.0.0.1:5099/api/status` → `200 {"mode":"spectator",...,"liveMatches":0,"offline":true}`; live matches were created through `POST /api/live` and deleted afterwards.

## Verdict table

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| M1 | Supported | 2700 randomized hands + 250 board-plays tie hands over 2–6 seats and unequal stacks, every terminal state compared with the independent top-down calculator: layer amounts, winners per layer and final stacks matched in every hand (`RESULT sidepots=PASS`, `RESULT tiefuzz=PASS`; 1368 + 97 multi-layer hands, 242 + 394 tied layers). Hand-computed 4-seat scenario seed 48926, board `2c 2d Ad 2s 2h`: all-in commitments `[1000,500,300,200]` gave pots `[800,300,400,500]` with winners `[0,1,2,3],[0,1,2],[0,1],[0]` and final stacks `[1000,500,300,200]`; the dead-money/fold variant `[5,500,300,200]` gave pots `[20,585,200,200]` with winners `[1,2,3],[1,2,3],[1,2],[1]` and final stacks `[995,502,302,201]`, including the 20/3 split where the two odd chips go to seats 1 and 2 (first eligible winners clockwise from button 0). Folded seats never appeared in any pot's `Winners`, and the top uncalled-excess layer was returned to its only contributor. | Random legal-action fuzz with 9 policies (uniform, all-in-first, fold-first, min-raise-first, 70%-all-in, check/call, all-in-then-min-raise, min-raise-then-all-in, random raise size), starting stacks 10–900 plus equal 1000, all seat counts 2–6 and random buttons; board-plays tie fuzz to force multiway odd-chip splits; two independent hand-computed scenarios including a folded seat and uncalled excess. No layer, winner or stack mismatch was found. | The engine's awards matched the independent calculator and both hand calculations exactly; I could not falsify the layered-by-contribution, folded-dead-money, uncalled-return or clockwise odd-chip rules. |
| M2 | Supported | The same 2700 + 250 hands checked `sum(Stacks) + Pot == sum(StartingStacks)` after **every** `Apply` (32751 and 1390 actions observed), plus `Pot == sum(TotalCommitted)` while live, no negative stack, a valid acting seat, `Pot == 0` on completion and final `sum(Stacks) == total`. All hands terminated; the longest hand took 104 actions. Suite property test also passed. | Adversarial policies including all-in-first, fold-first, min-raise wars, random raise sizes, short stacks (some seats start at exactly the 10-chip big blind) and equal/unequal stacks, 2–6 seats; every returned state was checked. No conservation violation or non-termination was found. | The invariant is structural in `AdvanceAfterChips`/`AwardFold`/`ResolveShowdown` and held in every observed transition; the largest observed action count is bounded well below the 5000-action cap. |
| M3 | Supported | Simulator run twice (2, 4 and 6 seats): `deterministic=True`, byte-identical files, identical manifest hashes. CLI vs simulator for the same seed/agents/seats/stacks: 2-seat 21/21 files, 4-seat 4/4 files, 6-seat 3/3 files byte-identical. CLI run twice: byte-identical. 4-seat CLI session `tholdem-d56ac5f4637e44ef`, manifest `96ffb0e1efeabfd94987542634a4d8c6db3975b049b913a42db664de7c7e271e`, hand hashes `cf674bd0…,68fab88c…,7eaa9c32…`; live 4-seat match (`POST /api/live`, seed 42, hands 3, same agents/stacks, delay 0) produced exactly those three hand hashes. 3-seat live match matched the CLI hashes `c17d2caa…,7904223971…,9f9c2293…`. Seed 43 produced a different session (`tholdem-05d79a7bccb44d15`, manifest `fd715854…`) and all four files differ. | Ran the simulator twice per configuration, ran the CLI twice, compared CLI files against simulator files, ran live matches through the viewer API and compared `handHashes`, and compared seed 42 against seed 43. No byte, hash or hash-order difference was found. | Every same-configuration run was byte-identical; different seeds differed; the live match's per-hand hashes equal the simulator/CLI record hashes. |
| M4 | Supported | Captured 216 real `PlayerObservation`/`AgentTurnContext` inputs over 30 hands plus 32 checkdown captures at 2, 3, 4, 5 and 6 seats. `PlayerObservation` exposes only `ActingSeat, AmountToCall, Board, ButtonSeat, HandNumber, IsComplete, LegalActionKinds, MaxRaiseTo, MinRaiseTo, OpponentStack, OpponentStreetCommitment, OwnHoleCards, OwnStack, OwnStreetCommitment, Pot, Seat, SeatCount, Seats, Street, Winners`; `AgentTurnContext` exposes exactly `ActionIndex, HandNumber, Seat`. For every capture, no string belonging to another seat's hole cards, to an undealt card or to a future board card appeared in the observation JSON, and no seed/deck key was present. Weak-seed attack: a river observation of 4-seat hand 1 (`seed 42`) recovered session seed **42 as the only match** over candidates 0..200000 in 1.7 s using only public `SeedDerivation.Derive` + `SeededDeck.Create`; the same attack at 2/3/5/6 seats also recovered 42. This is exactly the documented limitation, not a break of the stated observation surface. Strong-seed control (`1099511627776`): 0 matches over 0..200000. CLI guard: `play --seed 42 --hands 1` exit 2 with `seed 42 is below the minimum 1099511627776; use --allow-weak-seed…`; `--seed 1099511627775` exit 2; `--seed 1099511627776` exit 0; weak seed + `--allow-weak-seed` exit 0; `train --iterations 1 --seed 7` exit 2 and opt-in exit 0. HTTP: seed 42/1099511627775/"42"/`allowWeakSeed:false` → 400 `{"error":"weak-seed","minimum":1099511627776}`; seed 1099511627776 and seed 42 + `allowWeakSeed:true` → 201. `docs/tholdem/known-limits.md` §Threat model (a)–(d) and `engine-rules.md`/`viewer.md` state the enumeration limitation and the opt-out boundary; `known-limits.md` says the section replaces earlier absolute wording. | Captured every agent-side input for 2–6 seats and searched it for the session seed, hand seed, deck order, undealt cards, future board cards and other seats' hole cards; ran the seed-recovery attack against a weak and a strong seed; exercised the guard at CLI and HTTP including boundary, string, false and opt-in forms; read the current docs. The only exposure is the documented deterministic-deal inference for a weak seed, which the guard gates behind the explicit opt-in. Stale source XML comments in `IPokerAgent.cs`/`SeedDerivation.cs` still say inputs "cannot be inverted"; those are source comments, not `docs/tholdem`, and are recorded as a residual. | Direct hidden data was not found on the agent surface; the weak-seed recovery succeeded only as the docs already state, and the guard/doc clauses held. |
| M5 | Falsified | Positive checks: `play --seed 42 --hands 20 --allow-weak-seed` printed exactly `session: tholdem-06795fbeacfe528b` and `manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618`; the written `session.json` SHA-256 is `87f90413…` and the 20 hand files are v1. Replay probe over `artifacts/tholdem/sessions`: 33 files, 30 v1 + 3 v2, all parse, canonical-round-trip, hash-verify and replay with 0 failures; CLI `replay` of the v1 `probe-777/hand-00002.json` and the v2 4-seat `hand-00001.json` exited 0. Test scan: 59 files, 245 `[Fact]`, 8 `[Theory]`, 0 `Skip=`, 0 `#if`, 0 `Trait`, no `xunit.runner.json`/runsettings; `docs/plan/test-waivers.json` has `expectedTotals["Wip.Tholdem.Tests"] = 273` and no Tholdem failure/knownAborted entry. **But** the supplementary runner on the current tree exits 1: `TOTAL 273 PASSED 272 FAILED 1 SKIPPED 0`; `FAIL MultiwayHistoryTests.Parse_GivenEveryRecordedV1HandFile_ExpectedAllHashesVerifyAndAllReplay: EqualException ... Expected: 2 Actual: 4`. The test enumerates every `hand-*.json` under `artifacts/tholdem/sessions` and asserts `SeatCount == 2`; `artifacts/tholdem/sessions/tholdem-d56ac5f4637e44ef/hand-0000{1,2,3}.json` are v2 4-seat hands (written 2026-11-07 16:53:57, before this round) and are not filtered by the test. No waiver covers that failure, so `tools/verify-test-results.ps1` (lines 65–71) would report an unwaived failure and the test gate is red. Additionally, `git ls-files artifacts/tholdem` returns 0 files and `git check-ignore` matches `.gitignore:5:artifacts/`; the two artifact-sweeping tests therefore also cannot run on a clean checkout without operator data (code-reading inference from the `Assert.True(Directory.Exists(sessions))` lines, not executed in a clean clone). | Ran the suite twice (same result), ran the failing class in isolation (`TOTAL 8 PASSED 7 FAILED 1`), replayed every recorded file, ran the oracle twice, and inspected the waiver/git state. The compatibility *sub-clauses* (oracle, all 30 v1 files, no skip/waiver) hold, but the repository's compatibility test is red in the current tree. | I cannot mark COMPATIBILITY Supported while its own recorded-artifact test fails: the multiway recorder now writes a legitimate v2 session into the artifact root that the v1-sweep test treats as v1, and the failure is unwaived. The oracle and the v1 files themselves are fine; the delivered compatibility gate is not. |

## M1/M2 — how the randomized and hand-computed checks were run

```text
dotnet run --project .tmp\tholdem-verify\FalsifyA\FalsifyA.csproj -c Release --no-build -- handcomputed
handcomputed seed=48926 board=[2c 2d Ad 2s 2h]
independent B layers: 20:[1,2,3] | 585:[1,2,3] | 200:[1,2] | 200:[1]
RESULT handcomputed=PASS

dotnet run --project .tmp\tholdem-verify\FalsifyA\FalsifyA.csproj -c Release --no-build -- sidepots 300 500000
SIDEPOTS hands=2700 showdowns=2329 folds=371 multiLayerHands=1368 tiedLayers=242 totalActions=32751 maxActions=104 (policy=3 hand=24 seed=238260056 seats=6 stacks=[1000,1000,1000,1000,1000,1000])
RESULT sidepots=PASS

dotnet run --project .tmp\tholdem-verify\FalsifyA\FalsifyA.csproj -c Release --no-build -- tiefuzz 50
tiefuzz seats=2 seed=5626 board=[5s Ah 5h 5d 5c]
tiefuzz seats=3 seed=8861 board=[Ac 5s 5c 5h 5d]
tiefuzz seats=4 seed=48926 board=[2c 2d Ad 2s 2h]
tiefuzz seats=5 seed=9466 board=[2s 2h Ad 2d 2c]
tiefuzz seats=6 seed=4053 board=[Th Ah Ts Tc Td]
TIEFUZZ hands=250 showdowns=214 folds=36 multiLayerHands=97 tiedLayers=394 totalActions=1390 maxActions=24 (...)
RESULT tiefuzz=PASS
```

The four hand-computed facts checked against the engine (and against the top-down calculator) in
`handcomputed` were:

- Scenario A, no folds, commitments `[1000,500,300,200]`: layers `800,300,400,500`;
  winners `[0,1,2,3], [0,1,2], [0,1], [0]`; final stacks `[1000,500,300,200]`. The top 500-chip layer
  is the uncalled excess of seat 0.
- Scenario B, seat 0 folds its 5-chip small blind, commitments `[5,500,300,200]`: layers
  `20,585,200,200`; winners `[1,2,3], [1,2,3], [1,2], [1]`; final stacks `[995,502,302,201]`.
  The 20-chip main layer is 6 each plus two odd chips: seats 1 and 2 (first eligible winners
  clockwise from button 0) get 7 and seat 3 gets 6.
- In both scenarios the board is four of a kind plus an ace (`2c 2d Ad 2s 2h`), so every remaining
  seat ties and the only differences come from the pot layering, folded dead money and odd-chip rule.
- No folded seat won any layer in any of the 2950 hands, and every showdown award sum equalled
  `sum(TotalCommitted)` and `AwardedPot`.

## M3 — determinism evidence

```text
sim 42 3 1000,800,600,400 random-legal,tight-aggressive,pot-odds-equity,random-legal <A> <B>
sim seed=42 seats=4 hands=3 filesA=4 filesB=4 byteEqual=4 identical=True
session=tholdem-d56ac5f4637e44ef manifestA=96ffb0e1efeabfd94987542634a4d8c6db3975b049b913a42db664de7c7e271e ...
handHashes=cf674bd0...,68fab88c...,7eaa9c32...
RESULT sim=PASS
```

- CLI 4-seat run twice: both print `session: tholdem-d56ac5f4637e44ef` and manifest `96ffb0e1…`;
  directory hashes are equal. CLI directory vs simulator directory: 4/4 files byte-equal
  (`CF674BD0…`, `68FAB88C…`, `7EAA9C32…`, `96FFB0E1…`).
- 2-seat: simulator (agents `tight-aggressive,random-legal`, seed 42, 20 hands) wrote 21 files,
  byte-equal to the CLI oracle directory; manifest `87f90413…`, session `tholdem-06795fbeacfe528b`.
- 6-seat: simulator and CLI wrote 3/3 byte-equal files, session `tholdem-56e48ab6dc40e16f`,
  manifest `45157eb5…`, hand hashes `98b047f2…,011ba355…`.
- Live: `POST /api/live` with seed 42, hands 3, delay 0, seats 4, agents
  `random-legal,tight-aggressive,pot-odds-equity,random-legal`, stacks `[1000,800,600,400]` returned
  id `d06625d4716120d5`; polling gave status `complete` with handHashes
  `cf674bd0…,68fab88c…,7eaa9c32…` — identical to the simulator/CLI. 3-seat live (stacks
  `[1000,600,300]`, agents `random-legal,tight-aggressive,pot-odds-equity`) gave
  `c17d2caa…,7904223971…,9f9c2293…`, identical to the CLI session.
- Seed 43 with the 4-seat configuration printed session `tholdem-05d79a7bccb44d15` and manifest
  `fd715854…`; all four files differ from the seed-42 files.

## M4 — captured inputs, attack and guard

- Captures: `observe 42 4 30 …` → `captures=216`; checkdown runs at 2/3/5/6 seats → 8/12/20/24
  captures. All leak checks passed; `RESULT observe=PASS` for every seat count.
- Weak-seed recovery (from a river observation, only public derivation/shuffle):
  `seed-recovery hand=1 seat=1 candidates=0..200000 matches=1 first=42 session_seed_is_42=True
  elapsed_ms≈1200–1900` at 2/3/4/5/6 seats. This is the documented limitation in
  `docs/tholdem/known-limits.md` §Threat model (b)/(d), not a hidden-data member.
- Strong-seed control: `seed=1099511627776` → `matches=0`.
- CLI guard matrix (observed): weak 42 → exit 2; boundary 1099511627775 → exit 2; 1099511627776 →
  exit 0; 42 + `--allow-weak-seed` → exit 0; `train` seed 7 → exit 2; `--allow-weak-seed` train →
  exit 0; `--allow-weak-seed=false` → exit 2 usage error.
- HTTP guard matrix (observed): `{"seed":42,…}` → 400 `{"error":"weak-seed","minimum":1099511627776}`;
  `1099511627775` → same 400; `"42"` → same 400; `allowWeakSeed:false` → same 400;
  `1099511627776` → 201; `42` + `allowWeakSeed:true` → 201.
- Docs: `docs/tholdem/known-limits.md` §Threat model states that a guessable seed lets an observer
  enumerate candidate seeds and reconstruct the deck, cards and board; `engine-rules.md` and
  `viewer.md` state the same limitation and call it a statement about the agent surface, not the
  deal. No current `docs/tholdem` file claims absolute non-invertibility. Residual, not part of
  `docs/tholdem`: the XML doc comments in `WIP/Wip.Tholdem.Agents/IPokerAgent.cs` and
  `SeedDerivation.cs` still use absolute wording ("cannot be inverted", "is not a function of
  either"); that is a stale source-comment issue worth fixing, but it is not a current `docs`
  statement and was not used to change the verdict.
- Runtime path (code read): `TholdemRuntimeAgent.ActAsync` builds its task from `Hand <n>, seat <s>`
  plus the same canonical observation JSON, and its tools are equity/pot-odds/history/opponent-stats;
  `SessionHandStore.ToView` returns only board, that seat's own cards, action text, winners, showdown
  flag, pot awarded and net chips. The suite's `AgentInformationBoundaryTests`,
  `RuntimeSourceBoundaryTests` and `SessionHandStoreTests` all passed in the same 272/273 run; no
  seed/deck/opponent-card member is reachable through those surfaces.

## M5 — evidence and the concrete failure

- Oracle: observed exactly
  `session: tholdem-06795fbeacfe528b`
  `seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618`;
  the generated `session.json` hashes to the same manifest value and its 20 hand files are v1.
- Recorded artifacts: 33 hand files (30 v1, 3 v2) under `artifacts/tholdem/sessions`; all parse,
  canonical-round-trip, hash-verify and replay. The v1 `probe-777/hand-00002.json` and the v2
  4-seat `tholdem-d56ac5f4637e44ef/hand-00001.json` also replay through the CLI with exit 0.
- Test scan: 245 `[Fact]` + 8 `[Theory]` (273 executed cases), 0 `Skip=`, 0 `#if`, 0 `Trait`,
  no test runsettings; waiver register `expectedTotals["Wip.Tholdem.Tests"] = 273` and no Tholdem
  `failures`/`knownAborted` entry. No evidence of a skipped or weakened test was found.
- Failure: same command as every round, exit 1:
  `TOTAL 273 PASSED 272 FAILED 1 SKIPPED 0`
  `FAIL MultiwayHistoryTests.Parse_GivenEveryRecordedV1HandFile_ExpectedAllHashesVerifyAndAllReplay:
  EqualException: Assert.Equal() Failure: Values differ / Expected: 2 / Actual: 4`.
  The class alone: `TOTAL 8 PASSED 7 FAILED 1 SKIPPED 0`.
  Cause: `MultiwayHistoryTests.cs:171-186` enumerates **all** `hand-*.json` and asserts
  `SeatCount == 2`; the three `tholdem-d56ac5f4637e44ef` hand files are 4-seat v2 records written
  2026-11-07 16:53:57 (before this round), so the first such record trips the assertion. There is no
  waiver for this test; `tools/verify-test-results.ps1` rejects unwaived failures.
- Clean-checkout dependency (inference, not executed): `git ls-files artifacts/tholdem` = 0 and
  `git check-ignore artifacts/tholdem/sessions/probe-777/hand-00001.json` matches `.gitignore:5:artifacts/`;
  both artifact-sweeping tests assert `Directory.Exists(artifacts/tholdem/sessions)`, so without
  operator data they would fail with "Missing recorded sessions directory" (the suite in CI does not
  generate those files before the Tholdem project).

## Residuals not counted as claim breaks

- CI was not executed (no `gh`/token on this machine); `dotnet test` aborts with Win32Exception (5),
  so the supplementary runner is the only observed suite. The clean-checkout artifact absence above
  is a code-reading inference.
- **Session-identity gap (M3 scope note, not a same-configuration determinism failure)**:
  `MatchSimulator.ComputeSessionId` hashes seed, hands and agent names but not starting stacks.
  Observed: two CLI runs with seed 42, 1 hand, seats 2, agents `tight-aggressive,random-legal` but
  starting stacks `1000,600` then `800,400` both printed session `tholdem-c3d4ff060ef47ae4`; the
  second run replaced the first session directory, leaving only the `[800,400]` manifest
  (`e4240189…` hand hash) under that id. Same-configuration reruns are still byte-identical, so this
  is not a break of M3 as worded, but two different configurations share one session namespace.
- Extreme `long` starting stacks were not exercised; the claim was probed with realistic chip values
  (10–1000). This is outside the claim's poker range and is not reported as a counterexample.
- Stale source XML comments (M4 above) and the stale head-up odd-chip sentence in
  `docs/tholdem/known-limits.md` lines 18–20 ("in legal play a showdown pot is always even") are
  documentation defects observed while checking the claims; they do not change any of the five
  verdicts except that the M5 compatibility gate is red for the reason stated.
