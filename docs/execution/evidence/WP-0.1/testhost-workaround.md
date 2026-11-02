# Testhost unblock record — F-ENV (WP-0.1)

**Date:** 2026-11-02
**Status:** test execution unblocked for this environment; residual test failures are recorded separately.

## Symptom

`dotnet test` aborted before test discovery with:

```text
System.ComponentModel.Win32Exception (5): Access is denied.
   at System.Diagnostics.Process.GetProcessHandle(...)
   at System.Diagnostics.Process.EnableRaisingEvents(...)
   at Microsoft.VisualStudio.TestPlatform.TestHost.DefaultEngineInvoker.SetParentProcessExitCallback(...)
```

## Root cause

1. The WIP test projects used **Microsoft.NET.Test.Sdk 17.13.0**. Its testhost unconditionally tries to monitor the parent process. The DSH process sandbox denies `OpenProcess(SYNCHRONIZE)` on that parent.
2. Testhost also inherited a `TEMP` outside the writable workspace. After the parent-query crash was removed, tests failed with `UnauthorizedAccessException` creating directories under `%TEMP%`.

## Fix applied

1. **Source change:** upgraded `Microsoft.NET.Test.Sdk` from `17.13.0` to `17.14.1` in all 17 WIP test projects. Version 17.14.1 supports the `MSTest.EnableParentProcessQuery` AppContext switch; 17.13.0 does not.
2. **Local startup hook (not committed):** a tiny `WipTestHostStartupHook` assembly calls `AppContext.SetSwitch("MSTest.EnableParentProcessQuery", false)`. It is loaded through `DOTNET_STARTUP_HOOKS` into the testhost.
3. **Workspace temp:** set `TEMP`, `TMP`, and `TMPDIR` to `C:\Users\ricar\dev\Modus\.tmp\testhost-temp` for the test run, so testhost writes stay inside the workspace.

## Exact invocation pattern

```powershell
$env:DOTNET_STARTUP_HOOKS = '<temp>\WipTestHostStartupHook.dll'
$env:TEMP = 'C:\Users\ricar\dev\Modus\.tmp\testhost-temp'
$env:TMP = $env:TEMP
$env:TMPDIR = $env:TEMP
dotnet test <project> -c Release --no-build
```

The hook and temp path are environment-only; they are not part of the repository and are not needed in CI where the parent handle is accessible.

## Verification

- `HarnessSync.Tests.FindUtf16TextFiles_GivenUtf16InsideTmpScratch_ExpectedNotReported` passed.
- `Wip.Abstractions.Tests` passed **44/44**.
- Full WIP inventory executed: **1,354 tests, 1,305 passed, 49 failed** across 17 projects; see [baseline-test-status.md](../../../plan/baseline-test-status.md).
- Three suites aborted after test-specific hangs/crashes: `Wip.Modus.Tests`, `Wip.Bones.Host.Tests`, `Wip.Shell.E2E.Tests` (counts still captured; see the inventory).

## Residual

The workaround proves execution; it does not make the baseline green. The 49 failures and three aborts are tracked with owners and expiry in the baseline test status. The source-only change to commit is the Test SDK upgrade; the hook itself is a local diagnostic artifact.
