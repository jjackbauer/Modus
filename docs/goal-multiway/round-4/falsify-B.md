# Round 4 — Falsifier B: M1–M5

Repository `C:\Users\ricar\dev\Modus`, branch `WIP`; the Tholdem tree is untracked. Every verdict below was re-derived from the current tree in this round. No product, test, documentation or configuration file was edited; the only file written is this report, and scratch runs live under `.tmp/falsify-b/`. `dotnet test` was not run (local `Win32Exception (5)` limit); the supplementary runner `.tmp/tholdem-verify/RunXunit` over the compiled Release test assembly, the Release CLI, the running viewer at `http://127.0.0.1:5099`, and a scratch F# verification script were used.

Method:

- `.tmp/falsify-b/verify.fsx` (run with `dotnet fsi`) independently re-implements the side-pot layering, tie split/odd-chip walk and a 5/7-card hand evaluator. It generates random 2–6-seat hands through the public engine, reconstructs every seat's contribution from the recorded `HandRecord` actions and starting stacks (not from `TableState.TotalCommitted` or `Pots`), and compares every layer amount, every winner set, final stacks, winners, `potAwarded` and replay.
- The Release CLI `WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe` was used for the oracle, guard matrix, session determinism and replay.
- The viewer at `127.0.0.1:5099` was used for the 4-seat live hash comparison; its running `Wip.Tholdem.Web.dll` SHA-256 was `0557ff2b3791ebdcda57abea6675d120fabf67514623307bc4cec7476eaa6661`, byte-identical to the current `WIP\Wip.Tholdem.Web\bin\Release\net10.0\Wip.Tholdem.Web.dll`.
- The F# scratch probe was created and run from the built `Wip.Tholdem.dll` only; no product code or test code was changed.

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| M1 | Supported | `HoldemHandEngine.ResolveShowdown` layers on ascending distinct `TotalCommitted` levels over all contributors, filters folded seats from eligibility, returns the top layer to its only eligible contributor, and gives odd chips by `ClockwiseFromButton`. My independent calculator compared 400 random hands (seats 2–6, per-seat stacks 10–3000, six policies): `hands=400 apply_calls=14280 multi_layer_hands=264 hands_with_folds=159 multiwinner_layers=39 odd_chip_layers=7`, `RESULT=SUPPORTED discrepancies=0`. Hand-computed cases matched exactly: 3-seat `[1000,600,300]` board-plays seed 8861 → layers `[15,590,300]`, winners `[1,2]/[1,2]/[1]`, final `[995,603,302]` (odd chip of 15 to seat 1, top 300 returned to seat 1); 4-seat `[1000,300,200,100]` all-in → `[400,300,200,700]`; 5-seat `[1000,1000,300,600,1000]` board-plays seed 9466, folded seat 1 with 20 dead → layers `[80,840,600,400]`, winners `[0,2,3]/[0,2,3]/[0,3]/[0]`, final `[1006,980,307,607,1000]` (80 split 26 + two odd chips to seats 2 and 3, skipping folded seat 1); 6-seat `[1000,1000,600,600,600,600]` → `[3600,800]`. | Wrote my own randomized 2–6-seat hands with unequal per-seat stacks, all-ins, folds, check/call, min-raise and random-raise policies; independently reconstructed every contribution, tie and odd chip; added hand-computed folded-dead-money, uncalled-excess, three-way-tie and two-odd-chip cases. | No discrepancy: every engine layer, winner set, final stack, `potAwarded` and replay result matched the independent calculator and the hand-computed cases. |
| M2 | Supported | Conservation was checked after `Start` and every `Apply` in the 400 random M1 hands (14,280 `Apply` calls) and 305 adversarial hands (all seats at the 10-chip minimum; `[10,17,24,...]`; `[100000,99000,...]`; all-in, fold-heavy, passive and chaos policies), all seats 2–6: `M2 adversarial: hands=305 apply_calls=15875` (1,595 of them in this loop), zero violations; every hand reached `IsComplete` within the 1,000-action cap and replay reproduced the final stacks. | Tried to break the invariant with all-in/fold sequences, unequal stacks at and above the big-blind floor, and short all-in raises; also asserted the equality after the hand completed (`Pot == 0`). | `sum(Stacks) + Pot == sum(StartingStacks)` held after all 15,875 observed `Apply` calls (14,280 + 1,595) and every hand terminated. |
| M3 | Supported | (a) Two CLI runs, seed 2026, 4 seats, agents `random-legal,tight-aggressive,pot-odds-equity,random-legal`, stacks `1000,800,600,400`, 30 hands: 31/31 files byte-identical, session `tholdem-1484d8666c543e18`, manifest `2908dfa1e9c4d38bb509cc43292edebc75dd194f3932ad7d89b04cf43b0aa6c0` both times. (b) The direct simulator (`MatchSimulator.Run` via `.tmp/tholdem-verify/MultiwaySimProbe`, rebuilt with `dotnet run` against the current projects) produced the same 31 files byte-for-byte and the same manifest. (c) 2-seat `SimProbe session` (seed 1234, 3 hands, two `random-legal` agents) and the CLI produced 4/4 identical files, manifest `a9f8b2af23b3a2e07ff42beffe16259c9f96516a3cb6eab81a597aedb9b004e1`. (d) A 4-seat live match on 5099 (`POST /api/live` seed 2026, 30 hands, stacks `1000,800,600,400`) reached `status=complete` with 30 hashes, all 30 equal to the simulator files and to the CLI manifest's `handHashes` (0/30 differences). (e) Seed 2027 produced session `tholdem-9265d20d6d27e4bf` and manifest `a0dfb7f9b1e384e464b435ff82c1c79cee574aa60bf438dffc510c93e52a5776`; all 31 files differed. | Ran the CLI twice with the identical seed and compared every byte; compared the CLI to a fresh simulator run (2 and 4 seats); compared a live 4-seat match's per-hand hashes to the simulator's files; ran a different seed and compared every file. | Same seed/agents/seats gave byte-identical session files from CLI and simulator and identical live hashes; a different seed changed all 31 files. |
| M4 | Supported | `PlayerObservation` exposes only the acting seat's own hole cards plus public state, and `AgentTurnContext` is exactly `(HandNumber, Seat, ActionIndex)`; `IPokerAgent.ActAsync` receives only those two values. A live scan of 1,786 observations over 2–6-seat hands (every state, every seat) found `hidden_card_leaks=0` and `seed_or_deck_leaks=0`; every card string in each observation was in `own hole ∪ current board`, and the public property list contains no `Seed`/`Deck`/`OpponentHole`. `SessionHandView` and the hand-history tool carry no seed/deck/opponent cards. The documented seed-recovery attack was reproduced: enumeration of session seeds `0..20000` against `Qs,4s` returned `matches=11 first=42 deck=Qs Tc 4s Jd 8h 8s 8d Th 6c`, exactly the limitation stated in `docs/tholdem/known-limits.md` and `docs/tholdem/engine-rules.md`. CLI: seed 42 and 1099511627775 → exit 2 with `seed <n> is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos`; 1099511627776 → exit 0 (session `tholdem-88f28b70086c22cf`); 42 with `--allow-weak-seed` → exit 0; `train --seed 7` and `evaluate --seed 42` → exit 2. HTTP: missing seed, 42, boundary−1, string `"42"`, `allowWeakSeed:false` → 400 `{"error":"weak-seed","minimum":1099511627776}`; `allowWeakSeed:1`/seed `-1` → 400 (binding); empty body → 400 `missing-body`; seed 2^40 → 201; seed 42 with `allowWeakSeed:true` → 201. | Attempted the seed-recovery attack and tried to find seed/deck/undealt/foreign cards in observations and the agent surface; tested every weak-seed boundary/transport/opt-out combination at CLI and HTTP. | No hidden data was found in an agent input, and the weak-seed guard refused every non-opted-in form at both entry points; the inference limitation is stated in the docs, not denied. |
| M5 | Falsified | The oracle passes: `play --seed 42 --hands 20 --allow-weak-seed` (also with explicit `--seats 2 --agents tight-aggressive,random-legal`) printed `session: tholdem-06795fbeacfe528b` and `manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618`, exit 0, 20 hand files. All 30 recorded v1 hand files under `artifacts/tholdem/sessions` replay with exit 0 (and all 33 including the 3 v2 files). But the full suite is red: `TOTAL 273 PASSED 272 FAILED 1 SKIPPED 0`, exit 1, with `FAIL MultiwayHistoryTests.Parse_GivenEveryRecordedV1HandFile_ExpectedAllHashesVerifyAndAllReplay: EqualException: Assert.Equal() Failure: Values differ Expected: 2 Actual: 4`. Cause: `artifacts/tholdem/sessions/tholdem-d56ac5f4637e44ef/hand-00001.json` (and hand-00002/3) is `tholdem.hand.v2` with `"seats":4`; the test globs every `hand-*.json` under `artifacts/tholdem/sessions` and asserts `record.SeatCount == 2` at `MultiwayHistoryTests.cs:184`. `docs/plan/test-waivers.json` expects `Wip.Tholdem.Tests: 273` and lists no Tholdem entry in `failures` or `knownAborted`, so the failure is unwaived. Test scan: 245 `[Fact]` + 8 `[Theory]`/28 `[InlineData]` = 273 executed, 0 `Skip=`, 0 `Assert.Skip`, 0 `#if`; the only pre-existing test change is the round-1 `PlayerObservationTests` reflection pin extended with `Seat`/`SeatCount`/`Seats` (listed as required, not a weakening) and the architecture count 43→48 with new Tholdem rules. | Ran the full suite twice and the class isolated; replayed every recorded hand file, v1 and v2; checked the waiver register, skip markers and the pre-existing-test diff; tried to find a v1 replay failure (none). | The v1/oracle clauses hold, but the compatibility gate is not green in the current tree: the recorded-artifact v1 sweep fails on the v2 4-seat session, the suite is 272/273, and the waiver register expects 273 with no Tholdem failure. The claim's compatibility assertions are therefore not all satisfied as delivered. |

