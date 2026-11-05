# Goal journal — WIP-IMPL-001 phase 1 close-out

> Append-only. Entries follow the stretch-goal-harness evidence protocol.

---

### OBS-001: Worker mode is wired through the host bridge

- **Source**: `WIP/Wip.Bones/Engine/BonesStrategyScriptHost.cs:98-101,209-227`, `WIP/Wip.Bones/Engine/BonesWorkerBridge.cs:85-104`
- **Fact**: In Worker mode `TryCompile` delegates to `CompileWorker`, which calls `SandboxWorkerClient.CompileBonesAsync` and caches a `WorkerBonesPlayerSlot` whose `ChooseMove` round-trips the state and legal moves through `ChooseBonesMoveAsync`. `CompileSource` (the only `Assembly.Load` path) is not called in Worker mode.
- **Implications**: The host-side object graph can hold only the bridge; a test can prove routing by type identity and by the in-process compile counter.
- **Confidence**: High
- **Agent**: orchestrator (read)
- **Related**: EVD-005

### OBS-002: Legality enforcement sits outside the compiled slot

- **Source**: `WIP/Wip.Bones/Engine/BonesScriptStrategyPlayerSlot.cs:51-75`
- **Fact**: `BonesScriptStrategyPlayerSlot` executes the compiled slot and falls back to `legalMoves[0]` with a `BonesScriptStrategyExecutionWarning` when the selection is not in the legal set. This is the public player-slot contract path.
- **Implications**: A Worker-mode test through this class proves move legality is unchanged by routing.
- **Confidence**: High
- **Agent**: orchestrator (read)
- **Related**: OBS-001

### OBS-003: ScriptHost mode has no behaviour tests

- **Source**: `WIP/Wip.Bones.Host/BonesHostOptions.cs:590-615`, `WIP/Wip.Bones.Host/BonesHostServiceCollectionExtensions.cs:230`, `WIP/Wip.Bones.Host/appsettings.json`
- **Fact**: `ScriptHost:Mode` and `TrustedOnly` bind from configuration with an InProcess default; the host constructs `new BonesStrategyScriptHost(options.ScriptHost)` and passes no `WorkerClient`, so `Mode=Worker` fails closed at resolution.
- **Implications**: WP-1.6 must decide whether the flag switches the host mode or is a guarded, documented scaffold; either way it needs tests and docs.
- **Confidence**: High
- **Agent**: orchestrator (read)
- **Related**: TODO-002

### HYP-001: A Worker-mode compile+move adds no model assembly to the host AppDomain

- **Claim**: After `TryCompile` + `ExecuteChooseMoveDirect` in Worker mode, the host AppDomain gains no assembly named `BonesStrategyScript_*`, the host compile counter stays 0, and the cached `PlayerSlot` is a `WorkerBonesPlayerSlot` from `Wip.Bones`.
- **Supporting OBS**: OBS-001
- **Why it matters**: This is the WP-1.3 acceptance criterion "the host never loads a model-authored assembly". If false, the security boundary is not met.
- **Falsification strategy**: Run the paired InProcess test (positive control) in the same collection; if the detector does not notice the in-process load, the proof is invalid. Also check the worker test would fail if `Mode` were flipped to InProcess.
- **Status**: Under review
- **Confidence**: Medium

### HYP-002: The worker round-trips board, hands, current player and pass streak faithfully

- **Claim**: A strategy that chooses `legalMoves[(boardTiles + handTiles + passStreak) % legalCount]` returns the expected move for states that differ in each component.
- **Supporting OBS**: OBS-001
- **Why it matters**: "No behaviour change in move legality" requires the serialized state to be complete; a lost component silently changes strategy decisions.
- **Falsification strategy**: Four states whose expected index flips if board count, current-player hand size, or pass streak is lost; assert exact move equality (MoveId included).
- **Status**: Under review
- **Confidence**: Medium
### OBS-004: The worker exhausted the 256 MiB cap after seven compiles

