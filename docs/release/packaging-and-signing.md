# Packaging, signing and publish plan (Phase 6 — not executed)

**Status:** dry-run only. Nothing has been published, signed or deployed for WIP 1.0.

## Version

- Intended release version: **1.0.0**.
- The repository's `Directory.Build.props` remains at the pre-release version until the G5 decision;
  the dry-run uses the current build output and does not rewrite versions or publish.
- Previous registry state: NuGet `0.0.2` packages were published on 2026-11-02 under DEV-001 (an
  earlier product-owner exception). That publication is historical; it is **not** WIP 1.0.

## Local dry-run (performed)

```powershell
dotnet publish WIP/Wip.ShellHost/Wip.ShellHost.csproj -c Release -o .tmp/release-dry-run/shellhost
dotnet publish WIP/Wip.Bones.Host/Wip.Bones.Host.csproj -c Release -o .tmp/release-dry-run/bones-host
Get-FileHash .tmp/release-dry-run/**/*.dll -Algorithm SHA256 |
  Select-Object Path,Hash | Export-Csv .tmp/release-dry-run/checksums.csv -NoTypeInformation
```

Outcome and checksums are recorded in `docs/execution/evidence/WP-6.2/` after the run. Build
artifacts are local and are not committed.

## Package surface (proposed for G5)

| Artifact | Contents | Notes |
|---|---|---|
| Host applications | `Wip.ShellHost`, `Wip.Bones.Host` publish folders | self-contained framework-dependent; no secrets |
| Framework libraries | the `Wip.*` library packages (abstractions, builder, runtime, tools, workspaces, validation, policy, modus) | versioned contracts; do not include test projects |
| Symbols | `.pdb`/symbol package | optional, per release policy |

## Signing plan (human gate — credentials not available)

1. Obtain the organization signing certificate/HSM and the NuGet API key from the release owner.
2. Sign the host binaries and the NuGet packages (repository signing policy to be confirmed by the
   release owner).
3. Verify signatures (`signtool verify` on Windows; NuGet signature verification).
4. Record hashes and signatures in the release evidence.

**Not performed:** no key or certificate was used, and no signature exists for this draft. The key
must be supplied by the human owner; it is never placed in the repository or logs.

## Publish plan (not executed)

1. Freeze the release commit after G5 and re-run the full CI gates (`Tests CI` all six jobs,
   `Modus CI` with the blocking legacy suite).
2. Pack/publish to NuGet with the owner-supplied key **only after** G5.
3. Publish the host artifacts and checksums with the release notes and support matrix.
4. Run the post-release verification checklist (see `post-release-verification.md`).

Until the G5 decision: **do not run `dotnet nuget push`, do not sign, do not deploy.**