## M1 — side pots, detailed observation

The engine implementation is `WIP/Wip.Tholdem/Engine/HoldemHandEngine.cs:436-538` (`ResolveShowdown`), with the odd-chip walk at `:888-899`. The independent F# calculator (`calculate` in `.tmp/falsify-b/verify.fsx`) rebuilds blinds, street commitments, calls, bets/raises and all-ins from `HandRecord.Actions`, `StartingStacks`, `SmallBlind`, `BigBlind` and `ButtonSeat`; it builds layers from its own reconstructed contributions, filters folded seats, evaluates each eligible hand with its own 5/7-card evaluator, splits ties with the same integer rule and walks seats clockwise from the button for odd chips. It never reads `TableState.Pots` or `TableState.TotalCommitted`.

Every non-final action's reconstructed pot was also compared with the recorded `PotAfter`; no mismatch occurred, which validates the contribution reconstruction independently of the award check.

The output quoted in the table:

```text
M1 randomized: hands=400 apply_calls=14280 multi_layer_hands=264 hands_with_folds=159 multiwinner_layers=39 odd_chip_layers=7 elapsed_ms=6763
M1 hand-computed 3-seat (seed=8861): layers=[|15L; 590L; 300L|] final=[|995L; 603L; 302L|] calculator_layers=[15L; 590L; 300L] odds_to_seat1=8
M1 hand-computed 4-seat: engine_layers=[|400L; 300L; 200L; 700L|] calculator_layers=[400L; 300L; 200L; 700L] final=[|1600L; 0L; 0L; 0L|]
M1 hand-computed 5-seat odd chips (seed=9466): layers=[|80L; 840L; 600L; 400L|] winners=[|[|0; 2; 3|]; [|0; 2; 3|]; [|0; 3|]; [|0|]|] final=[|1006L; 980L; 307L; 607L; 1000L|] calculator_layers=[80L; 840L; 600L; 400L]
M1 hand-computed 6-seat: engine_layers=[|3600L; 800L|] calculator_layers=[3600L; 800L] final=[|4400L; 0L; 0L; 0L; 0L; 0L|]
RESULT=SUPPORTED discrepancies=0
```

