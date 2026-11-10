# Goal Journal — goal-llm-harness

> Append-only. See `references/evidence-protocol.md` (stretch-goal-harness) for the entry format.
> OBS/HYP/VAL/DEC/TODO/RISK/OQ entries below; only a HYP or TODO `Status` may be edited in place.

---

## Round 0 — contract and pre-registration (2026-11-09)

### OBS-001: The learning loop builds its refinement policy inline

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:54-60`, `:192-203`
- **Fact**: `TrainingLoop.Run` takes `options, policy, cancellationToken, proposer, observer, proposalGuard` and has no refinement-policy parameter; the `HarnessRefinement` branch constructs `new SignatureDrivenRefinementPolicy(...)` at the proposal point.
- **Implications**: WP-1 needs one optional parameter and one branch; the default construction must stay lazy so the deterministic path is unchanged.
- **Confidence**: High
- **Agent**: orchestrator (direct read)
- **Related**: EVD-003

### OBS-002: The refinement boundary is data-only and context-light

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/Refinement/IRefinementPolicy.cs:6-44`
- **Fact**: `IRefinementPolicy.Propose(RefinementContext)` returns `RefinementProposal(StrategyGenome Genome, string RationaleCode)`; `RefinementContext` carries only incumbent, analysis, signatures and iteration; the rationale vocabulary is fixed.
- **Implications**: the model-specific context (memory, lineage, bounds, prior champion) must be assembled inside the model policy from `dataDirectory`, as the requirements state.
- **Confidence**: High
- **Agent**: orchestrator (direct read)
- **Related**: EVD-004

### OBS-003: The Learning scan is directory-scoped

- **Source**: `WIP/Wip.Tholdem.Tests/Learning/LearningSourceSafetyTests.cs:12-49`
- **Fact**: the scan enumerates `WIP/Wip.Tholdem.Agents/Learning/**/*.cs` and forbids the ordinal tokens `Process`, `HttpClient`, `git `, `Assembly.Load`, `Compile`, plus `CSharpScript`/`CodeDom`.
- **Implications**: `WIP/Wip.Tholdem.ModelHarness/` is outside the scanned directory, so the model policy can use `IChatModel` without weakening the deterministic guarantee.
- **Confidence**: High
- **Agent**: orchestrator (direct read)
- **Related**: EVD-005, DEC-002

### OBS-004: The model transport already exposes the served model and usage

- **Source**: `WIP/Wip.Runtime/Runtime/DeepSeekChatModel.cs:352-395`, `:446-461`
- **Fact**: `MapCompleteResponse` reads the response `model` field into `ChatResponse.ModelId`, sets provider `deepseek`, and maps `usage.prompt_tokens`/`completion_tokens`; the requested model is `request.ModelId`.
- **Implications**: a transcript can record requested and served model, usage and correlation id without changing the transport.
- **Confidence**: High
- **Agent**: orchestrator (direct read)
- **Related**: EVD-006

### OBS-005: The Ponder surface is exactly four tools

- **Source**: `WIP/Wip.Tholdem.Agents/Runtime/TholdemToolCatalog.cs:25-78`; `TholdemRuntimeAgent.cs:56-77`, `:151-164`
- **Fact**: the catalog defines `equity-calculator`, `pot-odds`, `hand-history-query`, `opponent-statistics` with fixed schemas/permissions; the agent stores the tool list in its constructor and validates every parsed action against the observation.
- **Implications**: WP-6 reuses the agent unchanged; M-15's tool snapshot is checkable from the catalog.
- **Confidence**: High
- **Agent**: orchestrator (direct read)
- **Related**: EVD-007, EVD-008

### OBS-006: The live gate refuses without touching a model

- **Source**: `WIP/Wip.Tholdem.Agents/Runtime/LiveModelGate.cs:13-31`
- **Fact**: `IsEnabled` requires `THOLDEM_LIVE_MODEL` in {`1`,`true`} and a non-empty `DEEPSEEK_API_KEY`; the failure reason names the variable only.
- **Implications**: M-07/D4 can be proven with a counting `IChatModel` and a missing-env run.
- **Confidence**: High
- **Agent**: orchestrator (direct read)
- **Related**: EVD-009

