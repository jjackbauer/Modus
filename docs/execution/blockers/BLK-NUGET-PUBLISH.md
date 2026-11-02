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

## Update 2026-11-02T11:16:00-03:00

- **Local pack succeeded:** 18 `.nupkg` + 18 `.snupkg` at `0.0.2-local`; every `dotnet pack` exited 0 ([pack-local.txt](../evidence/WP-0.1/pack-local.txt), [nuget-manifest.md](../evidence/WP-0.1/nuget-manifest.md)).
- **Consumption smoke test succeeded:** local-only restore and build of `Wip.Abstractions` + `Modus.Core` in a clean temp project (exit 0, 0 warnings/errors; [nuget-consume.txt](../evidence/WP-0.1/nuget-consume.txt)).
- **Network correction:** `curl` and `dotnet package search` reach nuget.org; the published Modus versions are 0.0.1 and the checked WIP IDs are absent. `Invoke-WebRequest` timed out, but that is not a general network block. No push was attempted because no API key is available (see [nuget-registry-check.txt](../evidence/WP-0.1/nuget-registry-check.txt)).
- **Still missing:** API key, version decision, green test evidence, and a release-gate exception/approval.
- **Not packaged:** `Wip.Bones.Host` and `Wip.Bones.Web` (Web SDK, not packable by default); they are applications.
- **Published check:** `Modus.Core`/`Modus.Host` = 0.0.1; `Wip.*` sample IDs = unpublished. This confirms the Modus update is real, not just suspected.

## How to supply the API key without exposing it in chat

### A. Encrypted key file outside the repo (recommended if the agent should publish)

Create a DPAPI-encrypted key file outside the repository. The agent runs as the same Windows user and can decrypt it; the value is never printed.

```powershell
$secure = Read-Host -AsSecureString "NuGet API key"
$dir = Join-Path $env:USERPROFILE ".secrets"
New-Item -ItemType Directory -Force $dir | Out-Null
ConvertFrom-SecureString $secure | Set-Content -Path (Join-Path $dir "nuget.key") -Encoding ascii
```

Then tell the agent the path (default `$env:USERPROFILE\.secrets\nuget.key`), the approved version, and whether tests are waived. The agent reads/decrypts it in one command, passes it to `dotnet nuget push` without printing it, and deletes the file afterwards. Residual risk: the key is briefly present in the child `dotnet` process command line.

### B. Restart the DSH server with the environment variable

A key set in another terminal does **not** reach the running DSH process (pid 28924, `node ... web`). Restart DSH from a shell that has the variable:

```powershell
# Stop the existing "dsh web" process first (Ctrl+C in its terminal).
cd C:\Users\ricar\deepseek-harness
$env:NUGET_API_KEY = Read-Host "NuGet API key"   # input is not stored in PSReadLine history
pnpm dsh web
```

Reopen http://127.0.0.1:3080 and tell the agent the key is set. The conversation may restart; execution state is committed and the working tree is clean. Remove the variable and restart DSH again after publishing.

### C. Publish yourself (no key shared with the agent)

Ask the agent to pack at the approved version (for example `0.0.2`), then run in your own terminal:

```powershell
$env:NUGET_API_KEY = Read-Host "NuGet API key"
dotnet nuget push "artifacts/nuget-0.0.2/*.nupkg" --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
dotnet nuget push "artifacts/nuget-0.0.2/*.snupkg" --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
```

This is the safest option: the key never enters the agent process or this transcript.

## Security rules

- Do not paste the key into chat, a repository file, a commit, or an evidence file.
- Scope the key to push-only for `Modus.*` and `Wip.*` with a short expiry; revoke it after publishing (https://www.nuget.org/account/apikeys).
- Delete the encrypted file after use and clear the environment variable.
- If the key is exposed, revoke it immediately.
