# Checklist Transition Proof - Wip.ShellHost Repository Config and Effective-Config Display

## Baseline Unchecked Source Text Evidence
- [ ] Implement repository config support (`.wip/config.json`) and effective-config display command [depends on shell host and policy]

## Checked Completion Evidence
- [x] Implement repository config support (`.wip/config.json`) and effective-config display command [depends on shell host and policy] [transition-proof: .github/requirements/transition-proofs/checklist-item-wip-shellhost-repository-config-effective-config-transition-proof-2026-05-24.md]

## Implementation Evidence
- Repository config loading and effective host options merge behavior are implemented in `src/Wip.ShellHost/Hosting/WipShellHostOptions.cs`.
- Effective-config display command handling is implemented in `src/Wip.ShellHost/Hosting/WipShellHostFactory.cs`.
- Behavioral coverage for config merge and display command output is implemented in `tests/Wip.ShellHost.Tests`.