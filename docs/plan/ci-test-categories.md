# CI test categories (Phase 0)

The plan asks for xUnit `Category` traits. At Phase 0 the gate groups projects; adding traits across 239 test files is deferred to WP-4.3 with the compliance matrix (DEV-002).

| Group | Projects | CI job |
|---|---|---|
| Build/architecture | `Wip.slnx`, `tests/Modus.Architecture.Tests` | `wip-build-*`, `wip-architecture` |
| Unit/contract | Abstractions, Artifacts.Local, Builder, Policy.LocalSafe, Tools.Shell, Validation.DotNet, Workspaces.Git, ShellHost | `wip-offline-tests` |
| Integration | Modus, Runtime, Shell | `wip-offline-tests` |
| Bones offline | Bones, Bones.ModelProviders.DeepSeek, Bones.Host, Bones.E2E | `wip-offline-tests` |
| E2E/Playwright (optional) | Bones.Web, Shell.E2E | `wip-optional.yml` |
| Live | none configured | future |
| Security | none until WP-1.5 | explicit placeholder |