Hand calculation for the 5-seat case (button 0: SB 5, BB 10; live seats 0/2/3, folded seat 1 with 20 dead, folded seat 4 with 0): commitments `[1000,20,300,600,0]`; levels `20/300/600/1000`; layers `20*4=80`, `(300−20)*3=840`, `(600−300)*2=600`, `(1000−600)*1=400`. Board is four of a kind plus an ace, so seats 0/2/3 tie every layer. `80/3 = 26` each with remainder 2; walking clockwise from button 0 skips folded seat 1 and gives one extra chip to seat 2 and one to seat 3. Final stacks: seat 0 `26+280+300+400=1006`, seat 2 `27+280=307`, seat 3 `27+280+300=607`, folded seat 1 `1000−20=980`, folded seat 4 `1000`. The engine produced exactly these values.

No counterexample was found. Verdict: **Supported**.

## M2 — conservation and termination, detailed observation

The scratch probe checked `sum(state.Stacks) + state.Pot` against `state.StartingStacks.Sum()` immediately after `Start` and after every successful `Apply`; it also checked the final `Pot == 0` and re-derived the final stacks from each hand record. The 400 random hands and 305 adversarial hands covered seats 2–6, equal and unequal per-seat stacks (including exactly the 10-chip big blind and 100000/99000 stacks), all-in, fold-heavy, passive check/call, min-raise and random-legal policies. All hands completed within the 1,000-action cap; no conservation violation was recorded. Observed line:

