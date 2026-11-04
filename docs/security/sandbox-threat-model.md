# Sandbox threat model (WP-1.1 draft)

**Status:** DRAFT — security review required before G2. Not approved.
**Date:** 2026-11-02
**Owner:** platform/security engineer + tech lead
**Related:** ADR-IMPL-02, WIP-IMPL-001 WP-1.1..1.6

## 1. Scope

This model covers model-authored strategy code executed by Bones: C# source received from a model/provider, compiled with Roslyn, loaded, and invoked through the player-slot contract. It also covers the worker process that will own compilation/execution and the host/worker protocol.

It does not cover a malicious operator with local administrator rights, a compromised .NET runtime, or supply-chain compromise of NuGet packages. Those remain host/OS responsibilities.

## 2. Assets

| Asset | Why it matters |
|---|---|
| Host process memory and loaded assemblies | Must never load model-authored code; prevents arbitrary code in the trusted process |
| Repository/workspace files | Model code must not read or write them except through approved tools |
| User credentials and environment secrets | Model provider keys, tokens, SSH keys, machine secrets |
| Network | Model code must not exfiltrate data or reach internal services by default |
| Artifacts, transcripts, sessions | Integrity and confidentiality of run evidence |
| Plugin assemblies and contracts | Plugins are separate untrusted inputs; worker must not reach them |
| CPU, memory, process table, disk/temp | Resource exhaustion must be bounded |
| Worker protocol | Version, message size, correlation, and error semantics must be deterministic |

## 3. Trust boundaries

1. **Host ↔ model/provider**: model output is untrusted input.
2. **Host ↔ worker**: the worker is a separate OS process; the protocol is the only channel. The worker must not receive host DI, host objects, or inherited secrets.
3. **Worker ↔ OS**: Windows uses Job Object/timeouts; Linux uses unshare user/net/pid/mount namespaces + chroot where unprivileged user namespaces are allowed. Windows AppContainer is not implemented, so Windows remains resource-capped only.
4. **Host ↔ plugin**: plugins are untrusted and isolated by an AssemblyLoadContext; the worker cannot load host/plugin assemblies.
5. **Worker ↔ filesystem/network**: denied by default; only explicit, narrow paths/handles if a future approval allows them.

## 4. Guarantees the design targets

- Model-authored assembly is loaded **only** in the worker.
- Worker starts without host DI, host object graphs, or host assembly resolution.
- Worker environment contains no inherited secrets; a minimal environment is constructed explicitly.
- Worker has a killable lifetime: startup/handshake/compile/move timeouts and process-tree kill.
- Memory, CPU, and process count are capped; stdout/stderr are bounded.
- No network access by default where the OS permits; otherwise the residual risk is stated.
- Temp directories are per-worker and removed on stop/crash.
- Protocol messages are versioned, bounded, correlated, and reject malformed input deterministically.
- Worker crash/restart does not take down the host; the current move fails closed.

## 5. Abuse cases and test mapping

| ID | Abuse case | Expected result | Planned test (WP-1.5) |
|---|---|---|---|
| TM-01 | Read an arbitrary host file | Denied/fails; no host file content returned | FileRead_GivenHostPath_ExpectedDenied |
| TM-02 | Write/delete a host file | Denied/fails; host file unchanged | FileWrite_GivenHostPath_ExpectedDenied |
| TM-03 | Open a network connection | Denied/fails closed | Network_GivenExternalEndpoint_ExpectedDenied |
| TM-04 | Process.Start / shell escape | Denied/fails; no child process | ProcessStart_GivenCommand_ExpectedDenied |
| TM-05 | Reflect into blocked/host assemblies | Blocked; no host type execution | Reflection_GivenHostAssembly_ExpectedBlocked |
| TM-06 | Infinite loop | Killed within 2x move timeout | InfiniteLoop_GivenMove_ExpectedKilledWithinTwiceTimeout |
| TM-07 | Memory bomb | Capped; worker killed/restarted | MemoryBomb_GivenAllocation_ExpectedCapped |
| TM-08 | Huge stdout/stderr | Bounded; no host memory growth | OutputFlood_GivenHugeOutput_ExpectedBounded |
| TM-09 | Worker crash | Host survives; move fails deterministically | WorkerCrash_GivenKill_ExpectedHostSurvivesAndMoveFails |
| TM-10 | Malformed protocol frame | Deterministic protocol error; no crash | MalformedFrame_GivenInvalidJson_ExpectedProtocolError |
| TM-11 | Stale/replayed correlation ID | Rejected or ignored deterministically | Replay_GivenOldCorrelationId_ExpectedRejected |
| TM-12 | Temp data survives | Temp removed after stop/crash | TempCleanup_GivenCrash_ExpectedNoResidue |
| TM-13 | P/Invoke/native call | **Open gap** — currently not blocked by reference allowlist; G2 must restrict or accept | PInvoke_GivenNativeCall_ExpectedDeniedOrAcceptedRisk (WP-1.5) |

