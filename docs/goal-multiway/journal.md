# Goal Journal — multiway

> Append-only. See the stretch-goal-harness evidence protocol for entry format.

---

<!-- New entries go below this line -->

### DEC-001: Generalize the engine in place; keep v1 histories replayable

- **Decision**: Introduce a general `HoldemHandEngine` for 2–6 seats and keep `HeadsUpHandEngine` as a thin 2-seat wrapper so the 194 existing tests and every recorded 2-seat hand keep working. `HandRecord` keeps its v1 canonical form for 2-seat hands (so old hashes verify) and adds a v2 form for 3+ seats; `HandRecordJson` auto-detects both.
- **Rationale**: The heads-up evidence (194 tests, recorded sessions, viewer data) is the regression suite for the generalization. A v2-only serialization would invalidate every stored hash and silently break `replay`, which the previous goal proved anchored to the manifest.
- **Alternatives considered**: (a) a parallel multiway engine — rejected, two rule implementations diverge; (b) replace heads-up and rewrite its 194 tests — rejected, it discards the strongest regression evidence.
- **Related**: goal.md M1/M3/M6

### DEC-002: Side pots are layered by contribution, awarded to eligible winners, with uncalled excess returned

- **Decision**: Track total contribution per seat. At showdown, build ascending contribution levels over non-folded seats; each layer is `(level - previousLevel) * seatsWithContributionAtLeastLevel`, awarded among non-folded seats with contribution at least that level by hand rank, split ties, odd chip to the first eligible seat clockwise from the button. Chips from folded seats stay in the layers but they are never eligible. The top layer naturally returns uncalled excess to the only eligible contributor.
- **Rationale**: This is the standard side-pot construction, it conserves chips by construction, and it is independently checkable with a brute-force calculator.
- **Alternatives considered**: iterative all-in events with pot merging — rejected, easier to get wrong at equal contribution levels.
- **Related**: goal.md M2

### TODO-001: Round 1 — N-seat engine, side pots, observation, history v2

- **Task**: Deliver the general engine with 2–6 seats and side pots, N-seat observation and history v2 while v1 replay stays byte-identical, with property tests against an independent pot calculator and the existing 194-test suite still green.
- **Priority**: P0
- **Depends on**: —
- **Status**: Done, with one reachability gap (RISK-001)
- **Related**: DEC-001, DEC-002, VAL-001

### VAL-001: Round-1 verification — engine generalizes correctly, but side pots are only reachable through a test seam

- **Target**: round-1 delivery (fc4fdd7d)
- **Method**: Re-ran the 241-case suite and the CLI oracle myself; read `HoldemHandEngine.Start`, the side-pot resolver and the new side-pot tests; checked the one changed existing test.
- **Evidence examined**: EVD-005, EVD-006; `WIP/Wip.Tholdem/Engine/HoldemHandEngine.cs:35-39,384-470`; `WIP/Wip.Tholdem.Tests/MultiwayHoldemEngineTests.cs:316-390`; `WIP/Wip.Tholdem.Tests/PlayerObservationTests.cs:58-95`
- **Result**: Supported on compatibility and structure. 241/241 tests pass; the 2-seat CLI manifest is byte-identical to the pre-change oracle; the observation test's pinned property list was extended with the new public members while keeping the forbidden-name and type checks (not weakened). The gap: `Start` takes a single `startingStack`, and the hand-crafted side-pot tests build the state with `CraftRiverState`, then call the internal `ResolveShowdown`. With equal stacks, legal play cannot produce unequal all-in levels, so side pots are effectively dead code for public users.
- **Conclusion**: The generalization is sound, but the headline feature is not reachable. Round 1b adds per-seat starting stacks, rewrites the side-pot tests to reach the pots through `Start` + real actions, and keeps the equal-stack path byte-identical.
- **Related**: EVD-006, RISK-001, TODO-002

### RISK-001: Side pots cannot arise in public play with equal starting stacks

