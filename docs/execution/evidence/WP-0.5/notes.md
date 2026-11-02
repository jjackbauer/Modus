# WP-0.5 — WIP architecture fitness tests

**Status:** done
**Date:** 2026-11-02
**Project:** `tests/Wip.Architecture.Tests` (added to `Wip.slnx`; 37 WIP projects + this test project = 38 solution projects)

## Rules implemented

| Rule id | Source | Forbidden |
|---|---|---|
| `abstractions-no-implementations` | `Wip.Abstractions` | any `Wip.*` project |
| `runtime-no-ui-or-bones` | `Wip.Runtime` | `Wip.Shell`, `Wip.ShellHost`, `Wip.Bones`, `Wip.Bones.*` |
| `shellhost-no-bones` | `Wip.ShellHost` | `Wip.Bones`, `Wip.Bones.*` |
| `builder-no-runtime-or-host` | `Wip.Builder` | `Wip.Runtime`, `Wip.ShellHost` |
| `bones-host-no-shell` | `Wip.Bones.Host` | `Wip.Shell`, `Wip.Shell.*` |
| `bones-domain-no-agents-host-web` | `Wip.Bones` | `Wip.Bones.Agents`, `Wip.Bones.Host`, `Wip.Bones.Web` |

The test parses every `WIP/**/*.csproj`, builds the project-reference graph, evaluates transitive reachability, and rejects unwaived violations. It also rejects **stale** waivers so a fixed edge cannot leave a permanent exception.

## Known violation and waiver

The working tree still has `Wip.ShellHost -> Wip.Bones.Agents` (and its transitive `Wip.ShellHost -> Wip.Bones`). This is the WP-2.6 coupling. Both edges are waived by [ADR-IMPL-07](../decisions/ADR-IMPL-07-temporary-shell-bones-coupling.md) until 2026-11-30; the test fails if the waiver becomes stale. No other rule is waived.

## Evidence

- [architecture-tests.txt](architecture-tests.txt) — build exit 0; **3/3 tests passed**.
- [injected-forbidden-reference.txt](injected-forbidden-reference.txt) — temporarily added `Wip.Runtime -> Wip.Shell`; test **failed** with two unwaived violations; source restored with **0 diff**.
- [architecture-restored.txt](architecture-restored.txt) — after restore, **3/3 passed**.
- [build-wip-with-architecture.txt](build-wip-with-architecture.txt) — `dotnet build Wip.slnx` exit 0, 0 errors, 113 warnings.
- [Wip.Architecture.Tests.trx](Wip.Architecture.Tests.trx) — TRX.

## Acceptance

- Forbidden reference fails: demonstrated by the injected edge (exit 1) and by the synthetic negative test.
- All current violations fixed or waived: one coupling waived by ADR-07; everything else clean.
- Tests run in CI: `wip-architecture` step added; remote run is part of `BLK-CI-REMOTE`.

## Residual

The check is a **project-reference graph** check; it does not detect reflection/assembly-load coupling. WP-2.7 (plugin ALC isolation) and Phase 2 can add an assembly-level test if needed.
