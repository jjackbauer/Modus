---
agent: plan
description: Architecture analysis rule - pre-configures codebase exploration for modular monolith and plugin architecture documentation.
---

#file:.github/prompts/workflow-codebase-exploration.prompt.md

# Rule: Architecture Analysis

## Purpose

Pre-configured specialization of workflow-codebase-exploration for architecture and design documentation
in modular monolith systems with plugin extension points.

## Input (supplied by caller)

```
Scope:  <Optional: module or concern to focus on, for example Core, Host, Plugin contracts, or all (default)>
```

## Bindings

Follow workflow-codebase-exploration with the parameter bindings below.

**ExplorationMode**
> "broad" - architecture analysis requires wide coverage before conclusions are drawn.

**DistillIfNew**
> "auto" - distill automatically when at least 3 new observations are produced or any claim is validated.

**TargetDocs**
> "system-overview.md,module-map.md,domain-concepts.md,dependencies.md,runtime-flows.md,invariants-and-risks.md"

**Objective** (derived from Scope)
> If Scope is all or omitted: "Explore the full solution to document module responsibilities, plugin contracts,
> host composition boundaries, and dependency constraints."
>
> If Scope names a specific concern: "Explore <Scope> and document its public API, internal structure,
> runtime lifecycle, and dependency contracts."

## Focus Areas for Architecture Analysis

When applying skill-journal-observe, prioritize observations that inform:

1. Module responsibilities and ownership boundaries.
2. Plugin contract surfaces and versioning strategy.
3. Dependency contracts across Core, Host, Modules, and Adapters.
4. Runtime flows: discovery, validation, registration, activation, and teardown.
5. Isolation and failure containment for plugin faults.
6. Architectural constraints enforced by code and tests.

When applying skill-journal-validate, prioritize boundary claims:

- Host is the only composition root and plugin registrar.
- Plugins depend on contracts, not host internals.
- Cross-module interaction uses explicit abstractions, not direct concrete coupling.
- Startup pipeline applies deterministic plugin validation before activation.

## Stopping Note

Run this rule repeatedly until skill-convergence-metrics reports:
- Module coverage at 100%
- Distilled documents system-overview.md, module-map.md, domain-concepts.md,
  dependencies.md, runtime-flows.md, and invariants-and-risks.md have confidence at least Medium.