- **Risk**: With one `startingStack` for every seat and matched betting, active seats always have equal remaining stacks, so no seat can be all-in for a different amount. Side pots would only exist in tests that synthesize internal state.
- **Likelihood**: Certain with the current API.
- **Impact**: High for the multiway objective — the feature would be documented but unreachable.
- **Evidence**: EVD-006
- **Mitigation**: Add per-seat starting stacks to `Start` (default equal, preserving the oracle), extend v2 history with `startingStacks`, and rewrite the side-pot tests to reach three- and four-way all-ins through real actions.
- **Gate**: —

### TODO-002: Round 1b — per-seat starting stacks and public side-pot tests

- **Task**: Add `IReadOnlyList<long>? startingStacks` to `HoldemHandEngine.Start`, carry it in `TableState`/`HandRecord` v2, rewrite the side-pot tests to use public `Start` + actions, and prove the equal-stack oracle is unchanged.
- **Priority**: P0
- **Depends on**: RISK-001
- **Status**: Done (verified)
- **Related**: RISK-001, VAL-001, VAL-002

### VAL-002: Round-1b verification — side pots reachable and correct

- **Target**: round-1b delivery (63413421)
- **Method**: Re-ran the 246-case suite and the CLI oracle; drove my own 3-seat unequal-stack hand through the public API with real all-in actions and printed the state after every action; grepped the side-pot tests for the retired seam.
- **Evidence examined**: EVD-007; `.tmp/tholdem-verify/MultiwayProbe`; `WIP/Wip.Tholdem.Tests/MultiwayHoldemEngineTests.cs`
- **Result**: Supported. The probe produced layers `900 / 600 / 400`, awarded 1900, final stacks `1900,0,0`, conservation at every step, and `sum(Pots)==AwardedPot`. The oracle is byte-identical and the tests no longer call `ResolveShowdown`/`CraftRiverState`.
- **Conclusion**: M2's construction and reachability are covered for this scenario; the randomized property test against the independent calculator (in the suite) covers the rest. The simulator is next so multiway becomes playable end to end.
- **Related**: EVD-007, RISK-001 (closed), TODO-003

### RISK-001 status

- **Status**: Closed (see VAL-002)

### TODO-003: Round 2 — N-seat simulator, agents, metrics

- **Task**: Generalize `MatchSimulator` to N agents (2–6) with per-seat stacks, per-seat metrics, byte-identical N-seat histories, v2 hand records and a seat-aware session manifest, keeping the 2-agent overload and all existing tests unchanged.
- **Priority**: P0
- **Depends on**: Round 1b (verified)
- **Status**: Done (verified)
- **Related**: VAL-002, VAL-003

### VAL-003: Round-2 verification — N-seat simulation reproduces and sums to zero

- **Target**: round-2 delivery (c8cb55af)
- **Method**: Re-ran the 257-case suite and the CLI oracle; wrote and ran my own 4-seat simulation probe (4 different agents, per-seat stacks, 30 hands) twice into separate directories and compared every file; read the manifest and the per-seat metrics.
- **Evidence examined**: EVD-008, EVD-009; `.tmp/tholdem-verify/MultiwaySimProbe`
- **Result**: Supported. Two runs produced 31 byte-identical files and the same manifest; per-seat net chips sum to zero; the manifest carries `seats`, `startingStacks`, four agent names and 30 hand hashes; the 2-seat CLI oracle is unchanged; 257/257 tests pass.
- **Conclusion**: M4 is covered. The viewer and CLI still assume two seats; round 3 generalizes both, and the learning loop stays heads-up by documentation.
- **Related**: EVD-008, EVD-009, TODO-004

### TODO-004: Round 3 — N-seat viewer, CLI `--seats`, docs

- **Task**: Generalize the live API, spectator snapshot, replay rendering and viewer UI to 2–6 seats; add CLI `--seats`/`--agents`/`--starting-stacks`; update docs and waivers.
- **Priority**: P0
- **Depends on**: Round 2 (verified)
- **Status**: Done (verified)
- **Related**: VAL-003, VAL-004

### VAL-004: Round-3 verification — multiway is playable and watchable

