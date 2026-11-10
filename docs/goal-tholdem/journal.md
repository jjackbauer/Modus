# Goal Journal — tholdem

> Append-only. See the stretch-goal-harness evidence protocol for entry format.

---

<!-- New entries go below this line -->

### DEC-001: Working name and project placement

- **Decision**: Use `Wip.Tholdem` (never `Wip.Holdem`). Projects live under `WIP/`: `Wip.Tholdem`, `Wip.Tholdem.Agents`, `Wip.Tholdem.Host`, `Wip.Tholdem.Tests`.
- **Rationale**: `project-reference-graph` only scans `WIP/` (`tests/Wip.Architecture.Tests/ProjectReferenceGraph.cs:15`), and `verify-solution-coverage.ps1` requires every csproj under `WIP/` to appear in `Wip.slnx` (`tools/verify-solution-coverage.ps1:24-46`). Placing the projects under `WIP/` is the only placement that enters both the architecture graph and the CI solution-coverage check without weakening them.
- **Alternatives considered**: Top-level `src/Tholdem/` or `Tholdem/` — rejected: invisible to the architecture graph and to the coverage script; would require changing the architecture test to scan a second root, which widens rather than satisfies the rule.
- **Related**: EVD-003, EVD-004, EVD-014

### DEC-002: Verification substrate given the local VSTest limitation

- **Decision**: Treat `dotnet build`, the supplementary xUnit runner `.tmp/tholdem-verify/RunXunit`, and direct CLI executions as local verification; never report a test suite as green unless one of those observed it. Attempt WSL as a supplementary substrate; if unavailable, record it and rely on CI for authoritative `dotnet test`.
- **Rationale**: `dotnet test` aborts locally with `Win32Exception (5)` on the testhost parent-process handle (EVD-007), and the limitation is already documented (`docs/runbooks/operator-guide.md:109`). The supplementary runner executes the same compiled test assembly and reproduced the known-green architecture suite 4/4 (EVD-008).
- **Alternatives considered**: (a) Editing the repo to remove VSTest — rejected, weakens CI; (b) claiming CI green without running it — forbidden; (c) generating TRX files by hand — rejected as fabrication.
- **Related**: EVD-007, EVD-008, EVD-012, RISK-001

### RISK-001: CI cannot be observed from this machine

- **Risk**: The objective asks for `Tests CI` (six jobs) and `Modus CI` to run, but this machine has no `gh` CLI and no GitHub token (EVD-012), so workflow runs cannot be dispatched or observed, and no run IDs can be cited.
- **Likelihood**: High — already observed.
- **Impact**: Medium — a release-grade claim would be impossible; the deliverable can still be a working vertical slice with the CI surface updated and locally verifiable.
- **Evidence**: EVD-012
- **Mitigation**: Update the CI surface exactly (solution, architecture, waivers, offline-runner project list, security job list), run every local equivalent that is executable, and report CI as NOT RUN with the concrete missing capability named — never as green.
- **Gate**: —

### RISK-002: Over-large implementer dispatches return compile-broken or stubbed work

- **Risk**: A single implementer asked for the whole engine can return partially wired types, `NotImplementedException`, or tests that only assert `NotNull`.
- **Likelihood**: Medium
- **Impact**: High — would consume rounds and produce no EVD.
- **Evidence**: —
- **Mitigation**: Split the engine into evaluator/deck (round 1) and table/legality/showdown (round 2); require per-test concrete literals; verify by opening files, running the build, and running the compiled test assembly; replace any stub by re-dispatch, never by repairing it myself.
- **Gate**: —

### OQ-001: Will WSL provide an executable `dotnet test` substrate?

- **Question**: After installing the .NET 10 SDK in WSL Ubuntu, does `dotnet test` complete there for `Wip.Tholdem.Tests`?
- **Needed evidence**: `wsl -d Ubuntu -- ~/.dotnet/dotnet --version` plus a completed `dotnet test` run with a non-zero test count and exit code.
- **Priority**: P1 important
- **Raised by**: Round 1, orchestrator
- **Related**: EVD-007, DEC-002, RISK-001

### TODO-001: Round 1 — engine card model, seeded deck, 7-card evaluator

- **Task**: Create `Wip.Tholdem` and `Wip.Tholdem.Tests`, wire both into `Wip.slnx` and the architecture graph, and deliver the card/deck/evaluator core with known-vector tests.
- **Priority**: P0
- **Depends on**: —
- **Status**: In progress
- **Related**: DEC-001, EVD-003, EVD-004

### RISK-003: Another session is editing this working tree concurrently

- **Risk**: Between 10:17 and 10:26 local time, `docs/execution/harness/{goal,journal,evidence,state}.md` were modified by something other than this goal (their content describes a "Bones agents on the generic WIP agent runtime" goal, EVD-086..094, which this goal never produced). If that session also touches `Wip.slnx`, `tests/Wip.Architecture.Tests/*`, `docs/plan/test-waivers.json`, or `tools/run-wip-tests.ps1`, my round evidence could be attributed to the wrong writer or silently broken.
- **Likelihood**: Medium — already observed once.
- **Impact**: Medium — verification diffing and re-running commands mitigates it; a false "green" is the failure mode.
- **Evidence**: EVD-015
- **Mitigation**: Before recording each round, re-read `git status` and `git diff` for every integration file I claim was edited; verify the exact expected hunk is present; never `git checkout`/revert another writer's files; report unrelated working-tree changes in the final report.
- **Gate**: —

### EVD-015: Unrelated concurrent edits exist in the working tree

- **Note**: See `evidence.md` EVD-015 for the verified command output.

### OQ-002: Should the WSL Linux substrate be used for a real `dotnet test` run before convergence?

- **Question**: Can the repository be synced into the WSL filesystem and built/tested there with `dotnet test` to produce a genuine VSTest result for `Wip.Tholdem.Tests`?
- **Needed evidence**: A completed WSL `dotnet test` run with a non-zero discovered test count and exit code, or a recorded failure that names the missing dependency.
- **Priority**: P2 curiosity
- **Raised by**: Round 1, orchestrator
- **Related**: OQ-001, EVD-007, DEC-002

---

## Round 1 record (2026-11-06)

### VAL-001: Round-1 claim batch — cards, deck, evaluator, integration

- **Target**: round-1 claim batch (implementer 0d84ebc2)
- **Method**: Opened every delivered source and test file; re-ran `dotnet build Wip.slnx`; re-ran the compiled suites with the supplementary runner; re-ran `verify-solution-coverage.ps1` and `HarnessSync --check`; re-derived the deck order independently in PowerShell; built an independent reference evaluator and compared 20,000 random 7-card hands plus 2,000 best-five checks; ran two mutation experiments with hash-verified restore.
- **Evidence examined**: EVD-017…EVD-031; `.tmp/tholdem-verify/derive-deck.ps1`; `.tmp/tholdem-verify/EngineProbe`; mutations of `HandEvaluator.cs` and `SeededDeck.cs`
- **Result**: Supported — every claimed verification reproduced, with two quality defects found by mutation (not falsified claims): 2 evaluator tie tests survive a `return default` evaluator (EVD-024), and 4 of 6 deck tests are invariant checks that a disabled shuffle passes (EVD-025).
- **Conclusion**: The card/deck/evaluator foundation is real and independently corroborated. The two weak tests must be strengthened before D2 is recorded as fully covered; that is folded into round 2 rather than repaired by the orchestrator. A first probe run produced a disagreement that turned out to be the probe's own tiebreak convention, not the library — recorded here so a later round does not treat it as a library defect.
- **Related**: EVD-022, EVD-023, EVD-024, EVD-025, TODO-002

### TODO-002: Strengthen the two stub-survivable evaluator tests

- **Task**: Make `Evaluate_GivenBoardPlays_ExpectedIdenticalBestFive` and `Evaluate_GivenSameCardsInDifferentOrder_ExpectedEqualScore` assert the concrete tiebreakers (`[14,13,12,11,9]`) and the pinned `Score`, so a silently-wrong evaluator fails them. Owner: round-2 implementer.
- **Priority**: P1
- **Depends on**: —
- **Status**: Open
- **Related**: EVD-024

### DEC-003: Round 2 scope — heads-up hand engine, history hash, observation boundary