- **Source**: direct worker probe over the JSON-lines protocol (`.tmp/worker-probe`), 16 sequential `compileBones` frames
- **Fact**: Before the fix the worker's private memory grew 53.7 → 258.9 MB across seven compiles and the eighth failed with `OutOfMemoryException` under the default 256 MiB Job Object cap; the first full GC only occurred at compile 10 (peak 328.5 MB).
- **Implications**: A long Bones learning run would kill the worker mid-compile. WP-1.3 cannot be called complete without bounding this.
- **Confidence**: High
- **Agent**: orchestrator (measured)
- **Related**: DEC-001, EVD-007

### VAL-001: HYP-001 supported — Worker mode adds no model assembly to the host AppDomain

- **Target**: HYP-001
- **Method**: Ran the new 9-case worker-mode class; the negative test snapshots `AppDomain.CurrentDomain.GetAssemblies()` before/after a Worker compile+move, and the paired InProcess test proves the same detector observes a `BonesStrategyScript_*` load.
- **Evidence examined**: `.tmp/wp13-check/wp13-worker.trx` (9/9 passed). Negative: no `BonesStrategyScript_*` delta, `CompileInvocationCount == 0`, slot type `WorkerBonesPlayerSlot` in `Wip.Bones`. Positive control: exactly one `BonesStrategyScript_*` delta and `CompileInvocationCount == 1`.
- **Result**: Supported
- **Conclusion**: In Worker mode the host holds only the bridge; the model assembly is not loaded in the host process.
- **Related**: HYP-001, EVD-006

### VAL-002: HYP-002 supported — the worker round-trips board, hands, current player and pass streak

- **Target**: HYP-002
- **Method**: Four-state theory with a state-dependent script; each case's expected index flips if a state component is lost (board loss fails case 1, hand/seat loss fails case 3, pass-streak loss fails case 4), and exact `BonesMove` equality covers move id, tile, side and player.
- **Evidence examined**: `.tmp/wp13-check/wp13-worker.trx`, 4/4 theory cases passed.
- **Result**: Supported
- **Conclusion**: Serialized state is complete for strategy decisions; move identity round-trips.
- **Related**: HYP-002, EVD-006

### DEC-001: Bound worker memory with cached references and deterministic reclaim

