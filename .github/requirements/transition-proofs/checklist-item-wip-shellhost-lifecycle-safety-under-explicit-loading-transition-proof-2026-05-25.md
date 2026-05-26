# Checklist Transition Proof - Wip.ShellHost Lifecycle Safety Under Explicit Loading

## Baseline Unchecked Source Text Evidence
- Baseline snapshot source: `.github/requirements/transition-proofs/baselines/checklist-item-wip-shellhost-lifecycle-safety-under-explicit-loading.unchecked.txt`.
- [ ] Preserve lifecycle safety under explicit loading: single-active-run guard, one-time load gate per host container, and deterministic `StopPluginsAsync` on exit/cancellation remain enforced [depends on explicit lifecycle command path]

## Checked Completion Evidence
- [x] Preserve lifecycle safety under explicit loading: single-active-run guard, one-time load gate per host container, and deterministic `StopPluginsAsync` on exit/cancellation remain enforced [depends on explicit lifecycle command path] [transition-proof: .github/requirements/transition-proofs/checklist-item-wip-shellhost-lifecycle-safety-under-explicit-loading-transition-proof-2026-05-25.md]

## Line Evidence
- Current checked checklist line appears at `.github/requirements/Wip.ShellHost.md:69`.
- Current completed-item evidence line appears at `.github/requirements/Wip.ShellHost.md:114`.

## Runtime Proof Tests
- `RunAsync_GivenConcurrentInvocations_ExpectedSingleActiveRunPerHostContainerLifetime`
- `RunAsync_GivenAutoLoadAcrossMultipleInvocations_ExpectedLoadPluginsInvokedOncePerHostContainerLifetime`
- `PluginsLoadCommand_GivenPluginsAlreadyLoaded_ExpectedIdempotentResponseWithoutDuplicateActivation`
- `PluginsLoadCommand_GivenLoadThenUnloadThenSecondLoad_ExpectedSecondLoadRejectedForHostContainerLifetime`
- `RunAsync_GivenExplicitLoadThenCancellation_ExpectedStopPluginsInvokedExactlyOnce`

## Validation Commands
- `dotnet build src/Wip.ShellHost/Wip.ShellHost.csproj -v minimal`
- `dotnet test tests/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj --no-build -v minimal`
