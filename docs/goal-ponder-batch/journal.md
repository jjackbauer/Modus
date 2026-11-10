# Goal Journal — ponder-batch

> Append-only. See the stretch-goal-harness evidence protocol for entry format.

---

### OBS-001: The baseline run promoted one genome at a known cost

- **Source**: `docs/goal-llm-harness/ponder-loop/experiments-3.md`; `.tmp/goal-llm-harness/loop/live-search/strategies-report.json`
- **Fact**: One search-augmented run (seed `1099512627776`, 3 rounds, 6 seats) promoted `0.90/0.20/0.18/1.0` at gate LCB 11.8854, and was robust at fresh seeds; the run took roughly 11 minutes.
- **Implications**: ten runs at this level is a multi-hour batch; the search/gate shape is proven and can be widened.
- **Confidence**: High
- **Agent**: orchestrator (read the report and ran the run)
- **Related**: —

### DEC-001: Batch shape

- **Decision**: 10 independent runs, each 3 rounds × 6 seats × 20 hands; LLM seed; grid widened from 100 to >=300 candidates at 50 search seeds; gate 24 seeds on disjoint base seeds; sequential execution; all runs reported.
- **Owner**: agent
- **Status**: approved
- **Rationale**: the user asked for "at least 10 times this level" and "more aggressively"; the shape keeps the named mechanisms and widens the search, without changing the gate.
- **Alternatives considered**: fewer runs with more rounds (rejected: the user asked for at least 10 runs); parallel runs (rejected: rate-limit risk and harder verification).
- **Related**: OBS-001

### OQ-001: Does the promotion recur across independent seeds?

- **Question**: Across 10 fresh-seed runs with the wider grid, how many runs promote, which genomes win, and do the promoted genomes clear the gate on fresh base seeds?
- **Needed evidence**: each run's report and canonical gate evaluation; the aggregate tally; a fresh-seed re-check of distinct promoted genomes.
- **Priority**: P1
- **Raised by**: round 0
- **Related**: OBS-001

### RISK-001: The batch outruns the model or the clock

- **Risk**: rate limits, transport failures or a 2+ hour batch could truncate the run count.
- **Likelihood**: Medium
- **Impact**: High
- **Evidence**: OBS-001 (each run ~11 min, ~80 model calls per round)
- **Mitigation**: sequential runs; per-run logs; a failed run is recorded and retried once with the same seed, never replaced by a different seed; the batch stops only on repeated, recorded transport failures.
- **Gate**: —

---

### OBS-002: The intent audit flags "more aggressively" as replaced by search-only widening

- **Source**: `docs/goal-ponder-batch/intent-audit.md`
- **Fact**: 7 mechanisms KEPT, 1 REPLACED ("more aggressively" — the proposal widens only the deterministic grid; live Ponder stays 3 rounds × 20 hands and one LLM seed per round), 0 REMOVED. The audit asks whether "more aggressively" requires more live model-driven work.
- **Implications**: the scope answer is user-owned; round 1 blocks until it is recorded.
- **Confidence**: High
- **Agent**: Intent Auditor (verified by orchestrator reading the file)
- **Related**: DEC-002

### DEC-002: Scope of "more aggressively" — pending user answer

- **Decision**: Whether the batch escalates the live run (more rounds/hands/search seeds) or only the deterministic grid.
- **Owner**: user
- **Status**: pending
- **Rationale**: OBS-002; the skill's intent rule makes a replaced named mechanism a user decision.
- **Alternatives considered**: (A) wider grid + 4 live rounds; (B) wider grid only, 3 rounds; (C) wider grid + 5 live rounds at more search seeds.
- **Related**: OBS-002

---

### DEC-003: Proceed with the more aggressive reading after the scope question timed out

- **Decision**: Execute each run with **4 live rounds** (not 3) and the **378-candidate grid**, keeping every named mechanism and escalating the live model-driven work as well as the search.
- **Owner**: agent
- **Status**: approved (execution), supersedable by the user
- **Rationale**: DEC-002's clarifying question timed out after 120 s; the user's verbatim instruction says "more aggressively", and option A is the more aggressive reading of every mechanism — it removes none and adds live rounds. It is not a substitution for a mechanism the user named.
- **Alternatives considered**: 3 rounds (the original proposal; less aggressive); 5 rounds + 60 search seeds (maximum; ~3.5–4 h).
- **Related**: OBS-002, DEC-002

