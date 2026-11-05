# Goal — WIP-IMPL-001: Phase 3 completion through the G5 decision (run of 2026-11-05)

**Outcome (one sentence):** WIP 1.0 is release-ready pending an explicit human G5 decision — the
Phase 3 agent runtime is complete (vendor `IChatModel` adapter with SSE streaming, a real-model
write-capable sample agent proven at the HTTP boundary, the full safety suite), Phases 4–6 are
complete, the G2 re-review packet is assembled, and the G5 packet carries verified evidence and
exact release steps — with no release, publish, sign or deploy performed.

## Definition of done (falsifiable)

| # | Check | Command / observable |
|---|---|---|
| D1 | DeepSeek `IChatModel` adapter implements `CompleteAsync` and `StreamAsync` (SSE); vendor types stay inside the adapter; mock-`HttpMessageHandler` tests cover request mapping, tool calls, usage, deltas, cancellation and error status | focused `dotnet test` filter exit 0 with N tests; replacing a mapping body with a stub makes them fail |
| D2 | Host composition registers the adapter as `IChatModel`; shell/status renders streamed progress | focused test asserts DI resolution and rendered delta/progress output |
| D3 | HTTP-boundary E2E against a fake DeepSeek server: agent answers a repository question and makes a scoped edit in its isolated worktree, records artifacts/telemetry, never auto-merges | E2E exit 0; asserts file content, artifact/telemetry, and absence of merge/push |
| D4 | Opt-in live test gated by `DEEPSEEK_API_KEY`: skipped deterministically without the key, runs with it | test reports `Skipped` without the key; excluded from the offline gate |
| D5 | Safety suite: prompt injection via tool output, self-approval/merge attempt, exfiltration via shell/search, oversized observations, model error, cancellation — each fails closed or is bounded; wired into the Security/Agent CI job | security test class exit 0; CI step present and green |
| D6 | Phase 4: legacy Modus full-suite meta-tests reconciled; non-blocking step removed; full Modus suite blocking and green; docs/runbooks/operator guide truthful | `continue-on-error` absent on the suite step; Modus CI run all green; run id recorded |
| D7 | Phase 5: redaction + retention config + purge implemented and tested; security review packet + adversarial self-review; external sign-off recorded as a human gate (never fabricated) | tests green; packet exists with evidence IDs |
| D8 | Phase 6: versioning, packaging dry-run, signing plan, compatibility/upgrade notes, post-release verification plan; no publish/sign/deploy | pack dry-run exit 0 into a local folder; docs exist; no registry push |
| D9 | G2 re-review packet assembled: conditional/trusted-only preserved, C1 unclaimed, evidence cited | packet exists; status not upgraded without human sign-off |
| D10 | G5 packet: golden-path demos, security review, known limits, exact release steps, explicit decision request | `docs/execution/evidence/G5/release-readiness.md` updated with EVD ids and the decision request |
| D11 | Verification at the committed HEAD: 16-project offline gate with zero unwaived failures, architecture 4/4, security green, Tests CI six jobs green | `tools/run-wip-tests.ps1` + `tools/verify-test-results.ps1` exit 0; CI run id recorded in `docs/execution/state.json` |

## Hard constraints

- No release claim, no publish/sign/deploy before an explicit human G5 decision; external security
  sign-off is a human gate and is never fabricated.
- Preserve the G0 decisions: no `Wip.Sandbox.Worker` → `Wip.Bones`; no untrusted model code in the
  host; host-owned DI; worktree-confined writes; no auto-merge; no secrets in logs or artifacts.
- Do not weaken the 16-project gate, the architecture rules, the security posture or existing
  waivers. A new test must fail when its implementation body is replaced by a stub.
- Windows OS containment (C1) remains unclaimed; G2 stays conditional/trusted-only until the
  2026-12-03 re-review and human sign-off.
- No `reset --hard`, no `clean`, no force-push; preserve the working tree; one work package per
  commit; UTF-8 without BOM and the staged-encoding check before every commit.

## Stop criteria

- **Met** when D1–D11 hold and the G5 packet with the explicit decision request is delivered. The
  terminal hard stop is the human G5 decision; no release is performed by this run.
- **Round cap:** 12 rounds. If the cap is reached, leave the goal active and write a handoff packet.
