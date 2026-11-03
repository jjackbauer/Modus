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
3. **Worker ↔ OS**: the worker is constrained by Job Object/prlimit/cgroup, timeouts, output caps, and a private temp directory.
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
- The worker still runs with the developer's OS account; filesystem isolation is policy/protocol-level unless a stronger OS sandbox is added.
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

See [security-review-2026-11-03.md](../execution/evidence/WP-1.1/security-review-2026-11-03.md). The worker has since added a handshake gate, worker-computed source hashes, per-strategy collectible ALCs, stdout suppression, response caps, and fixed error messages; those do not make it a sandbox.
