# Control Plane Implementation Report

## Status

**ENGINEERING_COMPLETE** for the supported local-alpha engineering scope.

This report is an engineering handoff, not an independent acceptance decision. Independent evaluator acceptance remains evaluator-owned and is not inferred from any test, screenshot or release record below. The application itself repeats that boundary on the quality and release surfaces.

The implementation is local-first, fully Rust for the new application (Axum/Tokio/Leptos), and integrates with the existing .NET 10 Wip.Factory through a new versioned adapter. The factory remains the execution authority.

## What was built

- Rust workspace at `control-plane/`:
  - `control-plane-domain` — versioned camelCase DTOs, canonical requirement Markdown builder and validator, safe Markdown renderer, centralized labels/tones.
  - `control-plane-factory-client` — typed adapter client, typed errors, idempotency, resumable SSE parser.
  - `control-plane-web` — Leptos 0.8 SSR/hydrated application, design tokens, shell, pages and dialogs.
  - `control-plane-web-client` — wasm hydration entry point.
  - `control-plane-server` — Axum server, sessions, CSRF, roles, instances, audit, SSE gateway, SSR shell, static assets.
  - `control-plane-tests` — Rust HTTP contract/transport tests.
- .NET adapter at `Wip.Factory.ControlApi/` with 11 real integration tests.
- Factory-side narrow extensions: profile listing, template listing, tool-invocation evidence capture in agent traces, duplicate contract-approval guard, served URL in serve status.
- Windows build/start/stop scripts in `tools/`.
- OpenAPI contract at `control-plane/openapi/control-plane.v1.json`.
- ADRs in `control-plane/docs/adr/`.
- Reproduction and deployment notes in `control-plane/docs/`.

## Requirement-to-interface map

| Requirement | Delivered interface / evidence |
|---|---|
| Rust backend and frontend | Axum/Tokio server + Leptos SSR/hydrated wasm. Verified with headless Chrome. |
| No rewrite of .NET factory | Adapter references the factory assemblies and calls `FactoryRuntime`. No journal access or CLI parsing from Rust. |
| Work intake with actual factory parsing | `/work` form; `POST /v1/requirement/preview`; canonical Markdown preview; explicit acceptance contract; approved-template picker; target instance shown. |
| Work queue with table, kanban, priority, filters, saved views | `/work`; three views; state/indicator/search/sort; local saved views. |
| Work detail and lifecycle | `/work/{id}`; summary, stage rail, contract, review, release, serving, agent sessions, tool evidence, diff, lineage, events, planning, legal actions. |
| Contract approval and rejection | Exact-digest dialog; adapter binds the proposal digest; non-executable proposals are refused with a typed explanation. |
| Merge approval | Dialog requires typing the exact accepted review digest; adapter binds review evidence. |
| Dispatch | Header/action command; adapter owns one dispatch cycle; no browser-owned execution. |
| Agent profiles, sessions, tool evidence | `/agents`, `/agents/{definitionId}`; active profile, immutable versions, promote/rollback, session/tool evidence. |
| Quality evidence and independent acceptance boundary | `/quality`; pre-merge matrix; explicit "independent acceptance belongs to the evaluator" notice. |
| Releases and supervised serving | `/releases`, `/releases/{id}`; verified release evidence, served URL only while active, Stop serve. |
| Analytics and definitions | `/analytics`; measured/not-measured metrics; definitions; failure, stage, token series. |
| Improvement history | `/improvement`; profile versions and adaptation events; proposal vs applied explained; no second engine. |
| Settings, templates, users, audit | `/settings`; read-only factory configuration, instance registry, template register/approve, user create/role update, password change, server and adapter audit. |
| Roles, CSRF, sessions | Server-enforced viewer/operator/admin; Argon2id; HttpOnly Strict cookie; CSRF + origin checks; adapter re-checks roles. |
| Resumable activity | SSE with durable sequence cursor, Last-Event-ID, heartbeats, explicit resync, reconnect/stale/disconnected UI. |
| Local-first Windows startup | `tools/start-control-plane.ps1`; loopback binds; generated service token; generated first-admin password. |
| OpenAPI, ADRs, reproduction, deployment | Listed above. |
| Report | This file. |

## Verification evidence

Commands actually run on 2026-11-19 in `F:\dev\Modus\WIP`:

