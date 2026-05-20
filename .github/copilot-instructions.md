# Plugin Monolith Framework - Workspace Context for GitHub Copilot

## Project Purpose

This workspace hosts a reusable C#/.NET framework for modular monolith systems with plugin-driven extensibility.
The architecture emphasizes strict module boundaries, explicit plugin contracts, safe runtime registration, and test-first evolution.

## Core Architecture

The baseline architecture uses these roles:

- Core: shared domain contracts, plugin contracts, extension points
- Host: composition root, dependency injection wiring, plugin discovery and lifecycle orchestration
- Modules: bounded business capabilities implemented as internal application modules
- Plugins: optional feature packages that implement plugin contracts and register capabilities
- Infrastructure adapters: persistence, messaging, caching, and external system integrations

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

## Testing

- Default framework: xUnit.
- Test naming convention: MethodName_GivenScenario_ExpectedResult.
- Prioritize contract tests for plugin interfaces and integration tests for host-registration flows.

## Active Prompts and Workflows

| File | Purpose |
|---|---|
| .github/prompts/skill-falsify-claims.prompt.md | Verify or refute claims against source evidence |
| .github/prompts/skill-test-standards.prompt.md | Generate xUnit test plans from method behavior descriptions |
| .github/prompts/skill-impl-completeness.prompt.md | Audit source and target implementations and produce completeness checklist |
| .github/prompts/workflow-requirements-gathering.prompt.md | Produce requirements checklist and xUnit test plan from a pluggable analysis source |
| .github/prompts/rule-requirements-generic.prompt.md | Rule preset for generic requirements gathering in this workspace |
| .github/prompts/workflow-iterative-implementation.prompt.md | Implement one checklist item end-to-end |
| .github/prompts/workflow-codebase-exploration.prompt.md | Run journal-driven exploration cycle |
| .github/prompts/rule-architecture-analysis.prompt.md | Rule preset for architecture analysis in plugin monolith context |
| .github/prompts/skill-codebase-patterns.prompt.md | Rebuild codebase conventions reference from current source |
| .github/prompts/skill-plan-format-gate.prompt.md | Validate and self-heal plan document structure |
| .github/prompts/rule-no-primitive-obsession.instructions.md | Enforce typed value objects for semantic identifiers; reject raw string in contracts, DTOs, and domain types |

## Journal-Driven Distillation System

Use the journal and distillation flow to keep architecture and migration knowledge evidence-based:

- Journals: .github/journals/
- Distilled docs: .github/distilled/
- Metrics: .github/state/convergence-metrics.md
- Artifacts: .github/artifacts/

## Archived Migration Context

Lovelace migration-specific files are not active in this workspace.
Archived references are stored under:

- .github/prompts/archived-lovelace/