- **Decision**: Round 2 delivers `TableState`, `LegalActionSet`, `PokerAction`, `HeadsUpHandEngine` (blind posting, preflop/postflop order, min-raise, all-in, street progression, showdown, pot award, split-pot odd-chip rule), `HandRecord` canonical JSON + SHA-256 hash + replay, and `PlayerObservation` with a hidden-information boundary that excludes the seed, the deck and the opponent's hole cards. It also strengthens the two weak tests from TODO-002. No new project, so the architecture count stays at 45.
- **Rationale**: Everything later (baseline agents, simulator, runtime agent tools, learning loop) consumes exactly this surface; pinning the action vocabulary and the observation boundary now prevents four downstream re-designs. `PlayerObservation` excluding the *seed* is the non-obvious part: a seed leak reconstructs the deck and would be a hidden-information vulnerability (EVD-009 shows the runtime passes observations into model prompts).
- **Alternatives considered**: (a) Let agents see `TableState` with hidden fields redacted — rejected, a single missing projection leaks the deck; (b) async engine API now — rejected, determinism and replay are easier to prove synchronously, and the runtime agent can adapt at its own boundary.
- **Related**: DEC-001, EVD-019, EVD-023, TODO-002, TODO-003

### TODO-003: Round 2 — heads-up hand engine, history, observation

- **Task**: Deliver `WIP/Wip.Tholdem/Engine/*`, `WIP/Wip.Tholdem/History/*`, their tests, the strengthened evaluator tests, and the updated waiver count.
- **Priority**: P0
- **Depends on**: Round-1 engine foundation (verified)
- **Status**: Done
- **Related**: DEC-003, TODO-002

### VAL-002: Round-2 claim batch — engine, history, observation boundary

- **Target**: round-2 claim batch (implementer aa88ebdc)
- **Method**: Read all ten engine/history source files and the canonical writers; rebuilt and re-ran the 52-case suite; built `.tmp/tholdem-verify/HandProbe`, which reflects on the internal hole cards, deck and action log to independently re-check every showdown, replay, hash and hidden-information claim over 500 random legal hands; ran two mutations (stubbed evaluator; broken `LastRaiseSize`) with hash-verified restore; re-ran the solution build, architecture suite and coverage script.
- **Evidence examined**: EVD-033…EVD-041; `WIP/Wip.Tholdem/Engine/HeadsUpHandEngine.cs:69-109,475-583`; `.tmp/tholdem-verify/HandProbe`; mutations of `HandEvaluator.cs` and `HeadsUpHandEngine.cs`
- **Result**: Supported — 500/500 hands completed, 187 showdowns, zero discrepancies across conservation, termination, board progression, winner recomputation, canonical-JSON stability, SHA-256 recomputation, replay equivalence, input immutability, and observation leakage. Both mutations were caught (16 failures and 1 failure respectively).
- **Conclusion**: D1's engine half is covered at High confidence; the CLI-level determinism observable still belongs to D6. Two dispatch errors were corrected by the implementer and accepted here (DEC-004). One residual immutability weakness (mutable arrays behind `IReadOnlyList`) is recorded as RISK-004 and left for hardening.
- **Related**: EVD-034, EVD-035, EVD-036, EVD-037, DEC-004, RISK-004

### DEC-004: Accept the two round-2 spec corrections

- **Decision**: Accept `Start` returning stacks `[995, 990]` with pot 15 (not `[1000,1000]`), and accept that the odd-chip split rule is defensive because legal heads-up showdown pots are always even. Record both in the docs rather than forcing the engine to match a contradictory dispatch.
- **Rationale**: `995 + 990 + 15 == 2000` keeps the conservation invariant (EVD-038); `[1000,1000] + 15` would total 2015. Both players start with equal stacks and every commitment is matched, so a contested pot is `2 × matched` and even (EVD-039); the odd-chip function is still unit-tested directly at 21.
- **Alternatives considered**: (a) Have `Start` report `[1000,1000]` and treat blinds as "not yet deducted" — rejected, it would break both the posted-blind state and conservation; (b) drop the odd-chip rule — rejected, it is a documented defensive rule and removing tested code would reduce coverage.
- **Related**: EVD-038, EVD-039, EVD-035

### RISK-004: `IReadOnlyList` properties are backed by mutable arrays

- **Risk**: `TableState.Stacks`, `TableState.Board`, `TableState.Winners`, `PlayerObservation.OwnHoleCards` and `HandRecord` card lists are typed `IReadOnlyList<T>` but backed by `T[]`, so a caller can cast and mutate a supposedly immutable state or observation copy.
- **Likelihood**: Low — the runtime agent only ever receives its own `PlayerObservation` copy, and an agent cannot reach `TableState`.
- **Impact**: Low for security, Medium for the "immutable table state" claim.
- **Evidence**: `WIP/Wip.Tholdem/Engine/TableState.cs:37-53`; `WIP/Wip.Tholdem/History/PlayerObservation.cs:74-86`
- **Mitigation**: Harden with `Array.AsReadOnly` in a later round and add a test that the exposed collections are not arrays. Not blocking the current slice.
- **Gate**: —

### OQ-003: `Street.Showdown` is declared but never produced

- **Question**: Should the engine emit `Street.Showdown` before `Street.Complete`, or is the value reserved?
- **Needed evidence**: A round-3+ consumer needing to distinguish "showdown in progress" from "hand complete"; otherwise document the value as reserved.
- **Priority**: P2 curiosity
- **Raised by**: Round 2, orchestrator
- **Related**: EVD-033

### TODO-004: Round 3 — baseline agents, equity, simulator, reproducible histories

- **Task**: Create `Wip.Tholdem.Agents` with `IPokerAgent`, deterministic baseline agents, seeded Monte Carlo equity, a batch simulator with metrics/confidence intervals and byte-identical hand-history output; wire the project into the solution, architecture graph (45→46) and waiver register.
- **Priority**: P0
- **Depends on**: Round-2 engine (verified)
- **Status**: In progress
- **Related**: DEC-005

### DEC-005: Round 3 contract — agents receive observations only; determinism is derived from (seed, hand, seat, action index)

- **Decision**: `IPokerAgent.ActAsync(PlayerObservation observation, AgentTurnContext context, CancellationToken)`, where `AgentTurnContext` carries `SessionSeed`, `HandNumber`, `Seat` and a per-hand `ActionIndex`. Every random choice inside an agent derives from a splitmix64 of those four values, so an agent has no clock, no ambient randomness and no access to `TableState`. Metrics are computed by a pure `MetricsCalculator` over per-hand outcomes; confidence intervals are Wilson 95% for rates and a normal approximation over per-hand big-blind results for bb/100. History files are canonical JSON per hand plus a manifest whose hash is the session fingerprint.
- **Rationale**: A per-turn derived seed is the only design that makes "same seed + same agents ⇒ byte-identical histories" provable without giving the agent a hidden-information channel. Putting the metric formulas in a pure function makes them testable with literal inputs rather than through a 10,000-hand simulation.
- **Alternatives considered**: (a) Give each agent a `Random` instance seeded once — rejected, the stream depends on call order and breaks if the simulator parallelises or a hand is re-run; (b) let agents read `TableState` — rejected, it leaks the deck; (c) compute metrics inline in the simulator — rejected, untestable without a full simulation.
- **Related**: DEC-003, RISK-004, TODO-004

### DEC-006: Visualization UI scope, data-dir contract, and the spectator/agent boundary

