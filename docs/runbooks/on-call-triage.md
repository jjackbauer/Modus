# On-call triage runbook (pre-release)

**Status:** pre-release handover note. No release is claimed; C1 is unclaimed and G2 is
conditional/trusted-only.

## First 5 minutes

1. Identify the failure surface: shell, host, worker, model provider, plugin load, or CI gate.
2. Read the session state:
   - `.wip/sessions/<id>/session-state.json`
   - `.wip/sessions/<id>/artifacts/` (including `agent-memory.json`)
   - event journal `.wip/sessions/<id>/event-journal.ndjson` where present.
3. `retention status` for artifact growth; never purge during an incident without recording it.
4. Check child processes: a worker must not outlive the host (`ScriptHost:Mode` Worker).

## Common failures

| Symptom | First action |
|---|---|
| Host fails at startup (sandbox) | Check `ScriptHost:Mode`/worker path; the host fails closed by design — fix config, do not bypass |
| Worker handshake failure | Check the worker path and permissions; restart the host; a worker failure is a fail-closed condition |
| Model 401/403/503 | Check key presence (never print it) and provider status; transient retries are bounded |
| Permission denied unexpectedly | Check the grant (`*`, bare permission, `pluginId:permission`) and the mode (Enforce vs Audit) |
| Agent loop runaway | Use `abort`/cancel; the executor enforces step/token/time limits |
| Artifacts leaking secrets | Run the redaction patterns against the artifact; add a pattern and re-purge per policy |
| Retention did not delete | Files may be locked; the purge reports `retained`; retry after releasing locks |

## Escalation

- Security-relevant finding: stop, preserve evidence, do not publish; route to the human security
  review and record it (no external sign-off is claimed until obtained).
- Release-blocking defect: no release claim; record it in `docs/execution/journal.md` and return to
  the G5 packet.

## Evidence to collect

Exact command, exit code, timestamps, session id, TRX/log paths, and whether a secret appeared
(never include the value). Attach to the incident and the journal.
