# Independent security review — WP-1.1/WP-1.2 (2026-11-03)

**Reviewer:** subagent `031c78c9-d3a2-46df-a4cf-6dd0aca61da6`
**Verdict:** **NOT READY FOR G2.** WP-1.2 is a functional prototype, not a sandbox. Do not start WP-1.3 production routing.

## Answers to the two open questions

1. **Minimal contract assembly:** a contract project exists (`Wip.Sandbox.Protocol`), but there is **no minimal contract-only reference set**. Model source is compiled against every TRUSTED_PLATFORM_ASSEMBLIES assembly plus the protocol assembly. The protocol assembly also carries host-facing envelope/JSON types.
2. **P/Invoke/native:** **permitted.** A strategy with `[DllImport("kernel32.dll")]` compiled and returned a native PID. `DllImportAttribute` is in `System.Runtime`; there is no IL scan, allowlist, or OS block.

## Probe evidence (harmless, no file edits)

- compile/chooseMove before handshake succeeded; no handshake gate (fixed in the worker after review).
- protocolVersion 1.1 rejected although the spec allows additive 1.x (fixed to accept 1.x).
- native PID returned (still permitted; TM-13 open).
- model code wrote a forged chooseMoveResult to stdout (stdout suppression added after review).
- capability probe: File.Exists/username/HttpClient/Process.Start usable; no file content read or network request.
- same strategyId + host-supplied sourceHash with different source reused cached assembly (source hashing/ALC added after review).

## Findings

### Critical
- **C1 — No OS containment.** Job objects/prlimit are resource caps, not file/network/native boundaries. The planned isolation default cannot meet TM-01/03/04/13. G2 must choose AppContainer/low-integrity+firewall and Linux namespaces+seccomp/landlock, or explicitly narrow the guarantees.
- **C2 — Inherited environment/secrets.** The worker inherits the parent environment; no minimal environment is constructed. Host launcher must construct an allowlist and fail closed.

### High
- **H1 — P/Invoke/native calls allowed.** OS policy is the robust block; otherwise accept TM-13 explicitly with evidence.
- **H2 — Handshake not enforced.** Fixed: compile/chooseMove require handshake; version mismatch exits.
- **H3 — Cache trusts host hash.** Fixed: worker computes SHA-256, rejects mismatches, keys by verified hash.
- **H4 — No assembly isolation/unload.** Partially fixed: per-strategy collectible ALC and unload on replacement/shutdown; reflection over protocol/BCL remains.
- **H5 — No timeouts/memory/process caps.** WP-1.4; still open.
- **H6 — No outbound/output caps.** Partially fixed: response cap and diagnostics cap; stdout/stderr retained cap still WP-1.4.
- **H7 — Inbound limit is char-count after ReadLine.** Byte-count check added, but the line is allocated before rejection; bounded reader still needed.
- **H8 — Error taxonomy drift/leakage.** Fixed: fixed codes/messages, no exception type names; `error` documented; internal errors fail closed.

### Medium/Low
- M1/M2 fixed or documented (1.x accepted; `error`/sourceHashMismatch documented).
- M3 tests expanded to 7 (handshake gate, hash mismatch, stdout injection, minor version, malformed, compile/choose).
- M4 architecture rules added for sandbox protocol/worker.
- M5 curated reference assemblies still open.
- M6 cache cap 64; LRU/unload metrics still open.
- L1–L3 partially addressed.

## G2 decision required

G2 must decide the isolation level (C1), environment/secret handling (C2), P/Invoke (H1), curated references (M5), and timeout/limit evidence (H5/WP-1.4). The review recommends **failing G2 until C1, C2, H1 and the hostile suite are addressed**.

No G2 approval is recorded here.