```text
M2 adversarial: hands=305 apply_calls=15875 elapsed_ms=2802
```

(the `apply_calls` counter is cumulative after the 14,280 calls of the M1 loop; the adversarial loop itself contributed 1,595). Verdict: **Supported**.

## M3 — determinism, detailed observation

- Two CLI runs at seed 2026 produced identical `session.json` and 30 hand files (31/31, 0 SHA-256 differences); the manifest was `2908dfa1…`.
- The fresh simulator probe (built with `dotnet run --project .tmp/tholdem-verify/MultiwaySimProbe -c Release`) produced 31 files with 0 differences against the CLI session directory at the same seed/agents/stacks.
- The 2-seat probe (seed 1234, 3 hands, two random-legal agents) produced 4/4 identical files against the CLI; manifest `a9f8b2af…`.
- The 4-seat live match (`id d9d314161636f4b9`) returned 30 `handHashes`; all 30 matched the CLI/simulator files: `manifest_vs_live_diffs=0 manifest_vs_files=0 live_vs_files=0`.
- Seed 2027 produced different session/manifest and 31/31 different files.

No counterexample was found. Verdict: **Supported**.

## M4 — hidden information, detailed observation

Source checks: `WIP/Wip.Tholdem.Agents/IPokerAgent.cs` exposes only `ActAsync(PlayerObservation, AgentTurnContext, CancellationToken)`; `AgentTurnContext.cs` is `(int HandNumber, int Seat, int ActionIndex)`; `PlayerObservation.cs` has only own hole cards and public state; `WIP/Wip.Tholdem.Agents/Runtime/SessionHandView.cs` has board/own cards/actions/results only; `MatchSimulator` and `LiveMatch` pass only the observation and context to agents. The running observation scan covered every state and every seat of 12 hands per seat count 2–6.

The seed-recovery attack is a deliberate documentation check, not an agent-input leak: it uses the observer's own cards and the deterministic deal. Its output exactly matches `docs/tholdem/known-limits.md:50-56`: 11 candidate seeds, first 42, deck `Qs Tc 4s Jd 8h 8s 8d Th 6c`; the docs also state that opting out is outside the secrecy guarantee and that weak seeds are recoverable by enumeration (`known-limits.md:33-70`; `engine-rules.md:252-276`). The guard matrix is quoted in the table. Verdict: **Supported**.

## M5 — compatibility, detailed observation

The oracle and v1 replay clauses pass. The failing clause is the recorded-artifact compatibility sweep:

```text
FAIL MultiwayHistoryTests.Parse_GivenEveryRecordedV1HandFile_ExpectedAllHashesVerifyAndAllReplay: EqualException:
Assert.Equal() Failure: Values differ
Expected: 2
Actual:   4
TOTAL 273 PASSED 272 FAILED 1 SKIPPED 0
```

The same result was observed twice in full-suite runs; the isolated class run was `TOTAL 8 PASSED 7 FAILED 1`. The 4-seat v2 session under `artifacts/tholdem/sessions/tholdem-d56ac5f4637e44ef` was already present before this round (file creation 2026-11-07 16:55:01, hand mtime 16:53:57); it was not produced by this falsifier. `docs/plan/test-waivers.json` line 21 sets `"Wip.Tholdem.Tests": 273` and the register contains no Tholdem failure/abort entry, so the red test is unwaived. The 30 v1 files and the 3 v2 files all replay with exit 0, so the failure is specifically the recorded-artifact scope of the compatibility sweep, not a v1-format regression. The failing test is from the multiway round, not one of the 194 pre-multiway facts; no pre-multiway fact was skipped or weakened. The gap is the current compatibility gate itself: the recorded-artifact v1 sweep and the waiver expected total are no longer green.

The test-integrity check found no skipped or weakened test marker (245 facts, 8 theories, 28 inline data, 0 `Skip=`, 0 `Assert.Skip`, 0 `#if`); `git diff` of the tracked test files changes only the architecture count 43→48 and adds Tholdem architecture rules. The `PlayerObservationTests` property pin extension was listed as required in round 1. Verdict: **Falsified** because the compatibility gate that the claim relies on is red and unwaived in the current tree.

## Limits of this falsification

- `dotnet test` was not run (local `Win32Exception (5)`); the supplementary xUnit runner over the same compiled assembly was used.
- CI was not triggered or observed; no CI result is claimed.
- The viewer JavaScript was not executed in a browser; the 4-seat live evidence is the real HTTP API and the per-hand hashes, not a browser rendering.