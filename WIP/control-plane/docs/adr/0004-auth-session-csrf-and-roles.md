# ADR 0004: Server-enforced sessions, CSRF and roles

## Status
Accepted.

## Context
The control plane is local-first, but a browser session must still be protected from cross-site actions and accidental privilege escalation. The adapter service token must never reach JavaScript.

## Decision
- Argon2id password hashes; server-side session store; HttpOnly, SameSite=Strict cookies.
- Every mutation requires a per-session CSRF token plus same-origin checks.
- Roles are viewer, operator and admin. The Rust server rejects writes from viewers and identity/instance changes from non-admins. It forwards a role/actor assertion to the adapter, which re-applies authorization.
- The adapter service token is server-only and redacted from settings.
- Remote binds require an explicit HTTPS-proxy opt-in; loopback is the default.

## Consequences
- The browser cannot call the adapter directly.
- Session loss on server restart is acceptable for the alpha; users sign in again.
- Any future remote deployment must terminate TLS at a trusted proxy.