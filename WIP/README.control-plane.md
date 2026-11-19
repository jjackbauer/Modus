# Wip.Factory Control Plane

A local-first Rust web application that operates the existing **Wip.Factory** software factory. The .NET factory remains the execution authority. This application is the human control plane: it accepts work, exposes governed approvals, starts dispatch, shows evidence, supervises serving, and explains what the factory is doing. It does not execute agents, merge code, or edit factory journals.

## Status

Engineering implementation is complete for the supported local alpha. It has been verified against a real .NET adapter, a real `FactoryRuntime`, a real git target, the deterministic engineering runner, and the factory serve supervisor. It has **not** received independent evaluator acceptance; the UI and report say so explicitly where release evidence is shown.

## Prerequisites

- Windows 10/11 (the supported local path), .NET SDK 10, Rust 1.88+, `wasm32-unknown-unknown`, `wasm-bindgen-cli` 0.2.130, Git, Chrome/Edge for optional browser checks.
- A target git repository. An empty git repository is sufficient for the deterministic demo runner.

## Quick start (Windows)

```powershell
powershell -File tools/start-control-plane.ps1
```

The script builds the adapter and the Rust server, creates a demo target repository on first run, starts the adapter on `127.0.0.1:5099`, and serves this UI at <http://127.0.0.1:3000>. The first-admin bootstrap password is written to `control-plane-data/runtime/bootstrap-admin.txt`; it is never printed by the server.

Useful options:

```powershell
powershell -File tools/start-control-plane.ps1 `
  -TargetRepository F:\work\my-target `
  -Bind 127.0.0.1:3100 `
  -AdapterBind 127.0.0.1:5199 `
  -Implementation demo
```

Stop the two processes with `tools/stop-control-plane.ps1`.

## Manual run

1. Start the adapter:

```powershell
dotnet run --project Wip.Factory.ControlApi -- `
  --repo F:\work\my-target `
  --control-root F:\work\my-control `
  --token <service-token> `
  --implementation demo `
  --bind 127.0.0.1:5099
```

2. Build and serve the Rust application:

```powershell
cd control-plane
cargo build --release -p control-plane-server
cargo build --release -p control-plane-web-client --target wasm32-unknown-unknown
wasm-bindgen --target web --no-typescript --out-dir server/static/pkg --out-name control_plane_web target/wasm32-unknown-unknown/release/control_plane_web_client.wasm
$env:CONTROL_PLANE_ADAPTER_URL = "http://127.0.0.1:5099"
$env:CONTROL_PLANE_ADAPTER_TOKEN = "<service-token>"
$env:CONTROL_PLANE_DATA = "F:\work\cp-runtime"
$env:CONTROL_PLANE_BIND = "127.0.0.1:3000"
cargo run --release -p control-plane-server
```

Open <http://127.0.0.1:3000>. On first start the server creates an `admin` account and writes its generated password to `CONTROL_PLANE_DATA/bootstrap-admin.txt`.

## Configuration

| Variable | Required | Meaning |
|---|---|---|
| `CONTROL_PLANE_ADAPTER_URL` | yes | Base URL of the loopback adapter. |
| `CONTROL_PLANE_ADAPTER_TOKEN` | yes | Service token, at least 24 characters. Never sent to the browser. |
| `CONTROL_PLANE_DATA` | yes | Session, user, instance and audit data. Must not overlap the factory control root. |
| `CONTROL_PLANE_BIND` | no | Default `127.0.0.1:3000`. Remote binds require `CONTROL_PLANE_TRUSTED_HTTPS=true` and a trusted TLS reverse proxy. |
| `CONTROL_PLANE_ADMIN_PASSWORD` | no | Initial password only; otherwise generated and written once. |
| `CONTROL_PLANE_TRUSTED_HTTPS` | no | Explicit opt-in for a non-loopback bind. |
| `CONTROL_PLANE_PUBLIC_ORIGIN` | no | Comma-separated trusted browser origins for a TLS/proxy deployment, e.g. `https://factory.example.test`. The direct Host origin remains allowed. |
| `CONTROL_PLANE_LOG` | no | Tracing filter, default `info`. |

