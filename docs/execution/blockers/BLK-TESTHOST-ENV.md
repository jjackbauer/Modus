# BLK-TESTHOST-ENV — testhost cannot start under the DSH process sandbox

**Status:** resolved — test execution unblocked via Test SDK 17.14.1 + local startup hook + workspace TEMP
**Opened:** 2026-11-02T11:00:00-03:00
**Owner:** platform/QA (human action required)

## Exact failing command

```
dotnet test tools/HarnessSync.Tests/HarnessSync.Tests.csproj -c Release --filter "FullyQualifiedName~FindUtf16TextFiles_GivenUtf16InsideTmpScratch"
```

Exit code 1. Testhost aborts with `Win32Exception (5): Access is denied` at `Process.EnableRaisingEvents` / `SetParentProcessExitCallback`, before any test is discovered or run. Reproduced with direct invocation, `Start-Process` + file redirection, and `--arch x64`.

## Impact

- WP-0.1 cannot produce the required test inventory/known-failure list from this environment.
- No WIP test evidence can support a NuGet publication decision.
- The product build itself is not implicated; this is an execution-environment failure (F-ENV).

## Options

1. **Run the same commands in the user's terminal or CI** where testhost can open its parent process, and attach the TRX/logs. (Recommended.)
2. Adjust the DSH process sandbox/token so testhost can open the parent handle; requires harness-level change and re-verification.
3. Build a standalone xUnit runner outside VSTest; out of scope and still needs a host-level validation.

## Recommendation

Run the WP-0.1 inventory from an unconfined user shell/CI. Do not treat the baseline as tested, and do not publish NuGet packages, until that evidence exists.

## Resolution 2026-11-02T12:28-03:00

- **Root cause:** Test SDK 17.13.0 testhost unconditionally queries its parent process; the sandbox denies `OpenProcess`. 17.14.1 supports the `MSTest.EnableParentProcessQuery` AppContext switch, which a local startup hook disables. A workspace-local `TEMP` fixes testhost write denials.
- **Source change:** all 17 WIP test projects upgraded from `Microsoft.NET.Test.Sdk` 17.13.0 to 17.14.1.
- **Result:** full inventory executed — **1,354 tests, 1,305 passed, 49 failed**, 3 suites aborted after test-specific hangs/crashes.
- The local hook and temp redirect are environment-only; CI does not need them.
- **Residual:** the 49 failures/3 aborts are tracked in [baseline-test-status.md](../plan/baseline-test-status.md); this blocker is closed for testhost startup only.