- **Target**: round-3 delivery (b2bffcf8)
- **Method**: Re-ran the 273-case suite and the 2-seat oracle; ran the 4-seat CLI twice and replayed a v2 hand; restarted the viewer from a copied build, drove a 4-seat live match over HTTP, inspected the snapshot, and captured the table in headless Chromium.
- **Evidence examined**: EVD-010, EVD-011; `docs/goal-multiway/round-3/four-seat-live.png`
- **Result**: Supported. 273/273 tests; 4-seat CLI deterministic and replayable; the live snapshot carries four seats with per-seat cards and winners; the screenshot shows a real four-handed table with side-pot-relevant state, seat names, dealer and winner badges, and zero console errors. The 2-seat oracle is unchanged.
- **Conclusion**: M5 is covered. The remaining work is the adversarial gate (M8) and the final integration sweep (M6/M7).
- **Related**: EVD-010, EVD-011, TODO-005

### TODO-005: Round 4 — adversarial gate and final integration

- **Task**: Three independent falsifiers on side-pot correctness, conservation, N-seat determinism, hidden information and 2-seat compatibility; then final build/architecture/coverage/waiver sweep and the report.
- **Priority**: P0
- **Depends on**: Round 3 (verified)
- **Status**: Done — M1–M4 Supported 3/3; M5 Falsified 3/3
- **Related**: VAL-004, VAL-005

### VAL-005: Round-4 adversarial gate — four claims supported, compatibility falsified

- **Target**: multiway claims M1–M5
- **Method**: Three independent falsifiers attacked all five claims with their own calculators and probes; I read the M5 rows, reproduced the failing suite run, and inspected the test and the artifact directory.
- **Evidence examined**: `docs/goal-multiway/round-4/falsify-{A,B,C}.md`; `WIP/Wip.Tholdem.Tests/MultiwayHistoryTests.cs:163-190`; `artifacts/tholdem/sessions/tholdem-d56ac5f4637e44ef/hand-00001.json`
- **Result**: **M1 Supported 3/3** (independent calculators over 2,700–3,260 hands each, hand-computed cases, odd chips, folded dead money, uncalled excess), **M2 Supported 3/3** (conservation after every `Apply`, tens of thousands of actions), **M3 Supported 3/3** (simulator/CLI/live byte identity), **M4 Supported 3/3** (216+ captured observations, no seed/deck/other cards, guard intact), **M5 Falsified 3/3**: the suite is `TOTAL 273 PASSED 272 FAILED 1` in the current tree because `Parse_GivenEveryRecordedV1HandFile_ExpectedAllHashesVerifyAndAllReplay` globs every `hand-*.json` under `artifacts/tholdem/sessions` and asserts `SeatCount == 2`; the 4-seat v2 session I copied there for the viewer demo fails it (`Expected: 2 Actual: 4`), and no waiver covers it.
- **Conclusion**: The multiway logic is strong — three independent calculators agree. The compatibility claim fails on a test that is both too broad (assumes every recorded hand is v1) and environment-dependent (it reads gitignored operator data). The fix is to make the sweep schema-aware and self-contained, and to add the exact regression (a v2 hand in the artifacts root must not be treated as v1).
- **Related**: VAL-004, RISK-002, TODO-006

### RISK-002: Compatibility tests depend on gitignored operator data and assume v1

- **Risk**: Two history tests read `artifacts/tholdem/sessions` (gitignored, absent on a clean checkout) and one sweeps every `hand-*.json` asserting `SeatCount == 2`. A legitimate v2 multiway session in that directory turns the suite red, and on a clean checkout the tests cannot run at all (or assert against a missing directory).
- **Likelihood**: Certain — reproduced.
- **Impact**: High for test integrity: the suite is unwaived red in the delivered tree, and the compatibility evidence depends on files that are not in the repository.
- **Evidence**: `docs/goal-multiway/round-4/falsify-A.md` (M5); `MultiwayHistoryTests.cs:163-190`
- **Mitigation**: generate or embed v1 fixtures inside the test project; filter any artifact sweep by schema/seat count; add a regression that a v2 session in the artifacts root is skipped by the v1 sweep; keep the tests green on a clean checkout.
- **Gate**: G2

