# Codebase Patterns Reference

> Usage: include this reference in prompts that need implementation and test conventions.
> This is a reference-only document, not a runnable prompt.

---

## 1. Project Structure

| Convention | Baseline Rule |
|---|---|
| Layout | One project per bounded context or technical role (Core, Host, Module, Adapter, Plugin) |
| Test mirroring | Each production project has a matching test project with .Tests suffix |
| Target framework | net10.0 unless a module needs lower compatibility |
| Nullable | Enable nullable reference types |
| Implicit usings | Enabled unless project-specific constraints require explicit usings |
| Test framework | xUnit with Microsoft.NET.Test.Sdk and coverlet collector |

---

## 2. Dependency and Composition Model

- Dependency direction flows toward abstractions and contracts.
- Core contains contracts and shared domain primitives.
- Host owns composition root and runtime wiring.
- Modules and plugins depend on Core contracts, not on host internals.
- Adapters implement infrastructure boundaries and remain replaceable.

---

## 3. Module and Plugin Contracts

- Define explicit interfaces for plugin capabilities and lifecycle hooks.
- Keep contracts backward-compatible when possible; version breaking changes intentionally.
- Validate plugin metadata and dependencies before activation.
- Prefer constructor injection and explicit options binding over service location.

---

## 4. Error Handling and Thread Safety

- Place guard clauses at method start.
- Use typed exceptions aligned to domain and contract boundaries.
- Keep shared mutable state minimal and explicit.
- Use lock or Interlocked where shared mutable state cannot be avoided.
- Prefer immutable DTOs for cross-module/plugin communication.

---

## 5. Test Standards

- Test names follow MethodName_GivenScenario_ExpectedResult.
- Use Fact for single-scenario tests and Theory with InlineData for data-driven tests.
- Cover contract behavior, edge cases, and failure paths.
- Add integration tests for host startup and plugin registration lifecycle.

---

## 6. Anti-Patterns to Avoid

- Cross-module concrete coupling that bypasses contracts.
- Plugin implementations depending on host internals.
- Hidden runtime registration side effects.
- Silent failure of plugin load/activation without explicit diagnostics.
- Shipping behavior changes without contract and regression tests.

---

Regenerated baseline for plugin-monolith architecture framework.
