# Deployment notes

## Supported shape

The alpha supports a single-host, local-first deployment: the Rust server and the .NET adapter on the same trusted machine, loopback only. This is intentional. The adapter is not an internet-facing API.

## Production-like local deployment

1. Run the adapter as a dedicated local user with read/write access only to the target repository and factory control root.
2. Put the adapter service token in the server process environment. Do not put it in the repository, a URL, browser storage or logs.
3. Run the Rust server as a separate local user. Its data directory must not overlap the factory control root or target repository.
4. Put a trusted TLS reverse proxy in front of the Rust server if remote operators need access. Set `CONTROL_PLANE_TRUSTED_HTTPS=true` only when the proxy is trusted and forwards the original host correctly. The server still requires HttpOnly cookies and CSRF.
5. Restrict the adapter port to the server process; loopback binding is the default.
6. Protect `CONTROL_PLANE_DATA` as operator-sensitive: it contains password hashes, sessions and audit.
7. Back up the factory control root and target repository using the factory's normal operational procedure. The control plane does not own those backups.

## Startup and shutdown

Use `tools/start-control-plane.ps1` and `tools/stop-control-plane.ps1`. The script composes the two processes and records their process IDs in the data root. The Rust server stops accepting work on shutdown; the factory still owns in-flight dispatch and serving.

## Health

- Adapter liveness: `GET http://127.0.0.1:5099/v1/live` (no credential).
- Adapter authenticated health: `GET /v1/health` with the service token.
- UI: `GET /login` returns 200; authenticated pages require a session.
- The UI shows adapter health, torn journal records and dispatch state; it never claims the factory is healthy if the adapter is unreachable.

## Security boundaries

- The browser cannot reach the adapter directly.
- No journal access, CLI parsing, shared database or filesystem write from Rust.
- All mutations are audited with actor, role, action, subject, result, correlation and time.
- Remaining alpha limitation: CSP includes `'unsafe-inline'` for Leptos hydration scripts; nonces are present but inline scripts are not yet eliminated.