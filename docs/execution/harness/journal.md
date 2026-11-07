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

### OBS-019: Tests CI exposed a separate concurrency-test flake

- **Source**: Tests CI run `42028137512` on `22bcdd5`, job `wip-offline-tests`; annotations name `BonesLoopState_GivenConcurrentReadWrite_SeesCoherentIsRunning` and `BonesLoopState_GivenConcurrentIncrement_SeesMonotonicIterationCount`; `WIP/Wip.Bones.Host.Tests/BonesLoopStateConcurrencyTests.cs`
- **Fact**: Both tests failed unwaived. They assumed the reader and writer overlap in time; on a loaded single-core runner the reader can finish its fixed 1,000,000 iterations before the writer is scheduled. Reproduced 4/4 on `taskset -c 0` with a busy loop; the failure message is `Reader should have observed IsRunning=true at least once`.
- **Implications**: These are real test-design flakes (not product defects); no WIP product code changed in `22bcdd5`, and Tests CI was green on the previous HEAD.
- **Confidence**: High
- **Agent**: orchestrator (API + reproduction)
- **Related**: EVD-039, EVD-040, HYP-005

### HYP-005: Bounded writer/reader coordination makes the concurrency tests deterministic

- **Claim**: Letting the reader wait (bounded) for the writer's first value and letting the writer run until both values are observed keeps the same invariants while removing the scheduling assumption.
- **Supporting OBS**: OBS-019
- **Why it matters**: Restores the authoritative Tests CI gate without weakening the coherence/monotonicity assertions.
- **Falsification strategy**: 15 runs on one core with a busy loop (expect 0 failures); inject an always-true writer and a non-monotonic writer and confirm both fail; then run the full Host suite and the 16-project gate.
- **Status**: Supported
- **Confidence**: High
- **Related**: OBS-019, VAL-010

### VAL-010: HYP-005 supported — coordinated tests stable and still falsifiable

- **Target**: HYP-005
- **Method**: rewrote the two tests' coordination (bounded waits + yield), ran 15 repetitions under the failing single-core condition, then two clone-only violation injections.
- **Evidence examined**: `.tmp/concurrency-fix/cfix-1..15.trx` (15/15 pass); `.tmp/concurrency-controls/always-true.trx` (`Reader should have observed IsRunning=false at least once`); `.tmp/concurrency-controls/non-monotonic.trx` (`Assert.Equal() Failure`).
- **Result**: Supported
- **Conclusion**: Same invariants, deterministic scheduling; the tests still fail on a coherence or monotonicity violation. No waiver, no retry.
- **Related**: EVD-041, EVD-042

### DEC-008: Coordinate the concurrency tests instead of retrying or waiving them

- **Decision**: Add bounded coordination to the two `BonesLoopStateConcurrencyTests` cases (writer-start signal; reader reads until both values or a 10 s bound; iteration writer publishes 1 before the reader samples).
- **Rationale**: EVD-040/041/042 — the failures are test scheduling assumptions, the invariants are unchanged, and the fixed tests still fail on injected violations.
- **Alternatives considered**: retry at the gate (rejected); waive the two tests (rejected: they are meaningful assurances); reduce the iteration count (rejected: does not remove the race).
- **Related**: OBS-019, HYP-005, RISK-002

### OBS-020: The authoritative offline gate is Windows, so a WSL run cannot validate it

- **Source**: `.github/workflows/tests-ci.yml:76-94` (`wip-offline-tests` has `runs-on: windows-latest` and runs `tools/run-wip-tests.ps1`); WSL run `.tmp/gate-r1/*.trx`
- **Fact**: The 16-project gate is executed on Windows. A WSL run of the same projects produced platform-only failures: 30 `dotnet.cmd` execution failures in `Wip.Validation.DotNet.Tests`, a backslash path-guard expectation in `Wip.Artifacts.Local.Tests`, and a line-ending hash expectation in `Wip.Workspaces.Git.Tests`. With the adapter added, `Wip.Runtime.Tests` discovered 130 tests and all 7 adapter tests passed; its only non-environment failure is the pre-existing waived cancellation test.
- **Implications**: Local Windows VSTest is blocked (`Win32Exception 5`), so no local run of this session can be the authoritative gate. Tests CI `wip-offline-tests` (windows-latest) is the only authoritative evidence; WSL runs are supplementary. This must be stated in every gate claim.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-043, EVD-044, DEC-009

### DEC-009: Authoritative gate evidence comes from Tests CI windows-latest

- **Decision**: Record the 16-project offline gate result only from Tests CI's `wip-offline-tests` job on `windows-latest`; WSL runs are used for targeted Linux-relevant suites (for example the Modus migration test) and for adapter unit tests, never as the authoritative gate.
- **Rationale**: OBS-020 + EVD-043: the gate is Windows-only; the session's Windows testhost is blocked, and WSL semantics differ on Windows-specific tests. Claiming a WSL result as the gate would be false.
- **Alternatives considered**: claiming the WSL pass (rejected: false); excluding the Windows-only tests (rejected: weakens the gate); creating a Windows scheduled task to bypass the testhost restriction (rejected: containment-sensitive and out of scope).
- **Related**: OBS-020, EVD-043

---

# Run 2 — Phase 3 completion (started 2026-11-05, round 2)

### OBS-021: Shell streaming progress is implemented and falsifiable

- **Source**: `WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs` (`RunAgentStreamingAsync`); `WIP/Wip.Shell/Interactive/WipShellCommandLoop.cs` (`agent` command, delta rendering); `WIP/Wip.Shell/Interactive/WipShellCommandSemantics.cs`; tests `WIP/Wip.Runtime.Tests/Repo/AgentStreamingRuntimeTests.cs`, `WIP/Wip.Shell.Tests/Interactive/WipShellCommandLoopTests.cs`
- **Fact**: The runtime streaming entry yields adapter events and appends memory; the shell `agent "<task>"` command renders deltas before the status line. Focused tests pass (2 runtime, 1 shell); the full shell suite is 33/33. Removing the delta-rendering line makes the shell test fail; the first version of that assertion was satisfied by the final answer alone ("hello" also appears in `Agent answer: hello`) and was tightened after the control exposed it.
- **Implications**: WP-3.5 is complete locally; the authoritative Tests CI gate must confirm the new totals (Runtime 132, Shell 33) on the committed HEAD.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-050..EVD-053

### HYP-006: The streaming tests fail when the streaming implementation is stubbed

- **Claim**: The runtime and shell streaming tests depend on the actual streaming path, not on incidental output.
- **Supporting OBS**: OBS-021
- **Why it matters**: A green streaming test that passes with the stream path removed would not be evidence for WP-3.5.
- **Falsification strategy**: In the clone, replace the runtime `yield return streamEvent` with a fixed stub delta and remove the shell delta write; both tests must fail.
- **Status**: Supported
- **Confidence**: High
- **Related**: EVD-051

### VAL-011: HYP-006 supported — both streaming tests fail under stubs

- **Target**: HYP-006
- **Method**: clone-only stub controls, rebuild, run the focused tests.
- **Evidence examined**: runtime control `CTRL_A_EXIT=1`; shell control after tightening `CTRL_B_EXIT=1`; positive runs pass.
- **Result**: Supported
- **Conclusion**: The tests exercise the real streaming path; the shell assertion was strengthened after the first control passed spuriously.
- **Related**: EVD-051

### DEC-010: Implement shell progress as a first-class `agent` command

- **Decision**: Add `agent "<task>"` to the session command surface, its no-session guidance, help text and semantics; it calls the new `RunAgentStreamingAsync` and renders deltas/tool events/final status.
- **Rationale**: OBS-021 + EVD-050..053: this is the user-visible surface for WP-3.5 and it is covered by a falsifiable test.
- **Alternatives considered**: decorating the existing `run` command (rejected: `run` is workflow execution with different governance semantics); only logging stream events (rejected: not user-visible progress).
- **Related**: OBS-021, EVD-050, EVD-053

### OBS-022: The real-model write-capable sample agent works at the HTTP boundary

- **Source**: `WIP/Wip.Runtime.Tests/Repo/AgentRealModelHttpBoundaryE2ETests.cs`; run `.tmp/wp36/wp36-e2e-3.trx`; `WIP/Wip.Runtime.Tests/Runtime/DeepSeekChatModelLiveTests.cs`
- **Fact**: A local `HttpListener` fake DeepSeek server (search tool call → write tool call → final answer) drives the real adapter/tool invoker/executor/`RunAgentAsync` end to end. The test passes 3 requests: the search result contains `needle`, `docs/agent-note.md` is written inside the worktree with exact content (nothing outside), memory records the task and answer, session-state telemetry is persisted, usage is recorded, and the session is neither `Merged` nor `Approved` with no merge artifact.
- **Implications**: The G1 non-negotiable ("the harness must be able to work on itself") is demonstrated at the HTTP boundary with real tool/executor code. The worktree guard is exercised by the real write path.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-056, EVD-057, EVD-058, EVD-059

### HYP-007: The sample-agent E2E depends on the real write tool, not on a fake file

- **Claim**: The `docs/agent-note.md` assertion fails if the repository write tool stops writing.
- **Supporting OBS**: OBS-022
- **Why it matters**: Otherwise the E2E could pass while the write capability is stubbed.
- **Falsification strategy**: comment out `File.WriteAllText` in `RepoAgentTools` on the clone; rebuild and run the E2E (expect failure); restore.
- **Status**: Supported
- **Confidence**: High
- **Related**: EVD-059

