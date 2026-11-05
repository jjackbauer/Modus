# G2 re-review packet — prepared 2026-11-05 for the 2026-12-03 review

**Status:** **conditional / trusted-only** (unchanged). Windows OS containment (C1) is **not
claimed**. This packet is evidence for the scheduled re-review; it does **not** upgrade the status.
The upgrade requires the human G2 sign-off and the external security review.

## Scope

Sandbox isolation and worker protocol, permissions, plugin assembly-load contexts, prompt/artifact
handling, approval/merge boundary, privacy/retention.

## Evidence index

| Area | Evidence |
|---|---|
| Worker isolation, resource limits, hostile inputs | `docs/execution/evidence/WP-1.1..1.5/`; `tests/Wip.Sandbox.Worker.Tests` 18/18 on Windows and Ubuntu CI |
| ScriptHost mode flag (InProcess/Worker, fail-closed) | `WIP/Wip.Bones.Host/BonesScriptHostRuntime.cs`; `BonesHostScriptHostOptionsTests` + runtime flag tests 9/9 local (rollback rehearsal); Windows gate green |
| Permissions (catalog, Enforce/Audit, dispatcher/tool/plugin-load/model-invoke) | `docs/execution/evidence/WP-1.7/`; permission tests; `docs/security/sandbox-threat-model.md` |
| Plugin ALC and conflicting versions | `Wip.Modus.Tests` conflict fixtures; per-plugin collectible contexts |
| Prompt injection / self-approval / exfiltration | `Wip.Runtime.Tests/Repo/AgentSafetyTests.cs` (3 fail-closed tests + controls); both security CI jobs |
| Approval/merge boundary | shell command semantics; `run` is execution-only; merge requires explicit governance |
| Privacy/retention | `SecretRedactorTests`, `SessionArtifactRetentionTests`, memory-redaction test; `docs/security/data-classification.md` |
| Security review | `docs/execution/evidence/WP-5.5/security-review.md` (internal + adversarial; external sign-off **not** obtained) |
| Gate runs | Tests CI `42059745946` on `9dc8a56` (all six jobs); Modus CI `42059745863` (blocking legacy suite) |

## Control status

| Control | Status |
|---|---|
| Worker process isolation + resource limits (Linux) | verified |
| Windows OS containment (C1) | **not implemented / unclaimed**; fail-closed posture only |
| ScriptHost mode flag + fail-closed | verified |
| Permissions enforce at dispatcher/tool/plugin load/model invoke | verified |
| Plugin ALC dependency isolation | verified |
| Prompt injection treated as data; no auto tool execution from observations | verified |
| No self-approval / no auto-merge | verified |
| Worktree-confined writes / traversal denial | verified with controls |
| Secret redaction in agent memory; key never logged | verified (pattern-based limits documented) |
| Retention/purge | verified (operator-driven) |

## Residual risks (unchanged unless the review decides otherwise)

1. C1 (Windows OS containment) — High residual, fail-closed, trusted-only.
2. Windows in-process plugin/model execution — Medium residual.
3. Shell tool runs with operator process privileges under policy — Medium residual.
4. Pattern-based redaction can miss unknown credential shapes — Medium residual.

## Criteria for upgrading the status

- A C1 decision: implement Windows containment **or** a human-accepted alternative with documented
  residual risk.
- External/independent security review completed and signed; no open critical/high findings without
  accepted risk.
- All control tests green on the release commit's CI.
- Explicit human G2 sign-off recorded.

## Decision requested at the review

Upgrade, keep conditional, or descope. **No upgrade is asserted in this packet**; until the review
and sign-off, the posture remains conditional/trusted-only and C1 remains unclaimed.
