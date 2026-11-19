# State — Wip.Factory Operator Alpha v0.4 remediation

**Status: NOT ACCEPTED — remediated candidate awaiting a new independent unseen pair.**
Independent PA-01 failed on the previous candidate; PA-02 passed; the pair verdict failed.

## Independent acceptance (2026-11-19)

Package from `external-chatgpt-evaluator-2026-11-19`, executed on the frozen binary at code revision `e29df4d`
(host `sha256:b921c4c1…a9990`, config `sha256:3afdf0be…3bb7`).

- **PA-01: FAIL** — the first implementation attempt created a worktree and model source but the agent run failed
  before commit; retries then failed with `branch already exists`, replacing the original failure.
- **PA-02: PASS** — 7/7 scenarios in both phases, merged SHA == target head, `OperationallyValidated`.
- **Pair: FAIL**.
- Full record: `v0.4-independent-acceptance-result.md`; evidence in `independent-acceptance-2026-11-19/`.

## Remediation after the failure

- **Fix `baa3bfc`:** session worktree provisioning is now idempotent. An existing worktree for the same
  session/branch is reused (preserving uncommitted model work); an existing branch without a worktree is attached;
  a worktree on a different branch is refused. This directly addresses the PA-01 retry failure.
- **Measured:** 278 factory cases, 0 failures across core (21), agents (69), host (169), workspace-git (14) and
  architecture (5); build 0 errors. Traceability remains 61 exact + 18 renamed = 79/80.
- **New freeze candidate:** code `baa3bfc`, host `sha256:f3109d34…9f8b`, host-output composite
  `sha256:4db45323…33d4`, config `sha256:3afdf0be…3bb7` (see `freeze-candidate.json`).

## Why still not accepted

The 2026-11-19 tasks are spent. The evaluator's rules forbid changing the candidate and reusing the same tasks as
blind evidence. A new, previously unseen evaluator-authored PA-01/PA-02 pair is required for the remediated
candidate. No acceptance claim can be made before that pair passes.

See `v0.4-closure-map.md`, `v0.4-test-report.md`, `v0.4-fault-injection.md`, `v0.4-status.md`,
`v0.4-evaluator-handoff.md`, `v0.4-independent-acceptance-result.md`.