The adapter is loopback-first. Do not bind it to a public interface. Use a TLS reverse proxy for any remote access; the application does not terminate TLS itself.

## Architecture

```
Browser (Leptos SSR + hydrated DOM)
        │  same-origin HTTPS, HttpOnly session cookie, CSRF header
        ▼
Rust Axum server  ──  role checks, session/CSRF/audit, SSE relay, HTML shell
        │  server-side service token + role/actor headers
        ▼
.NET ControlApi adapter  ──  versioned /v1 JSON, idempotency, typed errors
        │  governed FactoryRuntime calls
        ▼
Wip.Factory  ──  append-only journal, contract freeze, review evidence,
                 merge authorization, post-merge verification, serve supervisor
```

- The browser never receives the adapter service token.
- Factory journals are never read or modified by the Rust code. The adapter reads through `FactoryRuntime` and `LocalFactoryStore` read APIs only.
- CLI text is never parsed. All integration is through typed contracts.
- UI indicators such as **AWAITING CONTRACT APPROVAL** and **VERIFYING** are derived by the adapter; `FactoryWorkState` is not extended.
- Planning metadata is stored separately by the adapter and is marked `authoritative: false`.

See [control-plane/README.md](control-plane/README.md), the OpenAPI contract at [control-plane/openapi/control-plane.v1.json](control-plane/openapi/control-plane.v1.json), and the ADRs in [control-plane/docs/adr](control-plane/docs/adr).

## Security

- Argon2id password hashing; server-side sessions; `HttpOnly`, `SameSite=Strict` cookies.
- CSRF double-submit token plus `Origin`/`Sec-Fetch-Site` checks for every mutation.
- Roles: viewer (read-only), operator (mutations), admin (identity and instance management). The adapter re-checks every role and every object.
- Exact-digest confirmation for contract approval, merge authorization, template approval and profile promotion.
- Optimistic version checks for planning metadata.
- Markdown is rendered by a restricted, escaped renderer; no raw HTML is accepted.
- File browsing is limited to changed files at the reviewed revision, bounded by size.
- Adapter audit and server audit record actor, role, action, subject, result, correlation and time.
- No secret is rendered into HTML or JavaScript.
- Remaining alpha limitation: CSP includes `'unsafe-inline'` because Leptos emits inline hydration scripts. Nonces are present; removing inline scripts is future work.

## Verification

```powershell
# .NET adapter integration (real Kestrel, real FactoryRuntime, real git repository)
dotnet test Wip.Factory.ControlApi.Tests/Wip.Factory.ControlApi.Tests.csproj

# Existing factory suites
dotnet test Wip.Factory.sln -c Debug

# Rust domain/client/server tests
cd control-plane
cargo test --workspace

# Type-check SSR and wasm
cargo check -p control-plane-web --features hydrate --target wasm32-unknown-unknown
```

The browser flow used for evidence is in `control-plane/tests/browser/` (Playwright core is optional; it launches an installed Chrome). See `CONTROL_PLANE_IMPLEMENTATION_REPORT.md` for the exact evidence and limitations.

## Deliberate boundaries

- No pause, cancel, scheduling, rollback or restart control is offered unless the factory exposes a governed command. Retry is implemented as a new dispatch cycle. Scheduling has no factory primitive and is shown as unsupported rather than simulated.
- The deterministic demo runner is an engineering fixture, not model-backed acceptance. Real model runs are available through the adapter's model runner when credentials are configured, but independent acceptance remains evaluator-owned.
- The adapter process owns dispatch. Closing the browser does not stop dispatch. The factory owns agent and serving processes.
- Local-first, single-operator-dominant deployment is the supported deployment shape.