- **Decision**: Add `Wip.Tholdem.Web` (ASP.NET Core minimal API + static `wwwroot/viewer` shell, mirroring `Wip.Bones.Web`'s structure: `Sdk.Web`, host class with `Add…Services`/`Map…Routes`, fixed-order JSON options, `wwwroot/viewer/index.html` + css + js). Data dir defaults to `artifacts/tholdem/` (gitignored) and is overridable by `--data-dir`. The UI reads a frozen directory schema so the learning-loop round can populate it without UI rework: `sessions/<sessionId>/{session.json,hand-NNNNN.json}`, `strategies/<strategyId>.json`, `ledger.json`, `active-champion.json`.
- **Rationale**: Mirroring the existing viewer keeps the two UIs recognizably the same product family and reuses the repo's proven hosting/test pattern. A frozen data-dir schema decouples the UI round from the learning-loop round, which is the difference between one round and three.
- **Spectator boundary**: a live/recorded spectator view intentionally shows both players' hole cards. That type (`SpectatorSnapshot`) stays inside `Wip.Tholdem.Web`; a source-scan test must prove no file under `WIP/Wip.Tholdem.Agents` references it, and the agent-facing `PlayerObservation` remains the only model surface (already leak-tested by EVD-035).
- **Alternatives considered**: (a) Blazor interactive server — rejected, heavier and unlike Bones; (b) a client-only SPA reading a static JSON export — rejected, cannot show a live match; (c) export the spectator view as a `HandRecord` — rejected, that would let an agent-shaped consumer read the deck seed.
- **Related**: DEC-003, EVD-035, D9, TODO-005

### VAL-003: Round-3 claim batch — baselines, equity, simulator, metrics

- **Target**: round-3 claim batch (implementer c5592a91)
- **Method**: Read `MonteCarloEquity`, `MetricsCalculator` and the equity tests; rebuilt and re-ran the 76-case suite; ran the architecture suite and coverage script; built `.tmp/tholdem-verify/SimProbe`, which re-runs two 120-hand simulations, compares every written byte, recomputes all five metrics per agent from the hand-history JSON, and cross-checks the pinned equity with an independent 200,000-sample estimator using a different generator and sampling loop; ran a `bb/100` scale mutation with hash-verified restore.
- **Evidence examined**: EVD-042…EVD-048; `WIP/Wip.Tholdem.Agents/Equity/MonteCarloEquity.cs:96-140`; `WIP/Wip.Tholdem.Agents/Simulation/MetricsCalculator.cs:66-96`; `.tmp/tholdem-verify/SimProbe`
- **Result**: Supported. 120 hands reproduced byte-for-byte, manifest `e9a43faff53a2a06…`, `net[0]+net[1]==0`, and every reported metric matched the value recomputed from the artifacts. Independent random-opponent equity `0.853073` vs the pinned `0.851125` (delta `0.0019`), and fixed-opponent `0.828070` vs the exhaustive `0.826366` — confirming the two models differ exactly as the implementer said. The `bb/100` mutation failed 2 tests.
- **Conclusion**: D3 is covered at High confidence. The implementer's one contest with my dispatch was correct and is recorded as DEC-007 (my error). D1/D2 remain supported; the UI (D9) is next because the user asked for it explicitly.
- **Related**: EVD-044, EVD-045, EVD-046, DEC-007

### DEC-007: My 0.826366 equity requirement was wrong for the contracted API

- **Decision**: Accept `MonteCarloEquity.Estimate` as a random-opponent estimator and treat the round-3 test's pinned `0.851125` (seed 7, 20k samples) as correct, with the exhaustive turn-board check as the real accuracy guard. Do not add a fixed-opponent overload.
- **Rationale**: My dispatch told the implementer to compare against 0.826366, which is the exhaustive equity of `As Ah` against the **fixed** `Ks Kh` (EVD-032). The contracted four-argument API has no opponent parameter, so it necessarily models a uniformly random opponent; the independent 200k-sample probe gives 0.853073 (within 0.005 of the pin), and a fixed-opponent probe gives 0.828070 (within 0.01 of 0.826366). A fixed-opponent overload would also hand an agent a hidden-information channel.
- **Alternatives considered**: (a) Force the pin to 0.826366 by biasing the sampler — rejected, wrong model; (b) add an opponent-cards overload — rejected, hidden-information surface; (c) drop the exhaustive check — rejected, it is the genuine accuracy evidence (45,540 turn boards).
- **Related**: EVD-032, EVD-044, DEC-006

### TODO-004 status

- **Status**: Done (see VAL-003)

### TODO-005: Round 4 — Bones-style visualization UI

- **Task**: Create `Wip.Tholdem.Web` with the live match feed, recorded-session replay, strategies/ledger views, server-rendered table HTML, and committed tests over the real HTTP surface; wire the project into the solution (46→47), the architecture graph, and the waiver register.
- **Priority**: P0 (explicit user request)
- **Depends on**: Round-3 simulator (verified)
- **Status**: In progress
- **Related**: DEC-006, VAL-003, D9

### RISK-005: The sandbox blocks `%TEMP%` for workspace-launched executables

- **Risk**: Test or tooling processes started from this workspace cannot create directories under `%TEMP%` (`UnauthorizedAccessException`/`EPERM`). This broke the first SimProbe run and the first headless-Chromium launch; it could also break any future sub-process helper that assumes the system temp directory.
- **Likelihood**: High — reproduced twice (EVD-048, EVD-049).
- **Impact**: Medium — verification tooling only; the product itself never writes to `%TEMP%`.
- **Evidence**: EVD-048, EVD-049
- **Mitigation**: all orchestrator probes and the Playwright tool redirect `TEMP`/`TMP` to a workspace directory (`.tmp/...`); test scratch stays under the test output directory. Recorded so a later round does not re-diagnose it.
- **Gate**: —

### EVD-049: Headless Chromium works once `TEMP` is redirected into the workspace

- **Note**: See `evidence.md` EVD-049 for the observed smoke-test output.

### VAL-004: Round-4 claim batch — visualization UI

- **Target**: round-4 claim batch (implementer b92022b3)
- **Method**: Re-ran the 86-case suite, architecture suite, coverage and waiver checks; started the real server on loopback and drove `/api/status`, `/`, `/viewer/index.html`, `POST /api/live`, polling, `/api/sessions`, session detail, `/viewer/table`, `/api/strategies`, `/api/ledger`, `/api/champion`; compared the live feed's per-hand hashes against the simulator's own hand files; opened the page in headless Chromium, started a match through the form and captured a screenshot; read the file-reading catalogs for path traversal; scanned the viewer for external URLs and the Agents sources for spectator references.
- **Evidence examined**: EVD-050…EVD-059; `docs/goal-tholdem/round-4/viewer.png`
- **Result**: Supported. 86/86 tests pass, 47/47 coverage, count 47, waiver 86. Every HTTP observable matched the contract. The live feed's five hand hashes were byte-identical to the simulator's artifacts, so the UI does not fork the engine rules. The screenshot shows a real hand (board `7d 5d 4s`, hole cards on both seats, 15 log rows) with zero console errors.
- **Conclusion**: D9 is covered at High confidence. One quality gap is recorded as TODO-006: the promotion-ledger table renders each entry's raw JSON in a single narrow column (header wraps, payload unreadable) and the strategies detail requires a click; the ledger schema becomes real in round 6, so the rendering fix belongs there with a committed test.
- **Related**: EVD-054, EVD-057, TODO-006, D9

### TODO-006: Render promotion-ledger entries as columns, not raw JSON

- **Task**: In `wwwroot/viewer/app.js` and the strategy/ledger markup, render ledger fields as a table (iteration, champion, challenger, promoted, reason, win rate, bb/100, LCB delta, sample, margin) and give the strategies list a visible selected-detail panel. Add a committed test asserting the ledger markup labels exist in `index.html`/`app.js` and, if feasible, a server-rendered ledger fragment test. Owner: round 6 (learning loop), where the ledger schema becomes real.
- **Priority**: P1
- **Depends on**: Round-6 ledger writer
- **Status**: Open
- **Related**: EVD-057, D5, D9

### TODO-005 status

- **Status**: Done (see VAL-004)

### TODO-007: Round 5 — LLM runtime agent over the WIP agent runtime

- **Task**: Add `TholdemRuntimeAgent` built on `IChatModel` -> `IAgentExecutor` -> `RuntimeToolInvoker` -> `ArtifactAgentMemory`, with equity, pot-odds, session hand-history and opponent-statistics tools, deterministic rejection of illegal/malformed model actions, budget enforcement, and the security suites (hidden-information leakage, tool-permission denial, prompt injection, truncation).
- **Priority**: P0
- **Depends on**: Round-3 agents (verified), round-4 UI (verified)
- **Status**: Done
- **Related**: DEC-009

### VAL-005: Round-5 claim batch — LLM runtime agent and tool boundary

- **Target**: round-5 claim batch (implementer 890dc98e)
- **Method**: Re-ran the 103-case suite; read the agent, the tool catalog and the security tests line by line; checked the hidden-information test is non-vacuous (it enumerates 50 forbidden cards and the seed); checked the permission-denial test consults the evaluator for `artifact.read`; ran an independent mutation on `LiveModelGate` with a hash-verified restore; re-ran architecture, coverage and boundary scans.
- **Evidence examined**: EVD-060…066; `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs`; `WIP/Wip.Tholdem.Tests/Runtime/TholdemRuntimeAgentTests.cs:161-243`
- **Result**: Supported. 103/103 tests pass after restore. The agent never receives `TableState`, ignores the turn context, validates every model action against the observation, caps the final message size, and its fallback is legal by construction. The gate mutant failed exactly one test.
- **Conclusion**: D4 and the agent-facing half of D7 are covered at High confidence. Two items remain: the learning loop (D5) and CLI/ops (D6). The UI's raw-JSON ledger rendering (TODO-006) is folded into round 6 because the ledger writer lands there.
- **Related**: EVD-061, EVD-062, EVD-063, EVD-064, EVD-065, TODO-008

### DEC-009: Round-6 learning-loop contract — data-only strategies, paired held-out evaluation, verifier-enforced gate

- **Decision**: Strategies are data (`StrategyGenome` parameters plus prompt text, content-hashed ids, no timestamps). Evaluation is paired on held-out seeds: for each seed the challenger plays once in each seat against the incumbent, per-hand big-blind differences give a mean with a normal 95% interval, and promotion requires `lowerBound > margin` **and** `seeds >= minSeeds`. A `PromotionVerifier` re-derives the decision from the report so an `AlwaysPromotePolicy` cannot pass. The loop persists `strategies/*.json`, `evaluations/*.json`, `ledger.json` and `active-champion.json` in the viewer's frozen schema, and never merges or deploys.
- **Rationale**: Paired seeds remove the deal variance that makes unpaired win-rate comparisons promote noise; a re-deriving verifier is the only way to make "a control that always promotes must fail" a property of the pipeline rather than of one policy class. Content-hashed ids make artifact paths reproducible.
- **Alternatives considered**: (a) promote on win-rate point estimate — rejected, trivially gameable by variance; (b) trust the policy's decision — rejected, an always-promote control would pass; (c) store strategies as executed code — rejected by the safety constraint; they stay data.
- **Related**: DEC-005, TODO-006, TODO-008, D5

### DEC-010: UI enhancement + independent review + remediation rounds added at user request

- **Decision**: Insert three UI-focused rounds after the learning loop: (a) an Implementer upgrades the viewer — real playing-card faces instead of text badges, hand-strength names, dealer button, to-call/pot-odds, winner highlighting, action-log styling, replay rendered in the main table area, and readable strategy/ledger panels; (b) an independent Reviewer (a different agent, no knowledge of the implementer's choices) performs a critical review against the running app, multiple viewports and screenshots, and writes severity-ranked findings to `docs/goal-tholdem/round-7/review.md`; (c) a second Implementer remediates every P0/P1 finding from that file and re-verifies. The round cap is raised to 12 to accommodate this user-requested scope.
- **Rationale**: The user asked for actual cards plus UX improvements, a critical review, and then remediation. Splitting review from implementation is the harness's independent-verification principle applied to UI quality: the reviewer must not have written the code it reviews.
- **Alternatives considered**: (a) let the same agent review its own work — rejected, self-review confirms its own assumptions; (b) skip the review and hand-polish — rejected, the user explicitly asked for a critical review; (c) review the whole product rather than the UI — deferred to the round-9 adversarial pass, which covers the full delivery.
- **Related**: DEC-006, TODO-006, EVD-057, D9

### VAL-006: Round-6 claim batch — learning loop, gate, verifier, persistence

- **Target**: round-6 claim batch (implementer 2b703679)
- **Method**: Re-ran the 124-case suite (≈115 s); read `PromotionVerifier` and the always-promote control test; ran the loop end-to-end through `.tmp/tholdem-verify/TrainProbe` into the viewer's data dir; served the results through the running viewer; captured the ledger panel in Chromium; re-ran architecture/coverage/waiver.
- **Evidence examined**: EVD-068…EVD-072; `docs/goal-tholdem/round-6/ledger.png`; `artifacts/tholdem/{strategies,ledger.json,active-champion.json}`
- **Result**: Supported. 124/124 tests pass. The verifier re-derives the criterion and the control is refused with `verification-failed`, champion unchanged and no pointer written. The loop produced four real artifacts and three ledger rows that the viewer serves. The ledger columns render but are unreadable at sidebar width (EVD-071).
- **Conclusion**: D5 is covered at High confidence for the gate/control property. Four quality gaps are recorded, not hidden: per-run ledger overwrite (TODO-008), silent champion-pointer fallback (RISK-007/TODO-009), behaviourally inert proposals in 2 of 3 iterations (OQ-004), and a constant VPIP in the analysis (OQ-005). The cramped ledger becomes part of the UI round.
- **Related**: EVD-069, EVD-070, EVD-071, TODO-008, TODO-009, TODO-010, OQ-004, OQ-005

### RISK-007: A stale or corrupt champion pointer silently falls back to the default champion

- **Risk**: `TrainingLoop.LoadChampion` reads `active-champion.json`, cannot load the referenced artifact (my earlier demo sample lacked the new fields), and silently substitutes the default genome — leaving the pointer naming one strategy while the loop trains another. A corrupt pointer would do the same with no signal.
- **Likelihood**: Medium — already triggered once by a schema-incompatible artifact.
- **Impact**: Medium — auditability: the persisted `active-champion.json` can disagree with the strategy the loop actually used.
- **Evidence**: `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:163-180`; EVD-070 (`active-champion.json` still named `abc12345` while the run champion was `v1-f100e793`)
- **Mitigation**: fail loudly on an unloadable pointer, or rewrite the pointer to the champion actually used, and add a test for both. Assigned to TODO-009 (CLI/ops round).
- **Gate**: —

### TODO-008: Accumulate the champion/challenger ledger across runs

- **Task**: `TrainingLoop` currently rewrites `ledger.json` with only the current run's entries. Append to the existing ledger (bounded, deduplicated by iteration+ids) so the viewer shows history across runs, and add a test for two sequential runs.
- **Priority**: P1
- **Depends on**: Round-6 loop (verified)
- **Status**: Open
- **Related**: EVD-070, D5

### TODO-009: Make the champion pointer consistent or fail loudly

- **Task**: On an unloadable `active-champion.json`, either throw with the strategy id and path, or rewrite the pointer to the champion actually used, and test both.
- **Priority**: P1
- **Depends on**: RISK-007
- **Status**: Open
- **Related**: RISK-007, EVD-070

### OQ-004: Proposals can be behaviourally inert

- **Question**: Two of three iterations produced `meanBb100 == 0.000` exactly, because a 0.02 threshold nudge changed no decision. Should the proposer reject a genome whose behaviour matches the incumbent on a probe set before spending an evaluation?
- **Needed evidence**: A behavioural-difference check (small fixed probe of observations) and a test that a no-op mutation is re-proposed or rejected.
- **Priority**: P1 important
- **Raised by**: Round 6, orchestrator probe
- **Related**: EVD-070, D5

### OQ-005: `SessionAnalysis.Vpip` is always 0

- **Question**: The aggregate `SimulationReport` has no action counts, so VPIP (and any preflop-aggression statistic) is reported as 0 and cannot inform proposals. Should the analyzer read the written hand histories instead?
- **Needed evidence**: Derive VPIP/PFR from `hand-*.json` actions and assert hand-crafted values.
- **Priority**: P2 curiosity
- **Raised by**: Round 6, implementer disclosure
- **Related**: EVD-070, TODO-008

### DEC-011: "Do both" — deepen the two-seat UI now, add multiway after the slice

- **Decision**: The user chose both options: (a) immediately deepen the heads-up viewer (real card faces, main-area replay, tabbed Strategies/Ledger with readable columns, hand strength, dealer button, to-call/pot odds, keyboard shortcuts, responsive layout); (b) after the remaining heads-up rounds (UI review + remediation, CLI/docs/CI, adversarial pass), add a multiway objective (4–6 seats with side pots, N-seat observation/history, N-seat simulator and viewer). Round cap raised to 14 to fit the multiway work.
- **Rationale**: Two seats is frozen scope for the *engine* (no side pots), so the UI can be deepened without touching the verified invariants; multiway is a genuine engine project that supersedes the heads-up evidence and must be its own objective, not a quiet widening of the current one.
- **Alternatives considered**: (a) multiway immediately — rejected, it would pause a nearly complete vertical slice and invalidate D1's evidence mid-flight; (b) UI only — rejected, the user asked for both.
- **Related**: DEC-010, D9, TODO-010, EVD-071

### TODO-010: Round 7 — real card faces and the viewer UX upgrade

- **Task**: CSS-drawn playing cards (rank + ♠♥♦♣, red/black, face-down backs, empty slots), replay rendered in the main table area, tabbed Strategies/Ledger with readable full-width tables, hand-strength names, dealer button, to-call/pot odds, winner highlighting, colour-coded action log, keyboard shortcuts, responsive layout, inline favicon; server additions for hand names and button seat with tests.
- **Priority**: P0 (explicit user request)
- **Depends on**: Round-6 viewer (verified)
- **Status**: Done for the implementation (review pending)
- **Related**: DEC-011, EVD-071, D9

### VAL-007: Round-7a implementation batch — card faces and UX upgrade

- **Target**: round-7a implementation (0fde535f) — review findings still pending (7b)
- **Method**: Re-ran the 130-case suite; rebuilt the Web project, served it from a copied output, fetched the replay HTML and checked specific card/suit/name/badge markers; started a live match over HTTP and checked the new snapshot fields; captured live and replay screenshots in headless Chromium and viewed them.
- **Evidence examined**: EVD-073…076; `docs/goal-tholdem/round-7/orch-live.png`, `orch-replay.png`
- **Result**: Supported. 130/130 tests pass. Cards render with correct `data-card` values and suit classes; `♠` and `♣` both appear; hand names and the winner badge are present; live snapshots carry `handNames`, `buttonSeat`, `amountToCall`, `potOdds`; screenshots show tabs, dealer button, winner highlight, colour-coded log and zero console errors.
- **Conclusion**: The user-visible card upgrade is real. One observation for the reviewer/fixer: the table shows `COMPLETE` and a winner while the meta line still says the match is `running` (inter-hand state), and the ledger/strategy panels now live in tabs whose content the reviewer must judge. Correctness of the hand names was cross-checked by hand on one hand (EVD-076).
- **Related**: EVD-074, EVD-075, EVD-076, TODO-011

### TODO-011: Round 7b/7c — independent review then remediation

- **Task**: An independent Reviewer files severity-ranked findings in `docs/goal-tholdem/round-7/review.md`; a second Implementer then remediates every P0/P1 (P2 where cheap) with tests and new screenshots.
- **Priority**: P0 (explicit user request)
- **Depends on**: Round-7a (verified)
- **Status**: 7b done (15 findings); 7c in progress
- **Related**: DEC-010, VAL-007

### VAL-008: Round-7b independent review — 15 findings, 2 blocking

- **Target**: round-7a implementation as reviewed by an independent Reviewer
- **Method**: Read the review; confirmed both P1 findings in the source and in the reviewer's capture; checked the report's method and evidence list.
- **Evidence examined**: EVD-077, EVD-078; `WIP/Wip.Tholdem.Web/wwwroot/viewer/app.js:166-191,263-264`; `docs/goal-tholdem/round-7/review-live-1440.png`
- **Result**: Supported. RV-01 (active ring follows the last actor, not `actingSeat`) and RV-02 (multi-hand log with no hand identifier) are real and reproducible; 13 P2s cover settlement pot, replay settlement row, idle table, live-count semantics, replay agent labels, ledger at 390, 901–1150 clipping/board wrap, mobile order, Space hijack, ARIA tabs, legend contrast, live-region semantics and autoscroll.
- **Conclusion**: The reviewer is right that the UI is not fit to show as-is; 7c was dispatched with all 15 findings mapped to required fixes. This is the independent-review value the user asked for: two correctness defects that the implementation's own tests did not catch.
- **Related**: EVD-077, EVD-078, TODO-011

### VAL-009: Round-7c remediation batch — all 15 findings

- **Target**: round-7c remediation against `docs/goal-tholdem/round-7/review.md`
- **Method**: Re-ran the 144-case suite; read the finding-by-finding remediation table; confirmed the RV-01/RV-02 fixes in `app.js`; viewed the fixed 1440 live capture and the 390 ledger capture; re-ran architecture, coverage and waiver checks; rebuilt the viewer and restarted the demo from a copied output.
- **Evidence examined**: EVD-079…083; `docs/goal-tholdem/round-7/{remediation.md,fix-live-1440.png,fix-ledger-390.png}`
- **Result**: Supported. 144/144 tests pass. The ring follows `actingSeat` with a separate `LAST ACTION` cue; `HAND N` separators attribute every log row; `Pot awarded` replaces `Pot 0`; replay has a settlement row and agent-labelled seats; the 390 ledger is a stacked labelled card list; mid-width clipping and mobile ordering are fixed; shortcuts no longer hijack focused controls; the legend contrast is 11.46:1; live regions and non-snapping autoscroll are in place. One existing status test was corrected because the review proved its old expectation wrong, and that correction is named in the report.
- **Conclusion**: The review's blocking findings are closed and the UI is now fit to show. The only residual from this cycle is that the browser-dependent checks (keyboard, 390 px geometry) are asserted through Chromium in the remediation evidence, not in the committed suite; the committed suite covers the API/markup side. Recorded for the adversarial round.
- **Related**: EVD-079, EVD-080, EVD-081, TODO-011, TODO-012

### ART-019: Remediated viewer (cards, tabs, review fixes)

- **Path**: `WIP/Wip.Tholdem.Web/`
- **Type**: implementation
- **Supporting evidence**: EVD-079, EVD-080, EVD-081, EVD-083
- **Confidence**: High
- **Verified by**: 144/144 suite; `actingSeat`-driven ring and `HAND N` separators read in `app.js` and seen in `fix-live-1440.png`; 390 ledger capture readable; demo viewer restarted from a copy and captured with 0 console errors
- **Last updated**: 2026-11-06

### ART-020: Round-7 remediation report and test suite

- **Path**: `docs/goal-tholdem/round-7/remediation.md`; `WIP/Wip.Tholdem.Tests/TholdemViewerRemediationTests.cs`
- **Type**: report / test-suite
- **Supporting evidence**: EVD-079, EVD-082, EVD-077
- **Confidence**: High
- **Verified by**: read the 15-row mapping; runner `TOTAL 144 PASSED 144 FAILED 0`; architecture 4/4; coverage ok; waiver 144
- **Last updated**: 2026-11-06

### TODO-012: Round 8 — CLI, hand-history parser, learning-loop persistence fixes

- **Task**: Create `Wip.Tholdem.Host` with `play`, `train`, `evaluate`, `promote`, `replay`, `retention`; add `HandRecordJson.Parse` with round-trip tests; fix the per-run ledger overwrite (TODO-008) and the silent champion-pointer fallback (TODO-009); wire the project into `Wip.slnx` (47→48), the architecture graph and the waiver register.
- **Priority**: P0
- **Depends on**: Rounds 6–7
- **Status**: Done (with one integrity defect found, TODO-013)
- **Related**: DEC-011, TODO-008, TODO-009, TODO-013

### TODO-013: Replay integrity must be anchored to an external hash, not the record's own hash

- **Task**: `replay` currently compares the file's SHA-256 to `record.Hash`, which is recomputed from the parsed (possibly edited) record, so a canonical semantic edit passes. Anchor the check: when the file sits inside a session directory, compare against that session's `session.json` `handHashes[handNumber-1]`; support `--expected-hash <hex>` for files outside a session; keep the canonical-bytes check. Test a canonical edit (e.g. `smallBlind` 5→6) inside a session → exit 5; a correct explicit hash → 0; a wrong one → 5.
- **Priority**: P1
- **Depends on**: Round-8 CLI
- **Status**: Open (assigned to 8b)
- **Related**: EVD-086, ART-021

### VAL-010: Round-8 verification — CLI works, one integrity claim is falsified

- **Target**: round-8 delivery (24607d15)
- **Method**: Re-ran the 172-case suite; ran every CLI command myself against a fresh data directory; tampered a recorded hand in a canonical way and replayed it; read `ReplayCommand` and its tamper test.
- **Evidence examined**: EVD-084, EVD-085, EVD-086; `WIP/Wip.Tholdem.Host/Commands/ReplayCommand.cs:32-55`; `WIP/Wip.Tholdem.Tests/Host/ReplayCommandTests.cs`
- **Result**: Partially supported. The CLI's functional claims hold: play/train/evaluate/replay/retention/help all behave and the exit-code contract works (`promote` refuses with 3 in both confirmation and margin cases). The claim that a tampered file is an invariant failure is **falsified** for canonical semantic edits (EVD-086): the hash is self-referential.
- **Conclusion**: The CLI is real and usable; the replay integrity claim needed an external anchor, filed as TODO-013 and fixed in 8b. The committed tamper test was too weak (it only broke canonical form) and was replaced with a semantic-edit case.
- **Related**: EVD-084, EVD-085, EVD-086, TODO-013

### VAL-011: Round-8b — anchored replay integrity, docs, CI surface

- **Target**: round-8b delivery (6007f9df)
- **Method**: Re-ran the 177-case suite; repeated my own canonical-edit attack on a recorded session and on unanchored copies; exercised `--expected-hash` correct and wrong; read the docs set and scanned it for release claims; read the two new CI step blocks.
- **Evidence examined**: EVD-087…091; `.tmp/tholdem-cli/sessions/tholdem-06795fbeacfe528b/session.json`; `.github/workflows/tests-ci.yml:119-195`; `docs/tholdem/*`
- **Result**: Supported. 177/177 tests pass; the exact attack that previously returned 0 now returns 5 with the manifest hand hash named; unanchored files are honest (`integrity: unanchored`); the explicit hash works both ways. Six documents exist with only negated release language, and both security jobs carry the Tholdem suite with the right results directories.
- **Conclusion**: TODO-013 is closed. D6 and D8 move to covered with one honest caveat: the CI steps are written and reviewed but CI was never executed from this machine (RISK-001), and the docs' factual claims are spot-checked rather than exhaustively proven — the adversarial round tests them independently.
- **Related**: EVD-087, EVD-088, EVD-089, EVD-090, EVD-091, TODO-013

### TODO-013 status

- **Status**: Done (see VAL-011)

### ART-023: Anchored replay integrity and round-8b test suite

- **Path**: `WIP/Wip.Tholdem.Host/Commands/ReplayCommand.cs`; `WIP/Wip.Tholdem.Tests/Host/ReplayCommandTests.cs`
- **Type**: implementation / test-suite
- **Supporting evidence**: EVD-087, EVD-088, EVD-091
- **Confidence**: High
- **Verified by**: my own attack matrix (clean 0, semantic edit 5 with manifest hash named, unanchored 0 + note, correct expected-hash 0, wrong 5); runner `TOTAL 177 PASSED 177 FAILED 0`
- **Last updated**: 2026-11-06

### ART-024: Documentation set

- **Path**: `docs/tholdem/{README,engine-rules,strategy-artifact-schema,training-promotion-runbook,known-limits,viewer}.md`
- **Type**: document
- **Supporting evidence**: EVD-089, EVD-091, EVD-076
- **Confidence**: High
- **Verified by**: listed the files and read them; release scan shows only negations; adversarial round C6 independently checks doc claims against code
- **Last updated**: 2026-11-06

### ART-025: Security CI surface for Tholdem

- **Path**: `.github/workflows/tests-ci.yml`
- **Type**: plan
- **Supporting evidence**: EVD-090, EVD-091
- **Confidence**: Medium (written and reviewed; not executed)
- **Verified by**: read both step blocks and their placement/results directories; YAML parsed as valid with 6 jobs by the implementer; execution is not observable from this machine (RISK-001)
- **Last updated**: 2026-11-06

### DEC-012: Final adversarial round before convergence

- **Decision**: Run an Adversarial round with three independent Falsifiers over six delivery-wide claims (determinism, promotion gate, hidden information, untrusted data, offline safety, test/docs integrity), tally by consensus (any disagreement → Falsified), and verify every Falsified row myself before reporting. Convergence is declared only if the tally is clean and my own re-checks agree.
- **Rationale**: Each round verified its own slice; nothing yet tested the delivery as a whole against claims that cross project boundaries. The harness requires an independent gate before a "done" claim.
- **Alternatives considered**: (a) one reviewer — rejected, no disagreement signal; (b) trust the per-round evidence — rejected, that is the failure mode the harness exists to prevent.
- **Related**: DEC-002, RISK-001, TODO-014

### VAL-012: Round-9 adversarial batch — three claims falsified

- **Target**: delivery-wide claims C1–C6 under three independent Falsifiers
- **Method**: Each Falsifier independently attacked all six claims and wrote `docs/goal-tholdem/round-9/falsify-{A,B,C}.md` (25–35 KB each). Tally by script: all-Supported → Supported, all-Falsified → Falsified, all-NotCheckable → Unresolved, any disagreement → Falsified. I then verified the falsified rows myself: read the cited code, reproduced the seed→deck reconstruction, read the cited docs lines.
- **Evidence examined**: `docs/goal-tholdem/round-9/falsify-*.md`; `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs:8`; `.tmp/tholdem-verify/derive-deck.ps1`; `.tmp/tholdem-cli/sessions/tholdem-06795fbeacfe528b/hand-00001.json`; `docs/tholdem/{engine-rules,viewer,known-limits}.md`
- **Result**: **C1 Supported** (three independent byte-identity demonstrations across CLI and simulator), **C2 Supported** (no promotion without the verifier; `--yes` is confirmation only; no merge/publish/deploy code), **C3 Falsified** (unanimous and reproduced by me), **C4 Supported** (injection, denial, truncation, fallback), **C5 Falsified with disagreement** (offline safety holds; a `dotnet pack`/`publish` path exists, so the absolute "no path" claim is false), **C6 Falsified** (docs contradict the code; the boundary test's regex cannot match `SessionSeed`; the docs call an uncommitted tree "the committed suite").
- **Conclusion**: G2 FAILS. The C3 finding is a genuine hidden-information vulnerability: `AgentTurnContext` exposes `SessionSeed`, and with public `SeedDerivation.Derive` + `SeededDeck.Create` an agent reconstructs both hole-card sets and the board — I reproduced hand 1 of the seed-42 session exactly (`Qs,4s` / `Tc,Jd`, board `8h,8s,8d,Th,6c`). This contradicts the objective's "never the deck, opponents' cards or future community cards" and must be fixed before any completion claim. The per-slice evidence was correct; the cross-cutting claim was not.
- **Related**: EVD-092, RISK-008, RISK-009, TODO-014

### RISK-008: Hidden information leaked through the agent turn context (G2 failure)

- **Risk**: Every `IPokerAgent` receives `AgentTurnContext.SessionSeed`, and the public `SeedDerivation.Derive` + `SeededDeck.Create` reconstruct the whole deck, both hole-card sets and every future board card. Any agent (including an LLM-driven one that follows a suggestion) can cheat perfectly.
- **Likelihood**: Certain — reproduced end-to-end.
- **Impact**: High — breaks the core safety property of the domain and the honesty of every simulation and learning result built on it.
- **Evidence**: EVD-092; `AgentTurnContext.cs:8`; `MatchSimulator.cs:68-72`; `.tmp/tholdem-cli/.../hand-00001.json`
- **Mitigation**: Round 10 replaces `SessionSeed` with a one-way per-turn `TurnSeed` (SHA-256 of session seed + hand + seat + action + stream), updates all agents/simulator/live match, strengthens the boundary test to catch `SessionSeed`, adds a cheat-path test that the TurnSeed does not reproduce the deck, and corrects the docs.
- **Gate**: G2

### RISK-009: The "no release/publish path" claim was too strong

- **Risk**: The docs claimed no release path exists, but `dotnet pack` produced `Wip.Tholdem.0.0.2.nupkg`/`.snupkg` and `dotnet publish` produced a local output. Packaging is possible for any packable project; what must be true is that this delivery performs no publish/sign/deploy.
- **Likelihood**: Certain (One falsifier executed both).
- **Impact**: Low for the product, Medium for honesty — an absolute claim that a reviewer can falsify in one command.
- **Evidence**: `docs/tholdem/known-limits.md`; falsify-C report
- **Mitigation**: Restate the claim precisely (no publish/sign/deploy is performed or automated; artifacts are packable as ordinary .NET projects; any package produced during review was scratch and not delivered) in round 10.
- **Gate**: G2

### TODO-014: Round 10 — close the seed-leak vulnerability and the falsified doc claims

- **Task**: Replace `AgentTurnContext.SessionSeed` with an opaque one-way `TurnSeed`, update all agents/simulator/live match, strengthen the boundary test and add a cheat-path test, correct the three docs claims, update the waiver count. Re-run a focused adversarial re-test of C3/C5/C6 afterwards.
- **Priority**: P0 (G2 failure)
- **Depends on**: RISK-008, RISK-009, VAL-012
- **Status**: Code side done (round 10); docs consistency in 10b
- **Related**: RISK-008, RISK-009, VAL-012, TODO-015

### VAL-013: Round-10 security fix — exploit closed, evidence wording imperfect

- **Target**: round-10 delivery (0d9236ce)
- **Method**: Re-ran the 179-case suite; scanned agent-visible sources for `SessionSeed`; independently recomputed the turn seed with SHA-256 and derived the deck with the real engine (`TurnProbe`); read the corrected docs and the cheat-path test; compared the report's claimed derived cards with the engine output.
- **Evidence examined**: EVD-096, EVD-097, EVD-098; `AgentTurnContext.cs`; `AgentInformationBoundaryTests.cs:112-146`; `.tmp/tholdem-verify/TurnProbe`; `docs/tholdem/{engine-rules,viewer,known-limits}.md`
- **Result**: Supported on the security property. `SessionSeed` is gone from agent-visible types; the turn seed matches my independent computation; the deck from it differs from the recorded cards, so the exploit I reproduced in round 9 no longer works. 179/179 tests pass. Two defects remain: the report's cheat-path example misdescribes which construction produced its cards (the property still holds), and three docs still contain pre-fix seed-derived examples plus one stale code comment.
- **Conclusion**: The G2 blocking finding is fixed in code, but convergence is not claimed: the docs/tests claims (C6) still need the 10b consistency pass, and C3/C5/C6 must be re-falsified independently before the gate can pass.
- **Related**: EVD-096, EVD-097, EVD-098, TODO-015

### TODO-015: Round 10b — regenerate stale doc examples and fix the stale comment

- **Task**: Regenerate every seed-dependent doc example from real post-fix CLI runs, fix the `TholdemRuntimeAgent.cs` comment that still mentions the session seed, append a correction note to the round-10 report, and rescan for stale values.
- **Priority**: P1 (C6)
- **Depends on**: Round 10
- **Status**: Done (one instruction of mine was wrong; see VAL-014)
- **Related**: EVD-097, EVD-098, VAL-013, VAL-014

### VAL-014: Round 10b — examples reproduce, and my stale-scan instruction was partly wrong

- **Target**: round-10b delivery (1dd6d388)
- **Method**: Ran the exact documented commands with the Release CLI into fresh directories and compared their output to the docs; scanned the docs for the old values; read the correction note and the fixed comment.
- **Evidence examined**: EVD-099; `docs/tholdem/{strategy-artifact-schema,training-promotion-runbook}.md`; `docs/goal-tholdem/round-10/implementation.md:498-511`; `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:99-101`
- **Result**: Supported. `train --seed 7` now yields `b155b98f` with mean/lcb 0.00 and a ledger matching the doc; `play --seed 777 --hands 2` yields session `f275b4ee…` with hand-1 hash `571049a9…` unchanged and hand-2 hash `6672b578…`; the old values are gone; the correction note and the `TurnSeed` comment are present.
- **Conclusion**: The implementer correctly refused a bad instruction: `571049a9` and `f275b4ee` are current because the engine's hand-seed derivation was deliberately unchanged, so only agent-RNG-derived values moved. My scan requirement was wrong for those two; the refusal is recorded here rather than punished. C6's docs half now has working examples.
- **Related**: EVD-099, TODO-015

### ART-027: Regenerated documentation examples and correction note

- **Path**: `docs/tholdem/{strategy-artifact-schema,training-promotion-runbook,engine-rules}.md`
- **Type**: document
- **Supporting evidence**: EVD-098, EVD-099, EVD-093
- **Confidence**: High
- **Verified by**: re-ran the documented `train`/`play` commands and compared byte-for-byte with the doc text; stale-value scan 0 hits
- **Last updated**: 2026-11-06

### VAL-015: Round-11 re-falsification — the TurnSeed fix is broken by inversion

- **Target**: C3/C5/C6 after rounds 10 and 10b
- **Method**: Three fresh Falsifiers re-tested the claims against the current tree; I reproduced the C3 break myself with an independent SHA-256 enumeration and the recorded seed-42 hand; I read all three refalsify reports.
- **Evidence examined**: EVD-100, EVD-101; `docs/goal-tholdem/round-11/refalsify-{A,B,C}.md`
- **Result**: **C3 Falsified unanimously.** All three agents recovered the low-entropy session seed from `TurnSeed` by enumerating 0..2 000–100 000 candidates (137 ms–2 s), then rebuilt the deck and matched recorded hole cards and boards for seeds 42, 7 and 777. C5 (offline safety) **Supported 3/3**. The docs/tests claim was not validly tallied: my workflow script mapped ids with a precedence bug, so claim 2 was reported as `C4` (Unresolved) and claim 3 as `C5`; I record that error and do not claim a C6 verdict from this round.
- **Conclusion**: Replacing `SessionSeed` with a one-way `TurnSeed` was necessary but not sufficient — exposing *any* deterministic function of a low-entropy secret to the agent lets it invert the secret. Round 12 removes every seed from the agent surface: the context carries only hand/seat/action, and randomness is derived from the agent's own observation, which reveals nothing about the deck.
- **Related**: EVD-100, EVD-101, RISK-010, TODO-016

### RISK-010: Any seed-derived value handed to an agent can be inverted for low-entropy seeds

- **Risk**: `TurnSeed = H(sessionSeed‖context)` was one-way in the cryptographic sense, but the documented seeds are small (42, 7, 777). An agent that receives the turn seed enumerates candidate session seeds and recovers the secret, then rebuilds the deck.
- **Likelihood**: Certain under the documented usage.
- **Impact**: High — same hidden-information break as RISK-008, one layer deeper.
- **Evidence**: EVD-100; `docs/goal-tholdem/round-11/refalsify-*.md`
- **Mitigation**: Remove the seed from the agent-facing surface entirely (round 12); derive agent randomness from `PlayerObservation.ToCanonicalJson()`, which is the agent's own information and cannot be inverted to the session seed. A high-entropy-seed requirement alone was rejected because the CLI and docs accept small seeds.
- **Gate**: G2

### TODO-016: Round 12 — remove every seed from the agent surface

- **Task**: `AgentTurnContext` loses its seed; delete `DeriveTurnSeed`; add `DeriveAgentSeed(canonicalObservation, …)` and use it in every baseline and `StrategyAgent`; simulator/live construct the three-field context; replace the cheat test with an enumeration attack that has no target; update docs and fixtures. Then re-falsify C3 with a corrected tally.
- **Priority**: P0 (G2 failure)
- **Depends on**: RISK-010
- **Status**: Code side done (round 12); final re-falsification in flight
- **Related**: RISK-010, VAL-015, VAL-016

### VAL-016: Round-12 verification — no seed reaches the agent

- **Target**: round-12 delivery (9bd4cf40)
- **Method**: Re-ran the 182-case suite; built an independent probe against the public assemblies that prints the agent context's properties, the public seed methods, the agent seed for the recorded seed-42 hand, and a deck built from it; compared with the recorded hand.
- **Evidence examined**: EVD-102; `.tmp/tholdem-verify/AgentSeedProbe`; `docs/goal-tholdem/round-12/implementation.md`
- **Result**: Supported. `AgentTurnContext` has exactly `HandNumber`, `Seat`, `ActionIndex`; `SeedDerivation` exposes only `Derive` and `DeriveAgentSeed`; the context has no `ulong`; the agent seed derived from the observation produces a deck that does not match the recorded one; 182/182 tests pass.
- **Conclusion**: The inversion attack has no input to work with now: the only seed the agent uses is computed from its own observation, which is not invertible to the session seed. The final gate is the independent C3/C6 re-test in flight; no completion claim before it returns.
- **Related**: EVD-102, TODO-016

### ART-028: Seed-free agent turn context and observation-derived randomness

- **Path**: `WIP/Wip.Tholdem.Agents/{AgentTurnContext,SeedDerivation}.cs`; baselines; `StrategyAgent`; `MatchSimulator`; `LiveMatch`
- **Type**: implementation / test-suite
- **Supporting evidence**: EVD-100, EVD-102, EVD-096
- **Confidence**: High
- **Verified by**: independent probe (context properties, seed methods, agent-seed deck mismatch vs recorded) and `TOTAL 182 PASSED 182 FAILED 0`
- **Last updated**: 2026-11-06

### ART-026: One-way turn seed and the strengthened boundary tests

- **Path**: `WIP/Wip.Tholdem.Agents/{AgentTurnContext,SeedDerivation}.cs`; `WIP/Wip.Tholdem.Tests/AgentInformationBoundaryTests.cs`
- **Type**: implementation / test-suite
- **Supporting evidence**: EVD-096, EVD-097, EVD-092
- **Confidence**: High for the code property; Medium for the round-10 evidence wording
- **Verified by**: independent SHA-256 recomputation and engine deck derivation showing no match to the recorded hand; 179/179 suite; `SessionSeed` scan = 0 hits in agent-visible files
- **Last updated**: 2026-11-06

### ART-018: Independent UI review report

- **Path**: `docs/goal-tholdem/round-7/review.md`
- **Type**: report
- **Supporting evidence**: EVD-077, EVD-078, EVD-075, EVD-076
- **Confidence**: High
- **Verified by**: Opened the report and confirmed RV-01/RV-02 in `app.js` and in the capture; 15 findings, verdict "not fit as-is"
- **Last updated**: 2026-11-06

### RISK-006: A viewer running from `bin/Release` blocks Release builds

- **Risk**: `dotnet run --project WIP/Wip.Tholdem.Web` (or running the DLL from its build output) keeps `Wip.Tholdem.Web`, `Wip.Tholdem.Agents` and `Wip.Tholdem` assemblies open in `bin/Release/net10.0`, so any concurrent Release build fails with MSB3027/MSB3021. This actually blocked the round-6 implementer mid-round.
- **Likelihood**: High when a viewer is left running during development.
- **Impact**: Medium — build failures that look like repository defects but are process locks.
- **Evidence**: EVD-067
- **Mitigation**: run the viewer from a copied output (`.tmp/viewer-run`) or a `dotnet publish` directory; document it in `docs/tholdem/known-limits.md` during the docs round. The orchestrator relaunched the demo viewer that way and confirmed the bin DLL is writable.
- **Gate**: —

### VAL-017: Round-12/13 re-falsification — the observation itself leaks the deck for weak seeds

- **Target**: C3 (hidden information) and C6 (test/doc integrity) after the seed-free context fix
- **Method**: Three fresh Falsifiers re-attacked the current tree; I reproduced the core attack myself with a compiled probe that uses only public product APIs (`SeedDerivation.Derive` + `SeededDeck.Create`).
- **Evidence examined**: EVD-103; `docs/goal-tholdem/round-12/refalsify-{A,B,C}.md`
- **Result**: **C3 Falsified 3/3** and **C6 Falsified 3/3.** The static clauses hold (no seed/deck/opponent cards in any agent API; spectator type unreachable), but the inference clause fails: matching an observation against candidate seeds recovers the deck for low-entropy seeds. All three also found concrete doc mismatches (absolute non-invertibility claims, `StrategyArtifact.Create` described as persisting, a session path attributed to `MatchSimulator`).
- **Conclusion**: Removing the seed from the agent surface was necessary but insufficient. The root cause is that the deal is a deterministic function of a guessable seed. The honest product fix is an entropy guard at the operator entry points plus a precise, non-absolute threat-model statement — not another claim that inversion is impossible. Round 13 implements that.
- **Related**: EVD-103, RISK-011, TODO-017

### RISK-011: Deterministic seeded deal permits seed recovery from observations when the seed is guessable

- **Risk**: Any observer of dealt cards can enumerate candidate session seeds, rebuild each candidate deck and select the seed whose dealt positions match. With seeds like 42 or 777 this succeeds in under a second; the recovered seed reveals the opponent's hole cards and every future board card.
- **Likelihood**: Certain for low-entropy seeds; infeasible for a uniformly random 64-bit seed.
- **Impact**: High if the product's default/documented usage is a weak seed — it would let a cheating agent see the future.
- **Evidence**: EVD-103; `docs/goal-tholdem/round-12/refalsify-*.md`
- **Mitigation**: Refuse seeds below 1,099,511,627,776 at CLI and viewer entry points, keep weak seeds only behind an explicit `--allow-weak-seed` for demos/tests, default the viewer form to a random 64-bit seed, and state the precise threat model in `known-limits.md`. A secret-salt design was rejected because it would break seed-only reproducibility, which the objective requires.
- **Gate**: G2

### TODO-017: Round 13 — entropy guard, precise threat model, doc mismatches

- **Task**: Implement the seed-entropy guard at CLI/viewer entry points (`--allow-weak-seed`, 400 `weak-seed`), default the viewer seed to random, correct the three doc claims, regenerate example commands, add guard tests, and update the waiver count.
- **Priority**: P0 (G2)
- **Depends on**: RISK-011
- **Status**: Done (code + docs + tests verified); final independent re-test in flight
- **Related**: RISK-011, VAL-017, VAL-018

### VAL-018: Round-13 verification — the guard is real and the docs no longer overclaim

- **Target**: round-13 delivery (9942d213)
- **Method**: Ran the CLI guard matrix myself (exit codes measured without truncating the pipeline), rebuilt the viewer and tested the HTTP guard on 5099, inspected the viewer assets for the random-seed default, read the threat-model section, scanned the docs for absolute non-invertibility sentences, and re-ran the 194-case suite.
- **Evidence examined**: EVD-104; `docs/tholdem/known-limits.md:29-66`; `WIP/Wip.Tholdem.Web/wwwroot/viewer/app.js:58-60,642`
- **Result**: Supported. Weak seeds are refused (CLI exit 2, HTTP 400) and accepted only behind the explicit opt-in; strong seeds pass; the viewer form defaults to a cryptographic random 64-bit seed and exposes the weak-seed checkbox; the docs state the inference attack with my reproduction numbers and explicitly place opt-outs outside the guarantee; no absolute claim remains; 194/194 tests pass.
- **Conclusion**: The product now has an honest, enforced secrecy boundary at its operator entry points, and the residual inference risk is documented rather than denied. The library APIs still accept any seed, which the docs define as outside the guarantee. The final gate is the independent re-test of the restated claims in flight.
- **Related**: EVD-104, RISK-011, ART-029

### ART-029: Seed-entropy guard and precise threat model

- **Path**: `WIP/Wip.Tholdem.Host` (guard), `WIP/Wip.Tholdem.Web` (HTTP guard, random default), `docs/tholdem/known-limits.md`
- **Type**: implementation / document
- **Supporting evidence**: EVD-103, EVD-104, EVD-101
- **Confidence**: High
- **Verified by**: my own CLI/HTTP guard matrix; viewer asset inspection; doc scan for absolute claims returning none; `TOTAL 194 PASSED 194 FAILED 0`
- **Last updated**: 2026-11-07

### VAL-019: Final adversarial gate — C3 and C6 Supported, zero falsified rows

- **Target**: restated C3 (API secrecy + enforced entry-point guard + honest docs) and C6 (test/doc integrity)
- **Method**: Three fresh Falsifiers re-tested the restated claims against the current tree with explicit ids; I checked their evidence categories against my own runs (guard matrix, suite, docs) before accepting the tally.
- **Evidence examined**: EVD-105, EVD-106; `docs/goal-tholdem/round-13/refalsify-{A,B,C}.md`
- **Result**: Supported 3/3 on both claims, `falsifiedCount: 0`, `gateG2: PASS`. Falsifiers independently confirmed: real agent inputs contain no seed/deck/opponent cards; `Wip.Tholdem.Agents` cannot compile against `SpectatorSnapshot`; weak seeds are refused at both entry points including the 2^40−1 boundary and string forms; docs make no absolute non-invertibility claim; 194 facts with 0 skips; no Tholdem waivers; all documented commands and seven JSON examples reproduce.
- **Conclusion**: The gate passes on the claims as now stated. Two honest residuals are recorded, not hidden: CI was never executed from this machine (no `gh`/token), and the deterministic-deal inference risk remains for operators who opt into weak seeds — both are documented in `known-limits.md` and in the final report.
- **Related**: EVD-105, EVD-106, RISK-001, RISK-011

### ART-030: Final evidence pack and completion record

- **Path**: `docs/goal-tholdem/` (goal, journal, evidence, state, deliverables, round artifacts)
- **Type**: report
- **Supporting evidence**: EVD-105, EVD-106, EVD-069, EVD-088, EVD-102, EVD-104
- **Confidence**: High
- **Verified by**: final gate sweep (194/194, architecture 4/4 at 48, coverage ok, waiver 194) and the three-falsifier PASS tally
- **Last updated**: 2026-11-07

### DEC-013: Converge — the goal objective is delivered; multiway is a separate follow-on

- **Decision**: Mark the goal complete. The objective's heads-up vertical slice is delivered and independently gated: deterministic engine and replayable hashes, baseline agents and reproducible simulator, an agent limited to its observation with no seed on its surface, a learning loop whose always-promote control is refused by a re-deriving verifier, a viewer with real cards and an independent review/remediation cycle, a CLI with anchored replay integrity, and docs with a precise threat model. No release, publish or deploy was performed and none is claimed.
- **Rationale**: All nine definition-of-done dimensions have High-confidence evidence; the final adversarial tally is zero falsified rows on the restated claims; every fix was reproduced by the orchestrator. The multiway request (4–6 seats, side pots) is a new engine objective that supersedes rather than extends the heads-up evidence, so it is recorded as the next goal, not smuggled into this completion.
- **Residuals, stated not hidden**: CI (`Tests CI` six jobs, `Modus CI`) was not executed or observed — no `gh` CLI, no token, and local VSTest aborts with `Win32Exception (5)`; the repo's offline gate cannot produce TRX locally. The deterministic-deal inference risk is gated at entry points and documented; library-level callers can still pass weak seeds. The Tholdem tree is uncommitted on branch `WIP`.
- **Alternatives considered**: (a) claim CI green — forbidden, not observed; (b) fold multiway in — rejected, it is not in this objective and not built; (c) keep the goal active for CI — rejected, no capability exists on this machine to satisfy it and the evidence is otherwise complete.
- **Related**: EVD-105, EVD-106, RISK-001, DEC-011