### TODO-006: Round 5 — schema-aware, self-contained history tests

- **Task**: Rewrite the artifact-dependent history tests to use generated/embedded v1 fixtures, make any sweep schema-aware, add the v2-in-artifacts regression, and restore a green, waiver-clean suite.
- **Priority**: P0 (G2)
- **Depends on**: RISK-002
- **Status**: Done (verified); M5 re-test in flight
- **Related**: RISK-002, VAL-005, VAL-006

### VAL-006: Round-5 verification — tests are self-contained and schema-aware

- **Target**: round-5 delivery (db78c429)
- **Method**: Re-ran the 274-case suite; checked the test sources for artifacts references; moved `artifacts/tholdem` aside, re-ran the suite, and restored it byte-identically (47 files before and after); read the new compatibility tests.
- **Evidence examined**: EVD-012; `WIP/Wip.Tholdem.Tests/HistorySessionCompatibilityTests.cs`; `WIP/Wip.Tholdem.Tests/HistorySessionFixture.cs`
- **Result**: Supported. 274/274 in both the normal tree and the clean-checkout simulation; zero test sources mention `artifacts/tholdem`; the sweep asserts v1 = 2 seats and v2 = 3..6 with both schemas present, and the regression covers a v2 hand in a sessions root.
- **Conclusion**: The compatibility test defect is fixed at the root (generated fixtures, schema-aware assertions) rather than waived. The remaining step is the independent M5 re-test, then the final integration sweep.
- **Related**: EVD-012, RISK-002 (closed), TODO-007

### RISK-002 status

- **Status**: Closed (see VAL-006)

### TODO-007: Round 6 — M5 re-falsification and final integration

- **Task**: Three fresh falsifiers re-test the repaired compatibility claim; then final build/architecture/coverage/waiver sweep and the completion report.
- **Priority**: P0
- **Depends on**: Round 5 (verified)
- **Status**: Re-falsification done: M5 Falsified 3/3 on the v2 seat rule; round 6 fix required
- **Related**: VAL-006, VAL-007

### VAL-007: M5 re-falsification — the v2 seat rule is wrong, not the product

- **Target**: repaired compatibility claim M5
- **Method**: Three fresh falsifiers tested every clause, including moving the artifacts tree aside and generating a 2-seat unequal-stack session with the Release CLI; I read all three reports and reproduced the counterexample path.
- **Evidence examined**: `docs/goal-multiway/round-5/refalsify-{A,B,C}.md`; `WIP/Wip.Tholdem.Tests/HistorySessionFixture.cs:147-156`; `HistorySessionCompatibilityTests.cs:70`
- **Result**: **M5 Falsified 3/3.** Clauses (a) oracle, (b) all recorded v1 replays, (c) self-containment and (d) green suite all pass. The schema clause fails: the product legitimately writes `tholdem.hand.v2` for a **2-seat hand with unequal starting stacks** (`HandRecordCanonicalWriter.cs:17-19`), `HandRecordJson` accepts seats 2..6, and a passing multiway test pins that shape — but the fixture asserts `v2 ⇒ SeatCount 3..6`, so such a hand in a sessions root throws `InRangeException` (`Range: (3 - 6) / Actual: 2`).
- **Conclusion**: My claim's premise "v2 means 3..6" is false; the correct rule is v1 ⇒ 2 equal-stack seats, v2 ⇒ 2..6 seats. The fix is a test/claim correction plus a 2-seat-v2 regression, and the claim must be restated before it can pass.
- **Related**: EVD-012, RISK-003, TODO-008

### RISK-003: The compatibility fixture encodes a false schema invariant

