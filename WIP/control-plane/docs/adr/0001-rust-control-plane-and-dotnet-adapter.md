# ADR 0001: Rust control plane with a narrow .NET adapter

## Status
Accepted.

## Context
The existing Wip.Factory is .NET 10 and owns execution, contract freezes, merge authorization, post-merge verification and serving. The requested human control plane must be a new Rust application. Rewriting the factory would violate its authority and discard governed behavior.

## Decision
Build the UI and server in Rust (Axum, Tokio, Leptos SSR/hydration). Add a separate .NET project, `Wip.Factory.ControlApi`, that references the factory assemblies and exposes a versioned `/v1` JSON API. The adapter is the only component that talks to `FactoryRuntime`; the Rust server is the only component that holds the adapter service token.

## Consequences
- The factory remains the single execution authority.
- The Rust side never reads journals, parses CLI text or mutates factory data directly.
- The adapter must be versioned and tested independently.
- Two processes are required locally; the startup script composes them.