### VAL-012: HYP-007 supported — removing the write fails the E2E

- **Target**: HYP-007
- **Method**: clone-only removal of the write call, rebuild, run the E2E.
- **Evidence examined**: `/tmp/wp36-ctrl.log` (`CTRL_EXIT=1`); restored build.
- **Result**: Supported
- **Conclusion**: The E2E's edit assertion depends on the real worktree-confined write tool.
- **Related**: EVD-059

### DEC-011: Keep the live DeepSeek test opt-in and offline-gate safe

- **Decision**: `DeepSeekChatModelLiveTests` performs a real call only when `DEEPSEEK_API_KEY` is set; otherwise it returns immediately without network access.
- **Rationale**: OBS-022 + EVD-057: the offline gate (Windows CI) has no credentials, and a live call must never make the gate network-dependent or flaky.
- **Alternatives considered**: `[Fact(Skip=...)]` (rejected: static skip cannot honour an env key); filtering the test by category in CI (rejected: the gate would silently drop it; an env-gated early return keeps it discoverable and honest).
- **Related**: OBS-022, EVD-057

### OBS-023: The agent safety remainder is implemented and wired into the security jobs

- **Source**: `WIP/Wip.Runtime.Tests/Repo/AgentSafetyTests.cs`; `.github/workflows/tests-ci.yml` (`wip-security` and `wip-security-linux` now run the filter); `.tmp/wp37/wp37-safety2.trx`
- **Fact**: Three fail-closed tests cover prompt injection via tool output (observation is data; `.git/config` intact; no delete call), self-approval (unregistered `tool.session.approve` → `unknown-tool:`, session not Approved/Merged, no approval artifact) and exfiltration (`../../secret.txt` at the exact resolved path and an out-of-worktree search return no secret). The traversal control (both `WorktreePathGuard` throws removed) makes the test fail. The suite runs in both security jobs.
- **Implications**: WP-3.7 is complete locally; the authoritative confirmation is the next Tests CI run (Windows security job plus offline gate at Runtime 137).
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-060, EVD-061, EVD-062

### VAL-013: The safety controls fail closed

- **Target**: OBS-023 / the WP-3.7 safety assertions
- **Method**: positive suite run (3/3) and a clone-only removal of both worktree-guard throws, rebuild, rerun the traversal test.
- **Evidence examined**: `.tmp/wp37/wp37-safety2.trx` (3/3); control `/tmp/wp37c-ctrl.log` (`CTRL_EXIT=1`).
- **Result**: Supported
- **Conclusion**: The exfiltration test detects a real escape, not a missing file; the injection and self-approval assertions hold.
- **Related**: EVD-060, EVD-062

### DEC-012: Run the agent safety suite in both security jobs

- **Decision**: Add `--filter "FullyQualifiedName~AgentSafetyTests"` over `Wip.Runtime.Tests` to `wip-security` (Windows) and `wip-security-linux`, with TRX in the existing security artifacts.
- **Rationale**: OBS-023: the agent-loop abuse cases belong to the security posture, not only to the offline suite; running on both OSes catches platform-specific guard behavior.
- **Alternatives considered**: offline suite only (rejected: a security regression would not fail the security job); a separate workflow (rejected: more CI surface for no benefit).
- **Related**: OBS-023, EVD-061

---

# Run 3 — Phase 4 completion → Phase 5 (rounds 5–6)

### OBS-025: Privacy/retention implemented with falsifiable controls; security packet written

- **Source**: `WIP/Wip.Abstractions/Privacy/PrivacyContracts.cs`, `WIP/Wip.Runtime/Runtime/SecretRedactor.cs`, `SessionArtifactRetention.cs`, `ArtifactAgentMemory.cs`, `WIP/Wip.Shell/Interactive/WipShellCommandLoop.cs`; tests under `WIP/Wip.Runtime.Tests/Privacy/` and the shell retention test
- **Fact**: Message content and tool-call arguments are redacted before persistence and on load; `retention status|purge [days]` reports/purges artifacts under `<repo>/.wip/sessions/*/artifacts`; 5 redactor tests, 3 retention tests, 1 memory-redaction test and 1 shell test pass; full counts are Runtime 146 / Shell 34. The no-op redactor and age-ignoring purge controls both fail. The WP-5.5 packet records 9 findings (C1 unclaimed, shell privilege, pattern-based redaction limits) and explicitly states external sign-off is absent.
- **Implications**: WP-5.2 is complete locally and WP-5.5 has its review packet; CI confirmation and the human G2/G5 gates remain.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-070..EVD-074

### HYP-009: The privacy tests depend on the real redaction/purge behavior

- **Claim**: Replacing the redactor with a no-op, or ignoring the retention age boundary, makes the privacy tests fail.
- **Supporting OBS**: OBS-025
- **Why it matters**: Otherwise the privacy suite could pass without enforcing anything.
- **Falsification strategy**: clone-only no-op redactor and age-ignoring purge; both focused suites must fail; restore.
- **Status**: Supported
- **Confidence**: High
- **Related**: EVD-070, EVD-071

### VAL-015: HYP-009 supported

- **Target**: HYP-009
- **Method**: clone-only controls, rebuild, focused runs.
- **Evidence examined**: `CTRL_A_EXIT=1` (redactor no-op), `CTRL_B_EXIT=1` (purge ignores age); restored builds.
- **Result**: Supported
- **Conclusion**: The privacy tests exercise the real behavior and fail closed under stubs.
- **Related**: EVD-070, EVD-071

### DEC-014: Pattern-based redaction plus operator-driven purge

- **Decision**: Ship a deterministic pattern-based `SecretRedactor` (built-ins + optional patterns) and an explicit `retention status|purge` command; no background scheduler in this increment.
- **Rationale**: OBS-025: covers the known credential shapes and gives operators a deterministic purge; documented limits are honest and extensible.
- **Alternatives considered**: heuristic ML redaction (rejected: non-deterministic); automatic scheduled purge in-process (rejected: deployment concern; out of scope for 1.0).
- **Related**: OBS-025, EVD-070..EVD-073

### OBS-026: Phase 6 release engineering is drafted and locally rehearsed; no release performed

- **Source**: `docs/release/` (release notes, packaging/signing, compatibility/upgrade, rollback, post-release verification); `docs/execution/evidence/WP-6.1/stabilization.md`, `WP-6.2/draft.md`, `G2/re-review-packet-2026-12-03.md`, `G5/release-readiness.md`
- **Fact**: Local publish dry-run of both hosts succeeded (exit 0; 99 DLLs, 36,775,200 bytes; version 1.0.0.0 / `1.0.0+e3fee2d`); checksums recorded. Rollback flag/fail-closed tests are 9/9. Nothing was signed, published or deployed. The soak/benchmark/three-green-freeze items are explicitly not executed and remain G5 blockers. The G5 packet says **not release-ready**; G2 is conditional/trusted-only and external security sign-off is open.
- **Implications**: Phase 6 documentation/dry-run is ready for the human decision, but release verification is incomplete by design (no soak/benchmark) and no release claim is made.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-076..EVD-080

### HYP-010: The release dry-run and rollback controls are reproducible and truthful

- **Claim**: The publish dry-run succeeds locally without signing/publishing, and the documented rollback controls are exercised by passing tests; the worker-execution failure on WSL is an environment gap, not a release-gate failure.
- **Supporting OBS**: OBS-026
- **Why it matters**: The G5 packet must distinguish "prepared" from "verified" and not hide an environment failure.
- **Falsification strategy**: re-run the publish commands and the flag tests; inspect the worker TRX message; confirm no registry/signing side effects.
- **Status**: Supported
- **Confidence**: High
- **Related**: EVD-076..EVD-078

### DEC-015: Keep the release dry-run local; no signing or publish before G5

- **Decision**: Produce local publish artifacts, checksums and documentation only. Signing/publishing remain human-gated because credentials/authority are not available.
- **Rationale**: OBS-026 + the hard constraints: no publish/sign/deploy before an explicit human G5 decision; credentials are human-owned.
- **Alternatives considered**: self-signed test signing (rejected: not the release signature and could confuse evidence); publishing a pre-release (rejected by the constraints).
- **Related**: OBS-026, EVD-076, EVD-080

### OBS-027: The G5 golden path works on the real shell host

- **Source**: `.tmp/golden/demo-repo.log`; `docs/execution/evidence/G5/golden-path-demo.md`
- **Fact**: On a clean clone at `977a600` with a Release build, the real `Wip.ShellHost` process ran `init` → workflow selection → session start → plan → run → status → artifacts → retention status and exited 0. It created 12 artifacts (9,195 bytes), listed them, and printed the governance guidance requiring explicit validate/review/approve/merge (no auto-merge). The agent model path was not exercised because no `IChatModel` was configured; it is covered by the shell streaming test and the HTTP-boundary E2E.
- **Implications**: The G5 packet now has a reproducible golden-path demo. It is evidence, not an approval; G2/C1 and external sign-off remain human gates.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-081

### VAL-016: Golden-path demo reproduced by the orchestrator

- **Target**: OBS-027 / G5 demo
- **Method**: built the host from the clean clone, ran the real process with scripted stdin, captured the transcript and exit code.
- **Evidence examined**: `.tmp/golden/demo-repo.log` (exit 0, session id, 12 artifacts, retention status).
- **Result**: Supported
- **Conclusion**: The operator golden path works end to end at the current commit; no release claim follows from it.
- **Related**: EVD-081, OBS-027

