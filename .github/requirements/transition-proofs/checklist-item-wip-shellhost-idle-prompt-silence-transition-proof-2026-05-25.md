# Checklist Transition Proof - Wip.ShellHost Idle Prompt Silence

## Baseline Unchecked Source Text Evidence
- User-supplied unchecked source text: [.github/requirements/transition-proofs/baselines/checklist-item-wip-shellhost-idle-prompt-silence.unchecked.txt](../transition-proofs/baselines/checklist-item-wip-shellhost-idle-prompt-silence.unchecked.txt)
- Exact unchecked checklist line:

	`- [ ] Guarantee idle prompt silence: while no plugin-load command has been executed, recurring scheduled operations must not emit runtime output to host console [depends on explicit lifecycle command path] [mandatory - no unsolicited runtime output]`

## Checked Completion Evidence
- Exact current checked checklist line:

	`- [x] Guarantee idle prompt silence: while no plugin-load command has been executed, recurring scheduled operations must not emit runtime output to host console [depends on explicit lifecycle command path] [mandatory - no unsolicited runtime output] [transition-proof: .github/requirements/transition-proofs/checklist-item-wip-shellhost-idle-prompt-silence-transition-proof-2026-05-25.md]`

- Checked checklist source: [.github/requirements/Wip.ShellHost.md#L67](../Wip.ShellHost.md#L67)
- Functional tests proving the behavior:
	- [IdlePrompt_GivenNoExplicitLoad_ExpectedZeroRecurringPluginOutputsWithinBoundedWindow](../../../tests/Wip.ShellHost.Tests/Hosting/WipShellHostPluginDiagnosticsTests.cs#L210)
	- [IdlePrompt_GivenExplicitLoad_ExpectedRecurringOutputsAppearOnlyAfterLoadCommand](../../../tests/Wip.ShellHost.Tests/Hosting/WipShellHostPluginDiagnosticsTests.cs#L230)
- Current runtime members proving the gate:
	- [WipShellHost.RunAsync](../../../src/Wip.ShellHost/Hosting/WipShellHost.cs#L18)
	- [WipShellCommandLoop.HandlePluginsLoadAsync](../../../src/Wip.Shell/Interactive/WipShellCommandLoop.cs#L235)

## Line Evidence
- Current checked checklist line appears at [.github/requirements/Wip.ShellHost.md#L67](../Wip.ShellHost.md#L67).
- Current completed-item evidence line appears at [.github/requirements/Wip.ShellHost.md#L102](../Wip.ShellHost.md#L102).

## Runtime Proof Tests
- [IdlePrompt_GivenNoExplicitLoad_ExpectedZeroRecurringPluginOutputsWithinBoundedWindow](../../../tests/Wip.ShellHost.Tests/Hosting/WipShellHostPluginDiagnosticsTests.cs#L210)
- [IdlePrompt_GivenExplicitLoad_ExpectedRecurringOutputsAppearOnlyAfterLoadCommand](../../../tests/Wip.ShellHost.Tests/Hosting/WipShellHostPluginDiagnosticsTests.cs#L230)

## Runtime Control Path
- [WipShellHost.RunAsync](../../../src/Wip.ShellHost/Hosting/WipShellHost.cs#L18) preserves idle silence by skipping `LoadPluginsAsync` unless the host is configured for auto-load.
- [WipShellCommandLoop.HandlePluginsLoadAsync](../../../src/Wip.Shell/Interactive/WipShellCommandLoop.cs#L235) is the explicit lifecycle command path that invokes `LoadPluginsAsync` and flips the explicit-load gate.
- [RecurringOutputBridge.LoadPluginsAsync](../../../tests/Wip.ShellHost.Tests/Hosting/WipShellHostPluginDiagnosticsTests.cs#L709) starts recurring emissions only after explicit load in the proving test double.

## Validation Commands
- `dotnet build src/Wip.ShellHost/Wip.ShellHost.csproj -v minimal`
- `dotnet build tests/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj -v minimal`
- `dotnet test tests/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj --no-build -v minimal --filter "FullyQualifiedName~WipShellHostPluginDiagnosticsTests"`