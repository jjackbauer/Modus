# WP-1.1 — Threat model and worker protocol (draft)

**Status:** draft complete; **security review and G2 not passed**.
**Date:** 2026-11-02
**Owner:** platform/security engineer + tech lead

## Deliverables

- [Sandbox threat model](../../security/sandbox-threat-model.md): assets, trust boundaries, guarantees, abuse cases TM-01..12, residual risk, review checklist.
- [Worker protocol v1.0](../../security/worker-protocol.md): JSON-lines framing, handshake/compile/chooseMove/ping/shutdown, limits, correlation IDs, error taxonomy, timeouts, compatibility.

## Acceptance

| Criterion | Status |
|---|---|
| Threat model documented | **DONE (draft)** |
| Protocol versioned and documented | **DONE (v1.0)** |
| Abuse cases mapped to tests | **DONE (planned TM-01..12)** |
| Threat model reviewed by security | **PENDING — G2 review** |
| G2 approval | **NOT PASSED** |

## Next

Security review of the threat model, protocol limits, and residual risk. WP-1.2 (worker host) can scaffold against the draft; WP-1.3 production routing must not start until G2.