### OBS-007: The frozen gate is strict at the pre-registered settings

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/EvaluationCriteria.cs:8-11`; `PromotionPolicy.cs:29-46`; `PromotionVerifier.cs:30-58`
- **Fact**: promotion requires `report.LowerBoundBbPer100 > 5.0` and `report.Seeds >= 24`; the verifier re-derives the same two conditions from the report.
- **Implications**: a promotion from the 0.70/0.70 demo start is possible but not expected; a no-promotion LLM arm is a legitimate pre-registered outcome and must be reported with the exact best LCB/mean.
- **Confidence**: High
- **Agent**: orchestrator (direct read)
- **Related**: RISK-001, DEC-003

### OBS-008: The operator secret file exists and is gitignored

- **Source**: `.secrets/deepseek.env` (98 bytes); `.gitignore:53-54`
- **Fact**: the secret file is present and `.gitignore` ignores `.secrets/`. The contents were not read into this journal.
- **Implications**: the loader can be built against the documented path; verification greps only for a key prefix.
- **Confidence**: High
- **Agent**: orchestrator (direct read + file metadata)
- **Related**: EVD-002, HG5

### DEC-001: Adopt the model-harness contract and its hard gates

- **Decision**: The goal is the real-LLM harness exactly as specified: D1–D9, HG1–HG8, the 22-test plan and the pre-registered arms.
- **Rationale**: `Wip.Tholdem.ModelHarness.md:317-325`, `:95-113`; the deterministic substrate is the control (OBS-001, OBS-002), not the deliverable.
- **Alternatives considered**: a deterministic-only promotion experiment (rejected: explicitly a hard failure); a scripted-model arm (rejected: unit-test double only, HG4).
- **Related**: OBS-001, OBS-002

### DEC-002: Put the model policy in a new `WIP/Wip.Tholdem.ModelHarness` project

- **Decision**: create `WIP/Wip.Tholdem.ModelHarness/` (namespace `Wip.Tholdem.ModelHarness`), referenced by `Wip.Tholdem.Tests`, added to `Wip.slnx`; it references `Wip.Tholdem.Agents` (for `IRefinementPolicy`) and `Wip.Runtime` (for `IChatModel`/`DeepSeekChatModel`).
- **Rationale**: matches WP-2/WP-3 file paths and keeps the `Learning` scan clean by construction (OBS-003).
- **Alternatives considered**: a folder inside `Wip.Tholdem.Agents` (works but contradicts the requirements' path and weakens the isolation claim).
- **Related**: OBS-003, OBS-004

### DEC-003: Freeze the pre-registration before the first run

- **Decision**: starting champion 0.70/0.70/0.12/1.0 demo start, seed `1099511927776`, default gate margin 5 / `MinSeeds` 24, arms `llm-refinement` + `deterministic-refinement` (+ optional `legacy`), budget 10×2000 and 50×20, one success rule, one live 20-hand Ponder match.
- **Rationale**: OBS-007; the values are fixed in `goal.md` and may not change after the first arm run.
- **Alternatives considered**: a relaxed margin (rejected: HG3); a lower seed floor (rejected).
- **Related**: OBS-007, RISK-001

### DEC-004: Round 1 ships the parser with the transcript

- **Decision**: `LlmProposalParser` (deterministic JSON → genome, clamp, unknown-field policy) ships in WP-2 with `LlmTranscript`/`LlmTranscriptStore`, so "replay reproduces a candidate with zero model calls" is a real check; WP-3 adds `LlmPromptBuilder`, `LlmRefinementPolicy` and the repair loop.
- **Rationale**: WP-2's exit criterion is not provable without a parser; shipping a replay-shaped stub would be a falsified delivery.
- **Alternatives considered**: defer replay to round 2 (would leave WP-2's stated exit criterion unmet); add a candidate field to the transcript (deviates from the fixed transcript schema).
- **Related**: OBS-002, TODO-002

### TODO-001: Round 1 — WP-1 seam and WP-2 transcript/replay

- **Task**: dispatch one Implementer to add the optional `IRefinementPolicy` parameter to `TrainingLoop.Run`, the `ModelHarness` project with canonical transcript/store/parser/replay, and focused tests; then verify default-path identity, supplied-policy use, replay with zero model calls, full suite and legacy hashes.
- **Priority**: P0
- **Depends on**: DEC-001, DEC-002, DEC-004
- **Status**: Done
- **Related**: OBS-001, OBS-002

### TODO-002: Round 2 — WP-3 live refiner policy

- **Task**: `LlmPromptBuilder` + `LlmRefinementPolicy` with schema validation, one repair, transcript outcomes, injection-as-data and missing-key refusal.
- **Priority**: P0
- **Depends on**: TODO-001
- **Status**: Done
- **Related**: DEC-004

### TODO-003: Round 3 — WP-4 live gate/secret loader, WP-5 LLM arm

- **Task**: secret-file loader + live configuration + redaction tests; pre-registered arms from the 0.70/0.70 start; real transcripts; report re-derivation and pointer bytes.
- **Priority**: P0
- **Depends on**: TODO-002
- **Status**: Done
- **Related**: DEC-003, DEC-009

### TODO-004: Round 4 — WP-6 live Ponder match

- **Task**: compose `TholdemRuntimeAgent` + `AgentExecutor` + `ArtifactAgentMemory` + `TholdemToolCatalog` with `DeepSeekChatModel`; run 20 hands; record turns, tool calls, results, legal actions, latency, errors.
- **Priority**: P0
- **Depends on**: TODO-002
- **Status**: Done
- **Related**: OBS-005, DEC-010

### TODO-005: Round 5 — WP-7 falsification and final report

- **Task**: 22-test plan, deterministic regression, frozen hashes, final report, coverage pass.
- **Priority**: P0
- **Depends on**: TODO-003, TODO-004
- **Status**: Done
- **Related**: OBS-021, DEC-012

### RISK-001: No promotion is likely at the frozen margin

- **Risk**: the LLM arm may produce no verified promotion because the gate needs LCB > 5 bb/100 over 50 seeds from a deliberately suboptimal start (OBS-007).
- **Likelihood**: Medium-High
- **Impact**: Medium (outcome, not integrity)
- **Evidence**: OBS-007; `docs/goal-rsi/evidence.md:11` recorded a 0.70/0.70 demo with mean 1.05 and LCB 0.32 under margin 0.
- **Mitigation**: run the pre-registered arm once and report the exact best LCB/mean and the refusal; never lower the gate or rerun for a favourable number.
- **Gate**: —

### RISK-002: The live model may never call a Ponder tool

- **Risk**: in 20 hands the real model may answer with actions only, so D6's "at least one tool call" would be unmet.
- **Likelihood**: Medium
- **Impact**: High (D6)
- **Evidence**: OBS-005; the agent's system prompt does not require tool use.
- **Mitigation**: run the match as specified; if zero tool calls occur across every turn, record it as an OQ and report D6 as unresolved rather than fabricating a call or changing the agent.
- **Gate**: —

### RISK-003: The pre-registered budget is slow

- **Risk**: 2000 training hands × 10 iterations × 2 arms plus 50×20 evaluation hands per iteration may take long enough to pressure the round cap.
- **Likelihood**: Medium
- **Impact**: Medium
- **Evidence**: `TrainingOptions` defaults are 120/24/10; the pre-registered budget is ~17× larger per iteration.
- **Mitigation**: measure one iteration first; run arms as background jobs; never shrink the pre-registered budget silently.
- **Gate**: —

### RISK-004: Key leakage through errors or transcripts

- **Risk**: an exception message, URL or model echo could carry the key into an artifact.
- **Likelihood**: Low
- **Impact**: High (HG5)
- **Evidence**: OBS-006, OBS-008.
- **Mitigation**: env-only key, `Bearer` header set after payload construction, redaction of secret-shaped text before persistence, prefix grep over every artifact directory.
- **Gate**: —

### RISK-005: `dotnet test` is unusable on this machine

- **Risk**: claim of a green suite cannot be produced with the standard runner.
- **Likelihood**: Certain
- **Impact**: Low (documented)
- **Evidence**: `docs/goal-tholdem-harness/final-report.md` records Win32Exception (5); all local evidence uses `.tmp/tholdem-verify/RunXunit`.
- **Mitigation**: use RunXunit for every run; no CI-green claim.
- **Gate**: —

### OQ-001: Which memory key does an LLM proposal write?

- **Question**: the loop writes `rationaleCode` into `HarnessMemoryStore`; a model proposal has no fixed-vocabulary code. Should `LlmRefinementPolicy` return a sanitized `llm-*` code derived from its `memory` operation, and does `HarnessMemoryStore` accept it (≤128 chars, non-empty)?
- **Needed evidence**: read `HarnessMemoryStore.Record` + its key validation, and a focused test that an LLM proposal's key is bounded and sanitized.
- **Priority**: P1 important
- **Raised by**: Round 0 (orchestrator)
- **Related**: OBS-002, DEC-004

### OQ-002: Does the live model need the repair path?

- **Question**: does the real model return a schema-valid single JSON object on the first attempt, or does the run exercise the one-repair path?
- **Needed evidence**: the live transcripts' `outcome` field.
- **Priority**: P2 curiosity
- **Raised by**: Round 0 (orchestrator)
- **Related**: RISK-001

---

### OBS-009: The live transport answers with a real completion

- **Source**: `.tmp/goal-llm-harness/smoke/Program.cs` + run output (EVD-015); transport `WIP/Wip.Runtime/Runtime/DeepSeekChatModel.cs:72-99`
- **Fact**: a request with model `deepseek-chat` returned a non-empty completion `{"ok":true,"n":7}`, served as `deepseek-flash`, provider `deepseek`, usage 22 prompt / 9 completion tokens, 2829 ms.
- **Implications**: WP-4/WP-5 and WP-6 can rely on the transport and the stored key; the remaining risk is the run budget, not connectivity.
- **Confidence**: High
- **Agent**: orchestrator (ran the probe)
- **Related**: EVD-015, RISK-004

### DEC-005: The live smoke is pre-flight evidence, not a delivery

- **Decision**: the smoke probe proves the transport and the key; it is not the LLM-refinement arm and is never reported as one.
- **Rationale**: HG4 — a one-prompt smoke has no proposal and no gate verdict; only the WP-5 arm counts for D5.
- **Alternatives considered**: counting the smoke as the live transcript (rejected: it would be a falsified delivery claim).
- **Related**: EVD-015, TODO-003

---

### OBS-010: The pre-registered seed was reserved but never consumed

- **Source**: `docs/goal-promotion/goal.md:18`; `docs/goal-promotion/state.md:3`; absence of `docs/goal-promotion/final-report.md`
- **Fact**: the aborted `docs/goal-promotion` contract pre-registered the same start, seed `1099511927776`, gate and 10×2000/50×20 budget, but its state is "Round 0 of 6 (contract)" and no arm, raw rows or final report exist.
- **Implications**: the seed and budget are fresh with respect to observations, but they are not novel values; the goal's provenance note now says so, and this run is the first to consume them.
- **Confidence**: High
- **Agent**: orchestrator (direct read)
- **Related**: EVD-013, DEC-003

### DEC-006: Clarify seed provenance and the LLM arm's skill flag before any run

- **Decision**: keep seed `1099511927776`; document that `goal-promotion` reserved but never ran it; run both arms with `SkillRefinement=false` (the shipped default), because the LLM arm must not be intercepted by `RefinementGuard` and the control must stay the shipped deterministic path.
- **Rationale**: OBS-010; the requirements say the LLM arm is selected only by passing the policy while `HarnessRefinement = true`, and the guard is installed only when `SkillRefinement` is on.
- **Alternatives considered**: a different seed (would deviate from the user's explicit instruction); `SkillRefinement=true` in both arms (would install the guard and change the control away from the shipped default).
- **Related**: OBS-010, DEC-003

---

### OBS-011: Round 1 delivered a working seam and a canonical transcript

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:54-61`, `:193-210`; `WIP/Wip.Tholdem.ModelHarness/*.cs`; `WIP/Wip.Tholdem.Tests/ModelHarness/*.cs`
- **Fact**: the seam is present with the exact required signature, the new project holds the transcript record/store/parser/replay/redaction, and the ten new tests assert literal genomes, canonical member order, hash tamper refusal, redaction of three secret shapes, and byte-equality of the null-policy and explicit-policy artifacts.
- **Implications**: D1 is Covered; D2 is Partial until a real model call is transcribed.
- **Confidence**: High
- **Agent**: orchestrator (read every delivered file)
- **Related**: EVD-017 to EVD-023