These names are proposed; WP-1.5 must implement them on Windows and Linux. TM-13 is explicitly not claimed as contained in v1.0.

## 6. Residual risk

A local developer tool cannot provide a perfect sandbox. The following remain explicit risks for G2:

- OS-specific limits (Windows Job Objects vs Linux prlimit/cgroup v2) may differ.
- Network denial is best-effort unless a platform primitive is available.
- A kernel/runtime vulnerability can escape any local sandbox.
- The worker runs with the developer's OS account. Linux additionally uses unshare+chroot when user namespaces are enabled; Windows still has Job Object resource limits only.
- The worker has no host DI, but it may still use the .NET BCL broadly; the deny-list is defense in depth, not the boundary.
- **Inherited environment (open):** the worker inherits the parent environment; a minimal allowlist/fail-closed launcher is not implemented yet (C2).
- **P/Invoke/native escape (open):** the worker compiles against the trusted platform assemblies and can declare `[DllImport]`; the current reference set does **not** block native calls. This is a G2 blocker unless the worker restricts references/IL or an OS policy contains it. TM-13 is not yet contained.
- **Contract surface:** the model source references only `Wip.Sandbox.Protocol` plus the BCL/TPA; there is no minimal-contract-only reference set yet. G2 must decide whether to curate references.

G2 must accept, restrict, or explicitly waive these residuals before WP-1.3 routes production execution through the worker.

## 7. Review checklist (G2)

- [ ] Assets and trust boundaries reviewed.
- [ ] Each TM-01..13 mapped to an executable test, or TM-13 explicitly accepted as residual risk.
- [ ] Worker protocol version and limits approved.
- [ ] Residual risk accepted or mitigated.
- [ ] Fallback/trusted-mode policy approved.

## 8. Independent review (2026-11-03)

**Verdict: NOT READY FOR G2.** The independent review found no OS containment (C1), inherited environment/secrets (C2), permitted P/Invoke (H1), and gaps in limits/ambient surface. It recommends failing G2 until C1, C2, H1, and the hostile suite are addressed.

See [security-review-2026-11-03.md](../execution/evidence/WP-1.1/security-review-2026-11-03.md). The worker has since added a handshake gate, worker-computed source hashes, per-strategy collectible ALCs, stdout suppression, response caps, fixed error messages, a curated reference set/deny-list, and a bounded byte reader. The host client clears the inherited environment and applies timeouts, process-tree kill, and Windows Job Object limits. **C1 is partially closed:** Linux now runs the worker in unshare user/net/pid/mount namespaces + chroot with host paths unmounted (where unprivileged user namespaces are allowed). Windows still has Job Object resource caps only; AppContainer profile creation requires elevation and the current launcher fails with access denied/error 5. C1 is **not closed on Windows**, and P/Invoke remains permitted; the worker is not yet a full sandbox.

## 9. Execution-mode policy (WP-1.6)

Bones routes strategy compilation and move selection through `BonesStrategyScriptHost`. The mode is explicit configuration (`BonesHost:ScriptHost:Mode`) and the default is **`InProcess`**, a deliberate deviation from the plan's "default Worker" recorded in [DEV-004](../execution/deviations/DEV-004-script-host-mode-default.md).

- **`InProcess` (default, trusted-only).** Model-authored code compiles with Roslyn and loads in the host process (`Assembly.Load`). The runtime logs a warning at startup. This is safe only while all strategies are trusted; it is not a sandbox and must not receive untrusted model or provider output.
- **`Worker` (opt-in, trusted-only).** The host starts `Wip.Sandbox.Worker` (path from `BonesHost:ScriptHost:WorkerPath`), completes the handshake, and routes `compileBones`/`chooseBonesMove` over the JSON-lines protocol. The host holds only `WorkerBonesPlayerSlot` and never loads the model assembly (WP-1.3 evidence: `docs/execution/evidence/WP-1.3/worker-coverage.md`). `TrustedOnly=false`, a missing worker binary, or a failed handshake fails the host at startup; there is no silent in-process fallback.
- **Not claimed.** `Worker` mode does not close C1 on Windows, does not contain P/Invoke (TM-13), and does not make untrusted model code acceptable. Full G2 remains unapproved and no release is authorized.

Known limitation: the workflow runtime currently builds a nested service provider per run (WP-2.1), which instantiates a second script host and therefore a second worker process for the duration of that run. Both are resource-capped and die with the host, but worker start-up should be unified when WP-2.1 lands. Bounded per-compile memory in the worker is deterministic (metadata references are cached per process and a full collection runs after each compile) so a long learning run does not exhaust the 256 MiB Job Object cap.
