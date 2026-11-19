# Engineering evidence index

**Candidate source revision:** `1d1e5128120fae2865e112615173c351e5fd8fa9` (branch
`feature/wip-factory-alpha`). **Engineering status:** `ENGINEERING_COMPLETE` per
`ENGINEERING_TEST_RESULTS.json`. **Acceptance:** `HISTORICAL_FAILURE_PRESERVED` / `NOT_ACCEPTED`.

The gate run executed in a clean detached worktree at that revision, so the untracked concurrent work in the main
workspace (a separate session's `Wip.Factory.ControlApi` and `control-plane/` additions) could not enter the
measurement.

## Files

| File | Meaning |
|---|---|
| `ENGINEERING_GATE_REPORT.json` | Raw measured build/suite/gate report written by the gate runner |
| `evidence/build.log` | Full `dotnet build Wip.slnx -c Debug` output |
| `evidence/factory-core.trx` | Core suite TRX, 21 passed |
| `evidence/factory-agents.trx` | Agents suite TRX, 69 passed |
| `evidence/factory-host.trx` | Host suite TRX, 216 passed |
| `evidence/workspace-git.trx` | Workspace-git suite TRX, 28 passed |
| `evidence/architecture.trx` | Architecture suite TRX, 5 passed |

Every TRX digest in `ENGINEERING_TEST_RESULTS.json` matches the corresponding file in `evidence/`; `build.log`
matches the record's `buildDigest`.

## Reproduction

```powershell
git worktree add --detach C:\tmp\modus-candidate 1d1e5128120fae2865e112615173c351e5fd8fa9
cd C:\tmp\modus-candidate
powershell -NoProfile -ExecutionPolicy Bypass -File docs/goal-wip-factory-operator-alpha/engineering/run-engineering-gates.ps1 -BranchName feature/wip-factory-alpha
```

The candidate manifest binds its digests in that clean checkout. The main workspace may contain concurrent,
uncommitted files from another session; those files are not part of this candidate and are not measured by the
gate runner.
