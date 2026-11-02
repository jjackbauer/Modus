# Failure record — NuGet publish 0.0.2 — expired API key (F-ENV)

- **WP:** WP-0.1 (out-of-plan NuGet publication, DEV-001)
- **Command:** `dotnet nuget push artifacts/nuget-0.0.2/Modus.Core.0.0.2.nupkg --source https://api.nuget.org/v3/index.json --api-key <redacted> --skip-duplicate`
- **Exit code:** 1
- **Failure class:** F-ENV (expired credential)
- **Observed:** `403 (The specified API key has expired. Please create a new API key at https://www.nuget.org/account/apikeys.)`
- **Hypothesis:** the key in `C:\Users\ricar\.secrets\nuget.key` was issued with an expiry that has passed. The key decrypts correctly (46-char NuGet-shaped value), so this is not a format or transport failure.
- **Repair attempted:** none — the agent cannot renew an account credential. The push loop stopped on the first package.
- **Result:** **no package was published.** `Modus.Core` was the first package; it failed before any other push.
- **Residual risk:** the 18 packages remain local at `artifacts/nuget-0.0.2`; version bump `0.0.2` is committed but unpublished.