### OBS-028: A 2,000-game champion benchmark executes; the domain data is small

- **Source**: `.tmp/bench/wp61/history.json`, `.tmp/bench/wp61/champion/`; `docs/execution/evidence/WP-6.1/stabilization.md`
- **Fact**: `champion --games 2000 --seeds 910001` on a snapshot built from the 2-session stub dataset exited 0 in 26,168 ms, selected `initial-seat-2-v2@1f5136b3` (Promoted), 583/2000 wins (29.15%, LCB 27.2%), and wrote the strategy library + champion result. The full 1,973-session corpus was not benchmarked; the 24-hour soak remains unexecuted.
- **Implications**: The benchmark path is real and fast enough for CI-like runs, but the statistical evidence is limited by the input corpus; the G5 packet keeps soak/freeze as blockers.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-082, OBS-026

### VAL-017: Benchmark evidence reproduced and scoped honestly

- **Target**: OBS-028
- **Method**: built the history snapshot from a stub dataset, ran the champion benchmark with a fixed seed, measured duration and read the result artifacts.
- **Evidence examined**: console output (exit 0, 2000 games), `champion-result.json`, `seat-1.json`.
- **Result**: Supported (with the small-corpus limitation recorded)
- **Conclusion**: The 2,000-game benchmark requirement is met in execution, not in statistical scale; the limitation is in the G5 packet.
- **Related**: EVD-082

### OBS-029: Human gates recorded and one viewer-stage flake hardened

- **Source**: `docs/execution/evidence/G2/decision-2026-11-05-owner-review.md`; `docs/execution/evidence/G5/release-readiness.md`; Tests CI `42065481595`
- **Fact**: The owner accepted the prepared G2 packet as the security review (no external expert; not claimed) and asked for a best-effort G5 outcome; no release/publish was performed or authorized. Tests CI on `4bba218` failed one unwaived Playwright stage test; the same commit passed 3/3 locally, and after widening the bounded observation window (240 s outer / 15 s inner DOM match) it passed 3/3 locally again. G2 stays conditional/trusted-only and C1 unclaimed.
- **Implications**: The engineering objective and the human decision are complete; the only remaining item is the CI confirmation of the flake fix and the final records commit.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-083, EVD-084

### DEC-016: Owner review replaces the unavailable external G2 reviewer

- **Decision**: Record the owner's acceptance of the prepared G2 packet as the review, explicitly disclosing that no independent external expert was available; do not upgrade containment or claim C1.
- **Rationale**: EVD-083: the owner chose this option; fabricating an external review is forbidden and would be false.
- **Alternatives considered**: holding indefinitely for an external reviewer (declined by the owner); claiming independent review (rejected: false).
- **Related**: OBS-029, EVD-083

### DEC-017: Record the G5 decision as best-effort acceptance, not as a release

- **Decision**: Record the owner's "best effort" answer as acceptance of the residual risk and completion of the decision gate, with no release claim and no publish/sign/deploy (no credentials supplied). Release execution remains a separate, credentialed action.
- **Rationale**: EVD-083: the owner declined a human expert and asked for best effort; the hard constraints still forbid a release claim or unauthorized publication.
- **Alternatives considered**: treating "best effort" as publication authorization (rejected: no credentials, and no explicit release instruction); holding the goal open indefinitely (declined by the owner).
- **Related**: OBS-029, EVD-083

---

# Continuation run (2026-11-06): Bones agents on the generic WIP agent runtime

> New goal contract in `goal.md`. Round numbering restarts; IDs continue from the prior run.

### OBS-030: The runtime agent seams are absent from the Bones agent assembly

- **Source**: `WIP/Wip.Bones.Agents/**/*.cs` (grep), `WIP/Wip.Bones/**/*.cs` (grep)
- **Fact**: A ripgrep over `WIP/Wip.Bones.Agents` for `AgentExecutor|IChatModel|RuntimeToolInvoker|IAgentMemory|ArtifactAgentMemory|AgentStreamEvent|RunAgentStreamingAsync|PermissionCatalog|PermissionMode` returns **no matches**. The seams exist only under `WIP/Wip.Runtime` and `WIP/Wip.Abstractions`.
- **Implications**: The migration is a real change, not a rename; the Bones assembly currently has no compile-time dependency on the generic loop.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-090, HYP-011

### OBS-031: The four domain agents depend on `IModelProvider`, and the DeepSeek coupling is in the host

- **Source**: `WIP/Wip.Bones.Agents/Ponder/BonesPonderAgent.cs:13,20,88`; `Ponder/BonesCoLearningPonderAgent.cs:17`; `Enhance/BonesEnhanceStrategyAgent.cs:17,29,180`; `Enhance/BonesCoLearningEnhanceAgent.cs:16`; `WIP/Wip.Bones.Host/BonesHostServiceCollectionExtensions.cs:275-293`; `WIP/Wip.Bones.ModelProviders.DeepSeek/BonesDeepSeekServiceCollectionExtensions.cs:86-88`
- **Fact**: All four agents implement `IAgent<,>` and call `_modelProvider.ExecuteAsync(...)` through `IModelProvider<...>`. No Bones.Agents file references `Wip.Bones.ModelProviders.DeepSeek`. The production wiring (`AddBonesDeepSeekModelProviders`) lives in `Wip.Bones.Host` and registers the three DeepSeek providers, including `DeepSeekBonesPlayTurnProvider`.
- **Implications**: The user's starting-point wording ("call Wip.Bones.ModelProviders.DeepSeek directly") is imprecise; the real coupling is the `IModelProvider` abstraction plus host registration. Porting must replace the abstraction in the agents and the registration in the host.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-089, EVD-094, VAL-018

### OBS-032: The declarative workflow plan is the runtime-preferred path, and the legacy shim is still present

- **Source**: `WIP/Wip.Bones.Agents/Workflow/BonesLearningWorkflow.cs:12-59`; `Workflow/BonesLearningWorkflowRegistration.cs:18-29`; `WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs:255-263,1794-1802`
- **Fact**: `BonesLearningWorkflow` implements both `IWorkflow<,>` (with a stub `ExecuteAsync` returning `workflow-dispatch` ids) and `IWorkflowDefinition` (4 stages, 3 `StageMap`s, initial request). It is registered twice — `AddWorkflow<...>` and `AddWorkflowDefinition<BonesLearningWorkflow>()`. `RunAsync` calls `ResolveDefinitionPlan` first and falls back to `LinearWorkflowPlanFactory` only when no definition exists.
- **Implications**: WP6 can delete the legacy registration/implementation without changing execution, provided no consumer resolves `IWorkflow<BonesLearningWorkflowRequest,...>` directly.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-088, OQ-004

### OBS-033: Baseline CI is green at `dc6b76c` but red at the current HEAD

- **Source**: GitHub Actions API runs `42125309033`, `42125309034` (`dc6b76c`); run `42211927009` at `ba5debf`; job annotations for check run `127872411446`
- **Fact**: Tests CI `42125309033` on `dc6b76c` succeeded in all six jobs and Modus CI `42125309034` succeeded. The current HEAD `ba5debf` (docs-only) has Tests CI run `42211927009` **failed** in `wip-offline-tests`: the gate annotation names `Wip.Bones.Host.Tests.BonesHostViewerLearningLoopStatusTests.BonesHostViewerLearningLoopStatus_GivenRunningLoop_ExpectedSidebarStageTracksStatusApi` as an unwaived failure.
- **Implications**: The final green must be proven on the new HEAD; the pre-existing viewer-stage flake is a live risk and is not caused by this goal's changes.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-086, EVD-087, RISK-004

### OBS-034: The project graph already enforces the key boundaries

- **Source**: `WIP/Wip.Bones/Wip.Bones.csproj:12-13`; `WIP/Wip.Sandbox.Worker/Wip.Sandbox.Worker.csproj`; `WIP/Wip.ShellHost/Wip.ShellHost.csproj`; `tests/Wip.Architecture.Tests/ArchitectureRules.cs:13-24`; `ArchitectureReferenceTests.cs:16`
- **Fact**: `Wip.Bones` references `Wip.Sandbox.BonesContracts` and `Wip.Sandbox.WorkerClient`; `Wip.Sandbox.Worker` references only `Wip.Sandbox.Protocol` + `Wip.Sandbox.BonesContracts`; `Wip.ShellHost` has no Bones reference. The architecture suite has 9 rules and asserts 43 projects; `RuntimeSource_GivenBuildServiceProvider_ExpectedNoNestedProvider` scans `Wip.Runtime` for `BuildServiceProvider`.
- **Implications**: A new `bones-agents-no-legacy-provider` rule is the natural guard for WP4; no boundary relaxation is needed.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-091, EVD-094

### OBS-035: Local Windows VSTest is blocked; WSL has an SDK but a separate NuGet cache

- **Source**: `dotnet test WIP/Wip.Abstractions.Tests/... --filter ChatContractsTests` (exit 1, Win32Exception 5); `wsl -d Ubuntu -- ~/.dotnet/dotnet --version` = 10.0.103; WSL build without restore = `NETSDK1064: Package xunit.analyzers 1.18.0 was not found`
- **Fact**: The Windows testhost aborts with `System.ComponentModel.Win32Exception (5): Access is denied` in `SetParentProcessExitCallback` both from pwsh and through WSL interop. WSL Ubuntu 24.04 has .NET SDK 10.0.103 but its package cache lacks the restored packages, so a Linux restore is required before any Linux test run.
- **Implications**: Local verification must run on WSL after restore; Windows builds remain available for compile checks.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-092, RISK-003