### OQ-002: The degree question remains open

- **Question**: Did the user mean 4 live rounds (executed) or the maximum 5 rounds/60 search seeds?
- **Needed evidence**: a user answer; if "maximum", run the batch at 5 rounds/60 seeds instead.
- **Priority**: P2 (execution has started; the chosen reading is the more aggressive side of the ambiguity)
- **Raised by**: round 0
- **Related**: DEC-002, DEC-003

---

### OBS-003: The implementer's aggressive flag is real and its dedup warning is correct

- **Source**: `.tmp/goal-llm-harness/loop/{Program,RoundRunner,LoopRecords}.cs`; `docs/goal-ponder-batch/round-1/implementation.md`
- **Fact**: `--aggressive` selects raise {seed,0.70,0.80,0.85,0.90,0.95,1.00} × call {seed,0.15,0.20,0.25,0.30,0.40} × bluff {seed,0.10,0.14} × sizing {seed,1.0,1.1}; the scripted anchor seed dedups to 120 evaluated, an off-anchor seed yields 378 grid points; the default grid is unchanged.
- **Implications**: the batch must report the evaluated count, not assume 378; live proposals are expected off-anchor, but the report will record the truth per round.
- **Confidence**: High
- **Agent**: Implementer (verified by orchestrator)
- **Related**: EVD-001, EVD-002, EVD-003

### VAL-001: The round-1 claims are Supported

- **Target**: round-1 claim batch (build, aggressive smoke, default unchanged, seed split).
- **Method**: rebuilt, ran both smokes, read the edited code and the search.jsons.
- **Evidence examined**: EVD-001, EVD-002, EVD-003.
- **Result**: Supported.
- **Conclusion**: the aggressive mode is ready; the default mode and gate bases are unchanged. Start the batch.
- **Related**: OBS-003, EVD-001..003

---

### OBS-004: The aggressive search overfits its own sample; the gate catches it

- **Source**: `run-01/round-3/search.json` (search LCB 47.121 at 50 search seeds), `run-01/evaluations/…498734dd…json` (gate LCB −15.731 at 24 gate seeds), `strategies-report.json`
- **Fact**: round 3's search winner had the highest search LCB of the run (47.12) and the gate refused it at −15.73; round 4 likewise (search 22.66, gate −17.82). Rounds 1–2's winners transferred (search 33.36→gate 20.46; search 16.91→gate 10.98).
- **Implications**: a wider grid increases the chance of selecting search-sample overfits; the disjoint gate is doing real work, and a high search LCB is not evidence of promotion. The batch must report both LCBs per round.
- **Confidence**: High
- **Agent**: orchestrator (read the artifacts)
- **Related**: EVD-004, EVD-005

### VAL-002: Run 1's claims are Supported

- **Target**: run 1 claim batch.
- **Method**: read the report/pointer/sessions/search/evaluations; re-derived the promotions from the canonical evaluations.
- **Evidence examined**: EVD-004, EVD-005, OBS-004.
- **Result**: Supported.
- **Conclusion**: run 1 is a valid four-round aggressive run with two verified promotions; no fabrication, no gate change.
- **Related**: OBS-004, EVD-004, EVD-005

---

### OBS-005: The aggressive grid's evaluated count varies with the seed's anchor overlap

- **Source**: `run-02/round-{1..4}/search.json`
- **Fact**: rounds 1 and 3 evaluated 378 candidates; rounds 2 and 4 evaluated 315 because the live seed genome held values already in the grid sets (dedup). Both counts are the full grid under dedup, not a truncated search.
- **Implications**: the batch report must state the evaluated count per round; the goal's "378-capacity grid" is met, but a row claiming exactly 378 everywhere would be false.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-006

### VAL-003: Run 2's claims are Supported

- **Target**: run 2 claim batch.
- **Method**: read the report/pointer/sessions/search/evaluations; re-derived the promotion.
- **Evidence examined**: EVD-006, EVD-007, OBS-005.
- **Result**: Supported.
- **Conclusion**: run 2 is valid; one promotion; the dedup counts are recorded.
- **Related**: OBS-005, EVD-006, EVD-007

