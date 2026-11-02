# NuGet 0.0.2 publication — test-gate risk record

**Status:** risk accepted for publication
**Recorded:** 2026-11-02T11:30:00-03:00
**Owner:** product owner (user)
**Scope:** the 18 packages at version `0.0.2` only; this is **not** G5 approval and **not** a WIP 1.0 release.

## Basis

- The user explicitly requested: "I want you to pack and publish wip in nuget as well. and update modus."
- The user supplied the NuGet API key through an encrypted file after the test blocker was disclosed, and answered "done" to the provisioning instructions.
- The product build is green: `dotnet build Modus.slnx -c Release` -> exit 0, 0 errors, 121 warnings.
- All 18 packages were packed and validated; nuspec IDs/versions/dependencies are consistent at `0.0.2`; a local consumption test previously passed.
- The WIP test suites **did not run**: `dotnet test` aborts at testhost startup with `Win32Exception (5)` under this environment (`BLK-TESTHOST-ENV`). This is an environment blocker, not a product failure, but the runtime behavior of the published packages is **not covered by executed tests**.

## Risk

A public package may contain a runtime regression that the unexecuted 1,300 test declarations would have caught. The packages are pre-1.0 and versioned `0.0.2`; Modus updates an existing `0.0.1` package.

## Mitigation

1. Run the 17 WIP suites on an unconfined host/CI immediately after publication; attach TRX to `docs/execution/evidence/WP-0.1/`.
2. If a failure is found, publish a patch version (`0.0.3`) and/or unlist `0.0.2` on nuget.org.
3. Do not treat this publication as evidence for G5 or for WIP 1.0.

## Expiry

2026-11-09, or when the test inventory is completed, whichever is earlier.

## Explicitly not claimed

This record does **not** claim tests passed. No test count or green suite is asserted.

## Update 2026-11-02T11:29:25-03:00

The publication attempt failed with an **expired API key**; **no package was published**. This risk record therefore describes a prepared-but-unexecuted publication, not a completed release. It remains valid only if a later authorized push succeeds.