| Suite / check | Result |
|---|---|
| `dotnet test Wip.Factory.ControlApi.Tests` | **11 passed**, 0 failed. Includes real Kestrel adapter, real git target, full release + supervised serve, typed errors, idempotency, roles, stale review rejection, template policy, non-executable contract refusal. |
| `dotnet test Wip.Factory.Core.Tests` | **21 passed**, 0 failed. |
| `dotnet test Wip.Factory.Agents.Tests` | **69 passed**, 0 failed. |
| `dotnet test Wip.Factory.Host.Tests` | **216 passed**, 0 failed. |
| `cargo test --workspace` | **18 passed**, 0 failed (domain 9, factory client 2, server 5, cross-crate contract 2). |
| `cargo check -p control-plane-web --features hydrate --target wasm32-unknown-unknown` | Passed. |
| Release builds | Server and wasm released; wasm bundle 23,042,550 bytes (~23 MB). |
| Browser tour: 12 authenticated pages | No console/page errors; no placeholder pages; screenshots captured. |
| Browser lifecycle `WORK-cea4804b` | Intake → dispatch → contract approval → dispatch → accepted review → merge authorization → dispatch → released → verified serve → preview. Final state `OperationallyValidated`; release `9798890c14be2db6b4f57d48d88be3fc55d229c8`; review digest `sha256:551629c88521606358c42517cffb231be9fcf447c33c30ef25569160b63fe2f4`. |
| UI checks | Attention count 1; blocked/failed metric 1; light theme toggle; breadcrumbs; single serve action; legal targets; planning save; template picker; historical playback. |
| Hydration | Headless Chrome loaded and hydrated the real pages; initial hydration mismatch (skip link outside the Leptos tree) was found and fixed. |

Evidence images are in `control-plane/docs/evidence/`. The lifecycle screenshot was captured while the factory-owned process was serving; the serve window was stopped after evidence collection.

## Architecture summary

```
Leptos UI (browser, no adapter token)
  → Rust Axum server (session, CSRF, roles, audit, instances, SSR, SSE relay)
    → .NET ControlApi adapter (/v1, service token, role/actor, idempotency, typed errors)
      → FactoryRuntime / governed factory services
        → append-only factory journal, target repository, builders, verifier, serve supervisor
```

Key invariants:

- No Rust process reads or writes factory journal files.
- No CLI output parsing.
- The UI shows authoritative factory states separately from adapter-derived indicators.
- No transition is animated or assumed; success is rendered only after an adapter response.
- Planning metadata is separate and marked `authoritative: false`.
- Contract and merge approvals bind exact digests.
- The adapter, not the browser, owns dispatch and serving.

## Security posture

- Argon2id password hashes; server-side sessions; HttpOnly/SameSite=Strict cookie.
- CSRF token + same-origin checks on mutations.
- Viewer, operator, admin roles; adapter independently enforces read/write/admin.
- Service token never reaches JavaScript; settings redact it.
- File viewer is limited to changed files from the reviewed revision, with a size bound.
- Markdown is escaped; raw HTML and unsafe schemes are rejected.
- Auth failures and mutations are audited with actor/role/subject/result/correlation.
- Loopback bind is the default; remote access requires an explicit trusted-HTTPS opt-in and a TLS proxy.
- Remaining alpha limitation: CSP currently includes `'unsafe-inline'` for Leptos hydration scripts; nonces are emitted, but inline scripts are not eliminated yet.

## Deliberate exclusions / limitations

These are not silently faked:

1. **No pause/resume/cancel/rollback of work items.** The factory has no governed work-item command for these. Retry is implemented as a new dispatch cycle. The adapter capabilities report `cancellation: false`, `pauseResume: false`, `scheduling: false`.
2. **No scheduling.** No factory primitive exists; the UI does not present a scheduler.
3. **No attachments upload.** References/links and template references are supported; arbitrary file upload is not exposed.
4. **One repository per adapter instance.** The intake form shows the bound instance and target repository; selecting a different repository means registering a different adapter instance. The UI does not claim cross-repository scheduling.
5. **Session store is in-memory.** Restarting the Rust server signs users out (durable factory work is unaffected).
6. **Demo runner.** The deterministic demo runner is an engineering fixture, not model-backed acceptance. Real model runs are available through the adapter when credentials are configured.
7. **No browser-owned close.** Closing the browser does not stop dispatch or serving; the factory owns those processes.
8. **No independent acceptance.** Release evidence explicitly separates engineering verification from evaluator acceptance.
9. **Browser evidence scripts require an existing authenticated session.** They are evidence drivers, not a CI authentication bypass.
10. **Serve-window state transitions are asynchronous.** The UI and tests poll the durable state rather than assuming an immediate transition.
11. **Wasm is a release artifact.** The debug bundle is very large; the startup script builds release. The server serves `server/static/pkg`.

## Reproduction

See `control-plane/docs/REPRODUCTION.md` and `control-plane/docs/DEPLOYMENT.md`. Quick path:

```powershell
powershell -File tools/start-control-plane.ps1
```

Then read the first-admin password path printed by the script and open <http://127.0.0.1:3000>.

## Residual engineering gaps

- Automated Rust coverage is strongest at domain, client transport, server auth helpers and cross-crate contract level. Full browser auth/CSRF and SSE reconnect are covered by the browser evidence flow and manual fault checks, not yet a fully automated Rust E2E suite.
- Accessibility was implemented with semantic landmarks, labels and keyboard-native controls but not audited with an automated accessibility tool.
- Performance was not benchmarked under large journals or many concurrent operators; read models are bounded and the UI is event-driven.
- The OpenAPI file documents the adapter contract; it is hand-maintained and should be validated against a generated schema in a future change.

## Final statement

The control plane is operational against a real factory, demonstrates the complete governed lifecycle through its UI, preserves the factory's authority, and exposes its uncertainty honestly. It does not claim independent acceptance.