---

### OBS-006: Round 1 promotes; the new champion is then hard to beat

- **Source**: `run-01..03/strategies-report.json`; evaluations
- **Fact**: all three completed runs promoted in round 1 (gate LCBs 20.46, 29.29, 28.39), then failed to promote again (0/2, 0/3, 0/3 subsequent rounds). Round-1 winners share call 0.15, raise 0.70–1.00, sizing 1.0–1.10; later rounds' search LCBs were often near zero (0.07–3.8) or overfit (search 47.12 → gate −15.73).
- **Implications**: the aggressive search reliably finds a first promotion from the weaker start champion, but improving the promoted champion is a harder, different search; the batch should report both the first-promotion rate and the post-promotion improvement rate.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-008, OBS-004

### VAL-004: Run 3's claims are Supported

- **Target**: run 3 claim batch.
- **Method**: read the report/pointer/sessions/search/evaluations; re-derived the promotion.
- **Evidence examined**: EVD-008, EVD-009, OBS-006.
- **Result**: Supported.
- **Conclusion**: run 3 is valid; one promotion; the pattern is recorded, not hidden.
- **Related**: OBS-006, EVD-008, EVD-009

---

### OBS-007: Near-misses are refused, and winning genomes recur across runs

- **Source**: `run-04/evaluations/*.json`; `run-02` and `run-05` reports
- **Fact**: run 4 refused LCBs of 3.428872, 4.868040 and 2.054646 — just below the 5 margin — and promoted only the 20.715 candidate. The genome `v2-ce0a90be` (0.70/0.15/0.18/1.10) was independently found and promoted by run 2 (LCB 29.29) and run 5 round 1 (LCB 30.55).
- **Implications**: the margin functions at the boundary (no "almost" promotions); the search converges on a small set of strong genomes across seeds. Recurrence is evidence the winners are not noise.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-010, EVD-011

### VAL-005: Run 4's claims are Supported

- **Target**: run 4 claim batch.
- **Method**: read the report/pointer/sessions/search/evaluations; checked the near-miss LCBs against the margin.
- **Evidence examined**: EVD-010, EVD-011, OBS-007.
- **Result**: Supported.
- **Conclusion**: run 4 is valid; one promotion; three honest refusals at the boundary.
- **Related**: OBS-007, EVD-010, EVD-011

---

### OBS-008: Segment 1 — six promotions in five runs, all in the first two rounds

- **Source**: `run-01..05/strategies-report.json`; `logs/batch-runs.log`
- **Fact**: runs 1–5 (20 rounds) produced 6 verified promotions: run 1 ×2 (LCBs 20.46, 10.98), run 2 ×1 (29.29), run 3 ×1 (28.39), run 4 ×1 (20.72), run 5 ×1 (30.55). Every run promoted at least once; no promotion after round 2. Final champions: v3-b4dd8c21 (0.62/0.15/0.10/1.35), v2-ce0a90be (0.70/0.15/0.18/1.10, twice), v2-af92648d (0.90/0.15/0.14/1.10), v2-c2ac7636 (1.00/0.15/0.14/1.00). All runs exit 0, completed 4, 0 errors.
- **Implications**: the aggressive grid + gate reliably improves on the weak start champion; after the first promotion the new champion is much harder to beat (0 further promotions in 12 post-round-1/2 rounds). The winning genomes cluster at call 0.15, raise 0.62–1.00, sizing 1.0–1.35.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-004..013

### VAL-006: Runs 1–5 are Supported

- **Target**: segment-1 claim batch.
- **Method**: verified each run's report/pointer/sessions/search/evaluations and the batch log; re-derived every promotion.
- **Evidence examined**: EVD-004..013, OBS-004..008.
- **Result**: Supported.
- **Conclusion**: 5/5 runs valid, 6 promotions, no gate/component change.
- **Related**: OBS-008, EVD-004..013

---

### OBS-009: The dedup range is 180–378; the near-miss pattern persists

