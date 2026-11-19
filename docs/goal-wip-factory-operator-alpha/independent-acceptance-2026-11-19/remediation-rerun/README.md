# Remediation re-run of the evaluator tasks — non-blind

**These results are not independent blind acceptance.** The 2026-11-19 tasks were already executed against the
previous candidate and are spent under the evaluator's rules. This directory records a **known-input remediation
re-run** to prove the two defects found by that attempt are fixed. The evidence files carry
`knownInput: true` and `blind: false`; they must not be relabelled.

## Candidate

| Item | Value |
|---|---|
| Code revision | `b31a446` (worktree-retry fix `baa3bfc` + architecture-conformance fix `b31a446`) |
| Host digest | `sha256:03c642350dc50ebadb242e5a8f526343db1cecc4b279e24a68368de0927f6650` |
| Config digest | `sha256:3afdf0bec8bb5565a329cd47c8787ba56a42e253e8194ca3da7574972f113bb7` |
| Model | live, `deepseek` |

## Results

| Task | Result | Scenarios | Release |
|---|---|---|---|
| PA-01 — limited-use pass redemption | **pass** | 8/8 pre-merge, 8/8 post-merge | merged SHA == target head; `OperationallyValidated` |
| PA-02 — ISO 8601 week boundary | **pass** | 7/7 pre-merge, 7/7 post-merge | merged SHA == target head; `OperationallyValidated` |

## Defects fixed between the blind attempt and this re-run

1. `baa3bfc` — session worktree provisioning reuses an existing worktree/branch on retry instead of failing with
   `branch already exists` (the PA-01 failure).
2. `b31a446` — architectural conformance accepts one web application plus test/library projects, and refuses only a
   second non-test executable. The PA-02 attempt generated `IsoWeekApi` + `tests/IsoWeekTests`; the old check
   rejected any non-web project.

## What this proves and what it does not

It proves both previously failing tasks pass on the remediated candidate, with real model runs, real builds, real
HTTP contracts, real merges and real serves. It does **not** restore the independent blind property: the requirement
contents are known to the implementer, so a new evaluator-authored, previously unseen pair is still required for an
acceptance claim.
