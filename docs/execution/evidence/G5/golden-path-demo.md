# G5 golden-path demo — real shell host (2026-11-05)

**Status:** executed locally; no release is claimed. This is G5 evidence, not an approval.

## Environment

- WSL Ubuntu, clean clone at `977a600` (contains all Phase 3–6 work).
- `dotnet build WIP/Wip.ShellHost/Wip.ShellHost.csproj -c Release` → exit 0.
- Real process: `dotnet WIP/Wip.ShellHost/bin/Release/net10.0/Wip.ShellHost.dll` with scripted stdin,
  run at the solution root (the host requires a .NET workspace). Not a unit test stub.
- Full transcript: `.tmp/golden/demo-repo.log` (generated locally, not committed).

## Golden path executed

```
init
use workflow workflow.linear
session start "Golden path demo"
plan
run
status
artifacts
retention status
exit
```

## Observed result (exit code 0)

```
wip> Repository initialized: /home/ricardo/modus-repro2
wip> Workflow selected: workflow.linear
wip> Session started: 772bfee825054c2091204098581d8fe8 task="Golden path demo" workflow=workflow.linear
Plan artifact: .wip/sessions/<id>/artifacts/agent-plan-….md
AgentPlan artifact: .wip/sessions/<id>/artifacts/agent-plan-result-….json
wip[<id>]> Workflow executed: workflow.linear
Workflow artifact: .wip/sessions/<id>/artifacts/workflow-execution-….json
AgentRunResult artifact: .wip/sessions/<id>/artifacts/agent-run-result-….json
wip[<id>]> Session status: <id>
wip[<id>]> Artifacts for session <id>: (6 artifact records listed, 12 files on disk)
wip[<id>]> Retention status: sessions=1 artifacts=12 bytes=9195
Oldest artifact (UTC): 2026-11-05T16:26:42.9595621+00:00
Default policy: 30 days. Run 'retention purge [days]' to delete older session artifacts.
```

The shell also printed the governance guidance after `run`: explicit `validate`, `review`, `approve`
and `merge` remain required (no auto-merge), and the session stayed pre-governance.

## Scope and gaps

- The `agent` command needs a registered `IChatModel`; this demo used no provider, so the model path
  was not exercised here. It is covered separately by:
  - the shell command test with a scripted streaming model (`Wip.Shell.Tests`),
  - the real-adapter HTTP-boundary E2E with a fake DeepSeek server (`Wip.Runtime.Tests`).
- `validate`/`review`/`approve`/`merge` were intentionally not run in this demo because they need
  validation evidence and a confirmation prompt; their contract is covered by the shell/contract
  tests. The post-release verification plan includes them.
- This demo does not upgrade G2 or provide external security sign-off; C1 remains unclaimed.

## Verdict

The golden path (init → session → plan → run → status → artifacts → retention) runs on the real host
with exit code 0 and produces inspectable artifacts. **No release claim.**
