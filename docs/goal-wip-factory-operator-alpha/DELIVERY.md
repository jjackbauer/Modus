# Wip.Factory Operator Alpha — delivery record (v0.4 remediation)

**Status: NOT ACCEPTED.**
**Independent acceptance (2026-11-19):** PA-01 **failed**, PA-02 **passed**, pair **failed**. See
`v0.4-independent-acceptance-result.md`.

Baseline: `feature/wip-factory-alpha` at `e142bb43fb2faed6884f85387833186eac06457d` plus the v0.4 remediation
changes. Code revision: `baa3bfc` (worktree-retry remediation); measured evidence: `d1563d5`.
Spec: `harness/requirements/Wip.Factory.Operator.Alpha.v0.3.md`; remediation requirements: v0.4 (this record).
The superseded v0.3 narrative is preserved in git history, not restated here with stale numbers.

---

## What is delivered

An operator submits a requirement; the factory implements it through the WIP tool loop, executes four independent
review checks, requires a separate merge authorisation, merges, verifies the **merged revision** with build +
existing regressions + frozen HTTP contract, and can supervise the pinned release. No per-application C# and no
replacement factory, agent runtime, tool loop, merge policy or dashboard were introduced.

## Measured gate status at this revision

Source: `v0.4-test-report.md`, `gate-report.json`, `named-test-coverage.json`.

| Gate | Status | Basis |
|---|---|---|
| Build | **pass** | `dotnet build` 0 errors |
| Factory suites | **pass** | 278 cases, 0 failures across core (21), agents (69), host (169), workspace-git (14), architecture (5) |
| Named v0.3 tests | **61 / 80 exact** | plus 18 equivalent-but-renamed; 1 missing (blind PA-02, not-run); 0 name-only placeholders |
| FAC-P0-01 release integrity | **implemented + tested** | crash-after-merge resumes verification; no release on a ledger shortcut |
| FAC-P0-02 four review checks | **implemented + tested** | validation/coverage/architecture/security each with executed, digest-bound evidence |
| FAC-P0-03 immutable templates | **implemented + tested** | re-registration conflict, marker/body mismatch, revocation persistence, traversal refusal |
| FAC-P0-04 acceptance harness | **implemented + tested** | evaluator owns expected scenarios and frozen contract; missing/duplicate/uninspected evidence fails |
| FAC-P0-05 blind PA-01/PA-02 | **failed — new pair required** | PA-01 failed on worktree retry idempotency (fixed in `baa3bfc`); PA-02 passed 7/7 in both phases; pair exit 2; spent tasks cannot be reused |
| FAC-P1-01..08 | **implemented with named exceptions** | see `v0.4-closure-map.md`; secret redaction and its security-policy check are implemented |
| v0.3 PA-03–PA-08 | **mechanics exercised; PA-02 passed** | independent PA-02 passed all 7 scenarios in both phases; PA-01 failed before review; prior known-input A also passed 4/4 |

## Reproducing

```powershell
dotnet build Wip.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File docs/goal-wip-factory-operator-alpha/run-factory-gates.ps1
```

The gate script runs the four suites, regenerates `named-test-coverage.json`, and writes `gate-report.json` with
separate counts for cases, failures and exact named coverage. It does not mark PA-01/PA-02.

## Operator commands (unchanged public surface, plus evaluator verb)

```text
factory requirement submit --file <req.md> --repo <dir> [--control-root <dir>]
factory contract show   --work-item <id>
factory contract approve --work-item <id> --digest <sha256:...>
factory contract approve --work-item <id> --policy
factory run --until-idle
factory status
factory approve --work-item <id> --review-digest <sha256:...>
factory serve --work-item <id> [--duration-seconds N]
factory template register|approve|list
factory acceptance verify --manifest <evidence.json> --expectations <evaluator.json>
factory acceptance verify-pair --first ... --second ...
```

## What acceptance still requires

The 2026-11-19 attempt failed PA-01; its tasks may not be reused as blind evidence for a changed candidate. A new
freeze and a **new, previously unseen** evaluator pair are required:

1. Fix worktree-provisioning retry idempotency and preserve the original agent failure category.
2. Rerun the suites; record the new code revision and host/config digest.
3. Obtain two new evaluator-authored requirement files and expectation manifests.
4. Execute both with `run-acceptance.ps1` and `factory acceptance verify-pair`.

Until that new pair passes, the honest answer to "is this an accepted factory?" is **no**.

## Known limitations (not hidden)

- **Independent acceptance failed:** PA-01 failed at implementation retry (`branch already exists` after a soft agent
  failure); PA-02 passed 7/7 in both phases. The pair verdict failed. A fresh live **known-input** run
  (`v0.4-known-input-run.md`) also passed end to end but is explicitly not blind acceptance.
- **61 of the 80 v0.3 named tests are exact; 18 more are proven under explicitly mapped names; only the blind PA-02
  name remains unrun.**
  `named-test-coverage.json` and `named-test-renames.json` report each one. No pass-through tests were manufactured.
- **Child-process output is redacted** by the versioned `factory/redaction@1` policy; a redactor failure is the named
  `redaction-failed` refusal. Tool-boundary, path-escape and missing-OS-bounds negatives are implemented.
- **Crash injection is deterministic in-process**, not an OS process kill: the merge effect is durable before the
  verification process is interrupted, and a fresh handler over the same state re-verifies. A kill during the git
  merge syscall itself is not covered.
- **Windows process identity is proven on Windows.** Other platforms refuse with `unsupported-platform` rather than
  guessing.
- **Review architecture/security checks are narrow by design** (single-repo ASP.NET Core .NET 10; static policy
  checks). They are not a general vulnerability scanner and do not claim to be.
- **DSH-P2-01–06 are harness-scoped** and were not implemented in the factory; see `v0.4-status.md`.