- **Risk**: `HistorySessionFixture` classifies v2 as 3–6 seats. A legitimate 2-seat unequal-stack v2 hand is therefore rejected by the shared verification path, which would make a real operator session fail the compatibility gate — the same failure mode as RISK-002, moved from v1 to v2.
- **Likelihood**: Certain (three independent reproductions).
- **Impact**: Medium for the product (no runtime defect), High for test correctness and the compatibility claim.
- **Evidence**: `HistorySessionFixture.cs:147-156`; `HistorySessionCompatibilityTests.cs:70`; falsifier counterexamples
- **Mitigation**: Change the rule to v1 ⇒ 2 seats and all starting stacks equal; v2 ⇒ 2..6 seats; add a generated 2-seat unequal-stack v2 case to the sweep and a dedicated regression.
- **Gate**: G2

### TODO-008: Round 6 — correct the schema invariant and add the 2-seat-v2 regression

- **Task**: Fix the fixture/compat tests to the true rule, add a 2-seat unequal-stack v2 hand to the generated sweep and a regression that it is accepted, update the waiver count, and restate the M5 claim.
- **Priority**: P0 (G2)
- **Depends on**: RISK-003
- **Status**: Done (verified); final M5 re-test in flight
- **Related**: RISK-003, VAL-007, VAL-008

### VAL-008: Round-6 verification — the corrected invariant is real

- **Target**: round-6 delivery (9fa10a0f)
- **Method**: Re-ran the 275-case suite; inspected the fixture rule and the three generated shapes; generated a 2-seat unequal-stack session with the Release CLI and replayed it; re-ran the clean-checkout proof with `artifacts/tholdem` aside.
- **Evidence examined**: EVD-013; `WIP/Wip.Tholdem.Tests/HistorySessionFixture.cs`; `.tmp/v2two/sessions/…/hand-00001.json`
- **Result**: Supported. The rule is now v1 = 2 seats + equal stacks, v2 = 2..6 with unequal stacks required for the 2-seat case; the fixture contains and verifies all three shapes; the CLI-produced 2-seat unequal v2 hand has the expected shape and replays; 275/275 in both the normal tree and the clean-checkout simulation.
- **Conclusion**: The claim wording is now aligned with the product. The independent M5 re-test is the last gate.
- **Related**: EVD-013, RISK-003 (closed), TODO-009

### RISK-003 status

- **Status**: Closed (see VAL-008)

### TODO-009: Round 7 — final M5 re-test and integration sweep

- **Task**: Three fresh falsifiers re-test the corrected M5; then final build/architecture/coverage/waiver sweep and the completion report.
- **Priority**: P0
- **Depends on**: Round 6 (verified)
- **Status**: Done — M5 Supported 3/3; gates green
- **Related**: VAL-008, VAL-009

### VAL-009: Final M5 re-test and integration sweep — Supported 3/3, gates green

- **Target**: corrected M5 and the multiway definition of done
- **Method**: Three fresh falsifiers re-tested M5 against the current tree; I re-ran the solution build, architecture, coverage, waiver and a docs scan myself; I confirmed there are no Tholdem waiver entries.
- **Evidence examined**: EVD-014, EVD-015; `docs/goal-multiway/round-6/refalsify-{A,B,C}.md`; `docs/plan/test-waivers.json`
- **Result**: **M5 Supported 3/3**, `gateG2: PASS`. Falsifiers independently confirmed the oracle, all recorded v1 replays with manifest anchors, the three-shape fixture (18/18), CLI-generated 2/3/5/6-seat v2 sessions through the shared fixture, rejection of a crafted equal-stack v2 2-seat file, 275/275 with `artifacts/` moved aside and restored hash-identically, and zero skip/waiver markers. My own sweep: build exit 0, architecture 4/4 at 48, coverage ok, waiver 275 with zero Tholdem failures/aborts, docs describe 2–6 seats and side pots.
- **Conclusion**: The multiway objective is delivered and independently gated. The residual limitations are the same as the heads-up slice (CI not observed, local VSTest abort, uncommitted tree, weak-seed inference documented and gated) plus the deliberate scope line that the learning loop remains heads-up. Multiway work was not committed and no release is claimed.
- **Related**: EVD-014, EVD-015, DEC-001, DEC-002
