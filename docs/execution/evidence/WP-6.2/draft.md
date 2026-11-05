# WP-6.2 release-engineering dry-run evidence (2026-11-05)

**Status:** local dry-run only. **No package was published, signed or deployed.** No release claim.

## Artifacts built locally

| Artifact | Command | Result |
|---|---|---|
| `Wip.ShellHost` publish folder | `dotnet publish WIP/Wip.ShellHost/Wip.ShellHost.csproj -c Release -o .tmp/release-dry-run/shellhost` | exit 0 |
| `Wip.Bones.Host` publish folder | `dotnet publish WIP/Wip.Bones.Host/Wip.Bones.Host.csproj -c Release -o .tmp/release-dry-run/bones-host` | exit 0 |

- 99 `.dll` files, **36,775,200 bytes** total across both publish folders.
- Assembly version metadata: `FileVersion 1.0.0.0`, `ProductVersion 1.0.0+e3fee2d194dd1fa775e780dc15597102d049c09e`
  (the build stamps the commit; the intended release version 1.0.0 matches the build metadata).
- Full SHA-256 manifest: `.tmp/release-dry-run/checksums.json` (generated locally, not committed).

## Primary assembly checksums

| Assembly | Bytes | SHA-256 |
|---|---|---|
| `Wip.ShellHost.dll` | 79,360 | `761CF09A783DDBB745CD8EF4D3D62AA2A4B545B31889DA9C4B1BB22E704F380D` |
| `Wip.Bones.Host.dll` | 155,648 | `DDCEF59D71B7162ACCD4E182EBC1DCAF672FA199E28D17409552067AD533388B` |

## Signing / publishing

- No signing key or certificate was available or used; no signature exists.
- No `dotnet nuget push` was run and no registry was modified.
- The publish steps and the human gates are documented in `docs/release/packaging-and-signing.md`.

## Rollback-control rehearsal

See `docs/release/rollback.md`: ScriptHost flag/fail-closed tests and permission Enforce/Audit
tests were re-run locally; the results are recorded in the round-7 journal/evidence. No deployment
exists to roll back.
