# State — Wip.Factory Operator Alpha v0.4 remediation

**Status: NOT ACCEPTED.** Independent PA-01 failed; PA-02 passed; the pair verdict failed.

- **Independent acceptance (2026-11-19):** package from `external-chatgpt-evaluator-2026-11-19`, executed on the frozen
  binary at code revision `e29df4d` (host `sha256:b921c4c1…a9990`, config `sha256:3afdf0be…3bb7`).
  - **PA-01: FAIL** — the first implementation attempt created a worktree and model source but the agent run failed
    before commit; retries then failed with `branch already exists`, replacing the original failure.
  - **PA-02: PASS** — 7/7 scenarios in both phases, merged SHA == target head, `OperationallyValidated`.
  - **Pair: FAIL**.
  Full record: `v0.4-independent-acceptance-result.md`.
- **Engineering baseline before acceptance:** 264 factory cases, 0 failures; 61 exact + 18
  equivalent-but-renamed = 79/80 v0.3 names accounted; build 0 errors.
- **Why not accepted:** PA-01 failed. The evaluator's rules forbid reusing these tasks after a code change; a fix
  requires a new freeze and a new unseen pair.
- **Next:** fix worktree-provisioning retry idempotency and preserve the original agent failure category; rerun the
  suites at a new revision; obtain a new evaluator-authored unseen pair; do not label the current tasks blind again.

See `v0.4-closure-map.md`, `v0.4-test-report.md`, `v0.4-fault-injection.md`, `v0.4-status.md`,
`v0.4-evaluator-handoff.md`, `v0.4-independent-acceptance-result.md`.
