# Plugin Monolith Framework - Agent Workspace Context

## Project Purpose

This workspace hosts a reusable C#/.NET framework for modular monolith systems with plugin-driven extensibility.
The architecture emphasizes strict module boundaries, explicit plugin contracts, safe runtime registration, and test-first evolution.

## Core Architecture

- **Core**: shared domain contracts, plugin contracts, extension points
- **Host**: composition root, dependency injection wiring, plugin discovery and lifecycle orchestration
- **Modules**: bounded business capabilities implemented as internal application modules
- **Plugins**: optional feature packages that implement plugin contracts and register capabilities
- **Infrastructure adapters**: persistence, messaging, caching, and external system integrations

## Architectural Rules

1. Core contracts are stable and versioned; plugin implementations depend on contracts, not host internals.
2. Modules communicate through explicit abstractions and events, never by reaching into each other's internals.
3. Host is the only layer that composes runtime dependencies and plugin loading.
4. Plugins must be discoverable, validated, and registered through a deterministic startup pipeline.
5. Public APIs and plugin contracts must be covered by automated e2e integration tests before behavioral changes are accepted.

## Naming and Style

- Use English identifiers and .NET naming conventions.
- Prefer explicit interfaces for plugin capabilities and lifecycle hooks.
- Keep project and namespace names aligned to bounded contexts and plugin concerns.
- **C# semantic identifiers**: use typed value objects (`PluginId`, `OperationName`, etc.) - never raw `string` in contracts, DTOs, or domain types. Full rule: `harness/rules/no-primitive-obsession.md`.

## Testing

- Default framework: xUnit.
- Test naming convention: `MethodName_GivenScenario_ExpectedResult`.
- Prioritize contract tests for plugin interfaces and integration tests for host-registration flows.

## Harness (single source of truth)

All agent capabilities live under `harness/`:

| Directory | Purpose |
|---|---|
| `harness/skills/` | Atomic capabilities |
| `harness/workflows/` | Multi-step orchestrators |
| `harness/rules/` | Policy presets and always-on rules |
| `harness/schemas/` | Reference-only schemas |
| `harness/requirements/` | Active requirements docs and new transition-proofs/evidence |

**Generated stubs** (never hand-edit): `.cursor/commands/`, `.claude/commands/`, `.github/prompts/*.prompt.md`.

**Hand-authored exceptions**: `.cursor/rules/*.mdc`, `.claude/agents/*.md`.

### Add or modify a capability

1. Edit the canonical body at `harness/<kind>/<name>.md`.
2. Run `dotnet run --project tools/HarnessSync -- generate`.
3. Run `dotnet run --project tools/HarnessSync -- --check` and commit only when it passes.

### Invoke capabilities

Use `/name` slash commands in Cursor, Claude Code, or GitHub Copilot (e.g. `/falsify-claims`, `/requirements-gathering`).

Full maintenance rule: `harness/rules/harness-maintenance.md`.

## Evidence and exploration

- **Active requirements/evidence**: `harness/requirements/`
- **Frozen history**: `.github/requirements/transition-proofs/` (immutable)
- **Archived exploration**: `harness/archive/`
- **Metrics**: `.github/state/convergence-metrics.md`

## Archived migration context

Lovelace migration-specific files are not active. Archived references: `harness/archive/lovelace/`.