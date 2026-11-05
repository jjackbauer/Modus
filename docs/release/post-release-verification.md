# Post-release verification plan (not executed — no release)

**Status:** plan only. Nothing has been released, so no post-release verification has run. This is
the checklist the human owner must run after a G5-approved release.

## 1. Install and start

```powershell
dotnet --version                         # 10.0.x
dotnet run --project WIP/Wip.ShellHost/Wip.ShellHost.csproj
```

- Shell starts with the startup banner and no configuration error.
- `config` shows the expected effective provider/model and no secret values.
- `help` lists the full command surface (including `agent` and `retention`).

## 2. Golden path

```
init
session start "post-release smoke"
plan
run
status
artifacts
```

- Session state advances and artifacts are listed with stable paths.
- `validate` / `review` / `approve` / `merge` still require explicit evidence and the confirmation
  prompt; `merge` never happens automatically.

## 3. Agent path (stub or configured provider)

```
agent "Summarize the repository README"
```

- Deltas stream to the console; tool calls/results appear; the final answer is recorded.
- With a real provider key set, confirm the model request succeeds and no key appears in output or
  logs.

## 4. Isolation and permissions

- Worktree escape attempts (`../`, absolute paths, `.git`) are denied.
- Plugin-origin capabilities are denied without grants; `Enforce` mode is active.
- `ScriptHost:Mode=Worker` starts, and no worker process remains after exit.

## 5. Privacy and retention

- `retention status` reports zero/known artifacts and the oldest timestamp.
- `retention purge 30` deletes only expired artifacts and prints counts.
- A prompt/answer containing a test credential is stored as `[REDACTED]` in `agent-memory.json`.

## 6. Security and telemetry

- No secret values in logs, status output, or artifacts (grep the smoke transcript for the key
  prefix).
- Sandbox hostile suite and agent safety suite are green in the release commit's CI.
- Health/status endpoints (where enabled) return expected shapes.

## 7. Rollback check

- Confirm the documented rollback works within 15 minutes on a staging deployment.
- Feature-flag rollback: `ScriptHost:Mode` back to `InProcess`; permission mode back to `Enforce`.

## 8. First week

- Track: session failures, worker start failures, permission denials, model errors/timeouts,
  retention purge results, and any secret-redaction misses.
- Open follow-ups; do not re-release a rollback without a re-gate.

## Exit criteria

All checks pass and the owner records the release decision and evidence; otherwise the release is a
no-go and the findings return to the backlog (no release claim without this record).
