# Checklist Transition Proof: Shell and ShellHost Command Surface

Date: 2026-05-24
Checklist item:

Document Shell and ShellHost command surface including usage, failure messages, config precedence, and diagnostics bridge behavior [depends on Runtime lifecycle documentation]

## Baseline Unchecked Source Text Evidence

Exact unchecked checklist text captured in-workspace:

- [ ] Document Shell and ShellHost command surface including usage, failure messages, config precedence, and diagnostics bridge behavior [depends on Runtime lifecycle documentation]

Deterministic source reference:

- .github/requirements/transition-proofs/baselines/checklist-item-document-shell-and-shellhost-command-surface.unchecked.txt:1

Deterministic fingerprint:

- Baseline unchecked text SHA256: 947CBF240A92D5EEDD8CC52BD42EC35CB4C6105C9041FE01F1D00250F32471F7

## Checked Completion Evidence

The checked checklist text for this exact item is:

- [x] Document Shell and ShellHost command surface including usage, failure messages, config precedence, and diagnostics bridge behavior [depends on Runtime lifecycle documentation] [transition-proof: .github/requirements/transition-proofs/checklist-item-document-shell-and-shellhost-command-surface-transition-proof-2026-05-24.md]

Deterministic workspace evidence:

- Requirements file checked line: .github/requirements/WIP.Contributor-Readmes.md:86
- Requirements line capture: - [x] Document Shell and ShellHost command surface including usage, failure messages, config precedence, and diagnostics bridge behavior [depends on Runtime lifecycle documentation] [transition-proof: .github/requirements/transition-proofs/checklist-item-document-shell-and-shellhost-command-surface-transition-proof-2026-05-24.md]
- Requirements SHA256: A9A1A9D7BB545BD1B55F8ACE57B4F020C5192D072F2B04108D4D4EE10BD64B79

Supporting implementation and tests for this item:

- src/Wip.Shell/README.md documents shell command usage, deterministic failure messages, and diagnostics bridge behavior.
- src/Wip.ShellHost/README.md documents host command surface and configuration precedence.
- tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs contains behavior-proof tests:
  - ShellReadme_GivenHelpCommand_OutputListsSupportedCommandsIncludingConfigAndDiagnostics
  - ShellReadme_GivenInvalidTransitionSyntax_UsageMessageReturnedWithoutStateMutation
  - ShellHostReadme_GivenConfigFileAndCliPluginsPath_CliOverrideWinsInEffectiveConfigurationOutput
  - ShellReadme_GivenUnknownCommand_DeterministicUnknownCommandMessageIncludesHelpHint
  - ShellReadme_GivenDiagnosticsCommands_DiagnosticsBridgeOutputIsSurfaced
