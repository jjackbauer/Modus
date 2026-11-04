# WP-1.6 — ScriptHost execution-mode flag

**Status:** complete 2026-11-04; default remains `InProcess` (DEV-004).

## Delivered

- `BonesStrategyScriptHostOptions.WorkerPath` plus `WithWorkerClient`; doc comments state the trust boundary.
- `BonesHostOptions`: `ScriptHost:Mode` binding validated by the configuration binder (unknown values fail deterministically, no silent InProcess fallback); `ScriptHost:WorkerPath` bound.
- `WIP/Wip.Bones.Host/BonesScriptHostRuntime.cs`: owns the mode.
  - `InProcess`: logs a startup warning naming the trust boundary.
  - `Worker`: validates `TrustedOnly`, resolves `WorkerPath` (absolute, or relative to the host base directory; default `Wip.Sandbox.Worker.dll`), starts the worker, completes the handshake, and returns the host wired to the client. Missing binary, failed handshake, or `TrustedOnly=false` throw before any process is left behind.
  - Singleton runtime implements `IDisposable` and `IAsyncDisposable` so the container kills the worker on shutdown.
- `BonesHostServiceCollectionExtensions` registers the runtime and the hosted `BonesStrategyScriptHost`; `BonesHostApplication.Build` resolves the host eagerly so Worker misconfiguration fails at startup and the mode is logged before learning starts.
- `appsettings.json` gains `ScriptHost:WorkerPath` (empty = default).
- Docs: host README configuration rows + “Strategy execution sandbox (`ScriptHost:Mode`)” section; `docs/security/sandbox-threat-model.md` §9.

## Tests (10/10, `.tmp/wp16-check/wp16-flag.trx`)

- Options: no section defaults to InProcess/TrustedOnly; explicit Worker binds mode + path; explicit InProcess; case-insensitive `worker`; unknown mode fails deterministically with the offending value; `TrustedOnly=false` binds for the runtime guard.
- Runtime: InProcess logs the warning and still compiles in-process; Worker without `TrustedOnly` fails before starting a worker; Worker with a missing worker path fails with the path; Worker with the built worker compiles+executes through the worker, the host AppDomain gains no `BonesStrategyScript_*` assembly, the slot is `WorkerBonesPlayerSlot`, and the client is running until disposal.

## Host-process evidence (2026-11-04)

`dotnet run --project WIP/Wip.Bones.Host -c Release --no-build` with `BonesHost__ScriptHost__Mode=Worker`, `WorkerPath` set, `ModelProvider=stub`, `MaxGamesPerRun=1`:

- Logged `Script execution mode is Worker: strategies compile and run in the sandbox worker at …` and started listening on `http://127.0.0.1:18080`.
- First attempt without a writable `TEMP` failed closed at startup with `UnauthorizedAccessException` from the worker temp directory, proving there is no silent in-process fallback.
- The same line appeared twice during a run: the workflow runtime’s nested service provider creates a second script host/worker (WP-2.1). Recorded as a risk; resource-capped and killed with the host.
- The iteration stopped on the configured game budget (expected at `MaxGamesPerRun=1`); a separate `AggregateException` came from the Windows Event Log logger being denied in this environment, not from the worker path.

## Not claimed

- Default is not `Worker`; full G2 and worker deployment in release packaging (WP-6.2) are still required.
- Windows OS containment is not claimed.