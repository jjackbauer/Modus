# WP-5.5 security review packet (internal + adversarial self-review)

**Date:** 2026-11-05
**Scope:** sandbox/isolation, permissions, plugin assembly-load contexts, prompt/artifact handling,
approval/merge boundary, privacy/retention.
**Status:** internal review complete; **external/independent sign-off NOT obtained**. G2 remains
conditional/trusted-only and Windows OS containment (C1) remains unclaimed. Any upgrade of that
status requires the human G2 re-review (scheduled 2026-12-03) — it is not asserted here.

## Method

- Read the threat model and the enforcement code (`docs/security/sandbox-threat-model.md`,
  `Wip.Runtime`, `Wip.Modus`, `Wip.Tools.Repo`, `Wip.Shell`).
- Ran the hostile suites and adversarial cases: sandbox hostile/protocol (18), agent safety
  (3: prompt injection, self-approval, exfiltration), permission evaluator/dispatcher/gateway/
  plugin-load/model-invoke tests, worktree escape/delete tests.
- Attempted to falsify each mitigation with a concrete input (below); controls were run for the
  worktree guard, the write tool and the redactor.

## Findings

| # | Finding | Severity | Evidence | Status |
|---|---|---|---|---|
| 1 | Windows OS containment (C1) is not implemented: the AppContainer helper is absent / fails with access denied | **High residual** | `docs/security/sandbox-threat-model.md`; `docs/execution/evidence/G2/`; G2 decision (residual accepted) | **Open by design**: fail-closed, trusted-only, not claimed. Requires human acceptance at G2. |
| 2 | Sandboxed plugin/model code runs in-process on Windows; isolation on Linux uses the worker + Job Object limits | **Medium residual** | `Wip.Sandbox.Worker`, WP-1.1..1.6 evidence | Open; mitigation: worker mode for untrusted profiles, feature flag default `InProcess` documented. |
| 3 | Prompt injection via tool output could bias the model toward harmful tool calls | Medium | `AgentSafetyTests.RunAgentAsync_GivenToolOutputContainingInjection…`; observations are data, never commands | Mitigated: worktree guard, permissions, no auto-merge, explicit approval; residual model-choice risk documented. |
| 4 | The agent could try to self-approve or merge | Medium | `AgentSafetyTests.RunAgentAsync_GivenSelfApprovalAttempt…` (unknown-tool, session not Approved/Merged) | Mitigated: approval/merge are not agent tools; shell requires explicit governance commands. |
| 5 | Exfiltration via traversal read/search or shell output | Medium | `AgentSafetyTests.RunAgentAsync_GivenTraversalReadAndOutOfWorktreeSearch…`; worktree guard control | Mitigated for repo tools; **shell** commands still run with the operator's process permissions under policy. Residual: treat shell as privileged, scope `process` permission grants. |
| 6 | Secrets could be embedded in prompts/artifacts | Medium | `SecretRedactorTests`; `ArtifactAgentMemory` redaction tests | Mitigated for agent memory; other logs/status paths rely on never logging secrets. Residual: pattern-based redaction can miss unknown formats. |
| 7 | Session artifacts accumulate indefinitely | Low | `SessionArtifactRetentionTests`; `retention status/purge` | Mitigated: 30-day default policy and purge command; no scheduler yet. |
| 8 | Plugin dependency confusion / version unification | Medium | `Wip.Modus.Tests` conflicting-dependency fixture (per-plugin ALC) | Mitigated: collectible per-plugin ALC + `AssemblyDependencyResolver`; tested with V1/V2 fixtures. |
| 9 | Untrusted model code in the host | High (G0 decision) | Architecture rules; no Worker→Bones reference; host-owned DI | Mitigated by architecture gates (4/4 over 43 projects); no relaxation. |

## Adversarial attempts and results

| Attempt | Result |
|---|---|
| Write outside the worktree (`../`, absolute, `.git/config`) | Denied; file intact; test fails if both guard checks are removed (control) |
| Self-issue `tool.session.approve` | `unknown-tool:`; session not Approved/Merged |
| Read `../../secret.txt`; search out-of-worktree secret | Denied/no match; control (guards removed) leaks and the test fails |
| Persist a message containing `sk-…` / Bearer token | Stored as `[REDACTED]`; load also redacts |
| Purge with a 30-day boundary | Old deleted, recent retained; disabling the age check makes the test fail |
| Green legacy suite with the failure-reporting step | Reporting step bug found and fixed (not a security issue) |

## Residual risk statement

- The security posture is **conditional/trusted-only**. The sandbox is fail-closed on Windows but is
  not OS-level containment. Do not represent it as such.
- The shell tool is privileged by design; operators must grant `process` only where required.
- Redaction is pattern-based; new credential shapes require pattern updates.
- **External/independent sign-off has not been obtained.** This packet is evidence for that review,
  not a substitute. No approval is fabricated.

## Recommendation

Proceed to the G2 re-review with this packet and the linked test evidence. The G5 release packet
must carry finding 1 as an accepted residual only after explicit human sign-off; otherwise WIP 1.0
is not release-ready.