- **Decision**: Cache `MetadataReference` arrays per worker process and run a forced, blocking full GC after each compile request.
- **Rationale**: OBS-004 measured OOM at compile 7 under the default cap. After the change, a 16-compile probe peaked at 60.7 MB and averaged ~15 ms per compile after warm-up, so the security bound (256 MiB) is kept instead of being raised.
- **Alternatives considered**: Raise the default cap (does not remove the race against GC and weakens the cap); evict cached strategies (the cached assemblies are small; the garbage is Roslyn's compilation graph).
- **Related**: OBS-004, EVD-007

### OBS-005: The InProcess warning crashed hosts where EventLog writes are denied

- **Source**: `.tmp/gate-check` attempt 1, `Wip.Bones.Host.Tests.trx` (42 unwaived failures); stack `Microsoft.Extensions.Logging.Logger.ThrowLoggingError` → `BonesScriptHostRuntime.Create`
- **Fact**: A non-admin Windows process cannot write to the ".NET Runtime" event source. The default host logging pipeline includes the EventLog provider at Warning level; the new InProcess `LogWarning` threw an `AggregateException`, which failed `BonesHostApplication.Build` for 42 integration tests and crashed the host E2E iteration.
- **Implications**: A trust-boundary warning must not be able to take down the host because a log sink is misconfigured.
- **Confidence**: High
- **Agent**: orchestrator (reproduced one test and read the stack)
- **Related**: DEC-002

### DEC-002: Startup logging is best-effort; the test runner filters the EventLog sink

- **Decision**: `BonesScriptHostRuntime` wraps its startup log calls in `TryLog`, which catches an aggregated provider failure and falls back to `Console.Out`. `tools/run-wip-tests.ps1` sets `Logging__EventLog__LogLevel__Default=None` so non-admin local runs match CI (the console logger still shows the warning).
- **Rationale**: OBS-005 proved a denied sink fails host construction. The product fix protects the host; the runner setting removes the environment-only failure mode without hiding the warning.
- **Alternatives considered**: Demote the warning to Information (loses the operator-visible warning); require elevation (not acceptable for a local tool).
- **Related**: OBS-005, EVD-010

### OBS-006: A process-global workflow map registry races under parallel tests

- **Source**: `.tmp/gate-check` attempt 1, `Wip.Bones.Tests.trx`; `WIP/Wip.Bones.Tests/Agents/BonesLearningLoopReliabilityAgentTests.cs:25-27`; `WIP/Wip.Runtime/Runtime/WorkflowStageMapAdapterRuntime.cs:16-37`
- **Fact**: Three test classes reset the static `WorkflowStageMapAdapterRuntime`/`BonesLearningWorkflowMapRuntime` bindings while other tests map stage payloads; the failing test reported "No runtime map adapter binding exists for 'BonesPlayMatchResult' -> 'BonesEnhanceStrategyRequest'" and passed in isolation.
- **Implications**: This is the P1-03 global-registry defect the plan fixes in WP-2.4; until then the mutating tests must not run in parallel with readers.
- **Confidence**: High
- **Agent**: orchestrator (reproduced in isolation vs suite)
- **Related**: DEC-003

### DEC-003: Serialize the workflow map-registry mutators

- **Decision**: Add the non-parallel `BonesWorkflowMapRuntime` collection and put `BonesLearningWorkflowRegistrationTests`, `BonesLearningWorkflowMapRuntimePerStageModelTests`, and `BonesLearningLoopReliabilityAgentTests` in it.
- **Rationale**: OBS-006 shows the race is between resets and mappings; xUnit runs a non-parallel collection exclusively, so no reader overlaps with a reset. Post-fix the full Bones suite is 395 / 393 passed / 2 waived.
- **Alternatives considered**: Disable parallelization for the whole Bones assembly (broader and slower); waive the flaky test (hides a real defect); remove the global registry now (WP-2.4 scope).
- **Related**: OBS-006, EVD-010

### RISK-001: The nested service provider duplicates the sandbox worker

- **Risk**: `WipRuntimeOrchestrator` builds a nested `BuildServiceProvider` per run (WP-2.1). With Worker mode, that instantiates a second `BonesScriptHostRuntime`, starting a second worker process for the run; the nested provider may not dispose it promptly.
- **Likelihood**: High (observed in the host E2E: the Worker log line appears twice)
- **Impact**: Medium (resource use; two sandbox processes; disposal dependent on the nested scope)
- **Evidence**: `.tmp/host-worker-e2e/host.log`; `WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs:250`
- **Mitigation**: WP-2.1 host-managed DI; the workers are Job-Object-capped and kill-on-host-exit. Recorded in the threat model §9.
- **Gate**: —

### OBS-007: The host suite expected total was stale

- **Source**: `docs/plan/test-waivers.json` (153) vs `.tmp/fix-check/Wip.Bones.Host.Tests.fix3.trx` (251 total)
- **Fact**: `verify-test-results.ps1` fails a project only when `total < expected`, and `Wip.Bones.Host.Tests` is a known-abort project, so the register never noticed that the suite had grown to 251 tests.
- **Implications**: Count drift could hide a large block of untested tests; the register is now set to 251.
- **Confidence**: High
- **Agent**: orchestrator (TRX comparison)
- **Related**: EVD-011

### OQ-001: G1 autonomy/streaming scope is undecided

- **Question**: Is general autonomy a product goal for 1.0, and is streaming in scope?
- **Needed evidence**: Product owner's written decision on the G1 packet.
- **Priority**: P0 blocking
- **Raised by**: Round 1, orchestrator
- **Related**: ART-003

### OQ-002: `IScriptExecutor` is not the Bones routing seam

- **Question**: Should `BonesWorkerBridge` depend on `IScriptExecutor` (adding the Bones operations to it) instead of the concrete `SandboxWorkerClient`, so the seam matches the ADR/plan wording?
- **Needed evidence**: A design decision in WP-2.x or a later hardening pass; the current `IBonesPlayerSlot` abstraction already provides Worker/InProcess implementations and is fully tested.
- **Priority**: P2 curiosity
- **Raised by**: Round 1, orchestrator
- **Related**: OBS-001
### OBS-008: No push credentials in this session

- **Source**: `git push origin WIP` -> `Permission denied (publickey)`; HTTPS push -> "could not read Username"; no `gh`, no `GH_TOKEN`.
- **Fact**: The two commits cannot be pushed from this session, so no remote CI run exists for them.
- **Implications**: Local gate evidence is the authority; remote CI remains unverified for these commits and must run on the next authorized push.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-011

### OQ-003: Remote CI for ca6f41d/0ffefe2

- **Question**: Does Tests CI pass for these two commits (16 offline projects, 16/251-test suites, Linux/Windows security)?
- **Needed evidence**: A pushed run of Tests CI with the gate job green.
- **Priority**: P1 important
- **Raised by**: Round 1, orchestrator
- **Related**: OBS-008
### OBS-009: Remote Tests CI green on 57f5632

- **Source**: GitHub Actions run [41839526456](https://github.com/jjackbauer/Modus/actions/runs/41839526456) (`Tests CI`, branch WIP, head `57f5632`)
- **Fact**: All six jobs succeeded: `wip-build (ubuntu)`, `wip-build (windows)`, `wip-architecture`, `wip-security`, `wip-security (ubuntu)`, `wip-offline-tests` (suite run and the known-failure/zero-test/count gate).
- **Implications**: The 395-test Bones suite and the 251-test Host suite run in CI under the 27-waiver register; the Windows security job is green after the diagnostics fix. Closes OQ-003.
- **Confidence**: High
- **Agent**: orchestrator (API read)
- **Related**: EVD-012, OQ-003

### OBS-010: Two CI-only flakes came from host-test parallel load

- **Source**: runs 41834133205 (loop-state `readCount==0`) and 41836821034 (ViewerUrl wait); both tests pass in isolation and locally
- **Fact**: The Host suite starts ~10 full web hosts; parallel collections starved timing waits on the CI runner. The loop-state test also had a genuine design race (reader could miss all writers). Fixed in `24e4d77` (deterministic at-least-one-read) and `57f5632` (assembly-level sequential execution).
- **Implications**: The green run is a real green, not a waiver; no assertions were weakened.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: OBS-009
### DEC-004: G1 approved — autonomy, streaming, write-capable sample agent

- **Decision**: Phase 3 is in scope with streaming; the sample agent must be able to modify the repository (harness working on itself), inside the worktree and through the validation/approval boundary.
- **Rationale**: product-owner decision, 2026-11-04; see `docs/execution/evidence/G1/decision-2026-11-04.md`.
- **Alternatives considered**: read-only sample (rejected by the owner); descope autonomy (rejected); descope streaming (rejected).
- **Related**: OQ-001, ART-003

### OBS-011: G1 closes the phase-1 exit

- **Source**: `docs/execution/evidence/G1/decision-2026-11-04.md`
- **Fact**: D1 yes, D2 yes, D3 write-capable (verbatim recorded). Plan, state and evidence updated in the same change.
- **Implications**: Phase 2 is the next executable work; Phase 3 requires G4 and G3 first. The write-capable scope adds +2 ED and a write-path security obligation.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: DEC-004
### VAL-003: WP-2.1 supported — the runtime resolves from the host provider

- **Target**: WP-2.1 acceptance (host/runtime share one provider; no nested provider)
- **Method**: Recording `IServiceProvider` wrapper test (asserts the run resolves `WorkflowRunTool` and `IArtifactStore` through it) plus a source-scan architecture test over `WIP/Wip.Runtime`; then the full gate and a host E2E in Worker mode.
- **Evidence examined**: `.tmp/gate-check` (16 TRX, verifier exit 0); architecture 4/4; host log line appears once.
- **Result**: Supported
- **Conclusion**: The nested provider is gone; the duplicate worker (RISK-001) is resolved.
- **Related**: RISK-001, EVD-013
### VAL-004: WP-2.2 supported — no process-wide pool

- **Target**: WP-2.2 acceptance ("No `StrategyPool.Instance` references; status API and agents see the same pool")
- **Method**: source grep (0 code references), full gate (16 projects, verifier exit 0), web suite run, host DI identity tests.
- **Evidence examined**: `.tmp/gate-check` (16 TRX); `.tmp/web-check/web.trx` (276/267, 9 pre-existing); grep.
- **Result**: Supported
- **Conclusion**: The pool is a single DI singleton; web routes, status, agents and tests resolve it explicitly.
- **Related**: EVD-016
### VAL-005: WP-2.4/2.5 increment supported — Bones runs from an explicit plan

- **Target**: Bones definition with plan-scoped maps and an initial request; production resolves the definition before the legacy path.
- **Method**: shape test on the registered definition (4 stages, 3 maps, initial request), seven workflow-run tests, the full 396-test Bones suite, and full Tests CI.
- **Evidence examined**: `.tmp/gate-check/Wip.Bones.Tests.trx` (396/394/2 waived); Tests CI run 41954106076 (all six jobs green).
- **Result**: Supported
- **Conclusion**: Bones executes through explicit plan maps in production; the legacy registry remains only for legacy/default-linear test fixtures (tracked as remaining WP-2.4 work).
- **Related**: EVD-017
### VAL-006: WP-2.4 increment 2 and WP-2.6 supported

- **Target**: no process-wide map registry; ShellHost free of Bones.
- **Method**: full gate (1,106/1,081/25 with only env-bound viewer waits), architecture suite (42 projects, zero waivers), ShellHost suite 47/47, Bones E2E 3 passed / 7 pre-existing waived, and Tests CI.
- **Evidence examined**: `.tmp/gate-check` TRX set; Tests CI run 41964635848 (six jobs green).
- **Result**: Supported
- **Conclusion**: the map registry is deleted (race source gone) and the ShellHost→Bones edge is removed and enforced by the architecture test.
- **Related**: EVD-018
## 2026-11-05T10:35:00-03:00 - Round 12 close: AgentExecutorTool + G5 packet; run ends not release-ready

- Added `AgentExecutorTool` (`tool.agent.run`) with tests; `Wip.Runtime.Tests` 123 total (1 pre-existing waived).
- Wrote the honest G5 release-readiness packet: **no release**, G5 not requested. Remaining: DeepSeek chat/SSE adapter + shell progress; real-model sample agent; WP-3.7 safety remainder + Agent/Security job; Phase 4 CI truth; Phase 5 security/privacy; Phase 6 release engineering; G2 re-review 2026-12-03; Windows containment C1.
- CI at the final HEAD: Modus CI green; Tests CI verified in the run record (see docs/execution/state.json). The 16-project offline gate was last fully green locally at 1,112/1,089/23 waived, zero unwaived; architecture 4/4 at 43 projects.
- **Goal status:** objective not achieved within the 12-round budget; goal left active for a continuation. No release claim.
### VAL-007: final run status supported

- **Target**: honest close of the 12-round run.
- **Method**: G5 packet review, full gate TRX set (last green), architecture run, CI run history, and the per-work-package evidence directories.
- **Evidence examined**: `docs/execution/evidence/G5/release-readiness.md`; `.tmp/gate-check` (16 TRX); Tests CI runs `41997346243` (success) and the final HEAD run; `docs/execution/state.json`.
- **Result**: Supported (not release-ready).
- **Conclusion**: Phases 2-3 core, G3/G4 accepted; Phase 3 remainder, Phases 4-6, G2 re-review and G5 remain. No release claim.
- **Related**: EVD-019

---

# Run 2 — Phase 3 completion through G5 (started 2026-11-05)

### OBS-012: Modus CI is red at a hard gate on the current HEAD

- **Source**: GitHub Actions API run `42003338267` (Modus CI) jobs and `check-runs/127163820471/annotations`; `.github/workflows/modus-ci.yml:45-46`
- **Fact**: The `tests` job fails at "Gate migration regression suite"; the failure annotation names `RuntimeAddedScheduledJobsAbsoluteGatesTests.ScheduledJobs_GivenRuntimeAddedTimerPlugin_ExpectedDeterministicRegistrationAndAbsoluteCadenceGates`. The non-blocking legacy suite step is skipped. Tests CI (`42003337898`) on the same HEAD is fully green.
- **Implications**: A CI hard gate is red on committed HEAD; per the run contract this must be fixed first, as its own commit, before Phase 3 feature work.
- **Confidence**: High
- **Agent**: orchestrator (API read)
- **Related**: EVD-020, EVD-021, EVD-022, EVD-023

### OBS-013: The failing gate is a timing test whose first sample is not a timer tick

- **Source**: `tests/Modus.Host.IntegrationTests/RuntimeAddedScheduledJobsAbsoluteGatesTests.cs:47-65`; `src/Modus.Host/Domain/Plugins/Host/AssemblyLifecycleHost.cs:141-154`
- **Fact**: `RegisterAndRunSchedules` executes the recurring operation synchronously at registration (line 141) and only then starts `PeriodicTimer` inside `Task.Run` (lines 147-154). The test records that registration execution as sample 0 and asserts every consecutive gap is within `interval ± max(250 ms, 20%)`.
- **Implications**: The first measured gap is "registration → first timer tick", which includes task scheduling, timer construction, first-time JIT and DI resolution. It is not an interval between periodic ticks, so the assertion conflates startup latency with cadence.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: OBS-012, HYP-003, EVD-025

### OBS-014: Local `dotnet test` is blocked by the session environment

- **Source**: `dotnet test tests/Modus.Core.Tests ... --filter UnitTest1`; `dotnet vstest <dll>`; both abort with `Win32Exception (5)` in `Microsoft.VisualStudio.TestPlatform.TestHost.DefaultEngineInvoker.SetParentProcessExitCallback`
- **Fact**: Every VSTest run in this session aborts before executing a test; opening the parent process from a direct child PowerShell succeeded, so this is a testhost/session interaction, not a repository defect.
- **Implications**: All test execution this run is performed on the WSL Ubuntu clean clone; the unchanged PowerShell verifier still validates the produced TRX on Windows. Recorded as an environment fact, not a waiver.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-026, DEC-005

### OBS-015: The Phase 3 vendor adapter is genuinely missing

- **Source**: `WIP/Wip.Abstractions/Chat/ChatContracts.cs:209-220`; grep for `: IChatModel` across `WIP/**/*.cs`
- **Fact**: `IChatModel` declares `CompleteAsync`/`StreamAsync`, and the only implementers are private test doubles in `WIP/Wip.Runtime.Tests`. No production adapter, host registration or shell rendering exists.
- **Implications**: WP-3.5 remains the first Phase 3 work package after the CI fix.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-027

### OBS-016: The harness records were stale at round start

- **Source**: `docs/execution/harness/goal.md` (phase-1 close-out), `state.md` (round 2, double-encoded characters), `docs/execution/evidence/G5/release-readiness.md`
- **Fact**: The prior run's goal/state stopped at the Phase-1 G1 gate and were not valid for this run; the G5 packet predates the Phase 3 completion work.
- **Implications**: `goal.md` and `state.md` were replaced with this run's contract (Phase 0); the journal and evidence files remain append-only. The G5 packet will be regenerated at the end.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-020

### HYP-003: Excluding the registration sample makes the cadence gate deterministic without weakening it

- **Claim**: The 1/8 failure under 2-core contention is caused by the registration-time execution being treated as the first cadence sample; asserting cadence only between periodic ticks (and bounding first-tick latency separately) removes the environment sensitivity while still failing on a wrong interval or missing ticks.
- **Supporting OBS**: OBS-013, EVD-025
- **Why it matters**: If true, the Modus CI hard gate can be made green deterministically with a small test fix instead of a retry or waiver. If false, the scheduler itself is defective and a product fix is required.
- **Falsification strategy**: Apply the fix; rerun 10 times under the same 2-core contention (expected 0 failures). Then inject a 600 ms interval into the scheduler and confirm the fixed test fails; also remove the scheduling loop and confirm the minimum-execution assertion fails.
- **Status**: Falsified
- **Confidence**: High

### VAL-008: HYP-003 supported — cadence fix removes the environment sensitivity

- **Target**: HYP-003
- **Method**: applied the fix, rebuilt the clone, ran 10 repetitions under the exact 2-core condition that produced the 1/8 failure; then two clone-only negative controls (600 ms interval; one-hour timer).
- **Evidence examined**: `.tmp/ci-fix-runs/fixed-1..10.trx` (10/10 pass); `.tmp/ci-fix-controls/wrong-interval.trx` (`597.4609ms between run 1 and 2`); `.tmp/ci-fix-controls/no-ticks.trx` (`TimeoutException`); `.tmp/ci-fix-controls/migration-after-fix.trx` (19/19).
- **Result**: Supported
- **Conclusion**: The failure was the registration sample being counted as a cadence interval; cadence between periodic ticks is stable under load while the gate still detects a wrong interval and a dead scheduler. No tolerance widening, no retry, no waiver.
- **Related**: EVD-028, EVD-029, EVD-030, EVD-031

- **Decision**: For this run, run `dotnet test` on the WSL Ubuntu clone at the same commit and run the unchanged `tools/verify-test-results.ps1` on the produced TRX from Windows; do not claim a Windows-local gate.
- **Rationale**: OBS-014 — VSTest is blocked in the Windows session, while CI (the authoritative gate) is Linux; the verifier and waiver register are unchanged, so the gate semantics are preserved.
- **Alternatives considered**: retrying the gate (rejected: does not fix anything); a Windows waiver (rejected: no evidence and would weaken the gate); running tests directly with the xUnit runner (rejected: not the project's gate).
- **Related**: OBS-014, EVD-026

### RISK-002: A flaky hard gate blocks every Modus CI push

- **Risk**: The absolute-cadence test fails intermittently on the CI runner, turning every push's Modus CI red even when the product is unchanged.
- **Likelihood**: High (3 of the last 20 Modus CI runs, all on this test)
- **Impact**: High (hard gate, blocks the release path and hides real regressions)
- **Evidence**: EVD-022, EVD-023, EVD-025
- **Mitigation**: Deterministic test fix (HYP-003), then a green Modus CI run on the pushed HEAD.
- **Gate**: —

### OBS-017: The fix is landed and locally proven; remote confirmation is pending the push

- **Source**: `git diff` of `tests/Modus.Host.IntegrationTests/RuntimeAddedScheduledJobsAbsoluteGatesTests.cs`; `.tmp/ci-fix-runs`, `.tmp/ci-fix-controls`
- **Fact**: The test now bounds sample 0 → 1 as first-tick latency and asserts cadence from index 2. All measurements listed in VAL-008 were run by the orchestrator on the clean Linux clone at the same commit with the patched file copied in.
- **Implications**: The commit can be pushed; Modus CI on the new HEAD is the remaining confirmation.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: OBS-013, VAL-008

### DEC-006: Fix the test's interval semantics, not the runner or the gate

- **Decision**: Treat the synchronous registration execution as a liveness sample and assert absolute cadence between `PeriodicTimer` ticks; keep the 300 ms ± 250 ms tolerance, the ≥5-execution requirement and every diagnostics assertion unchanged.
- **Rationale**: OBS-013 + EVD-025: the failing quantity measured startup latency, not cadence; EVD-028..031 show the corrected assertion is stable under load and still rejects a wrong interval and a dead scheduler.
- **Alternatives considered**: retrying the job (rejected: masks the defect and violates the no-retry-at-the-gate rule); widening the tolerance or excluding all outlier gaps (rejected: would hide a real cadence defect); an adaptive control-timer tolerance (rejected: more machinery, and the measured failure is fully explained by the startup sample); serializing the whole assembly (rejected: no evidence it affects a thread-pool startup delay and it costs CI time).
- **Related**: HYP-003, VAL-008, RISK-002

### OBS-018: The first fix was necessary but not sufficient — a second failure mode appeared

- **Source**: Modus CI run `42022906402` on `5cea565` (same test, hard gate); local reproduction under 2-core contention with an instrumented test: `.tmp/instrumented-2core/instr-4.trx`, `.tmp/control-timer-2core/ctl-1.trx`
- **Fact**: After the first fix, the test still failed: job gaps `[935.5, 10.5, 959.1, 166.0] ms` against a 300 ms interval. A reference `PeriodicTimer` in the same process produced `[1.5, 248.8, 304.0] ms` in the same run, so the burst pattern is a runtime/CPU-starvation artifact, not a host-scheduler defect.
- **Implications**: Per-gap wall-clock precision is not an observable property under starvation; the cadence gate must compare the job's rate with a reference that experiences the same starvation.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: OBS-013, HYP-003, VAL-009, EVD-032

### HYP-004: Rate parity against a reference timer is deterministic and still detects scheduler defects

- **Claim**: Requiring every recurring job tick to occur within one tick of a same-process reference `PeriodicTimer`'s count removes the burst/starvation flakiness while still failing for a wrong configured interval (slower or faster) or a dead scheduler.
- **Supporting OBS**: OBS-018
- **Why it matters**: This is the difference between a hard gate that blocks every push and one that measures the scheduler contract.
- **Falsification strategy**: 12 full-migration runs under 2-core contention (expect 0 failures); clone-only injections of 600 ms, 150 ms and a one-hour timer (expect all three to fail); full migration filter must stay 19/19.
- **Status**: Supported
- **Confidence**: High
- **Related**: OBS-018, VAL-009

### VAL-009: HYP-003 falsified, HYP-004 supported

- **Target**: HYP-003 (superseded sufficiency claim) and HYP-004 (rate parity)
- **Method**: shipped the first fix, observed Modus CI still red at `5cea565`; reproduced the post-fix failure under 2-core contention with an instrumented gap list and a reference timer; then replaced per-gap assertions with rate parity and re-ran the matrix (12 loaded runs + three injected-defect controls + the full filter).
- **Evidence examined**: Modus CI `42022906402`; `.tmp/instrumented-2core/instr-4.trx` (`Gaps=[935.5, 10.5, 959.1, 166.0]`); `.tmp/control-timer-2core/ctl-1.trx` (`ControlGaps=[1.5, 248.8, 304.0]`); `.tmp/rate-2core/rate-1..12.trx` (12/12 pass); `.tmp/rate-controls/slow-interval.trx`, `fast-interval.trx`, `no-ticks.trx` (all fail); `.tmp/rate-controls/migration-rate.trx` (19/19).
- **Result**: HYP-003 Falsified (necessary but not sufficient); HYP-004 Supported
- **Conclusion**: The permanent gate compares job tick rate with a reference timer under the same starvation, catches 600 ms/150 ms/dead-scheduler defects, and is stable across 12/12 loaded runs.
- **Related**: EVD-032..EVD-037, DEC-007

### DEC-007: Cadence gate measures rate parity, not wall-clock gaps

- **Decision**: Replace the per-gap `300 ms ± 250 ms` assertion with a reference-timer rate-parity assertion (±1 tick), keeping the first-tick bound, the ≥5-execution liveness check, the one-time assertion and every diagnostics assertion.
- **Rationale**: OBS-018 + EVD-032: the runtime's own `PeriodicTimer` bursts under CPU starvation (`Gaps=[935.5, 10.5, 959.1, 166.0]`, `ControlGaps=[1.5, 248.8, 304.0]`), so wall-clock precision is not a property of the host scheduler. EVD-033..036 show the replacement is stable and still catches slow, fast and dead schedulers.
- **Alternatives considered**: widening tolerance (rejected: hides real defects); retry (rejected: forbidden at the gate); serializing the assembly (rejected: does not remove CPU starvation); keeping per-gap with an environment allowance (rejected: tried, still failed 1/12 because burst timings differ between the two timers).
- **Related**: HYP-003, HYP-004, VAL-009, RISK-002