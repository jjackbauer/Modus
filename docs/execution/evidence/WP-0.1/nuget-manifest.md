# Local NuGet package manifest (NOT PUBLISHED)

captured=2026-11-02T11:11:41.9735098-03:00
version=0.0.2-local
source=artifacts/nuget-local

| Package | Version | Dependencies | lib files |
|---|---|---|---:|
| Modus.Core | 0.0.2-local | Microsoft.Extensions.DependencyInjection.Abstractions 10.0.0 | 1 |
| Modus.Host | 0.0.2-local |   | 0 |
| Wip.Abstractions | 0.0.2-local |   | 1 |
| Wip.Agent.Basic | 0.0.2-local | Wip.Abstractions 0.0.2-local | 1 |
| Wip.Artifacts.Local | 0.0.2-local | Wip.Abstractions 0.0.2-local; Microsoft.Extensions.Logging.Abstractions 9.0.0 | 1 |
| Wip.Bones | 0.0.2-local | Microsoft.CodeAnalysis.CSharp 4.13.0 | 1 |
| Wip.Bones.Agents | 0.0.2-local | Wip.Abstractions 0.0.2-local; Wip.Artifacts.Local 0.0.2-local; Wip.Bones 0.0.2-local; Wip.Builder 0.0.2-local; Wip.Runtime 0.0.2-local; Microsoft.Extensions.DependencyInjection.Abstractions 10.0.0; Microsoft.Extensions.Logging.Abstractions 10.0.0 | 1 |
| Wip.Bones.ModelProviders.DeepSeek | 0.0.2-local | Wip.Abstractions 0.0.2-local; Wip.Bones.Agents 0.0.2-local; Wip.Runtime 0.0.2-local; Microsoft.Extensions.DependencyInjection 10.0.0; Microsoft.Extensions.Http.Resilience 9.0.0 | 1 |
| Wip.Builder | 0.0.2-local | Wip.Abstractions 0.0.2-local; Microsoft.Extensions.DependencyInjection.Abstractions 10.0.0 | 1 |
| Wip.Modus | 0.0.2-local | Modus.Core 0.0.2-local; Wip.Builder 0.0.2-local | 1 |
| Wip.Policy.LocalSafe | 0.0.2-local | Wip.Abstractions 0.0.2-local | 1 |
| Wip.Runtime | 0.0.2-local | Wip.Abstractions 0.0.2-local; Wip.Agent.Basic 0.0.2-local; Wip.Builder 0.0.2-local; Wip.Validation.DotNet 0.0.2-local; Wip.Workspaces.Git 0.0.2-local; Microsoft.Extensions.DependencyInjection 10.0.0; Microsoft.Extensions.DependencyInjection.Abstractions 10.0.0 | 1 |
| Wip.Shell | 0.0.2-local | Wip.Agent.Basic 0.0.2-local; Wip.Builder 0.0.2-local; Wip.Modus 0.0.2-local; Wip.Policy.LocalSafe 0.0.2-local; Wip.Runtime 0.0.2-local; Wip.Workspaces.Git 0.0.2-local; Microsoft.Extensions.DependencyInjection 10.0.0 | 1 |
| Wip.Shell.E2E | 0.0.2-local | Wip.ShellHost 0.0.2-local | 1 |
| Wip.ShellHost | 0.0.2-local | Modus.Core 0.0.2-local; Wip.Abstractions 0.0.2-local; Wip.Artifacts.Local 0.0.2-local; Wip.Bones.Agents 0.0.2-local; Wip.Builder 0.0.2-local; Wip.Modus 0.0.2-local; Wip.Runtime 0.0.2-local; Wip.Shell 0.0.2-local | 2 |
| Wip.Tools.Shell | 0.0.2-local | Wip.Abstractions 0.0.2-local | 1 |
| Wip.Validation.DotNet | 0.0.2-local | Wip.Abstractions 0.0.2-local; Wip.Workspaces.Git 0.0.2-local; Microsoft.CodeAnalysis.CSharp 4.13.0; Microsoft.Extensions.DependencyInjection.Abstractions 10.0.0 | 1 |
| Wip.Workspaces.Git | 0.0.2-local | Wip.Abstractions 0.0.2-local | 1 |

## Local validation

- **18 packages** packed with version `0.0.2-local` (plus 18 `.snupkg` symbol packages); all `dotnet pack` exits were 0 ([pack-local.txt](pack-local.txt)).
- The nuspec metadata above was read from each package; the local dependency graph is internally consistent at `0.0.2-local`.
- **Consumption smoke test passed:** a clean temporary console project with a local-only NuGet source added `Wip.Abstractions` and `Modus.Core`, restored, and built with **0 warnings / 0 errors** ([nuget-consume.txt](nuget-consume.txt)).
- `Wip.Bones.Host` and `Wip.Bones.Web` are **not** in the package set: both use `Microsoft.NET.Sdk.Web`, whose default `IsPackable` is `false`. They are applications, not libraries; publishing them would need an explicit decision and packaging changes.

## Publication blockers

1. **No API key:** no `NUGET_API_KEY` (or equivalent) in the process, user, or machine environment.
2. **No version decision:** packages were built as `0.0.2-local` only. `Directory.Build.props` still says `0.0.1`. Registry check: `Modus.Core` and `Modus.Host` publish **0.0.1**; all checked `Wip.*` IDs are absent (BlobNotFound).
3. **Push not tested:** PowerShell `Invoke-WebRequest` timed out, but `curl` and `dotnet package search` reach nuget.org (see [nuget-registry-check.txt](nuget-registry-check.txt)). No `dotnet nuget push` was attempted because no API key is available.
4. **Tests:** `BLK-TESTHOST-ENV` prevents a green WIP test run.
5. **Release gate:** WIP-IMPL-001 packs at WP-6.2/G5; this request is `DEV-001`.

## Proposed versions (requires owner decision)

- **Modus:** `0.0.2` if `0.0.1` is the currently published version (likely, given the local `artifacts/nuget` packages).
- **WIP:** `0.0.1` if these packages have never been published, or `0.1.0-preview.1` for a clearly labelled pre-release. Do **not** use `1.0.0` before G5.

## Publish procedure (not executed)

```powershell
# On a networked machine with the approved version and key:
dotnet build Modus.slnx -c Release --nologo
dotnet pack src/Modus.Core/Modus.Core.csproj -c Release --no-build -p:PackageVersion=<version> -o artifacts/nuget-<version>
# repeat for each approved project; do not publish 0.0.2-local
dotnet nuget push "artifacts/nuget-<version>/*.nupkg" --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
```

Never put the API key in the repository or a command transcript.

## Registry check

`Modus.Core` and `Modus.Host` publish **0.0.1** on nuget.org. All checked WIP IDs (`Wip.Abstractions`, `Wip.Runtime`, `Wip.Builder`, `Wip.ShellHost`, `Wip.Shell`) are unpublished. Read-only evidence: [nuget-registry-check.txt](nuget-registry-check.txt). This confirms the Modus package is outdated relative to the working tree, but a version bump still requires an owner decision.
