# Checklist Transition Proof - Wip.ShellHost Explicit Plugin Lifecycle Command Path

## Baseline Unchecked Source Text Evidence
- Baseline snapshot source: `.github/requirements/transition-proofs/baselines/checklist-item-wip-shellhost-explicit-plugin-lifecycle-command-path.unchecked.txt`.
- [ ] Introduce explicit plugin lifecycle command path (for example `plugins load` and `plugins unload`) that controls when plugin assemblies activate and when scheduled jobs begin [depends on startup gate]

## Checked Completion Evidence
- [x] Introduce explicit plugin lifecycle command path (for example `plugins load` and `plugins unload`) that controls when plugin assemblies activate and when scheduled jobs begin [depends on startup gate] [transition-proof: .github/requirements/transition-proofs/checklist-item-wip-shellhost-explicit-plugin-lifecycle-command-path-transition-proof-2026-05-25.md]

## Line Evidence
- Current checked checklist line appears at `.github/requirements/Wip.ShellHost.md:66`.
- Current completed-item evidence line appears at `.github/requirements/Wip.ShellHost.md:98`.

## Validation Commands
- `dotnet build src/Wip.ShellHost/Wip.ShellHost.csproj -v minimal`
- `dotnet test tests/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj --no-build -v minimal`