### OBS-036: The waiver register declares the baseline counts and 27 failures

- **Source**: `docs/plan/test-waivers.json` (`expectedTotals`, `failures`)
- **Fact**: `expectedTotals` declares `Wip.Bones.Tests` 396, `Wip.Runtime.Tests` 146, `Wip.Shell.Tests` 34; the `failures` array has 27 entries. These are the numbers the offline gate enforced in the green `dc6b76c` run.
- **Implications**: Any change to discovered counts must be justified by real TRX counters, never by editing the waiver to hide a failure.
- **Confidence**: High (file read) / Medium (the exact discovered counts are only indirectly evidenced by the passing gate)
- **Agent**: orchestrator
- **Related**: EVD-093, RISK-005

### HYP-011: Moving Ponder/Enhance to the adapter preserves domain outputs for identical model markdown

- **Claim**: If the prompt messages, parse/compile/persist logic and retry sequence are unchanged, then a scripted fake model returning the same markdown produces the same `BonesPonderResult`/`BonesEnhanceStrategyResult` (strategy ids, source, artifact kinds, promotion outcome) through the new `IAgentExecutor` path as through the legacy `IModelProvider` path.
- **Supporting OBS**: OBS-030, OBS-031
- **Why it matters**: This is the core behavior-preservation gate; if false, the port changes game-learning behavior.
- **Falsification strategy**: Build deterministic fixtures with a scripted fake `IChatModel` and a scripted fake `IModelProvider`; run both paths for the same request and compare every result field and artifact payload. A control that drops the tool call/turn from the new path or changes one prompt line must make the comparison fail.
- **Status**: Supported
- **Confidence**: High

### HYP-012: The adapter can map every non-success executor status to the domain's existing exception semantics

- **Claim**: Mapping `Failed`, `MaxStepsReached`, `ToolFailureLimitReached`, `TokenBudgetExhausted` and `TimedOut` to `InvalidOperationException` (with the executor error) preserves the existing tests' `Assert.ThrowsAsync<InvalidOperationException>` contract for model/compile failures while still surfacing the status deterministically.
- **Supporting OBS**: OBS-031
- **Why it matters**: Existing tests assert `InvalidOperationException` on retry exhaustion; the adapter must not leak a new exception type into those paths.
- **Falsification strategy**: Run the existing Bones test suite unchanged after the port; additionally assert the adapter's exception type/message in new tests and verify a stubbed `IAgentExecutor` returning each status raises the mapped exception.
- **Status**: Supported
- **Confidence**: High

### VAL-018: Starting-point verification — the seams are absent, but "calls DeepSeek directly" is falsified

- **Target**: round-0 starting-point claim batch (user-supplied)
- **Method**: read the four agent files and their constructors; grepped the Bones assemblies for runtime and DeepSeek symbols; read the host registration and the provider project; read the workflow and runtime plan resolution; re-ran the local test commands; read the GitHub API run/job/annotation records.
- **Evidence examined**: OBS-030..036, EVD-086..094.
- **Result**: **Supported** for the workflow-plan preference, the absence of runtime seams, the project graph, the baseline counts, and the DeepSeek host registration. **Falsified** for the literal claim that the agents "call `Wip.Bones.ModelProviders.DeepSeek` directly (`DeepSeekBonesPlayTurnProvider`)": no Bones.Agents file references the provider project, and `DeepSeekBonesPlayTurnProvider` is the play-turn provider, not the Ponder/Enhance one. The corrected fact is that the agents depend on `IModelProvider` and the host wires the DeepSeek implementations.
- **Conclusion**: The verified starting point stands with that correction; proceed.
- **Related**: OBS-030, OBS-031, OBS-032, OBS-033, OBS-034, OBS-035, OBS-036

### DEC-018: Adapter shape and test-compatibility strategy

- **Decision**: Add a generic `BonesAgentAdapter<TRequest,TResult>` in `Wip.Bones.Agents` that implements `IAgent<TRequest,TResult>`, takes an `IAgentExecutor` plus a request-factory and a result-mapper, delegates exactly one `ExecuteAsync` and maps non-success statuses to a `BonesAgentExecutionException : InvalidOperationException`. Port the four agents by composition (primary public constructor takes `IAgentExecutor`; an `internal` compatibility constructor keeps the existing `IModelProvider` signature for the 396 tests and delegates through a test-only executor shim). Agent tools are registered as a Bones agent toolset distinct from `WipBuilder` capabilities so `BonesLearningWorkflowRegistrationTests`' "exactly 4 descriptors" assertion is untouched.
- **Rationale**: OBS-031, OBS-036, HYP-012 — the migration must not rewrite the existing test corpus, and the builder descriptor count is itself a tested contract.
- **Alternatives considered**: (a) replacing the agents with adapter-typed registrations only — rejected because it would force rewrites of 26 construction sites and lose the old-vs-new comparison surface; (b) expressing compile as a builder capability — rejected because it changes the tested descriptor set; (c) keeping the `IModelProvider` path public — rejected as it would leave the private stack as production API.
- **Related**: HYP-011, HYP-012, TODO-001

### TODO-001: WP1 — Bones-side adapter, tests, commit

- **Task**: Implement `BonesAgentAdapter` + `BonesAgentExecutionException` + request/result mapping helpers and focused tests; build and run on WSL; commit one work package.
- **Priority**: P0
- **Depends on**: DEC-018, WSL restore
- **Status**: Done
- **Related**: DEC-018

### TODO-002: WP2 — port Ponder (single-seat + co-learning)

- **Task**: Prompts class, `ToolDefinition`s, `RuntimeToolInvoker` routing, `AgentLoopOptions` budgets, `ArtifactAgentMemory`; parity fixtures and negative controls; commit.
- **Priority**: P0
- **Depends on**: TODO-001
- **Status**: Done

### TODO-003: WP3 — port Enhance (single-seat + co-learning)

- **Task**: Same treatment; keep Observe/Play as deterministic tools; parity + controls; commit.
- **Priority**: P0
- **Depends on**: TODO-002
- **Status**: Done

### TODO-004: WP4 — replace the provider and guard the boundary

- **Task**: Register `AddDeepSeekChatModel` with host DI; remove production DeepSeek provider call sites; mark the project test-only; add and prove the architecture rule; commit.
- **Priority**: P0
- **Depends on**: TODO-003
- **Status**: Done

### TODO-005: WP5 — permissions, audit, safety negative controls

- **Task**: CapabilityContext/grants on tool calls; denial + Audit-mode tests; injection/hidden-data/unbounded-loop fail-closed; redaction; commit.
- **Priority**: P0
- **Depends on**: TODO-004
- **Status**: Done

### TODO-006: WP6 — retire the legacy workflow shim and run full suites

- **Task**: Remove `IWorkflow<,>` implementation/registration and stub `ExecuteAsync`; reconcile consumers; run Bones + Runtime + Shell suites; commit.
- **Priority**: P0
- **Depends on**: TODO-005
- **Status**: Done

### RISK-003: WSL/Linux verification differs from the authoritative Windows gate

- **Risk**: Linux runs may fail/pass differently (Windows-only validation tests, path handling, worker handshake), and local Windows VSTest cannot run at all.
- **Likelihood**: High
- **Impact**: Medium
- **Evidence**: OBS-035, EVD-092; prior EVD-043 recorded the same platform gap.
- **Mitigation**: Use WSL for fast falsification; treat CI `wip-offline-tests` on windows-latest as the authoritative gate; record platform-only failures explicitly.
- **Gate**: G4

### RISK-004: The pre-existing viewer-stage flake may keep the final HEAD red

- **Risk**: `BonesHostViewerLearningLoopStatus_GivenRunningLoop_ExpectedSidebarStageTracksStatusApi` failed at `ba5debf`; it may recur and make the final CI gate red for an unrelated reason.
- **Likelihood**: Medium
- **Impact**: High
- **Evidence**: OBS-033
- **Mitigation**: Re-read every job at the final HEAD; if the flake recurs, prove it with repeated local/CI evidence and harden the bounded wait without weakening the assertion (never waive it).
- **Gate**: G2

### RISK-005: Test counts change when the legacy workflow tests are reconciled

- **Risk**: WP6 deletes the legacy registration, so a registration test may legitimately change; a careless edit could lower the discovered total to hide a failure.
- **Likelihood**: Medium
- **Impact**: High
- **Evidence**: OBS-036
- **Mitigation**: Keep test intent, replace legacy assertions with definition-plan assertions; update `expectedTotals` only from real TRX counters and record the delta.
- **Gate**: G1

### RISK-006: A concurrent session is writing to the same workspace

- **Risk**: `docs/goal-tholdem/` appeared at 10:15 while this run was active, so another autonomous session may edit the same tree.
- **Likelihood**: Low
- **Impact**: Medium
- **Evidence**: `git status` shows untracked `docs/goal-tholdem/`; timestamps 10:15.
- **Mitigation**: Stage only explicit paths (never `git add -A`), re-read `git status` before every commit, and do not touch `docs/goal-tholdem/`.
- **Gate**: —

### OQ-004: Can the legacy `IWorkflow` registration be deleted without breaking a consumer?

- **Question**: Besides `BonesLearningWorkflowRegistrationTests`, does any host, E2E harness or shell path resolve `IWorkflow<BonesLearningWorkflowRequest, BonesLearningWorkflowResult>` by type?
- **Needed evidence**: grep for `BonesLearningWorkflow` and `IWorkflow<` consumers, plus the host integration tests that run the workflow.
- **Priority**: P1
- **Raised by**: round 0 (orchestrator)
- **Related**: OBS-032

