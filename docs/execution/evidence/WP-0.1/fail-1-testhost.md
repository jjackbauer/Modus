# Failure record — WP-0.1 — testhost process access (F-ENV)

- **WP:** WP-0.1
- **Command:** `dotnet test tools/HarnessSync.Tests/HarnessSync.Tests.csproj -c Release --filter FullyQualifiedName~FindUtf16TextFiles_GivenUtf16InsideTmpScratch`
- **Exit code:** 1
- **Failure class:** F-ENV
- **Observed:** testhost aborts before executing any test with `System.ComponentModel.Win32Exception (5): Access is denied` at `Process.GetProcessHandle` → `Process.EnableRaisingEvents` → `DefaultEngineInvoker.SetParentProcessExitCallback`. The same failure occurs with:
  1. direct `dotnet test`;
  2. `Start-Process dotnet test` with stdout/stderr redirected to files (no pwsh pipe capture);
  3. `dotnet test --arch x64` after rebuilding the test project for `win-x64`.
- **Hypothesis 1:** the DSH process sandbox/token denies the testhost `OpenProcess(SYNCHRONIZE)` on its parent. Evidence: the failure is in testhost startup, before test discovery; the build succeeds; the same code path fails in all three launch shapes.
- **Hypothesis 2:** a .NET 10 / Microsoft.NET.Test.Sdk 17.14.1 testhost compatibility issue. Evidence: none yet — no network to upgrade the SDK, and `--arch x64` did not change the failure.
- **Evidence:** [harnesssync-tmp-test-red.txt](harnesssync-tmp-test-red.txt); the HarnessSync check itself passes after excluding `.tmp` from the encoding validator ([harnesssync-check-after.txt](harnesssync-check-after.txt)).
- **Repair attempted:** excluded `.tmp` from the repo-wide encoding validator (the original `--check` failure) and re-ran `HarnessSync --check` → exit 0. The unit test added for this fix still cannot execute because of the testhost startup failure.
- **Result:** test execution is blocked at the environment layer, not by a product assertion.
- **Residual risk:** WP-0.1 cannot claim a green test inventory from this environment. No NuGet package should be published until the test suite is run on a host/CI where testhost can start.