### VAL-001: Round-1 claim batch survived falsification

- **Target**: round-1 claim batch — (CLM-1) a supplied policy drives every iteration; (CLM-2) the null policy leaves the deterministic artifacts byte-identical; (CLM-3) replay reproduces a candidate with no model call; (CLM-4) transcript hashes are canonical and tamper-refusing; (CLM-5) no key shape survives into a transcript.
- **Method**: read every delivered source and test; rebuilt the test assembly; ran the filtered and full suites myself; ran the legacy CLI twice and hashed every JSON; recomputed the five frozen hashes; ran the key-prefix scan. Tried to break CLM-2 by comparing two fresh directories byte-for-byte, and CLM-3 by reading the replay path for any model reference.
- **Evidence examined**: `docs/goal-llm-harness/round-1/full-suite.log` (`TOTAL 404 PASSED 404 FAILED 0 SKIPPED 0`), `focused-suite.log` (`TOTAL 10 PASSED 10 FAILED 0 SKIPPED 0`), `.tmp/goal-llm-harness/round-1/legacy-a|b` (7/7 hashes, no `harness` directory), the five frozen hashes (`same`), the key scan (`0` hits), and the delivered sources.
- **Result**: Supported
- **Conclusion**: all five claims hold. CLM-3 is supported structurally (the replay type holds no model and parses the recorded response); CLM-2 additionally holds for the refinement path through the byte-equality test and for the legacy path through the 7/7 CLI replay.
- **Related**: EVD-017 to EVD-023

### DEC-007: Accept Round 1 and move to the live refiner policy