### OQ-005: What is the exact old-vs-new parity surface for artifact payloads?

- **Question**: Which result/artifact fields are byte-stable across runs (artifact ids contain GUIDs), and which must be normalized before comparison?
- **Needed evidence**: inspect the artifact ids/paths produced by both paths in a fixture; define a normalizer.
- **Priority**: P1
- **Raised by**: round 0 (orchestrator)
- **Related**: HYP-011

### OQ-006: Does `AddDeepSeekChatModel` need a container-resolved overload for host DI?

- **Question**: The current extension requires an options instance; the Bones host builds its options inside `AddBonesDeepSeekModelProviders`. Should the extension gain a factory overload instead of duplicating registrations?
- **Needed evidence**: read both registration paths and the host options type; decide in WP4.
- **Priority**: P1
- **Raised by**: round 0 (orchestrator)
- **Related**: TODO-004

### OBS-037: WP1 adapter is delivered and its tests detect a swallowed failure

- **Source**: `WIP/Wip.Bones.Agents/Runtime/BonesAgentAdapter.cs:12-51`; `WIP/Wip.Bones.Agents/Runtime/BonesAgentExecutionException.cs:11-37`; `WIP/Wip.Bones.Tests/Runtime/BonesAgentAdapterTests.cs`; `.tmp/wp1-trx/wp1-focused.trx`; `.tmp/wp1-trx/wp1-control.trx`; `WIP/Wip.Bones.Tests/TestResults/_Ricardo-PC_2026-11-06_11_02_48.trx`
- **Fact**: The adapter implements `IAgent<,>`, builds the executor request once, awaits `IAgentExecutor.ExecuteAsync` once and translates every non-success `AgentExecutionResult` into `BonesAgentExecutionException : InvalidOperationException` with the status, steps and deterministic message; cancellation is not caught. Orchestrator reproduction in an isolated Linux clone: focused filter **14/14 passed**; with the control mutation `Succeeded || true` the same filter produced **6 failed / 8 passed** with `Assert.Throws() Failure: No exception was thrown ... Expected: BonesAgentExecutionException`. Full `Wip.Bones.Tests` after the change: **410 total, 409 passed, 1 failed**, the single failure being the pre-existing waived `BehaviorProofCompliance_GivenScriptStrategiesRequirements_RequiresExecutableRuntimeProofForEachItem`.
- **Implications**: D1 is met; WP1 can be committed. The pre-existing corpus is unchanged (396 + 14 new = 410) and no existing test was edited.
- **Confidence**: High
- **Agent**: orchestrator (reproducing the WP1 implementer)
- **Related**: EVD-096..EVD-099, VAL-019, DEC-019

### VAL-019: WP1 delivery validated — adapter behavior and failing control

- **Target**: HYP-012 and the WP1 delivery claims
- **Method**: read both production files and the test file; re-ran the focused filter in an isolated Linux clone; ran a clone-only mutation that swallows non-success statuses; restored the original file (SHA-256 `D1182E1485483B1665C804B062196080564C09F2C668A0E798D32E731D8FB9A3`); ran the full Bones suite; diffed the changed-path set.
- **Evidence examined**: `.tmp/wp1-trx/wp1-focused.trx` (14/14); `.tmp/wp1-trx/wp1-control.trx` (8 pass / 6 fail); `.tmp/wp1-control-clone.log`; full-suite TRX (410/409/1, single waived meta failure); `git status --porcelain`.
- **Result**: Supported
- **Conclusion**: The adapter delegates the loop, maps results and errors as specified, propagates cancellation, and the tests fail when the error translation is removed. No existing test or project file was modified by WP1.
- **Related**: OBS-037, HYP-012, EVD-097, EVD-098, EVD-099

### VAL-020: Independent falsification of the starting point agrees with VAL-018

