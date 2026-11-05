# WP-2.7 — collectible plugin load contexts (increment 1)

**Status:** in progress 2026-11-05; verification running.

## Change

- **`PluginAssemblyLoadContext`** (`WIP/Wip.Modus/Hosting/PluginAssemblyLoadContext.cs`): one
  collectible `AssemblyLoadContext` per plugin assembly. Host contracts (`Wip.*`), Modus contracts
  (`Modus.*`) and framework assemblies resolve from the **default** context so contract type
  identity is preserved; every other dependency resolves through an `AssemblyDependencyResolver`
  rooted at the plugin path, so two plugins may carry conflicting versions of a dependency.
  Plugin assemblies are read through a stream (`LoadFromStream`), so loading does not lock the DLL
  and unload releases it deterministically. Unmanaged dependencies resolve relative to the plugin.
- **`ModusWipBridge`**: loads each plugin assembly into its own context, records the context on the
  plugin runtime, emits `[assembly-load] ... collectible context` diagnostics, unloads the context
  when an assembly exposes no plugin types, and on `StopPluginsAsync` unloads each used context
  after the lifecycle stop/unload and emits `[unload] Unloaded collectible context ...`.
  Lifecycle activation failures stay contained per plugin (existing behavior).
- Plugin assemblies that fail to load have their context unloaded before the error is reported.

## Tests

`WIP/Wip.Modus.Tests/Hosting/PluginAssemblyLoadContextTests.cs`:

1. the context is collectible, the plugin assembly loads into it, and `IWipHostPluginContract` is
   the **same** interface type as the host's (shared-contract identity);
2. two contexts over the same assembly path produce distinct assembly identities (the mechanism
   that lets plugins carry conflicting dependency versions);
3. after `Unload` + GC the context is unreachable (collectible proof);
4. the bridge reports the collectible-context load and the unload diagnostics, and the temp plugin
   folder can be deleted (no file lock).

Plus the existing `ModusWipBridgeTests` (loading, precedence, manifests, ambiguous-reference fault
containment) — all green.

## Increment 2 — conflicting dependency versions (2026-11-05)

Added `tests/Fixtures/ConflictDependency.V1|V2` (two assemblies with the same name `Conflict.Dep`
and versions 1.0.0.0 / 2.0.0.0) and `ConflictPlugin.V1|V2`, each compiled against its own dependency
version and reporting it through the operation catalog. `Modus.Tests` references the fixture
projects with `ReferenceOutputAssembly=false`, so both build into separate folders without a
conflict in the test output. The new test copies each plugin folder (plugin + its own
`Conflict.Dep.dll`) and loads it through its own `ModusWipBridge`; the manifests report
`conflict.operation.v1` and `conflict.operation.v2` respectively — the deeper dependency version is
never unified. The architecture graph is unchanged (fixtures live under `tests/`, not `WIP/`).

## Verification

Focused run: 10/10 (4 new + 6 bridge). Full `Wip.Modus.Tests`: running; full gate and CI: pending.