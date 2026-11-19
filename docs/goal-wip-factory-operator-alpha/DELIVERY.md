# Wip.Factory Operator Alpha — delivery record (v0.4 remediation)

**Status:** `implementation-ready / acceptance-pending`.
**Accepted:** no. **PA-01/PA-02:** not run — no independent evaluator supplied withheld requirements.

Baseline: `feature/wip-factory-alpha` at `e142bb43fb2faed6884f85387833186eac06457d` plus the v0.4 remediation
changes. Spec: `harness/requirements/Wip.Factory.Operator.Alpha.v0.3.md`; remediation requirements: v0.4 (this record).
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
| Factory suites | **pass** | 259 cases, 0 failures across core (21), agents (69), host (164), architecture (5) |
| Named v0.3 tests | **56 / 80 exact** | plus 18 equivalent-but-renamed; 6 missing behaviours reported; 0 name-only placeholders |
| FAC-P0-01 release integrity | **implemented + tested** | crash-after-merge resumes verification; no release on a ledger shortcut |
| FAC-P0-02 four review checks | **implemented + tested** | validation/coverage/architecture/security each with executed, digest-bound evidence |
| FAC-P0-03 immutable templates | **implemented + tested** | re-registration conflict, marker/body mismatch, revocation persistence, traversal refusal |
| FAC-P0-04 acceptance harness | **implemented + tested** | evaluator owns expected scenarios and frozen contract; missing/duplicate/uninspected evidence fails |
| FAC-P0-05 blind PA-01/PA-02 | **not-run** | requires an external evaluator and withheld requirements |
| FAC-P1-01..08 | **implemented with named exceptions** | see `v0.4-closure-map.md`; secret redaction and its security-policy check are implemented |
| v0.3 PA-03–PA-08 | **unchanged, not re-certified** | prior evidence remains a session report; this remediation did not re-run live model acceptance |

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

Two requirement files the implementer has not seen, with evaluator-authored expectations
(`expectedScenarioIds`, `approvedContractDigest`, `frozenHostDigest`, `evaluatorOwned: true`, `evaluatorId`), run
against this binary and configuration with no source or configuration change:

```powershell
run-acceptance.ps1 -Requirement <withheld-A.md> -Gate PA-01 -OutRoot <dir> -Expectations <A-evaluator.json>
run-acceptance.ps1 -Requirement <withheld-B.md> -Gate PA-02 -OutRoot <dir> -Expectations <B-evaluator.json>
factory acceptance verify-pair --first <A.evidence.json> --first-expectations <A-evaluator.json> `
                              --second <B.evidence.json> --second-expectations <B-evaluator.json>
```

Until those exist, the honest answer to "is this an accepted factory?" is **no**. The engineering claims are
measured; the product acceptance is unclaimed.

## Known limitations (not hidden)

- **PA-01/PA-02 not run.** No independent evaluator was available in this session.
- **56 of the 80 v0.3 named tests are exact; 18 more are proven under explicitly mapped names; 6 remain absent.**
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
