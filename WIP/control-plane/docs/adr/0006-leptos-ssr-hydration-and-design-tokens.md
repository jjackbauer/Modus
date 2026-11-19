# ADR 0006: Leptos SSR/hydration and a tokenized design system

## Status
Accepted.

## Context
The application must be entirely Rust, interactive, deep-linkable and visually coherent. A pure client-side SPA would have poor first paint and no server-rendered session shell; a pure SSR app would not support live operator interactions.

## Decision
- Leptos 0.8 with Axum integration, SSR shell and a wasm hydration entry point.
- The server renders the authenticated shell and page structure; the wasm client hydrates and attaches live resources, forms, dialogs and SSE.
- CSS custom properties define the dark-first token system; a light token set is provided and selectable at runtime.
- Shared components and centralized label/tone helpers prevent page-specific visual or semantic drift.
- A manual wasm pipeline is used (`cargo build` + `wasm-bindgen --target web`) and documented in the workspace README.

## Consequences
- First paint is useful and authenticated; interactions are local and responsive.
- Hydration mismatch is a real risk, so the SSR shell and client component tree must stay identical (verified with headless Chrome).
- The wasm bundle must be rebuilt and copied into `server/static/pkg`.