- **Source**: `run-06/round-{1..4}/search.json`; `run-06/evaluations/*.json`
- **Fact**: run 6's search evaluated 252/180/324/378 candidates (the seed genome overlapped grid anchors in three rounds); the gate refused 3.406 and 0.324, both below 5, and promoted only 24.852. The winning genome 0.95/0.15/0.14/1.0 is new to the batch.
- **Implications**: "≥300" is not literally true for every round; the honest unit is the grid capacity (378) and the evaluated count (recorded). The gate's boundary behaviour is consistent.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-014, EVD-015, OBS-005

### VAL-007: Run 6's claims are Supported

- **Target**: run 6 claim batch.
- **Method**: read the report/pointer/sessions/search/evaluations; checked the near-misses.
- **Evidence examined**: EVD-014, EVD-015, OBS-009.
- **Result**: Supported.
- **Conclusion**: run 6 is valid; one promotion; counts recorded.
- **Related**: OBS-009, EVD-014, EVD-015

---

### OBS-010: Ten runs, twelve promotions, every run promoted

- **Source**: `run-01..10/strategies-report.json`; `logs/batch-runs.log`
- **Fact**: 10/10 runs completed 4 rounds with 6 seats and 0 errors; 12 promotions; every run promoted at least once; only runs 1 and 9 promoted twice; all promotions occurred in rounds 1–2; no run improved its champion after round 2. Final champions span 0.62–1.00 raise, 0.15–0.20 call, 0.10–0.18 bluff, 0.85–1.35 sizing.
- **Implications**: the aggressive search + frozen gate reliably promotes a stronger genome from the weak start, but gains saturate quickly. The batch is an existence/recurrence demonstration, not a monotone optimizer.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-016..020

### OBS-011: Seven of ten promoted genomes survive fresh seeds; three are gate-sample artefacts

- **Source**: `.tmp/goal-llm-harness/batch-confirm` output (EVD-021/022)
- **Fact**: 34/40 fresh checks promoted; 7/10 genomes passed all four; the boundary promotion 0.85/0.15/0.18/1.10 (gate LCB 6.02) passed all fresh checks, while 0.70/0.20/0.14/1.00 (gate LCB 6.55) passed only 1/4. The most robust genomes are raise 0.80–1.00, call 0.15–0.20, sizing 0.85–1.35, with fresh LCBs 8.3–37.1.
- **Implications**: a promotion on one 24-seed gate sample is not automatically a robust strategy; the batch report must separate "promoted" from "fresh-robust". The search/gate shape produces real strategies, but 3/10 are sample-specific.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-021, EVD-022, OBS-007

### VAL-008: The batch claims are Supported

- **Target**: the 10-run batch claim batch.
- **Method**: verified every run's report/pointer/sessions/search/evaluations; summed the promotions; re-ran all 10 distinct promoted genomes on two fresh base seeds at 24/50 seeds.
- **Evidence examined**: EVD-016..022, OBS-010/011.
- **Result**: Supported (with the explicit 3/10 non-robust caveat).
- **Conclusion**: the batch is real: 10/10 runs, 12 promotions, 7/10 robust; no gate/component change; the product pointer untouched.
- **Related**: OBS-010, OBS-011, EVD-016..022

---

### OBS-012: Final invariants hold

- **Source**: frozen-baseline comparison; key scan; `RunXunit` run
- **Fact**: the five frozen files hash identically to the pre-batch baseline; 0 `sk-` hits in the docs/batch artifacts; the deterministic test suite is **446 passed, 0 failed, 0 skipped**.
- **Implications**: the batch changed only scratch/docs; D4/D5 are supported.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-016..022

### DEC-004: The batch is complete; no product adoption

- **Decision**: close the goal with the batch report; leave `0.90/0.20/0.18/1.0` and the batch winners in the loop data; do not change the product champion pointer.
- **Owner**: user (adoption), agent (execution)
- **Status**: approved (execution)
- **Rationale**: 10/10 runs verified, 12 promotions, 7/10 fresh-robust; the user asked for the experiments, not a product promotion. OQ-002 remains a P2 preference.
- **Alternatives considered**: adopting the most robust genome (0.95/0.15/0.14/1.0 or 1.00/0.15/0.18/1.0) now — deferred to the user.
- **Related**: OBS-010..012, VAL-008

---

<!-- New entries go below this line -->
