# WP-2.6 — Shell/Bones decoupling (done)

**Status:** done 2026-11-04.

## Change

- The `WIP_BONES_E2E` shell-host registration (Bones workflow, stub model providers, match feed and
  artifact store) moved out of `Wip.ShellHost` into a new **test-only host project**
  `WIP/Wip.Bones.E2E.Host` (`Program.cs` + `BonesE2ERegistration.cs`). The host mirrors the
  production shell host startup and passes its registration through the new
  `WipShellHostFactory.CreateDefault(..., Action<WipBuilder>? configure)` hook.
- `Wip.ShellHost` no longer references `Wip.Bones.Agents` (or any `Wip.Bones.*` project); its csproj
  keeps only Modus/Shell/Runtime/Abstractions/Artifacts/Builder/Core references.
- The `BonesLearningShellE2EHarnessTests` process driver now launches the test-only host assembly.
- `Wip.slnx` contains the new project (42 WIP projects).
- ADR-IMPL-07 is **Superseded**; both `shellhost-no-bones` waivers were removed from
  `tests/Wip.Architecture.Tests/architecture-waivers.json`, and the architecture graph count moved
  from 41 to 42. The architecture test fails if the edge returns or if a waiver goes stale.

## Verification

| Check | Result |
|---|---|
| Architecture suite | 4/4 passed (project graph 42, no unwaived violations, no stale waivers) |
| Wip.ShellHost.Tests | 47/47 passed |
| Wip.Bones.E2E.Tests | 10 total / 3 passed / 7 failed — the same seven pre-existing waived failures; the three process-level tests pass, proving the new host boots and runs the transcript |
| Solution build (Release) | green |

## Follow-up

None required for G4. The `WIP_BONES_E2E` environment variable remains only inside the test host
and the E2E harness.