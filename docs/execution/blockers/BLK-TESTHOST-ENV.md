# BLK-TESTHOST-ENV — testhost cannot start under the DSH process sandbox

**Status:** open
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
