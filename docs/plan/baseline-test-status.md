# Baseline test status — WIP-IMPL-001 / WP-0.1

**Status:** INCOMPLETE — build is green, test execution is blocked by the testhost environment.
**Baseline commit:** `99c0b087e8ad5b9993baef807bab92b4cd37aa0e` (tag `wip-baseline-2026-11-02`)
**Plan baseline:** `d6b895f` + the 308-path working tree
**Date:** 2026-11-02

## Build

| Item | Result |
|---|---|
| Command | `dotnet build Modus.slnx -c Release --nologo` |
| Exit code | **0** |
| Errors | **0** |
| Warnings | **115** |
| Elapsed | 13.33 s |
| Evidence | [build-modus.txt](../execution/evidence/WP-0.1/build-modus.txt) |

`Modus.slnx` contains 34 of the 37 WIP projects; `Wip.Bones.Tests`, `Wip.Bones.Benchmark`, and `BonesLibraryRecovery` are outside both solutions and were not built by this command. This is the WP-0.3 gap.

## Test surface

| Item | Value |
|---|---|
| WIP test projects | 17 |
| Test source files | 239 |
| `[Fact]` / `[Theory]` declarations | 1,300 |
| Projects executed by CI | 1 of 17 (`Wip.ShellHost.Tests`) |
| Projects executed for this inventory | **0** |

## Test execution result

**Blocked at testhost startup — no test was discovered or executed.** The exact command:

```
dotnet test tools/HarnessSync.Tests/HarnessSync.Tests.csproj -c Release --filter "FullyQualifiedName~FindUtf16TextFiles_GivenUtf16InsideTmpScratch"
```

Exit code 1. Testhost aborts with `System.ComponentModel.Win32Exception (5): Access is denied` at `Process.EnableRaisingEvents` / `SetParentProcessExitCallback`. Reproduced with direct invocation, `Start-Process` + file redirection, and `--arch x64`. See [fail-1-testhost.md](../execution/evidence/WP-0.1/fail-1-testhost.md) and `BLK-TESTHOST-ENV`.

No known-failure list can be produced honestly from this environment, and no waiver is recorded: a waiver is not a substitute for execution.

## Known baseline risks

- 115 compiler/analyzer warnings; none promoted to errors.
- The 17 WIP test projects are unverified in this environment; CI still covers 1 of 17.
- No WIP architecture tests exist (WP-0.5).
- CI triggers `main` only and does not run on `WIP` (WP-0.4).
- The build graph omits `Wip.Bones.Tests` (58 source files), `Wip.Bones.Benchmark`, and `BonesLibraryRecovery` (WP-0.3).

## Waivers

None. Every unexecuted suite is an **open blocker**, not a waiver.

## Next action

Run the same `dotnet test` commands in the user's unconfined terminal or CI, attach TRX/logs to `docs/execution/evidence/WP-0.1/`, and replace this file's status with the actual pass/fail inventory and owner/expiry waivers.