- **Target**: The five user-supplied starting-point claims, independently checked by the round-1 Falsifier
- **Method**: read `docs/execution/harness/round-1/falsify-starting-point.md`; spot-checked its most surprising citations myself: the absence of any DeepSeek reference in `Wip.Bones.Agents` (EVD-089), the real HEAD vs `dc6b76c` (EVD-086), the 4-stage plan/registration (EVD-088), the zero-hit seam search (EVD-090) and the project edges (EVD-091).
- **Evidence examined**: falsifier artifact (5 rows: 3 Supported, 2 Falsified, 0 NotCheckable) plus my own EVD rows.
- **Result**: Supported for claims 1, 3, 4; Falsified for claim 2 ("calls DeepSeek directly" — the agents use `IModelProvider` and the host wires DeepSeek) and claim 5 (the current HEAD is `ba5debf`, not `dc6b76c`; the recorded green runs belong to `dc6b76c` and the current HEAD's offline gate is red on the pre-existing viewer flake). The falsifier's "notCheckable" note was only the offline CI API re-query, which I had already performed online (EVD-086/EVD-087).
- **Conclusion**: No disagreement between the two independent checks; the two falsified clauses are corrections, not delivery failures. No WP1 claim is falsified.
- **Related**: VAL-018, OBS-033, EVD-086, EVD-089, EVD-100

### DEC-019: Accept WP1 and keep the adapter minimal

- **Decision**: Accept WP1 as delivered (two production types plus 14 focused tests). Do not add streaming or a domain-agent shim to the adapter in WP1; the Ponder/Enhance ports in WP2/WP3 will compose the adapter and will decide streaming against the runtime's `IStreamingAgentExecutor` where a Bones operator surface actually consumes deltas.
- **Rationale**: VAL-019 (behavior and control verified) and VAL-020 (starting point corrected). A minimal adapter keeps the domain-port diff reviewable; streaming is only valuable where the operator sees it, and the Bones workflow currently produces artifacts rather than a live token surface.
- **Alternatives considered**: adding streaming to the adapter now (deferred: no consumer in WP1, dead surface); making the four agents directly `BonesAgentAdapter` registrations (rejected in DEC-018).
- **Related**: OBS-037, VAL-019, TODO-002, TODO-003

### OBS-038: WP2 ports Ponder onto the runtime with parity, controls and a real memory fix

- **Source**: `WIP/Wip.Bones.Agents/Ponder/BonesPonderAgent.cs`; `Ponder/BonesPonderPrompts.cs`; `Runtime/BonesAgentTools.cs`; `Runtime/BonesAgentLoopOptions.cs`; `WIP/Wip.Bones.Tests/Runtime/BonesPonderRuntimeTests.cs`; `WIP/Wip.Runtime/Runtime/ArtifactAgentMemory.cs`; `WIP/Wip.Runtime.Tests/Memory/ArtifactAgentMemoryTests.cs`; `.tmp/wp2-trx/*.trx`; `docs/execution/harness/round-2/`
- **Fact**: The Ponder agent now has a production constructor over `IAgentExecutor` (a `BonesAgentAdapter` per model turn) and keeps the 4-parameter legacy constructor byte-compatible. Prompts stay in `BonesPonderPromptBuilder` (single source), `BonesPonderPrompts` maps them to `AgentExecutionRequest`s, the compile capability is a `ToolDefinition` (`tool.bones.strategy-compile`) invoked through `RuntimeToolInvoker`, budgets come from `BonesAgentLoopOptions`, and `IAgentMemory` is loaded per execution and appended on success. Focused tests **13/13**; full Bones **423/420/3** (only the three pre-existing waived meta-tests fail); full Runtime **147/141/6** (5 Windows-only `dotnet.cmd` + 1 waiver; the new memory test passes). The memory defect (`ArtifactAgentMemory` could not reload a local-store-normalized `agent-memory-{id}.json`) was reproduced and fixed.
- **Implications**: D2 is met and the production memory path is no longer hollow. WP3 can reuse the tool/options/prompt-mapping pattern.
- **Confidence**: High
- **Agent**: orchestrator (reproducing the WP2 implementer)
- **Related**: EVD-101..EVD-107, VAL-021, VAL-022, DEC-020

### VAL-021: WP2 delivery validated — parity, controls, and unchanged suites

- **Target**: HYP-011, the WP2 delivery claims, and the memory-fix claim
- **Method**: overlaid the accepted WP2 files onto the isolated clone; ran the focused filter; ran two clone-only mutations of the runtime prompt/tools and restored them; ran the full Bones and Runtime suites; parsed every TRX counter and failure name; compared against the round-1 baseline.
- **Evidence examined**: `.tmp/wp2-trx/wp2-focused.trx` (13/13); `.tmp/wp2-trx/wp2-control-A.trx` and `-B.trx` (1 failed each, exit 1); `.tmp/wp2-trx/wp2-final-bones.trx` (423/420/3, all three waived); `.tmp/wp2-trx/wp2-final-runtime.trx` (147/141/6, the documented set); source and tests read.
- **Result**: Supported
- **Conclusion**: The Ponder paths produce identical domain outputs for the same inputs, the parity test fails when only the runtime prompt changes or the tool definitions are dropped, the negative controls behave deterministically, and the existing suites are unchanged apart from the intended additions.
- **Related**: OBS-038, HYP-011, EVD-102..EVD-107

### VAL-022: The pre-existing memory-load defect is falsified and fixed

- **Target**: "memory via `ArtifactAgentMemory` works with the host's artifact store"
- **Method**: read `ArtifactAgentMemory.LoadAsync` and `WipArtifactStoreLocal.NormalizeFileName`; reproduced the mismatch through the implementer's observed failure (first request had 2 messages instead of 3 with a hand-written exact-name store); applied the two-name discovery fix; re-ran the memory regression test and the Bones memory test against the real `WipArtifactStoreLocal`.
- **Evidence examined**: `ArtifactAgentMemory.cs:104-115` (`IsMemoryArtifact`); `ArtifactAgentMemoryTests.cs` (7/7, new local-name test); `BonesPonderRuntimeTests.cs:381-438` (real local store, 1/1); full Runtime 147/141/6.
- **Result**: Supported (defect real, fix verified)
- **Conclusion**: The memory requirement is real in production, not hidden by a test store; redaction behaviour is unchanged.
- **Related**: OBS-038, EVD-106

### DEC-020: Accept WP2, keep the waiver floor, and constrain WP4's registration order

- **Decision**: Accept WP2 including the shared memory fix. Keep `docs/plan/test-waivers.json` at its declared Bones floor (396) for now and record the real count (423) in the evidence; do not stage the concurrently edited waiver file. Carry the DI constraint into WP4: the host must retire the `IModelProvider` registrations when it adds `IAgentExecutor` (executor-only resolution is the supported production end state).
- **Rationale**: VAL-021/VAL-022; the implementer observed an ambiguous-constructor failure when both registrations coexist, and the waiver file currently contains a concurrent session's unrelated uncommitted hunk that must not be committed under this work package.
- **Alternatives considered**: raising the waiver floor to 423 now (rejected: would require committing the concurrent waiver hunk); making the executor parameter optional to force ctor selection (rejected: would hide the provider-only path and weaken the DI contract); keeping the memory workaround test store (rejected: hollow production memory).
- **Related**: OBS-038, RISK-007, TODO-007

### RISK-007: The two-registration transition can fail DI resolution

- **Risk**: A host that registers both `IModelProvider<BonesStrategyAuthoringRequest,...>` (or the enhancement equivalent) and `IAgentExecutor` makes `BonesPonderAgent`/`BonesEnhanceStrategyAgent` constructors ambiguous; resolution throws at startup. WP4 must remove the provider registrations in the same change that adds the executor.
- **Likelihood**: High during WP4, zero after
- **Impact**: High (host startup failure)
- **Evidence**: OBS-038; the implementer's observed `Unable to activate type ... constructors are ambiguous` message in `docs/execution/harness/round-2/implementation-wp2.md` §8
- **Mitigation**: WP4 registers the executor only after deleting the DeepSeek/stub authoring & enhancement `IModelProvider` registrations; the E2E host must be updated in the same work package; add a host-composition test that resolves both agents.
- **Gate**: G4

### TODO-007: Raise the Bones waiver floor to the real count when the waiver file is clean

- **Task**: Once the concurrent Tholdem waiver hunk is committed (or abandoned), update `docs/plan/test-waivers.json` `Wip.Bones.Tests` to 423 from `.tmp/wp2-trx/wp2-final-bones.trx`, and `Wip.Runtime.Tests` to 147 from `.tmp/wp2-trx/wp2-final-runtime.trx`, in a dedicated commit.
- **Priority**: P2
- **Depends on**: concurrent session's waiver edit resolution
- **Status**: Open
- **Related**: DEC-020, EVD-104, EVD-105

### OBS-039: WP3 ports Enhance onto the runtime with parity, controls and the co-learning path distinct

- **Source**: `WIP/Wip.Bones.Agents/Enhance/BonesEnhanceStrategyAgent.cs`; `Enhance/BonesEnhancePrompts.cs`; `WIP/Wip.Bones.Tests/Runtime/BonesEnhanceRuntimeTests.cs`; `.tmp/wp3-trx/*.trx`; `docs/execution/harness/round-3/`
- **Fact**: The Enhance agent now has a 15-parameter production constructor over `IAgentExecutor` (turn-level `BonesAgentAdapter`) and keeps the 13-parameter legacy constructor source-compatible. `BonesEnhancePrompts` maps the existing `BonesEnhancePromptBuilder` messages to `AgentExecutionRequest`s (memory after the system prompt), the compile capability is the shared `ToolDefinition` through `RuntimeToolInvoker`, budgets come from `BonesAgentLoopOptions`, and memory is appended once on success. Focused tests **10/10**, all Enhance-related tests **49/49**; full Bones **433/430/3** (only the three pre-existing waived meta-tests fail). Both parity controls fail the parity test as required. The unchanged co-learning wrapper runs the new path for four seats and returns the last completed seat.
- **Implications**: D3 is met. The WP4 provider swap can now target both Ponder and Enhance with the same agent-loop composition.
- **Confidence**: High
- **Agent**: orchestrator (reproducing the WP3 implementer)
- **Related**: EVD-109..EVD-112, VAL-023, DEC-021

### VAL-023: WP3 delivery validated — parity, controls and full-suite non-regression

- **Target**: The WP3 delivery claims, HYP-011's Enhance clause, and the co-learning distinction
- **Method**: overlaid the accepted WP3 files onto the isolated clone at `3e0087a`; ran the focused filter; ran the clone-only prompt/tool mutations and restored them; ran the full Bones suite; parsed the TRX counters and failed-test names; checked the changed-path set.
- **Evidence examined**: `.tmp/wp3-trx/wp3-focused.trx` (10/10); `.tmp/wp3-trx/wp3-control-A.trx`/`-B.trx` (1 failed each); `.tmp/wp3-trx/wp3-full.trx` (433/430/3, all three waived); source and test diff.
- **Result**: Supported
- **Conclusion**: The Enhance paths produce identical domain/artifact outputs for the same inputs; the parity test is sensitive to a runtime-only prompt change and to dropped tool definitions; no existing test or production path outside the agent changed.
- **Related**: OBS-039, HYP-011, EVD-109..EVD-112

### DEC-021: Accept WP3 and move to the provider swap

- **Decision**: Accept WP3 as delivered (new prompts mapper, dual-constructor agent, 10 focused tests) and proceed to WP4. The host composition in WP4 will use explicit factories over `IChatModel` + `IAgentExecutor` + `RuntimeToolInvoker` + `ArtifactAgentMemory` rather than registering `IAgentExecutor` globally, so the Bones tool set cannot leak into the generic shell agent path.
- **Rationale**: VAL-023; the DI ambiguity risk (RISK-007) disappears when the host composes the agents explicitly; the generic runtime's own executor fallback stays untouched.
- **Alternatives considered**: registering `IToolInvoker`/`IAgentExecutor` as global host services (rejected: the shell `agent` path would inherit the Bones compile-only tool set); solving the constructor ambiguity in the agent types (rejected: WP4 removes the provider registrations instead).
- **Related**: OBS-039, RISK-007, TODO-004

### OBS-040: WP4 retires the DeepSeek stack from production and composes both agents over the chat model

- **Source**: `WIP/Wip.Bones.Host/BonesHostServiceCollectionExtensions.cs`; `Wip.Bones.Host.csproj`; `WIP/Wip.Bones.Agents/Configuration/`; `WIP/Wip.Bones.Agents/Runtime/BonesStageChatModel.cs`, `BonesChatModelPlayTurnProvider.cs`; `WIP/Wip.Runtime/DeepSeekChatModelServiceCollectionExtensions.cs`; `tests/Wip.Architecture.Tests/`; `.tmp/wp4-trx2/*.trx`; `.tmp/wp4-trx/*`; `docs/execution/harness/round-4/`
- **Fact**: The host no longer references `Wip.Bones.ModelProviders.DeepSeek` and a production grep over Host+Agents returns zero DeepSeek `.cs` hits (only the provider's own test `InternalsVisibleTo` remains). `AddDeepSeekChatModel` registers the concrete `DeepSeekChatModel` and `IChatModel`; the host replaces `IChatModel` with `BonesStageChatModel` and composes both domain agents through explicit factories (`AgentExecutor` + `RuntimeToolInvoker` + `ArtifactAgentMemory`), with a stub chat model for the stub provider and a chat-model play-turn provider. Orchestrator reproduction: architecture 4/4 on the 43-project graph and the injected real edge fails with `bones-agents-no-legacy-provider`; Bones 446/443/3 (waived meta-tests), Runtime 150/144/6 (documented), provider 48/47/1 (waived), host composition 1/1, host loop classes 13/9/4 (all four failures already waived). The old `thinking` parameter is not carried by the new chat-model contract (default `disabled` unchanged).
- **Implications**: D4 is met; the production stack is on `IChatModel -> IAgentExecutor -> RuntimeToolInvoker -> ArtifactAgentMemory`, and the provider project is test-only.
- **Confidence**: High
- **Agent**: orchestrator (reproducing the WP4 implementer)
- **Related**: EVD-113..EVD-120, VAL-024, DEC-022, RISK-008

### VAL-024: WP4 delivery validated — retirement, rule proof and suite non-regression

- **Target**: The WP4 delivery claims and the production-retirement assertion
- **Method**: read the host/provider/runtime diffs; ran the production grep; ran the architecture suite in a clean 43-project clone and injected a real Agents→provider reference as the control; ran the full Bones, Runtime and provider suites and the host composition + the two modified host loop classes; parsed every TRX counter and failure name; checked that the moved types and parser are used by both the Agents and provider assemblies.
- **Evidence examined**: `.tmp/wp4-trx2/bones.trx` (446/443/3, all waived), `.tmp/wp4-trx2/runtime.trx` (150/144/6, documented), `.tmp/wp4-trx/provider.trx` (48/47/1 waived), `.tmp/wp4-trx/host-composition.trx` (1/1), `.tmp/wp4-trx/wp4-host-loops.trx` (13/9/4 all waived), `.tmp/wp4-architecture.log` (positive 4/4, control 1 failed with the exact rule message), and the host csproj/grep.
- **Result**: Supported
- **Conclusion**: Production no longer uses the private provider assembly; the architecture rule is real and detects a genuine edge; the suites show no unwaived regression. The `thinking` parameter is the one recorded behavior delta.
- **Related**: OBS-040, EVD-114..EVD-120

### DEC-022: Accept WP4 with explicit host factories, and stage the architecture files cleanly

- **Decision**: Accept WP4 as delivered. The host keeps explicit agent factories instead of a global executor; the DeepSeek config types live in `Wip.Bones.Agents.Configuration`; the provider project stays for its own tests only. When committing, stage the architecture rule/test files reconstructed against the committed HEAD (without the concurrent session's Tholdem rules and 47-project count), because those uncommitted changes must not enter this work package. Record the lost `thinking` pass-through as a limitation rather than claiming behavior parity.
- **Rationale**: OBS-040/VAL-024; the rule control proves the guard works; committing the concurrent architecture hunks would reference uncommitted Tholdem projects and break CI.
- **Alternatives considered**: registering global `IAgentExecutor`/`IToolInvoker` (rejected in DEC-021); keeping the provider project referenced by the host for options only (rejected: that is still a production dependency); adding a thinking field to the generic chat contract (out of scope; recorded as RISK-008).
- **Related**: OBS-040, RISK-008, TODO-004

### RISK-008: The DeepSeek `thinking` setting is no longer applied

- **Risk**: The retired per-stage DeepSeek providers sent a `thinking` parameter; `DeepSeekChatModel`'s `ChatRequest` has no thinking field, so a host configured with `Thinking=enabled/default` now gets the API's default reasoning behavior instead of the configured one. The default `disabled` matches the old default.
- **Likelihood**: Certain for non-default configurations
- **Impact**: Low/Medium (model behavior, not data or safety)
- **Evidence**: OBS-040; `DeepSeekChatModel.cs` has no `thinking` mapping; `BonesHostOptions` still parses/diagnoses it
- **Mitigation**: documented in the WP4 artifact and this entry; extend the `IChatModel`/`ChatRequest` contract with an optional thinking hint if the setting must be honored.
- **Gate**: —

### TODO-008: Refresh the waiver floors from the final counts

- **Task**: When `docs/plan/test-waivers.json` no longer carries the concurrent session's uncommitted Tholdem hunk, update `Wip.Bones.Tests` to the observed final count (446 at WP4; may change at WP6) and `Wip.Runtime.Tests` to 150, using the final TRX counters, in a dedicated commit.
- **Priority**: P2
- **Depends on**: concurrent session's waiver edit resolution; WP6 final counts
- **Status**: Open
- **Related**: DEC-022, EVD-116, EVD-117

### OBS-041: WP5 proves the runtime permission/safety posture and closes the tool-artifact redaction gap

- **Source**: `WIP/Wip.Bones.Tests/Runtime/BonesAgentRuntimeSafetyTests.cs`; `WIP/Wip.Runtime/Runtime/RuntimeToolInvoker.cs:203-217`; `WIP/Wip.Runtime.Tests/Runtime/RuntimeToolInvokerTests.cs`; `.tmp/wp5-trx/*`; `.tmp/wp5-trx2/*`; `docs/execution/harness/round-5/`
- **Fact**: Nine safety tests prove: a plugin-origin compile tool without a grant returns the exact `permission-denied:` error before the tool body runs; repeated denials stop at `ToolFailureLimitReached`; Audit mode allows the call and records an `Allowed=true, Audited=true` decision; the tool receives the agent's `CapabilityContext` (session + worktree); session A's memory never enters session B's prompt/artifacts; prompt-injection text cannot register or execute a non-advertised tool; Ponder and Enhance stop at `MaxStepsReached`; and memory redaction keeps `[REDACTED]` with no raw secret in prompts/artifacts. The implementer's own residual-gap review found tool-call artifacts persisted raw arguments/results; `RuntimeToolInvoker` now redacts both through `ISecretRedactor` (DI or default) while the model-visible observation stays raw; a new red→green `[Theory]` covers it. Counts: safety 9/9, invoker 10/10, full Bones 455/454/1 (one waived meta-test), full Runtime 152/146/6 (the documented set).
- **Implications**: D5 is met. The remaining design boundary is that the host's compile tool is a first-party built-in capability (trusted by design); plugin-origin tools are the denied/audited surface.
- **Confidence**: High
- **Agent**: orchestrator (reproducing the WP5 implementer)
- **Related**: EVD-121..EVD-125, VAL-025, DEC-023, RISK-009

### VAL-025: WP5 delivery validated — denial, Audit, context, isolation, loops and redaction

- **Target**: The WP5 safety claims and the tool-artifact redaction fix
- **Method**: read the safety tests and the invoker diff; ran the 9-test safety filter in the isolated clone; ran the invoker class (including the new theory) in the clone; ran the full Bones and Runtime suites in the main tree; parsed the TRX counters and failure names; checked the fix's red→green evidence in the implementer's artifact.
- **Evidence examined**: `.tmp/wp5-trx/wp5-focused.trx` (9/9); `.tmp/wp5-trx/wp5-invoker.trx` (10/10); `.tmp/wp5-trx2/bones.trx` (455/454/1, waived); `.tmp/wp5-trx2/runtime2.trx` (152/146/6, documented); the invoker diff.
- **Result**: Supported
- **Conclusion**: Every requested safety property is exercised with concrete assertions, the denied path is deterministic and precedes execution, and the artifact-redaction gap is fixed and covered. No unwaived regression.
- **Related**: OBS-041, EVD-122..EVD-125

### DEC-023: Accept WP5; the built-in compile tool is a design boundary, not a bypass

- **Decision**: Accept WP5. The production host keeps the compile capability built-in (trusted); the deny/audit behaviour is proven with a plugin-origin descriptor, which is the registration shape a third-party model tool would use. Record the per-executor step budget and the in-memory audit sink as documented limits.
- **Rationale**: OBS-041/VAL-025; `PermissionGuard` is the single evaluation point and runs before every tool body, so the mechanism is real even though first-party capabilities are trusted by policy.
- **Alternatives considered**: declaring a permission on the built-in compile tool (rejected: built-ins are trusted by the evaluator, so it would be a no-op and misleading); disabling artifact recording for Bones (rejected: the redaction fix preserves observability and protects the shell path too).
- **Related**: OBS-041, RISK-009

### RISK-009: First-party tools bypass grants by design

- **Risk**: A built-in capability (including the Bones compile tool) is always allowed by `PermissionEvaluator`; only plugin-origin capabilities require grants. A future model-facing first-party tool that accesses sensitive data would not be gated unless registered as a plugin or the evaluator policy changes.
- **Likelihood**: Low today (the only model tool is the deterministic compile capability)
- **Impact**: Medium if a sensitive first-party tool is added
- **Evidence**: OBS-041; `PermissionEvaluator.Evaluate` returns `built-in capability` for built-ins
- **Mitigation**: keep the model-facing tool set minimal and deterministic; register any sensitive tool with a plugin origin + required permission; add a review rule when expanding `BonesAgentToolDescriptors`.
- **Gate**: —

### OBS-042: WP6 retires the legacy Bones workflow shim and leaves the definition plan as the only path

- **Source**: `WIP/Wip.Builder/WipBuilder.cs:245-275`; `WIP/Wip.Bones.Agents/Workflow/BonesLearningWorkflow.cs:11`; `BonesLearningWorkflowRegistration.cs:18-22`; `WIP/Wip.Bones.Tests/Workflow/BonesLearningWorkflowRegistrationTests.cs:340-343`; `.tmp/wp6-trx/*`; `.tmp/wp6-ctrl/viewer-pre.trx`; `docs/execution/harness/round-6/`
- **Fact**: `WipBuilder.AddDeclarativeWorkflow` registers the definition singleton (same instance behind `IWorkflowDefinition`) plus one selection registration whose run-request factory throws because the definition plan supplies the initial request. `BonesLearningWorkflow` now implements only `IWorkflowDefinition`; the stub `ExecuteAsync` and `workflow-dispatch` ids are deleted; the Bones registration and its one test helper use the new method; two builder tests and one runtime declarative-only end-to-end test were added. The grep for `IWorkflow<BonesLearningWorkflow|AddWorkflow<BonesLearningWorkflow|AddWorkflowDefinition<BonesLearningWorkflow|workflow-dispatch` returns 0 hits. Reproduction: Bones 455/454/1 (the failure is a waived meta-test; the implementer's run had 455/455), Runtime 153/147/6 (documented), Shell 34/34, Builder 34/34, architecture 4/4 in a clean 43-project clone.
- **Implications**: D6 is met; the declarative plan is the only Bones execution path. The `Wip.Bones.Web.Tests` viewer failure is pre-existing (it fails identically on the pre-WP6 clone at `7158280`) and outside the 16-project offline gate.
- **Confidence**: High
- **Agent**: orchestrator (reproducing the WP6 implementer)
- **Related**: EVD-126..EVD-130, VAL-026, DEC-024

### VAL-026: WP6 delivery validated — shim absent, declarative-only execution, suites non-regressed

- **Target**: The WP6 removal claims and the declarative-only selection/execution claim
- **Method**: read the builder/workflow/registration diffs; ran the legacy grep; ran the full Bones, Runtime, Shell and Builder suites; ran the architecture suite in a clean 43-project clone; ran the viewer workflow test on the pre-WP6 clone as the pre-existing-failure control.
- **Evidence examined**: `.tmp/wp6-trx/bones.trx` (455/454/1, waived), `runtime.trx` (153/147/6, documented), `shell.trx` (34/34), `builder.trx` (34/34); `.tmp/wp6-ctrl/viewer-pre.trx` (1 failed pre-WP6); the grep (0 hits); the clone architecture run (4/4).
- **Result**: Supported
- **Conclusion**: The legacy implementation, registration and stub dispatch are gone; the new registration keeps selection working while the definition plan supplies all stages and the initial request; no unwaived regression in the gate projects.
- **Related**: OBS-042, EVD-127..EVD-130

### DEC-024: Accept WP6 and treat the viewer workflow failure as pre-existing

- **Decision**: Accept WP6. Record the `BonesViewerWorkflowTests` failure as pre-existing (it reproduces on the pre-WP6 clone) and outside the offline gate, not as a WP6 regression. Keep the runtime `CreateRunRequest` throwing for declarative registrations so a wrong fallback fails loudly instead of silently constructing a default request.
- **Rationale**: VAL-026 and the pre-WP6 control; the runtime never calls `CreateRunRequest` when a definition plan exists (only `LinearWorkflowPlanFactory` does).
- **Alternatives considered**: keeping the legacy `IWorkflow` registration (rejected: the user's WP6 explicitly retires it); making the declarative registration reuse the default request factory (rejected: it would hide a misuse).
- **Related**: OBS-042, EVD-130

### OBS-043: The final HEAD is green in both CI workflows

- **Source**: GitHub Actions API: Tests CI run `42289775138` (six jobs) and Modus CI run `42289774965`, head `724d3a623fab91aae09095f31f8401fe9c97ec7b`; `.tmp/wp6-trx/*`; `docs/execution/harness/round-6/`
- **Fact**: Tests CI run `42289775138` succeeded in all six jobs (wip-build windows/ubuntu, wip-architecture, wip-offline-tests, wip-security windows/ubuntu) and Modus CI run `42289774965` succeeded at the final code+docs HEAD `724d3a6`. The offline gate accepted the real counts (Bones 455, Runtime 153, Shell 34, Builder 34, provider 48, plus the other gate projects) with the pre-existing waivers.
- **Implications**: D7 and D8 are met; the migration is verified on the authoritative windows-latest gate, not only locally. No release/publish/sign/deploy was performed.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-131, VAL-027, DEC-025

### VAL-027: Final gate — D1–D8 met, both CI workflows green

- **Target**: The goal's definition of done (D1–D8)
- **Method**: map each D item to its evidence; reproduce the local suites; read the final CI run and every job conclusion; confirm no production reference to the provider; confirm the legacy grep is empty; confirm no release action was taken.
- **Evidence examined**: D1 EVD-097..099/VAL-019; D2 EVD-102..107/VAL-021/022; D3 EVD-109..112/VAL-023; D4 EVD-114..120/VAL-024; D5 EVD-122..125/VAL-025; D6 EVD-127..130/VAL-026; D7/D8 EVD-131 and the final CI job list.
- **Result**: Supported
- **Conclusion**: All eight dimensions are met. Residual risks are recorded (RISK-008 thinking parameter, RISK-009 built-in-tool trust, the pre-existing viewer flake, the concurrent-session waiver hunk); none blocks the migration.
- **Related**: OBS-043, EVD-131

### DEC-025: Close the goal without touching the waiver floors or making a release claim

- **Decision**: Close the migration goal. Leave `docs/plan/test-waivers.json` `expectedTotals` at their declared floors (Bones 396, Runtime 146, Shell 34) and record the real discovered counts (Bones 455, Runtime 153, Shell 34, Builder 34) in this evidence table; the floors are regression guards and the concurrent session still owns an uncommitted Tholdem hunk in that file. Make no release/publish/sign/deploy claim.
- **Rationale**: OBS-043/VAL-027; raising the floors would require committing the concurrent hunk (which would demand a Tholdem TRX that does not exist in this tree) and is not needed for a green gate; recording the real counts keeps the evidence honest.
- **Alternatives considered**: raising the floors to 455/153 (deferred to TODO-008 once the concurrent edit is resolved); adding the concurrent Tholdem hunk (rejected: it would break the gate).
- **Related**: OBS-043, TODO-008, EVD-131

### OBS-044: The live model comparison found a real 400 on the new path (dotted tool names)

- **Source**: `docs/execution/harness/round-7/live-comparison.md`; `.tmp/live-compare/live.log` (pre-fix), `live3.log` (post-fix), `.tmp/live-compare-out/run-bf0eaff6dce54c34ae542304ca70b169/transcript.json`; `WIP/Wip.Runtime/Runtime/DeepSeekChatModel.cs`; `.tmp/live-compare/adapter-tests.log`; `.tmp/live-compare/live-tests3.log`
- **Fact**: A live comparison against `https://api.deepseek.com` (`deepseek-flash`, same prompt/model, 2 rounds per path) showed the **old** provider path completing and the **new** runtime path failing with `HTTP 400`: `Invalid 'tools[0].function.name': string does not match pattern. Expected a string that matches the pattern '^[a-zA-Z0-9_-]+$'`. Both requests had identical prompts and model ids; the only difference was the advertised compile tool named `tool.bones.strategy-compile`. Root cause: WIP capability ids are dotted (also `tool.repo.*` in the shell path), while the provider restricts function names. Provider-specific name mangling now lives in `DeepSeekChatModel`: outgoing tool/assistant tool-call names are sanitized deterministically (invalid chars → `_`, collision suffix, 64-char cap) and incoming tool-call names are restored to the canonical dotted id. After the fix the live comparison re-ran: **both paths complete, both generated scripts compile**, first-call prompts are byte-identical, scripts differ as expected (stochastic). Cost delta: new path 2–3 calls / 11.7k–13.5k tokens / 63–68 s vs old 1 call / 1.7k–1.9k tokens / 5–20 s, because the live model uses the compile tool.
- **Implications**: The migration was functionally broken against the real provider despite a green offline gate; the live comparison is the evidence that caught it. The fix is in the provider adapter, so it also repairs the shell's dotted `tool.repo.*` live path.
- **Confidence**: High
- **Agent**: orchestrator (live run) + implementer fix
- **Related**: EVD-133..EVD-137, VAL-028, DEC-026, RISK-010

### VAL-028: Live comparison validated — pre-fix failure, post-fix parity and cost delta

- **Target**: The live-behavior claim of the migration, and the tool-name fix
- **Method**: read the local DeepSeek credential from the machine credential store (never printed or persisted), ran a purpose-built .NET harness that drives both production paths against the real API with identical prompts, captured request/response bodies, usage and latency, and compiled each returned script; then re-ran after the adapter fix; ran the offline adapter unit tests and the opt-in live tests; re-ran the full Runtime and Bones suites offline.
- **Evidence examined**: pre-fix `live.log` (new path 400, old OK); post-fix `live3.log` + transcript (both OK, calls/tokens/latency/script lengths, prompt equality); `adapter-tests.log` (8/8, including the new collision test); `live-tests3.log` (2/2 with the real key); `.tmp/live-fix-trx/runtime.trx` (155/149/6 documented) and `bones.trx` (455/454/1 waived).
- **Result**: Supported
- **Conclusion**: The new path is live-compatible after the fix; the comparison also exposed a real cost trade-off (the model uses the compile tool, ~6–7× tokens) that the offline parity fixtures could not show. No release claim.
- **Related**: OBS-044, EVD-133..EVD-137

### DEC-026: Fix dotted tool names in the provider adapter, not the domain

- **Decision**: Keep WIP capability ids dotted (the stable domain/runtime identifiers) and map them to provider-safe wire names inside `DeepSeekChatModel`, restoring the canonical names on incoming tool calls. Add opt-in live tests (env-gated) for the basic answer and for a dotted tool name; correct the stale live-test model id to `deepseek-flash` and raise its token budget to 256 because `deepseek-flash` reserves completion budget for reasoning.
- **Rationale**: The provider grammar is vendor-specific; changing the capability catalog to satisfy it would leak the vendor into the domain and would still break other names (`tool.repo.read_file`). The adapter is the correct boundary; it repairs the shell path too.
- **Alternatives considered**: renaming only the Bones compile tool (rejected: does not fix the shell's dotted repo tools); adding a tool-name alias in the invoker (rejected: the invoker should not know provider grammars); making the live test part of the offline gate (rejected: no credentials/network in CI).
- **Related**: OBS-044, VAL-028, RISK-010

### RISK-010: The new path's live cost/latency is materially higher than the old one

- **Risk**: With the compile tool advertised, the live `deepseek-flash` model calls it 1–2 times before the final answer, round-tripping the full script through the prompt. In the two live rounds the new path used 11.7k–13.5k tokens and 63–68 s vs the old path's 1.7k–1.9k tokens and 5–20 s.
- **Likelihood**: High (observed in both rounds)
- **Impact**: Medium (cost/throughput of the learning loop; no correctness impact)
- **Evidence**: OBS-044, `live3.log`
- **Mitigation**: document the trade-off; if cost matters, narrow the compile-tool description/usage policy or make tool advertisement configurable per stage; re-measure with the production learning loop before scaling.
- **Gate**: —