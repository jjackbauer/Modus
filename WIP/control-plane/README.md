# Rust Control Plane Workspace

This workspace is the human control surface. It contains:

| Crate | Purpose |
|---|---|
| `control-plane-domain` | Versioned DTOs, canonical requirement builder, safe Markdown renderer, labels and tones. |
| `control-plane-factory-client` | Typed HTTP client for the adapter, mutations with idempotency, resumable SSE parser. |
| `control-plane-web` | Leptos SSR/hydrated UI, design tokens, shell and all operator pages. |
| `control-plane-web-client` | `cdylib` wasm entry point for hydration. |
| `control-plane-server` | Axum application, sessions, CSRF, roles, instance registry, audit, SSE relay, static assets. |
| `control-plane-tests` | Cross-crate contract/transport tests. |

## Build

```powershell
cargo build --release -p control-plane-server
cargo build --release -p control-plane-web-client --target wasm32-unknown-unknown
wasm-bindgen --target web --no-typescript --out-dir server/static/pkg --out-name control_plane_web target/wasm32-unknown-unknown/release/control_plane_web_client.wasm
```

The server serves `server/static` from its compile-time manifest directory. The wasm bundle must be in `server/static/pkg`. Debug profiles set `debug = 0`; a debug wasm bundle is very large, so use release for normal operation.

## Configure and run

See the root `README.control-plane.md`. The server requires `CONTROL_PLANE_ADAPTER_URL`, `CONTROL_PLANE_ADAPTER_TOKEN` and `CONTROL_PLANE_DATA`.

## Tests

```powershell
cargo test --workspace
cargo check -p control-plane-web --features hydrate --target wasm32-unknown-unknown
```

The .NET adapter suite is the deep integration test: `dotnet test Wip.Factory.ControlApi.Tests/Wip.Factory.ControlApi.Tests.csproj` from the repository root. Browser evidence scripts live in `tests/browser` and require `playwright-core` plus Chrome.

## Design system

- Dark-first CSS custom properties in `server/static/app.css`; a light token set is included and can be selected with the top-bar Theme button.
- Shared components: `Panel`, `PanelActions`, `Chip`, `MetricCard`, `Notice`, `EmptyBlock`, `LoadingBlock`, `ErrorView`, `MiniBar`, `InfoRow`, `DigestLine`, `ActionDialog`.
- Tone functions are centralized in `domain/src/labels.rs` so the UI never invents a new factory state.
- Every page has explicit loading, empty, error, disconnected and stale states.

## Integration discipline

- The server holds the adapter service token. The browser only sees same-origin `/api/*`.
- No journal reads, file-system writes, CLI parsing or direct database access.
- Mutations are command endpoints; the UI never renders success before the adapter returns.
- SSE is resumable from `Last-Event-ID` and falls back to an explicit resync snapshot when the adapter cursor is ahead of a fresh page.