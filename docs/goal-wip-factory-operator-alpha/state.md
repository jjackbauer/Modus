# State — Wip.Factory Operator Alpha v0.4 remediation

**Status: NOT ACCEPTED (independent blind).** The spent 2026-11-19 pair failed PA-01; after remediation **both tasks
pass in a non-blind rerun** on the final candidate.

## Independent blind attempt (2026-11-19)

Package `external-chatgpt-evaluator-2026-11-19`, frozen candidate `e29df4d` (host `sha256:b921c4c1…a9990`).

- **PA-01: FAIL** — worktree retry after a soft agent failure collided with `branch already exists`.
- **PA-02: PASS** — but exposed an architecture-check false negative on the *remediation rerun* (app + test project).
- **Pair: FAIL**; the tasks are spent and cannot be relabelled blind.
- Evidence: `independent-acceptance-2026-11-19/`.

## Remediation

- `baa3bfc` — idempotent session worktree provisioning: reuse an existing same-session worktree/branch, attach an
  existing branch, refuse a different branch.
- `b31a446` — architectural conformance accepts one ASP.NET Core web application plus test/library projects, and
  refuses only a second non-test executable.
- **Measured at `b31a446`:** 280 factory cases, 0 failures across core (21), agents (69), host (171), workspace-git
  (14), architecture (5); build 0 errors. Traceability: 61 exact + 18 renamed = 79/80.

## Non-blind remediation rerun (final candidate)

On committed binary `sha256:03c64235…6650`, config `sha256:3afdf0be…3bb7`:

| Task | Result | Scenarios |
|---|---|---|
| PA-01 | **pass** | 8/8 pre-merge, 8/8 post-merge; merged SHA == target; `OperationallyValidated` |
| PA-02 | **pass** | 7/7 pre-merge, 7/7 post-merge; merged SHA == target; `OperationallyValidated` |

Evidence: `independent-acceptance-2026-11-19/remediation-rerun/` with `knownInput: true`, `blind: false`.

## Why still not accepted

The independent blind property is gone for these tasks. An acceptance claim requires a **new, previously unseen
evaluator-authored PA-01/PA-02 pair** against the frozen `b31a446` candidate. The product path is now verified on
both previously failing requirements; the formal blind gate is not.

See `v0.4-closure-map.md`, `v0.4-test-report.md`, `v0.4-fault-injection.md`, `v0.4-status.md`,
`v0.4-evaluator-handoff.md`, `v0.4-independent-acceptance-result.md`.
