# Falsify C — round 4 (side pots, conservation, determinism, hidden information, compatibility)

- **Falsifier**: C (independent)
- **Repository**: `C:\Users\ricar\dev\Modus`, branch `WIP`, current tree (untracked `WIP/` and `docs/` included)
- **Time**: 2026-11-07, after 17:00 America/Sao_Paulo
- **Method**: re-derived every claim from the current tree, not from prior-round documents. Built the
  Release tree, ran the compiled xUnit suite through the supplementary runner, ran the Release CLI,
  drove the live viewer on `http://127.0.0.1:5099`, and wrote an independent probe under
  `.tmp/round4-falsify-c/Probe/` with its own side-pot calculator, its own 7-card evaluator, its own
  deterministic random driver and a recording agent.
- **Limits carried**: `dotnet test` is unusable here (Win32Exception 5), so the supplementary
  `.tmp/tholdem-verify/RunXunit` runner over the compiled test assembly was used; `%TEMP%` was not
  used for scratch; the viewer was already running on 5099 from a copied output, not from
  `bin/Release`.

## Verdicts

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| M1 | Supported | **Randomized property with a from-scratch calculator**: `.tmp/round4-falsify-c/Probe` ran 3,260 hands over seats 2–6 (all five seat counts, buttons 0..seats-1, mixed/all-in/max-raise/fold/min-raise modes plus tiny and extreme per-seat stacks), 2,721 showdowns, 539 fold endings, 2,539 layered-pot hands and 47 odd-chip layers. Every completed hand matched an independent calculator on layer count, every layer amount, every winner set, `AwardedPot` and every final stack; folded seats never appeared in `Pots` or `Winners`; `CHECKS=2925 FAILURES=0`. **Hand-computed cases** (board-plays seed 8861, board `Ac 5s 5c 5h 5d`): 3 seats [1000,600,300] all-in gives layers 900/600/400 and final stacks 1000/600/300 (the 400 top layer is seat 0's uncalled excess); the same board with the small blind folded gives layers 15/590/300 and final 995/603/302 (15 splits 7+7 with the odd chip to seat 1, the first eligible winner clockwise from button 0); button 1 gives layers 15/10, final 1002/595/303 (odd chip to seat 2, the first eligible winner clockwise from button 1); 4 seats [1000,300,200,100] gives 400/300/200/700 and 1600 chips conserved; 6 seats gives 3600/800 and 4400 chips conserved; 2 unequal seats gives 1200/400. All six cases passed `CompareWithReference`. | Tried to force a mismatch with 2–6 seats, per-seat stacks from 10 to 100000, every button seat, tiny stacks where a blind is all-in, folded dead money in the lowest layer, four-way and six-way all-ins, and odd-chip layers. Tried to make a folded seat the unique top contributor (which would make a pot layer ineligible). No engine/calculator disagreement and no exception was observed. | The engine's layering, eligibility, tie split, odd-chip order and uncalled-excess settlement matched my independent implementation on every randomized and hand-computed case. Two scope notes, not counterexamples: the uncalled excess is represented as a one-winner top layer (`Pots[3]=700` in the 4-seat case) rather than excluded from `AwardedPot`, and the final stacks are the observable that matches a return; `docs/tholdem/known-limits.md` calls the odd-chip split “defensive”, which is strictly true for the two-seat `SplitPot` helper but the legal multiway case above really does produce a 15-chip odd layer. |
| M2 | Supported | **Conservation after every Apply**: the probe drove 2,700 hands over seats 2–6 with unequal per-seat stacks in five modes (mixed, all-in, max-raise, fold, min-raise); after every single `Apply` it checked `sum(Stacks)+Pot == sum(StartingStacks)`, no negative stack, `Pot == sum(TotalCommitted)` while live, and a 3000-action termination cap. 2,154 showdowns and 546 fold endings, `CHECKS=2803 FAILURES=0`; every hand completed with `Pot==0` and `sum(Stacks)==sum(StartingStacks)`. **Targeted adversarial excess**: seat 0 shoves 1000 over short all-ins of 100 in 2, 3, 4 and 6-seat tables; each produced two layers (short main pot plus a 900-chip top layer awarded only to seat 0), no exception, conservation after every action. `VerifyAdversarialUncalledExcess` ran the same for 2/3/4/6 seats. | Tried all-in wars, min-raise chains, fold-after-raise dead money, short all-in raises below the minimum, stacks of exactly 10/11, and one 100000-stack seat against 10-chip seats. Tried to find a non-terminating or chip-creating/destroying legal sequence; none terminated late, threw, or changed the total. | Every legal transition the probe produced preserved the chip total and the hand always reached `IsComplete` within the cap, including the all-in and fold adversarial shapes required by the claim. |
| M3 | Supported | **Simulator vs CLI vs live**: `.tmp/round4-falsify-c/Probe` ran `MatchSimulator.Run` twice with seed 424242, 4 agents (`random-legal, tight-aggressive, pot-odds-equity, random-legal`), 3 hands and starting stacks 1000/800/600/400; both runs produced session `tholdem-555b713f43864d14`, manifest `68690367f9d5b944816fad29554099f3e32bf75952b934203d8d334413e9cd2e` and byte-identical `session.json` plus three hand files (`DirectoriesEqual` true). The Release CLI (`play --seats 4 --agents … --starting-stacks 1000,800,600,400 --seed 424242 --hands 3 --allow-weak-seed`) printed the identical session and manifest; every one of the four CLI/simulator files had the same SHA-256. A second CLI run was byte-identical; seed 424243 produced session `tholdem-5551836e24df9bc7`, manifest `47415d9f27623322a098639247977bf190f591f72bf6da26d1842b7e1265e5c5` and different bytes in every file. **Live**: `POST /api/live` with the same configuration returned 201 and, when polled to `status=complete`, `handHashes=[74b77c…, 34742c…, 7abce3…]`, exactly the simulator manifest hashes; the same test with equal starting stacks also matched (`[1c7dcb…, 2d050c…, 441b4c…]`). | Tried reruns into fresh directories, simulator output vs CLI output, equal and unequal stacks, a live HTTP match, and a different seed. Also observed that the equal-stack and unequal-stack runs share session id `tholdem-555b713f43864d14` while their manifests differ (`132ac7…` vs `6869…`), because `ComputeSessionId` hashes seed, hands and agent names but not starting stacks. | For the same full configuration, simulator, CLI and live match are byte-identical per hand and the different seed differs, exactly as claimed. The session-id collision is a real session-identity gap (two configurations can overwrite the same `sessions/<id>/` directory) but it is not a determinism failure for the same configuration, which is what the second clause scopes with “for the same configuration”. |
| M4 | Supported | **Observation surface**: a recording `IPokerAgent` captured every `PlayerObservation.ToCanonicalJson` for every seat of 2–6 seat tables, two hands each (`.tmp/round4-falsify-c/m4/seats-N`). All captured inputs contained no `seed`, `deck`, `undealt`, `holeCards`, `holeCardsSeat0` or `holeCardsSeat1` key, no session-seed decimal `424242`, and no card belonging to another seat; the 543 observations/checks all passed (`FAILURES=0`). **Seed-recovery attack**: using only the seat-0 observation of `HoldemHandEngine.Start(42, 2)` (`own=7d,9c`), enumerating seeds 0..20000 through public `SeededDeck.Create` produced 10 matches, the first `42`; the recovered deck `7d,Qc,9c,4s,8c,7h,2s,3d,5c` reproduces the opponent `Qc,4s` and the board, confirming the documented weak-seed limitation. **Guards**: CLI `play --seed 42` exit 2 with `seed 42 is below the minimum 1099511627776`, `play --seed 1099511627775` exit 2, `play --seed 1099511627776` exit 0, `play --seed 42 --allow-weak-seed` exit 0, `train --iterations 1 --seed 7` exit 2, `train … --allow-weak-seed` exit 0, `evaluate --challenger does-not-exist --seeds 1 --seed 42` exit 2 with the seed message (it reaches the artifact check only with a strong seed). HTTP on 5099: seeds 42, 1099511627775 and string `"42"` without the opt-in returned 400 `{"error":"weak-seed","minimum":1099511627776}`; `allowWeakSeed:true` returned 201; seed 1099511627776 returned 201; `allowWeakSeed:false` returned 400. Docs: `docs/tholdem/known-limits.md` §Threat model states the enumeration attack, the 11-match/first-42 reproduction, the 2^40 guard and “opting out is outside the secrecy guarantee”, and `engine-rules.md` §Observation boundary repeats that the agent surface contains no seed and that a guessable seed lets an observer reconstruct the deck. | Tried to find hidden data in the observation JSON, in every other seat's cards, in the seed decimal, in the minimum-seed decimal, and in the runtime agent's prompt/context; tried weak seeds through play, train and evaluate, and through `POST /api/live` including the boundary value, a decimal string, the opt-in and `allowWeakSeed:false`. The only “successful attack” is the documented weak-seed enumeration, which the docs do not deny. | Agent inputs are clean, the entry-point guard refuses weak seeds unless explicitly opted out, HTTP does the same, and the documentation states the inference limitation rather than denying it. The runtime agent (`TholdemRuntimeAgent.ActAsync`) passes only `observation.ToCanonicalJson()` and hand/seat/action coordinates into the model task; the context carries no seed. |
| M5 | Falsified | **Oracle passes**: Release CLI `play --seed 42 --hands 20 --allow-weak-seed` printed `session: tholdem-06795fbeacfe528b`, `manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618`, exit 0; the 20 hand files replay with the manifest anchor (samples hand-00001, hand-00010, hand-00020 exit 0). **V1 replay passes**: 30 v1 hand files under `artifacts/tholdem/sessions` were replayed with the Release CLI, 0 failures; the 3 v2 files also replay exit 0. **Suite fails**: `dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` printed `FAIL MultiwayHistoryTests.Parse_GivenEveryRecordedV1HandFile_ExpectedAllHashesVerifyAndAllReplay: EqualException … Expected: 2 Actual: 4` and `TOTAL 273 PASSED 272 FAILED 1 SKIPPED 0` with exit 1 (the same 272/273 result was observed on two full runs); the isolated run (`… Wip.Tholdem.Tests.dll MultiwayHistoryTests`) printed `TOTAL 8 PASSED 7 FAILED 1`. The cause is in the current artifact set: `artifacts/tholdem/sessions/tholdem-d56ac5f4637e44ef/hand-00001.json` is `{"schema":"tholdem.hand.v2", …, "seats":4, …}` (written 2026-11-07 16:53:57), while the test globs every `hand-*.json` under `artifacts/tholdem/sessions` and asserts `record.SeatCount == 2` at `MultiwayHistoryTests.cs:184`. `docs/plan/test-waivers.json` records `"Wip.Tholdem.Tests": 273` under `expectedTotals` and has no Tholdem entry under `failures` or `knownAborted`, so the offline gate treats this as an unlisted failure. Source scan: 245 `[Fact]`, 8 `[Theory]`, 28 `[InlineData]`, 273 executed, 0 `Skip=`, 0 `#if`; the only documented pre-multiway test change is the `PlayerObservationTests` reflection pin extended with `Seat`/`SeatCount`/`Seats` (round 1, listed and required), not a weakening; the round-1b changes are to multiway tests added in round 1 and the document records them as replacements with stronger assertions. | Tried to falsify the oracle, every v1 hand's parse/hash/replay, the skip/waiver checks, and the full suite in the current tree. The oracle, the 30 v1 replays and the no-skip scan held; the full suite did not. The round-3 green run (`TOTAL 273 PASSED 273`) predates the 4-seat v2 session now present in the recorded-artifacts directory, so the current tree cannot reproduce the claimed compatibility evidence. | The individual v1 compatibility facts hold, but the claim is a compatibility-and-test-integrity claim about the delivered tree, and the compatibility suite is red in that tree: an existing multiway test that sweeps the recorded hands fails because a v2 4-seat hand was added to `artifacts/tholdem/sessions` after the round-3 green run. The waiver register lists no such failure. A claim that the compatibility tests are intact and unweakened is therefore falsified as delivered, even though the two-seat oracle and every v1 file replay. |

## Reproduction detail

### Environment build

```
dotnet build WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj -c Release --nologo
Build succeeded. 0 Warning(s) 0 Error(s)
```

### M1 probe (excerpt)

```
== M1 randomized side pots (independent calculator) ==
== M1 edge stacks (tiny / extreme / every button) ==
hands=3260 showdowns=2721 folds=539 multiLayer=2539 oddChipLayers=47 seats=2,3,4,5,6
== M1 hand-computed scenarios ==
board-plays seed (3 seats)=8861
board=Ac,5s,5c,5h,5d seat0=Qs,Ah seat1=Qc,Jc seat2=6h,Ts
CHECKS=2925 FAILURES=0
```

### M2 probe (excerpt)

```
== M2 conservation and termination ==
hands=2700 showdowns=2154 folds=546
CHECKS=2803 FAILURES=0
```

### M3 files

```
simulator run A: session=tholdem-555b713f43864d14 manifest=68690367f9d5b944816fad29554099f3e32bf75952b934203d8d334413e9cd2e
simulator run B: same session, same manifest, every file byte-equal
CLI run A:       session=tholdem-555b713f43864d14 manifest=68690367… (same four files, same SHA-256)
CLI run B:       same session, same manifest, every file byte-equal
CLI seed 424243: session=tholdem-5551836e24df9bc7 manifest=47415d9f27623322a098639247977bf190f591f72bf6da26d1842b7e1265e5c5, all files differ
live:            status=complete handHashes=74b77ca1…,34742c8a…,7abce358… (equal to the simulator manifest)
```

### M4 guard (excerpt)

```
play --seed 42                         exit 2  tholdem: seed 42 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos
play --seed 1099511627775              exit 2  same message shape
play --seed 1099511627776              exit 0  session tholdem-88f28b70086c22cf
play --seed 42 --allow-weak-seed       exit 0  session tholdem-c3d4ff060ef47ae4
train --iterations 1 --seed 7          exit 2
train --iterations 1 --seed 7 --allow-weak-seed  exit 0
evaluate --seed 42                     exit 2  (seed check before artifact access)
POST /api/live seed 42                 -> 400 {"error":"weak-seed","minimum":1099511627776}
POST /api/live seed 42 allowWeakSeed   -> 201
POST /api/live seed 1099511627776      -> 201
seed-recovery: own=7d,9c candidates=20001 matches=10 first=42
recovered deck first nine=7d,Qc,9c,4s,8c,7h,2s,3d,5c
```

### M5 suite (excerpt)

```
FAIL MultiwayHistoryTests.Parse_GivenEveryRecordedV1HandFile_ExpectedAllHashesVerifyAndAllReplay:
  EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   4
TOTAL 273 PASSED 272 FAILED 1 SKIPPED 0
exit=1
```

Root cause file header:

```
artifacts/tholdem/sessions/tholdem-d56ac5f4637e44ef/hand-00001.json
{"schema":"tholdem.hand.v2","handNumber":1,"seed":13149409904026330690,…,"seats":4,…}
```

Timeline of the tree state: the round-3 implementation document (with its final `TOTAL 273 PASSED 273`
run) was last written at 16:51:10; the v2 4-seat session file was written at 16:53:57; the round-3
screenshot is 16:56:06. The current tree therefore contains an artifact created after the green run.

## Residuals recorded, not counted as claim falsifications

1. **M1 bookkeeping of uncalled excess.** The engine settles excess as a one-winner top pot layer
   (`Pots` includes 700 in the 4-seat case and `AwardedPot` is 1600), not as a pre-award return.
   Final stacks match the return and `docs/tholdem/known-limits.md` documents this as “the top pot
   layer”; this is an observable-model choice, not an award mismatch.
2. **M1/docs odd-chip sentence.** `docs/tholdem/known-limits.md` says a showdown pot is always even
   and the odd-chip branch is unreachable in legal play. That is true for the two-seat `SplitPot`
   helper but false for legal multiway layers: the hand-computed 3-seat folded-small-blind case has a
   15-chip main layer split 8/7. The engine's multiway odd-chip behaviour is correct; the docs
   sentence is too broad.
3. **M3 session identity.** `MatchSimulator.ComputeSessionId` hashes seed, hand count and agent names
   only. Equal-stack and unequal-stack 4-seat runs with the same seed/agents/hands both produce
   `tholdem-555b713f43864d14` while their manifests differ (`132ac7…` vs `6869…`). The CLI overwrites
   `sessions/<id>/`, so the first configuration's session is lost. This is a session-identity gap,
   not a determinism failure for a fixed configuration, but it is worth a separate fix decision.
4. **M4 library entry points.** The CLI and HTTP guards are enforced, but the public library APIs
   (`MatchSimulator.Run`, `LiveMatchRegistry.Start`, `SimulationOptions`) still accept a weak seed
   without an opt-in. `docs/tholdem/known-limits.md` scopes the guarantee to the operator entry
   points and explicitly places bypasses outside it, so this is documented, not a contradiction of
   M4.
5. **M5 suite state.** No test was skipped (0 `Skip=`), no `[Theory]` lacks data, and no source
   `#if` excludes code. The failure is an unlisted red test caused by the v2 artifact in the
   recorded-session directory; the waiver register expects 273 and lists no Tholdem failure, so the
   offline gate would fail until either the artifact set or the test's v1 filter is corrected. This
   is product/test hygiene, not something I repaired (I edited only this report).
