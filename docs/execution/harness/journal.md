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