- **Decision**: Round 1 is accepted as delivered; D1 Covered, D2 Partial. Round 2 implements WP-3 (`LlmPromptBuilder`, `LlmRefinementPolicy`, repair, live-gate refusal) with scripted-model tests only.
- **Rationale**: VAL-001; the parser shipped in WP-2 per DEC-004 so replay is proven without a stub.
- **Alternatives considered**: deferring the gate check to Round 3 (rejected: M-07's refusal test belongs with the policy constructor); a separate factory type (rejected: an optional environment lookup keeps the documented constructor and is testable without global env mutation).
- **Related**: VAL-001, DEC-004, TODO-002

### OQ-003: Does the lineage reader accept a fresh data directory?

- **Question**: on a fresh arm directory with `active-champion.json` and one v1 artifact but no `ledger.json`, does `StrategyLineageReader.Read` return the root entry or throw?
- **Needed evidence**: a Round-2 test that builds a fresh `TrainingLoop` data directory and reads lineage for the v1 champion, asserting the root entry with null outcome.
- **Priority**: P1 important
- **Raised by**: Round 1 (orchestrator)
- **Related**: OBS-002, TODO-002

---

### OBS-012: The live model complies with the M-02 prompt

- **Source**: `.tmp/goal-llm-harness/prompt-probe/Program.cs` run output (EVD-028)
- **Fact**: one real call through the delivered builder and policy returned a single valid JSON object (`0.62/0.66/0.18/1.15`, reason text), served as `deepseek-flash`, 371 prompt / 63 completion tokens, 3013 ms, no repair needed.
- **Implications**: the live arm should produce a real proposal per iteration; the remaining risks are the model's strategy judgment and the gate, not schema compliance.
- **Confidence**: High
- **Agent**: orchestrator (ran the probe)
- **Related**: EVD-028, OBS-009

### OBS-013: The ground truth for OQ-003 is answered

- **Source**: `WIP/Wip.Tholdem.Tests/ModelHarness/LlmRefinementPolicyTests.cs:345-384`
- **Fact**: on a fresh `TrainingLoop` data directory, `StrategyLineageReader.Read` returns at least the root entry and the policy's prompt lineage array equals the reader's ids.
- **Implications**: the LLM arm can build its lineage section from iteration 1; no empty-lineage special case is needed beyond a missing pointer.
- **Confidence**: High
- **Agent**: orchestrator (read the test, ran the suite)
- **Related**: OQ-003, EVD-024

### VAL-002: Round-2 claim batch survived falsification

- **Target**: round-2 claim batch — (CLM-6) a real call happens once per `Propose` and only under the live gate; (CLM-7) exactly one repair follows a malformed reply; (CLM-8) out-of-range values clamp and the original is still recorded; (CLM-9) the refiner sends no tools and cannot change permissions; (CLM-10) malformed twice and transport failures promote nothing; (CLM-11) the prompt carries every section and no seed/card/key.
- **Method**: read every new source and test; rebuilt; ran the full and focused suites; scanned for forbidden tokens and key shapes; **and ran a real model call through the delivered code** to test CLM-11 and the valid-path half of CLM-6 against a live provider rather than a scripted one.
- **Evidence examined**: `round-2/full-suite.log` (`TOTAL 418 PASSED 418 FAILED 0 SKIPPED 0`), focused logs, `LlmRefinementPolicyTests.cs`, `LlmPromptBuilderTests.cs`, and the live probe transcript (EVD-028).
- **Result**: Supported
- **Conclusion**: all six claims hold. The live probe is the first real-model evidence for the refiner; it does not count as an iteration because it used a synthetic context and no gate evaluation.
- **Related**: EVD-024 to EVD-029

### DEC-008: Accept Round 2; run the pre-registered arms in Round 3

- **Decision**: Round 2 is accepted. Round 3 adds WP-4 (live configuration and secret loader) plus WP-5 (the scratch arm runner and promotion re-derivation). The orchestrator personally runs the two arms and the verification, not the implementer.
- **Rationale**: VAL-002 and EVD-028 show the live refiner produces a valid proposal; the arm run is the evidence, so it must be executed and read by the orchestrator.
- **Alternatives considered**: letting the implementer run the arms (rejected: the implementer has no key and the run must be reproducible by the orchestrator); a scripted dry-run arm (rejected: it would create a second model-kind in the runner and a gate-bypass path that could be mistaken for live).
- **Related**: VAL-002, EVD-028, TODO-003

---

### OBS-014: The pre-registered LLM arm produced real proposals but no promotion

- **Source**: `docs/goal-llm-harness/round-3/llm-arm-attempt1.log`, `llm-arm-report.json`, `llm-arm-verdict.json` (EVD-031, EVD-033)
- **Fact**: 10 iterations, 10 real `deepseek-chat` calls served as `deepseek-flash` (6213/800 tokens, 1117–2951 ms each), all transcripts `ok`, challengers 0.55–0.62 raise / 0.45–0.62 call / 0.18–0.20 bluff / 1.15–1.5 sizing; every verdict re-derived as `lcb-below-margin`; best LCB **1.6233**, best mean **50.03** (iteration 6); no promotion, pointer unchanged.
- **Implications**: the mechanism under test (a real model proposing through the frozen gate) is demonstrated end-to-end; the pre-registered success criterion (at least one verified promotion) was **not met**. The gate was not lowered and the arm was not repeated.
- **Confidence**: High
- **Agent**: orchestrator (ran the arm)
- **Related**: EVD-031, EVD-033, RISK-001

### OBS-015: The deterministic control also produced no promotion

- **Source**: `docs/goal-llm-harness/round-3/deterministic-arm-run.log`, `deterministic-arm-report.json` (EVD-034, EVD-035)
- **Fact**: 10 iterations of the shipped `SignatureDrivenRefinementPolicy` from the same start/seed/budget: no model calls, 10 evaluations, all `lcb-below-margin`; best LCB **2.3966**, best mean **7.7**; no promotion.
- **Implications**: the LLM arm did not beat the control on the gate statistic (best LCB 1.62 vs 2.40), although its best mean was higher (50.03 vs 7.7). The comparison supports the honest statement that the model explored more widely without clearing the gate.
- **Confidence**: High
- **Agent**: orchestrator (ran the arm)
- **Related**: EVD-034, OBS-014

### OBS-016: The first LLM-arm invocation was refused before any request

- **Source**: `docs/goal-llm-harness/round-3/llm-arm-attempt0-refused.log` (EVD-032)
- **Fact**: the invocation lacked the operator `THOLDEM_LIVE_MODEL=1` opt-in, so `LiveModelConfiguration.Resolve` refused with `live-model-disabled`; no HTTP request was made, no evaluation file existed and the pointer stayed byte-identical.
- **Implications**: the live gate works in the operator path, not just in unit tests. The refusal is recorded as attempt 0; attempt 1 is the pre-registered run.
- **Confidence**: High
- **Agent**: orchestrator (ran it)
- **Related**: EVD-032, EVD-009

### VAL-003: Round-3 claim batch survived falsification

- **Target**: round-3 batch — (CLM-12) the LLM arm ran with a real model and a transcript per iteration; (CLM-13) each challenger equals the parsed model proposal; (CLM-14) the frozen gate and verifier alone decided, re-derived from the persisted reports; (CLM-15) no promotion means the pointer is byte-identical; (CLM-16) the deterministic control ran the same shape with zero model calls; (CLM-17) no key appears in any artifact.
- **Method**: ran both arms myself; read every transcript; ran the runner's verify mode and read every per-iteration row; independently hashed the pointers before/after and the report files; scanned for the 35-character key shape; recomputed the five frozen hashes; re-ran the full suite.
- **Evidence examined**: EVD-031 to EVD-039 and the artifacts they name.
- **Result**: Supported
- **Conclusion**: all six claims hold. The pre-registered success rule (a verified promotion) was not met; that is an experimental outcome, reported as such, not a falsified delivery claim.
- **Related**: EVD-031 to EVD-039

### DEC-009: Report the pre-registered no-promotion result and continue to the Ponder run

- **Decision**: do not lower the gate, do not rerun the arm for a favourable number, and do not substitute a deterministic arm for the LLM arm. Report the LLM arm's exact best LCB/mean and the refusal reason, mark D5 Covered by the honest no-promotion report, and proceed to WP-6.
- **Rationale**: the pre-registration in `goal.md` names this exact fallback; HG3 and the hard-failure list forbid gate edits or reruns.
- **Alternatives considered**: a second LLM arm attempt at the same settings (rejected: no new information and it is a rerun); reporting the deterministic control as the primary outcome (rejected: it is the control, not the claim).
- **Related**: OBS-014, VAL-003, DEC-003

### OQ-004: Why does the LLM arm's best mean far exceed its best LCB?

- **Question**: iteration 6 had mean 50.03 bb/100 but LCB 1.62 over 50 seeds × 40 hands; is the variance driven by the challenger's strategy or by the 40-hand evaluation granularity?
- **Needed evidence**: a separate, pre-registered evaluation with more hands per match or an analysis of per-seed dispersion; out of scope for this goal.
- **Priority**: P2 curiosity
- **Raised by**: Round 3 (orchestrator)
- **Related**: OBS-014, EVD-033

---

### OBS-017: The gate is reachable from this start; the model's direction was the limiting factor

- **Source**: `.tmp/goal-llm-harness/fixture-hunt` output (EVD-040)
- **Fact**: at test seed 20261109 and 24 seeds × 20 hands, tight genomes promote from the 0.70/0.70 champion (`0.80/0.40/0.10/1.0` mean 40.47, LCB 10.87; `0.90/0.20/0.08/1.0` LCB 15.59), while the model-like `0.60/0.55/0.18/1.15` is rejected (LCB −92.21).
- **Implications**: the arm's no-promotion is a statement about the ten model proposals, not about an unreachable gate. The model consistently loosened the incumbent in response to a "fold-to-raise high" signal when tightening was the profitable direction. That is a finding for the final report, and it supplies a gate-valid fixture for the positive pointer-move test.
- **Caveat**: this is a fixture hunt at a different seed, not a second arm run and not a re-interpretation of the pre-registered outcome.
- **Confidence**: High
- **Agent**: orchestrator (ran the hunt)
- **Related**: EVD-040, OBS-014

---

### OBS-018: The live Ponder match recorded actions but no tool call

- **Source**: `docs/goal-llm-harness/round-4/ponder-live.log`; `.tmp/goal-llm-harness/ponder/live/ponder-session.json` (EVD-041, EVD-042)
- **Fact**: 20 hands, 33 model turns, 33 legal model actions, 0 fallbacks, 0 violations, 0 errors, **0 tool calls**, served `deepseek-flash`, 773–1788 ms, 43,547/175 tokens. Every response was a direct action (the first `{"action":"fold"}`); the live agent folded on every acting turn and never reached a postflop decision.
- **Implications**: D6's "at least one tool call" is **not met**. The run is real and fully recorded; the tool surface was offered but unused.
- **Confidence**: High
- **Agent**: orchestrator (ran the match)
- **Related**: EVD-041, EVD-042, RISK-002

### OBS-019: The transport and model do support tools; the agent prompt suppresses them

- **Source**: `.tmp/goal-llm-harness/tool-probe` run (EVD-043)
- **Fact**: with the four catalog definitions captured on the wire, one live request returned exactly one tool call `pot-odds {"pot":100,"amountToCall":25}`. The runtime agent also forwards the catalog tools on every turn (EVD-044).
- **Implications**: the Ponder zero-tool-call result is a prompt-behavior finding, not a transport or model-capability limitation. The fixed `TholdemRuntimeAgent` system prompt ("Reply with exactly one JSON object and no other text") plus the one-action task yields direct answers and a fold-only policy.
- **Confidence**: High
- **Agent**: orchestrator (ran the probe)
- **Related**: EVD-043, EVD-044, OBS-018

### DEC-010: Do not rerun the Ponder match or switch models to manufacture a tool call

- **Decision**: report the live Ponder result exactly; mark D6 Partial; keep a failing gate test for the unmet tool-call criterion; do not run a second match, a different seat, a different memory size or a different model to obtain a tool call.
- **Rationale**: the pre-registration says one live 20-hand match; a second attempt at the same claim would be sampling to a favourable surface, and the requirement's own M-12 warns that capability must be recorded, not assumed. The honest state is D6 Partial.
- **Alternatives considered**: a second match with the same model/seed (rejected: re-roll); a second model (rejected: a cross-model claim would need its own pre-registration and is not the pre-registered surface); changing `TholdemRuntimeAgent`'s prompt (rejected: product code out of scope and a behavioural change needing its own goal).
- **Related**: OBS-018, OBS-019, OQ-005

### OQ-005: What product change would make the Ponder agent use its tools?

- **Question**: the live model calls tools when a prompt invites it but never calls one under the fixed runtime-agent prompt, folding every hand. Is the intended fix a prompt change, a tool-usage instruction, or a different Ponder model — and does it need its own pre-registration and threat review?
- **Needed evidence**: a product-scoped change to `TholdemRuntimeAgent`'s system/task text (or a documented tool-choice policy), with a live match showing at least one model-authored tool call and a non-degenerate action mix.
- **Priority**: P0 blocking
- **Raised by**: Round 4 (orchestrator)
- **Related**: OBS-018, OBS-019, DEC-010

### RISK-006: D6 cannot be completed inside this goal's scope

- **Risk**: the goal's stop criteria require D1–D9 covered and all 22 planned tests green; D6's tool-call criterion cannot be met without a product-level prompt change that this goal forbids.
- **Likelihood**: High (materialised)
- **Impact**: High (goal completion)
- **Evidence**: OBS-018, OBS-019
- **Mitigation**: report the exact state, keep the gate test red, and hand the product decision to the operator; do not weaken the test or the requirement.
- **Gate**: G3

---

### OBS-020: Round 5 delivered the remaining evidence tests, with one deliberately red gate

- **Source**: `docs/goal-llm-harness/round-5/full-suite.log`, `focused-promotion.log`, `focused-ponder.log` (EVD-045 to EVD-052)
- **Fact**: 10 new tests in 7 files; the full suite is 435 with 434 pass and 1 fail; the only failure is `Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall` asserting the recorded session's `summary.toolCalls >= 1` against the measured 0. The promotion-pointer test reproduced the gate-valid fixture (promoted, pointer moved with the report hash) and refused the always-promote control with unchanged bytes.
- **Implications**: D5's pointer evidence is now a unit-level reproduction as well as a live-arm observation; the tool-call requirement is enforced by a red test rather than hidden by a skip.
- **Confidence**: High
- **Agent**: orchestrator (ran the suite and read every new test)
- **Related**: EVD-045 to EVD-052

### VAL-004: Round-5 claim batch survived falsification

- **Target**: round-5 batch — (CLM-18) a gate-valid LLM challenger moves the pointer only after verification, and the always-promote control cannot; (CLM-19) a 503 records one http-5xx transcript, no evaluation and no pointer move; (CLM-20) cancellation inside the gate leaves no evaluation, no ledger row and no pointer move; (CLM-21) refinement-off runs are byte-identical and pin the legacy v1 hash; (CLM-22) the learning layer stays free of model/http/vendor references; (CLM-23) injection cannot add a tool or permission and the unknown tool does not execute.
- **Method**: read every new test; rebuilt; ran the full and focused suites myself; compared the observed failure and the pinned hash to the earlier baselines; checked the changed-source list (only new test files at 13:25–13:26).
- **Evidence examined**: EVD-045 to EVD-052 and the logs they name.
- **Result**: Supported
- **Conclusion**: all six claims hold. CLM-18's positive half is a real gate-valid promotion on a measured fixture; the live arm's own no-promotion result is unaffected because the fixture uses a different seed and is not an arm run.
- **Related**: EVD-045 to EVD-052

### DEC-011: Keep the red tool-call test; do not skip or weaken it

- **Decision**: the tool-call requirement stays encoded as a failing test against the recorded live session. The suite will close at 21 of 22 planned tests green and one red, and the final report will state that D6 is Partial and the goal is not complete.
- **Rationale**: skipping or weakening the test to reach a green suite would be the exact anti-pattern the harness forbids; a red gate is the honest signal.
- **Alternatives considered**: a gated/skipped live test (rejected: it hides a measured failure); changing the assertion to `>= 0` (rejected: weakening); rerunning the match (rejected: DEC-010).
- **Related**: DEC-010, OQ-005, RISK-006

---

### OBS-021: The final regression is clean except the one kept gate

- **Source**: `docs/goal-llm-harness/round-6/final-suite.log`, `final-build.log`, `final-legacy-*.log` (EVD-055 to EVD-060)
- **Fact**: build 0 warnings/0 errors; suite 439 tests with 438 pass and the single tool-call failure; legacy 7/7 hashes in two fresh directories; five frozen hashes unchanged; both arm pointers byte-identical; both report sidecars match; key-prefix scan 0.
- **Implications**: every delivery claim other than D6 is reproduced by the orchestrator at the end of the run. The goal stops with one named, measured, product-level gap.
- **Confidence**: High
- **Agent**: orchestrator (ran the final regression)
- **Related**: EVD-055 to EVD-060

### VAL-005: Round-6 claim batch survived falsification

- **Target**: round-6 batch — (CLM-24) every M-01…M-15 row names executable runtime proof that exists; (CLM-25) the validator rejects a metadata-only row and a missing API category; (CLM-26) a model-authored `promptText` is inert; (CLM-27) the final regression preserves the deterministic and frozen state.
- **Method**: read the behavior-proof document and the validator; ran the focused behavior-proof filter (3/3) and the M-05 test (1/1) myself; ran the final regression script and read every line; compared the legacy hashes and the five frozen hashes to the recorded baselines.
- **Evidence examined**: EVD-053 to EVD-060.
- **Result**: Supported
- **Conclusion**: all four claims hold. The validator's synthetic cases exercise a rejection path, and the real document passes only because every named path exists and the M-06 gap is stated.
- **Related**: EVD-053 to EVD-060

### DEC-012: Close the run with the goal not complete, one named blocker

- **Decision**: stop at Round 6 of 8 with the goal **not complete**. D6 is Partial because the live model made zero tool calls under the unchanged agent prompt. The 22-test plan closes at 21 green and one deliberately red gate. The final report states the exact numbers and the product-level next step. Do not mark the goal complete, and do not rerun the Ponder match or the LLM arm.
- **Rationale**: the stop criteria require D1–D9 covered and all 22 tests green; neither is true. The only way to close them inside the current code would be to weaken the test or change product behaviour out of scope. DEC-010 and DEC-011 already refuse those.
- **Alternatives considered**: green the suite by skipping the tool-call test (rejected: hides a measured failure); mark the goal complete with a caveat (rejected: the stop criteria are explicit); a second Ponder run (rejected: sampling).
- **Related**: OBS-018, OBS-021, DEC-010, DEC-011, OQ-005, RISK-006

---

### OBS-022: Round 7 closed the D2/M-06/M-09 evidence gaps without touching product code

- **Source**: `docs/goal-llm-harness/round-7/full-suite.log`, `focused-*.log` (EVD-061 to EVD-064)
- **Fact**: the suite is 442 tests (441 pass, 1 fail — the kept tool-call gate); the three new offline tests are 1/1 each. They prove (a) the ten live-arm transcripts replay offline onto the persisted challengers, (b) the wall-clock budget refuses before any call, and (c) the runtime agent's four-tool path returns the real pot-odds result and a legal action with zero violations.
- **Implications**: D2 and M-09 are fully proven end-to-end; M-06's mechanism is proven, but the live model's zero tool calls are unchanged. D6 remains Partial and the goal remains incomplete.
- **Confidence**: High
- **Agent**: orchestrator (read the tests, ran the suite and filters)
- **Related**: EVD-061 to EVD-064

### VAL-006: Round-7 claim batch survived falsification

- **Target**: round-7 batch — (CLM-28) a scripted tool-calling model drives the real four-tool path and receives the real result; (CLM-29) an expired wall-clock budget refuses with zero model calls; (CLM-30) the ten live transcripts replay onto the persisted challenger artifacts.
- **Method**: read all three tests; rebuilt; ran the full suite and all four focused filters myself; confirmed the only failure is the pre-existing tool-call gate and that only new test files changed.
- **Evidence examined**: EVD-061 to EVD-064.
- **Result**: Supported
- **Conclusion**: all three claims hold; none of them converts the live D6 gap into a pass, and they are not presented as doing so.
- **Related**: EVD-061 to EVD-064

### DEC-013: No in-scope work remains; D6 is the sole, product-level blocker

- **Decision**: close the harness work at Round 7. The goal stays incomplete on D6. The same condition has persisted through harness Rounds 4, 5, 6 and 7 (the live model makes zero tool calls under the unchanged agent prompt), every other dimension is Covered, and the only fix is a product-scoped change to `TholdemRuntimeAgent` (prompt/tool-choice) with its own pre-registration and threat review.
- **Rationale**: four rounds of the same measured condition; no rerun, no model switch, no seat change, no test weakening. The evidence is complete and the gap is named.
- **Alternatives considered**: a second Ponder match or a different model (rejected: sampling to a favourable surface); changing the agent prompt (out of scope); marking the goal complete with a caveat (rejected: violates the explicit stop criteria).
- **Related**: OBS-018, OBS-022, DEC-010, DEC-012, OQ-005, RISK-006

---

### OBS-023: The live Ponder policy was varied, not fold-only

- **Source**: `.tmp/goal-llm-harness/ponder/live/ponder-session.json`; action tally computed by the orchestrator
- **Fact**: the 33 recorded turns are Fold 6, Check 18, Call 7, Bet 1, Raise 1; the requests span streets 0/1/2/3 with 14/7/6/6 calls; 11 distinct hole-card pairs were observed; tool calls remain 0.
- **Implications**: the OBS-018 sentence "folded on every acting turn and never reached a postflop decision" is **false** and is superseded by this entry. The zero-tool-call fact is unchanged and is now stronger: a varied, multi-street live policy still produced no tool turn.
- **Confidence**: High
- **Agent**: orchestrator (parsed the recorded session)
- **Related**: OBS-018, OBS-019, EVD-042

### VAL-007: The fold-only characterization is Falsified

- **Target**: OBS-018's "the live agent folded on every acting turn and never reached a postflop decision" and OBS-019's "fold-only policy".
- **Method**: grouped the session's 33 turns by `actionKind`, parsed the `street` integer from each call's observation, and counted distinct hole-card pairs.
- **Evidence examined**: the session artifact; Fold 6 / Check 18 / Call 7 / Bet 1 / Raise 1; streets 0–3 present.
- **Result**: Falsified (the characterization). The underlying zero-tool-call measurement is not falsified.
- **Conclusion**: state.md, final-report.md and deliverables now carry the measured mix. The correction is recorded rather than silently rewritten; the D6 gap and the prompt-suppression finding stand.
- **Related**: OBS-018, OBS-019, OBS-023

---

### OBS-024: The session's request messages are memory-prefixed; the current observation is the last message

- **Source**: `.tmp/goal-llm-harness/ponder/live/ponder-session.json`; `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:109-119`
- **Fact**: each recorded call carries the system message, up to four session-memory messages and the current task as the **last** message. Reading `requestMessages[1]` returns an older observation (its hand number lags the turn). Reading the last message aligns all 33 calls with all 33 turns, zero mismatches, and every legal-action list matches.
- **Implications**: an apparent call/turn misalignment was an analysis artifact, not a session defect. Future audits must read the last message for the current observation.
- **Confidence**: High
- **Agent**: orchestrator (parsed the session both ways)
- **Related**: EVD-065, OBS-018, OBS-023

---

### OBS-025: The first Round-8 behavior test parsed the wrong message index

- **Source**: `WIP/Wip.Tholdem.Tests/ModelHarness/PonderBehaviorTests.cs` as first delivered; recount at `requestMessages[1]` vs the last message
- **Fact**: the orchestrator's Round-8 instruction named `requestMessages[1]`; that element is stale session memory once the memory window fills (20 of 33 calls carry a hand number that does not match their turn, and 11 rather than 13 distinct hole-card pairs). The implementer followed the instruction and documented the caveat. The current observation is the last message (0 hand mismatches, 13 hole pairs, streets 14/7/6/6).
- **Implications**: the test is being corrected to parse the last message and to assert the hand-alignment; the artifact itself was never wrong. This is an orchestrator specification error, not an implementer failure.
- **Confidence**: High
- **Agent**: orchestrator (recounted the session and inspected the delivered test)
- **Related**: OBS-024, EVD-065

---

### OBS-026: The corrected Ponder behavior test is pinned to the current observation

- **Source**: `WIP/Wip.Tholdem.Tests/ModelHarness/PonderBehaviorTests.cs`; `docs/goal-llm-harness/round-8/full-suite.log` (EVD-066)
- **Fact**: the corrected test reads the last request message, proves 0/33 hand mismatches against the paired turns, and pins the action tally (Fold 6 / Check 18 / Call 7 / Bet 1 / Raise 1), streets 14/7/6/6, 13 distinct hole-card pairs and the four catalog tools. The full suite is 443 with 442 pass and the unchanged tool-call gate red. The first version of the test parsed the stale memory index (20/33 mismatches, 11 hole pairs) and was corrected.
- **Implications**: the live behavior claim is now executable and aligned; D6 is unchanged because the model still made zero tool calls.
- **Confidence**: High
- **Agent**: orchestrator (read the test, ran the suite)
- **Related**: EVD-066, OBS-024, OBS-025

### VAL-008: The corrected behavior claim is Supported; the stale-index figures are Falsified

- **Target**: the live Ponder behavior claim and the first Round-8 test's street/hole figures.
- **Method**: recounted at both message indices; compared each call's last-message hand number with its paired turn; ran the full suite and the focused filter myself.
- **Evidence examined**: the session artifact; the corrected test; `round-8/full-suite.log` (443/442/1) and `focused-ponder-behavior.log` (1/1).
- **Result**: Supported for the corrected claim; the stale-index 11-hole figure is Falsified and replaced by 13.
- **Conclusion**: the action tally was always right (it comes from turns, not requests); the request-derived street/hole figures needed the last message. The artifact itself was consistent throughout.
- **Related**: OBS-023, OBS-024, OBS-025, EVD-065, EVD-066

### DEC-014: Harness round cap reached with D6 the only unmet dimension

- **Decision**: stop at harness Round 8 of 8 with the goal **not complete**. D6 is Partial; OQ-005 (P0) names the product-level fix. The goal stays active for a human decision; no further automatic round can close D6 inside this goal's scope.
- **Rationale**: every other dimension is Covered, the suite is 443 with 442 pass and one deliberate red gate, the live arm and Ponder run are recorded, and the deterministic/frozen state is unchanged. The only remaining item requires changing `TholdemRuntimeAgent`.
- **Alternatives considered**: rerunning the Ponder match or switching models (rejected: sampling); skipping or weakening the gate test (rejected); marking complete with a caveat (rejected: violates the explicit stop criteria).
- **Related**: OBS-026, OQ-005, RISK-006, DEC-010, DEC-013

---

### OBS-027: The closing regression confirms the non-D6 state after all test additions

- **Source**: closing `final-regression.ps1` run (EVD-067)
- **Fact**: 443-suite (442 pass, 1 red gate); legacy 7/7 in two fresh directories; five frozen hashes unchanged; arm pointers byte-identical; report sidecars match; key-prefix scan 0.
- **Implications**: the only open item is D6; nothing else regressed across eight harness rounds. The goal remains active because D6 cannot be closed in scope.
- **Confidence**: High
- **Agent**: orchestrator (ran the closing regression)
- **Related**: EVD-067, DEC-014

---

### OBS-028: The integrated final-evidence test pins every recorded artifact

- **Source**: `WIP/Wip.Tholdem.Tests/ModelHarness/FinalEvidenceTests.cs`; `docs/goal-llm-harness/round-9/full-suite.log` (EVD-068)
- **Fact**: one test now asserts the LLM arm report and sidecar (10 iterations, 0 promotions, 6213/800 tokens), the re-derived verdict (no-promotion, best LCB 1.6233195816931527, best mean 50.03, 10/10 lcb-below-margin, candidateMatchesProposal true), the deterministic report/sidecar (best LCB 2.396597390544052, mean 7.7), both pointer bytes, the live Ponder session (33/33 calls/turns, 0 tool calls, 6/18/7/1/1, streets 14/7/6/6, 13 hole pairs, 0 hand mismatches) and the five frozen hashes. The full suite is 444 with 443 pass and the single tool-call gate red.
- **Implications**: the final report's numbers are now executable; any drift in the recorded evidence fails a test. D6 is unchanged and remains the sole unmet requirement.
- **Confidence**: High
- **Agent**: orchestrator (read the test, ran the suite and filters)
- **Related**: EVD-068, OBS-027

### VAL-009: The integrated evidence test is Supported with zero literal changes

- **Target**: the final-evidence test's six artifact groups and every literal in them.
- **Method**: read the test; rebuilt; ran the full suite (444/443/1), the final-evidence filter (1/1) and the ModelHarness filter (50, 49 pass + the gate) myself. The implementer reported one test-first correction (the first draft parsed the stale memory index; the last-message hand-alignment assertion caught it).
- **Evidence examined**: EVD-068 and the logs it names.
- **Result**: Supported
- **Conclusion**: every supplied literal matched the recorded artifacts; no literal was changed to go green. The only red test is the deliberate D6 gate.
- **Related**: EVD-068, VAL-008

### DEC-015: Evidence is pinned; the goal stays active on D6 only

- **Decision**: keep the goal active and incomplete. The recorded evidence is now executable end-to-end; no in-scope work remains. A policy-compliant block requires a direct human turn, which this automatic round does not provide, and the disposition question returned no answer.
- **Rationale**: D6 needs a live tool call; the unchanged agent prompt yields none; the product change is out of scope; every alternative would be sampling or test weakening.
- **Alternatives considered**: rerun/switch model (rejected), weaken/skip the gate (rejected), mark complete (rejected: violates the stop criteria).
- **Related**: OBS-028, VAL-009, OQ-005, RISK-006, DEC-014

---

### OBS-029: A scoped D6 unblock proposal exists, unimplemented

- **Source**: `docs/goal-llm-harness/ponder-tool-use-proposal.md`
- **Fact**: the proposal names the exact prompt/task seam (`TholdemRuntimeAgent.cs:32-45`, `:166-180`), four options with scope/risk, the evidence for the prompt-suppression diagnosis, a fresh-seed one-match pre-registration, and the threat review. No product code was changed.
- **Implications**: the human decision needed to unblock D6 is now concrete and cheap; until it is made, the goal stays active/incomplete and the red gate stays red.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-041 to EVD-044, EVD-062, EVD-065, OQ-005

### DEC-016: Record the proposal; do not implement it without authorization

- **Decision**: publish the proposal as an artifact, keep the goal active, and do not touch `TholdemRuntimeAgent` or rerun the Ponder match in this automatic round.
- **Rationale**: the requirements say the runtime agent and its prompt are reused unchanged; the change is product-scoped and needs a human decision plus its own pre-registration.
- **Alternatives considered**: implementing option A now (rejected: out of scope, and it would invalidate the "existing agent unchanged" evidence); rerunning (rejected: sampling); marking complete (rejected).
- **Related**: OBS-029, OQ-005, RISK-006, DEC-015

---

### OBS-030: The live evidence now has durable byte-identical copies

- **Source**: `docs/goal-llm-harness/live-artifacts-mirror.md`; `round-3/live-artifacts/`, `round-4/live-artifacts/` (EVD-069)
- **Fact**: the ten LLM-arm transcripts, both champion-pointer copies, the Ponder session and the 33 Ponder transcripts are mirrored under `docs/goal-llm-harness/` with matching SHA-256 values on both sides. No product code, test or artifact was changed.
- **Implications**: the D2/D5/D6 audit surface survives a `.tmp` cleanup; the live claim no longer depends on scratch remaining in place. D6 is unchanged.
- **Confidence**: High
- **Agent**: orchestrator (copied and hashed both sides)
- **Related**: EVD-069, OBS-021

### DEC-017: Mirror the live evidence; leave the goal active

- **Decision**: mirror the live artifacts into the goal directory and keep the goal active/incomplete. The mirror is a durability measure, not a change to any claim.
- **Rationale**: the requirements make the transcript the audit surface; storing it only under `.tmp` would weaken the delivery after cleanup.
- **Alternatives considered**: pointing the tests at the docs copies (rejected for now: the operator-run tests intentionally read the scratch run and changing them is not necessary); leaving the evidence under `.tmp` (rejected: durability).
- **Related**: OBS-030, EVD-069, DEC-015

---

### OBS-031: The clean-room verification matches every recorded result

- **Source**: clean `--no-incremental` rebuild and full suite (EVD-070)
- **Fact**: build 0 errors, 16 pre-existing warnings; suite 444 with 443 pass and the one tool-call gate; key-prefix scan 0; HarnessSync check exit 0.
- **Implications**: the delivered state is reproducible from source, not dependent on incremental artifacts. The only red is the deliberate D6 gate.
- **Confidence**: High
- **Agent**: orchestrator (ran the clean verification)
- **Related**: EVD-070, EVD-067

### DEC-018: Do not mark the goal complete; the objective sentence is met but the contract gate is not

- **Decision**: the goal stays active/incomplete. The one-sentence objective (model-driven refinement through the gate with transcripts, a live 20-hand Ponder match with the four tools, byte-identical deterministic artifacts) is met, but the goal's own stop criteria also require D1–D9 covered and all 22 planned tests green. D6 is Partial and test 14 is red, so marking complete would contradict the contract and the final report.
- **Rationale**: the user's explicit STOP list governs; the D6 table's weaker wording does not override the named test 14 and the "all 22 green" criterion.
- **Alternatives considered**: mark complete on the objective sentence (rejected: contradicts the stop criteria and the report); mark blocked (rejected by policy without a human turn); rerun/sample (rejected).
- **Related**: OBS-031, OQ-005, RISK-006, DEC-014

---

### OBS-032: The current claim documents have no broken citations

- **Source**: citation audit (EVD-071)
- **Fact**: 21 backticked repo-relative paths and relative markdown links across the six current claim documents resolve to existing files; the behavior-proof validator separately resolves its 30 proof paths and passes 3/3.
- **Implications**: the audit surface is navigable; a later reader can open what the report cites. No claim changed.
- **Confidence**: High
- **Agent**: orchestrator (ran the audit)
- **Related**: EVD-071, EVD-053

---

### OBS-033: The unblock proposal now carries an exact patch sketch

- **Source**: `docs/goal-llm-harness/ponder-tool-use-proposal.md` (patch sketch section)
- **Fact**: the proposal shows the exact one-string diffs for option A (system prompt) and option B (task text), with the line anchors; neither is applied.
- **Implications**: the human decision is now a reviewable one-string change plus a one-match pre-registration, not an abstract choice. D6 and the red gate remain exactly as measured.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: OBS-029, EVD-041, OQ-005

---

### OBS-034: The authorized option A round still made zero tool calls

- **Source**: `docs/goal-llm-harness/round-10/ponder-live2.log`; `.tmp/goal-llm-harness/ponder/live2/ponder-session.json` (EVD-072)
- **Fact**: 20 hands, 39 model turns, 39 legal model actions (Fold 12 / Check 21 / Call 5 / Bet 1 / Raise 1), streets 18/9/9/3, 16 hole pairs, 0 tool calls, 0 errors, served `deepseek-flash`, 52,954/200 tokens. The first request's system message contains "You may call at most one of the provided tools"; the change was delivered.
- **Implications**: option A alone does not induce a tool call on this surface. D6 remains Partial; the D6 test now reads live2 and stays red; no rerun.
- **Confidence**: High
- **Agent**: orchestrator (ran the match, inspected the recorded prompt)
- **Related**: EVD-072, OBS-018

### VAL-010: "Option A induces a tool call" is Falsified

- **Target**: the hypothesis behind the authorized option A change.
- **Method**: pre-registered a fresh-seed one-attempt match, applied the one-string prompt change, verified the invitation in the recorded system message, ran the match, and counted tool calls in the session and transcripts.
- **Evidence examined**: the live2 session and 39 transcripts; the system message contains the invitation; `summary.toolCalls=0`.
- **Result**: Falsified
- **Conclusion**: the invitation is necessary-looking but not sufficient; the model still answers directly. The next options are B (task-text invitation), A+B, or C (bounded tool calls with the executor), or accepting D6 Partial.
- **Related**: OBS-034, OQ-005, EVD-072

### DEC-019: Report the null, keep the authorized change, and re-block

- **Decision**: keep the authorized option A change in the tree (it is a user-authorized product change, no tests break, the deterministic mode is unaffected), report the second round's null exactly, re-point the D6 test to the authorized run, and re-block the goal on D6. Do not attempt option B/C without a new authorization.
- **Rationale**: the user authorized A and one second round; both were done. The pre-registration says no rerun on failure.
- **Alternatives considered**: trying B/C now (rejected: not authorized); reverting A (rejected: user authorized it and it is harmless); weakening the test (rejected).
- **Related**: OBS-034, VAL-010, OQ-005, DEC-018

---

### OBS-035: The parallel round produced a clear winner

- **Source**: `docs/goal-llm-harness/round-11/variants-results.md`; the four sessions/logs (EVD-074)
- **Fact**: at fresh seed `1099512127776`, `task` made 0 tool calls in 55 turns, `both` 0 in 53, **`both-required` 54 tool calls in 61 turns / 106 model calls** (equity-calculator 44, pot-odds 10; 54 tool results; 61 legal actions; 0 errors), and `reasoner-both` 0 in 57 (requested `deepseek-reasoner`, served `deepseek-flash`). `BothRequired` is now the default; the D6 test passes; the suite is 446/446.
- **Implications**: D6 is resolved. The active ingredient is the *mandatory* task instruction; the optional system/task invitations alone changed nothing on this model.
- **Confidence**: High
- **Agent**: orchestrator (ran all four variants and inspected every session)
- **Related**: EVD-074, EVD-075, OQ-005

### VAL-011: The parallel-variant claims are Supported

- **Target**: the four variants, the winner rule and the adoption.
- **Method**: pre-registered seed and winner rule; ran the four matches concurrently; verified the system/task markers, response tool calls, tool result messages, action sources and errors; applied the frozen rule; re-ran the suite and the focused pivots; mirrored the winner's artifacts and hashed both sides.
- **Evidence examined**: EVD-074 to EVD-077.
- **Result**: Supported — V3 `both-required` is the sole qualifier (54 tool calls); V1/V2/V4 are null.
- **Conclusion**: OQ-005 is resolved: a mandatory task-surface tool instruction is what produced live tool use. The optional invitations are insufficient.
- **Related**: OBS-035, EVD-074 to EVD-077, OQ-005

### DEC-020: Adopt `BothRequired`, point the D6 test at the winner, and complete the goal

- **Decision**: adopt `TholdemToolPromptMode.BothRequired` as the default and the Ponder host default; point the D6 test at `v-required`; run the final regression; mark the goal complete because D1–D9 are Covered and all 22 planned tests are green (446/446, 0 failed, 0 skipped).
- **Rationale**: the winner rule was frozen before the run, V3 is the only qualifier, and every other dimension was already Covered; the final regression confirms legacy 7/7, the five frozen hashes, unchanged pointers, matching sidecars and 0 key hits.
- **Alternatives considered**: keeping the default at System and using the winner only in the host (rejected: "winner wins" means adoption); rerunning a losing variant (rejected); weakening any test (rejected).
- **Related**: OBS-035, VAL-011, EVD-074 to EVD-077

---

### OBS-036: Four live 6-seat rounds produced four coherent but gate-refused strategies

- **Source**: `docs/goal-llm-harness/ponder-loop/results.md`; `strategies-report.json` (EVD-078)
- **Fact**: 145 tool calls across four 6-seat live matches, 0 errors; every candidate loosened the incumbent (raise 0.55–0.62, call 0.55–0.66, bluff 0.18, sizing 1.15–1.25); the gate refused all four (LCB −40.49, −45.94, −34.51, −78.98) and the champion stayed 0.70/0.70.
- **Implications**: the 6-seat table pushes the refiner toward looser play, while the heads-up gate rewards tightening; the mismatch is structural to this loop shape (Ponder is a different game and the genome does not drive the Ponder agent). The strategies are exposed for inspection, not promoted.
- **Confidence**: High
- **Agent**: orchestrator (ran the loop, read every record)
- **Related**: EVD-078, EVD-079, OBS-017

### VAL-012: The loop claims are Supported

- **Target**: the four live rounds, the refiner proposals, the gate verdicts and the exposure.
- **Method**: ran the pre-registered loop; verified each session's modelKind/served/usage/errors, each refiner transcript's real response/usage/latency, each 24-seed evaluation's mean/LCB against the report; hashed every copied artifact.
- **Evidence examined**: EVD-078, EVD-079 and the artifacts they name.
- **Result**: Supported
- **Conclusion**: the loop ran as designed and the four strategies are real model proposals, all refused by the frozen gate. No promotion, no champion move, no test or gate change.
- **Related**: OBS-036, EVD-078, EVD-079

### DEC-021: Expose the four strategies; do not promote or rerun

- **Decision**: publish the four candidates, the model's reasons, the Ponder metrics and the gate verdicts; keep the champion unchanged; no rerun for a different draw. The loop is an exploratory exposure, not a promotion experiment.
- **Rationale**: the gate refused all four at 24 seeds; rerunning would be sampling. The structural mismatch (6-seat Ponder vs heads-up gate) is the finding to expose.
- **Alternatives considered**: feeding the genome into the Ponder prompt (a product change, not authorized); lowering the gate (forbidden); rerunning (sampling).
- **Related**: OBS-036, VAL-012

---

### OBS-037: The LLM's direction is the bottleneck; the clearing region is tight-aggressive

- **Source**: `docs/goal-llm-harness/ponder-loop/experiments-2.md` (EVD-080)
- **Fact**: across 21 LLM proposals (two loop batches, gate-aware 3, best-of-10), the best gate LCB was **2.568**; the model consistently lowered the raise threshold (0.55–0.68) and repeated its highest-mean loosening genome. A deterministic search found **0.95/0.20/0.10/1.0** clearing at 24/50/100 seeds (LCB 21.16/23.93/15.61), and a local search at raise 0.90 from the LLM's best proposal cleared (**0.90/0.30/0.14/1.05**, LCB 19.06 at 50). The clearing direction is higher raise (0.90–0.95), lower call (0.20–0.40).
- **Implications**: the model is not a reliable optimizer for this gate; a search-augmented loop (LLM proposes, local search refines, frozen gate decides) can promote. The gate is reachable and robust for the right genome; the old 0.80/0.40 fixture is seed-sensitive (cleared at 24 seeds on one base, −12.55 at 50 on another).
- **Confidence**: High
- **Agent**: orchestrator (ran all experiments)
- **Related**: EVD-080, OBS-036

### VAL-013: The second-series claims are Supported

- **Target**: the six-seat scoring, batch B, gate-aware/best-of, and the search/clearing results.
- **Method**: ran each experiment myself; read the reports/transcripts; recomputed the gate evaluations with the frozen evaluator/policy/verifier; compared the LLM's best LCB (2.568) with the search winners (23.931/19.057); checked the 24/50/100-seed robustness of the winner.
- **Evidence examined**: EVD-080, EVD-081 and the artifacts they name.
- **Result**: Supported — the LLM's failure and the search's success are both reproduced; the gate is reachable, the model's direction is wrong.
- **Conclusion**: the actionable design is a search-augmented loop; the model alone does not find the clearing strategy. The gate, components and deterministic mode are untouched.
- **Related**: OBS-037, EVD-080, EVD-081

### DEC-022: Expose the clearing strategies; recommend search augmentation

- **Decision**: publish the second-series results and the gate-clearing genomes; do not silently adopt a new champion or change the product/gate. The recommended next design is LLM-proposes + local-search-refines + frozen-gate-decides.
- **Rationale**: the LLM-only loop produced 21 refused proposals; the search produced verified promotions; the evidence is decisive. The user should decide whether to adopt `0.95/0.20/0.10/1.0` (or `0.90/0.30/0.14/1.05`) as the next champion.
- **Alternatives considered**: adopting the search winner now (possible; left to the user to avoid an unrequested product change); more LLM prompt tuning (rejected: 21 proposals, no clear); lowering the gate (forbidden).
- **Related**: OBS-037, VAL-013, EVD-080

---

### OBS-038: The search-augmented loop promoted a strategy the LLM never proposed

- **Source**: `docs/goal-llm-harness/ponder-loop/experiments-3.md` (EVD-082)
- **Fact**: round 1's LLM seed (0.60/0.55/0.18/1.25) was refined by a 100-point grid on disjoint seeds to **0.90/0.20/0.18/1.0** (search LCB 43.36 at 50); the frozen gate on untouched 24 seeds promoted it (mean 33.59, LCB **11.89**). Rounds 2–3 were refused. The champion is `tholdem-strategy-v2-6a108c2b`; fresh-base 24/50/100-seed checks all promoted (LCB 27.7–36.2).
- **Implications**: the LLM is a useful **seed** but not the optimizer; the decisive move (raise 0.90, call 0.20) came from evaluation. The hybrid is the first loop shape in this session that produced a verified promotion. The promotion is in the loop's scratch pointer, not the product's.
- **Confidence**: High
- **Agent**: orchestrator (ran the loop, verified the pointer/evaluations)
- **Related**: EVD-082, EVD-083, OBS-037

### VAL-014: The hybrid promotion is Supported

- **Target**: the round-1 promotion, the champion pointer, and the robustness.
- **Method**: read the report, pointer and canonical evaluation; checked mean 33.59375, LCB 11.8854 > 5, 24 seeds; re-evaluated the champion on two fresh base seeds at 24/50/100 seeds.
- **Evidence examined**: EVD-082, EVD-083 and the artifacts they name.
- **Result**: Supported — the gate promoted and verified; the genome clears on fresh seeds; no gate/component change.
- **Conclusion**: the hybrid loop works; the promoted strategy is real. No product pointer was changed; adoption is the user's call.
- **Related**: OBS-038, EVD-082, EVD-083

### DEC-023: Keep the promotion in the loop data; expose it for adoption

- **Decision**: record the promotion and the champion artifact under the loop's scratch data dir and the docs mirror; do not overwrite the product's champion pointer or the shipped artifacts. The user decides whether to adopt `0.90/0.20/0.18/1.0`.
- **Rationale**: the experiment's promotion is real and verified, but adopting it into the product is a separate, user-owned change; the behavior/test contract of the product champion is outside this exploratory run.
- **Alternatives considered**: adopting it now (deferred, user-owned); rerunning for a bigger effect (sampling; not needed — the result is robust).
- **Related**: OBS-038, VAL-014, EVD-082

---

<!-- New entries go below this line -->
