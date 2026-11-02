# BLK-NUGET-PUBLISH — publication is blocked on credentials, version, and release approval

**Status:** open
**Opened:** 2026-11-02T11:00:00-03:00
**Owner:** product owner + release engineer

## What is blocked

Packing and publishing WIP packages to NuGet, and updating the Modus packages on NuGet, as requested on 2026-11-02.

## Facts

- `Directory.Build.props` sets `PackageVersion` = **0.0.1**; local artifacts for `Modus.Core.0.0.1` and `Modus.Host.0.0.1` exist under `artifacts/nuget`. Re-publishing 0.0.1 would collide with existing versions.
- No `NUGET_API_KEY` (or equivalent) is present in the process, user, or machine environment. No credential value was printed.
- WIP test execution is blocked by `BLK-TESTHOST-ENV`; a public package should not be published without green tests.
- WIP-IMPL-001 reserves packaging for WP-6.2 and the G5 release gate; this request is a scope deviation, recorded as `DEV-001`.
- The requested package surface is not yet agreed (Modus.Core/Host vs 20+ Wip.* IDs, including app hosts).

## Options

1. **Pack locally and validate now; publish later** after the user supplies the version and API key and tests are green. (Recommended.)
2. Supply a version + API key now and accept a pre-release publication exception; publish only after the build/test gate is satisfied.
3. Defer the whole request to WP-6.2/G5.

## Recommendation

Do not push to nuget.org from this session. Prepare local packages and a validated manifest, then ask for the version and API key through a secure channel (environment variable or CI secret, never chat). Publishing must be an explicit action with a recorded version and package list.
