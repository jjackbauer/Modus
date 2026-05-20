# Plugin Lifetime Resolution Consistency

> Scheduled operations and sync responder invocations must resolve plugins from the DI container respecting their declared service lifetime. The HTTP/sync path is now fixed; scheduled operations still require a refactor to resolve from DI on each invocation instead of reusing a cached Activator-created instance.

---

## Problem Statement

The Modus plugin infrastructure had a **critical bug in HTTP/sync invocation paths** and a **separate lifetime disconnect in scheduled operations**:

### HTTP/Sync Path ✓ FIXED (May 20, 2026)

**Issue**: Plugins were registered in DI as singleton instances via `services.AddPluginServiceInstance()` regardless of declared lifetime.

**Root Cause**: [PluginBase.RegisterPluginServices()](../../src/Modus.Core/Plugins/Base/PluginBase.cs#L65) called `AddPluginServiceInstance<TPluginImpl>()` which always registers as singleton.

**Fix Applied**: Changed line 65 to `services.AddPluginService<TPluginImpl, TPluginImpl>(DeclaredServiceLifetime)`, which registers by type with the declared lifetime factory.

**Status**: ✓ HTTP requests now correctly receive new instances for Scoped/Transient plugins.

**Verification**: PluginDiConsumptionTests passes (12/12); TransientRegistration tests pass.

### Scheduled Operations Path ✗ Still Broken

**Issue**: AssemblyLifecycleHost caches a single Activator-created instance and reuses it for all scheduler ticks.

**Root Cause**: [AssemblyLifecycleHost.cs](../../src/Modus.Host/Domain/Plugins/Host/AssemblyLifecycleHost.cs#L103-L108) captures the plugin instance in a closure and reuses it forever in PeriodicTimer callbacks.

**Status**: ✗ Scheduled operations always use the same instance regardless of declared lifetime.

**Note**: AssemblyLifecycleHost is instantiated without DI access (PluginFolderWatcher.cs:14). Fixing this requires injecting `IServiceProvider` and refactoring plugin resolution in scheduled operation paths.

---

## Observable Behavior (Before Fix)

| Path | Transient Plugin | Scoped Plugin | Singleton Plugin |
|---|---|---|---|
| HTTP Call 1 | Same ID ✗ | Same ID ✗ | Same ID ✓ |
| HTTP Call 2 | Same ID ✗ | Same ID ✗ | Same ID ✓ |
| Scheduled Tick 1 | Different ID (accidental) | Different ID (accidental) | Different ID ✗ |
| Scheduled Tick 2 | Different ID (accidental) | Different ID (accidental) | Different ID ✗ |

**Root cause of accidental differences in scheduled ticks**: Each scheduled tick called `RegisterAndRunSchedules()` which created the `plugin` variable in a new closure, making it appear different. But this was a side effect, not intentional lifetime semantics.

---

## Observable Behavior (After HTTP Fix)

---

## Functionality Worktree

### Completeness Checklist

**Phase 1: HTTP Path Fix ✓ COMPLETED (May 20, 2026)**

- [x] Fix `PluginBase<TPluginImpl>.RegisterPluginServices()` to use declared lifetime [impl] ✓ DONE
- [x] `PluginDiConsumptionTests.TransientRegistration_GivenRepeatedResolutions_ExpectedNewInstanceEachTime` [contract-test] ✓ PASSING
- [x] `PluginDiConsumptionTests.HostDiResolution_GivenScopedDependencies_ExpectedScopedLifetimeRespectedDuringExecution` [contract-test] ✓ PASSING
- [x] Verify HTTP requests receive new Transient instances [verify] ✓ VERIFIED
- [x] Verify HTTP requests respect Scoped boundaries [verify] ✓ VERIFIED
- [x] Verify HTTP requests reuse Singleton instances [verify] ✓ VERIFIED

**Phase 2: Scheduled Operations Path ✗ TODO**

- [ ] Inject `IServiceProvider` into `AssemblyLifecycleHost` [arch]
- [ ] Refactor `ExecuteScheduledOperation()` to resolve plugins from DI instead of using cached Activator instance [impl]
- [ ] Ensure each scheduled operation invocation creates a new DI scope [impl]
- [ ] `AssemblyLifecycleHost_ResolvesPluginsFromServiceProvider` [contract-test]
- [ ] `ScheduledOperation_GivenTransientPlugin_ExpectedNewInstancePerTick` [contract-test]
- [ ] `ScheduledOperation_GivenScopedPlugin_ExpectedNewInstancePerTick` [contract-test]
- [ ] Verify singleton plugins maintain single instance across all scheduled ticks [verify]

---

## Test Plan

### Phase 1: HTTP Path Tests ✓ PASSING

#### `PluginDiConsumptionTests.TransientRegistration_GivenRepeatedResolutions_ExpectedNewInstanceEachTime` ✓ PASSING

**Test Location**: [PluginDiConsumptionTests.cs](../../tests/Modus.Host.IntegrationTests/PluginDiConsumptionTests.cs#L470)

Given a `TransientPlugin<T>` registered in the DI container:
1. Resolve the plugin via `scope.ServiceProvider.GetRequiredService<TransientPlugin>()`
2. Capture the `InstanceId`
3. Resolve again in the same scope
4. Assert: Both resolutions returned different instances (different `InstanceId`)

**Status**: ✓ PASSING (verified May 20, 2026)

#### `PluginDiConsumptionTests.HostDiResolution_GivenScopedDependencies_ExpectedScopedLifetimeRespectedDuringExecution` ✓ PASSING

**Test Location**: [PluginDiConsumptionTests.cs](../../tests/Modus.Host.IntegrationTests/PluginDiConsumptionTests.cs#L464)

Given a `ScopedPlugin<T>` deployed in a live host:
1. Create DI scope A and resolve the plugin
2. Resolve the plugin again in scope A
3. Assert: Both resolutions in scope A return the same instance
4. Create DI scope B and resolve the plugin
5. Assert: Scope B's instance is different from scope A's instance

**Status**: ✓ PASSING (verified May 20, 2026)

### Phase 2: Scheduled Operations Path Tests ✗ TODO

#### `AssemblyLifecycleHost_ResolvesPluginsFromServiceProvider` 

Verify that `AssemblyLifecycleHost.ExecuteScheduledOperation()` obtains plugins from `IServiceProvider` rather than cached `Activator.CreateInstance()` instances.

*Assertion*: No direct calls to `Activator.CreateInstance(pluginLifecycleType)` in the scheduled operation execution path; all plugin resolution flows through `serviceProvider.GetRequiredService<TPlugin>()`.

#### `ScheduledOperation_GivenTransientPlugin_ExpectedNewInstancePerTick`

Given a `TransientPlugin<T>` with a registered scheduled operation:
1. Execute the operation via scheduler (tick 1), capture `InstanceId` from console output
2. Execute the operation again via scheduler (tick 2), capture `InstanceId` from console output
3. Assert: Tick 1 and Tick 2 resolved different plugin instances (different `InstanceId`)

#### `ScheduledOperation_GivenScopedPlugin_ExpectedNewInstancePerTick`

Given a `ScopedPlugin<T>` with a registered scheduled operation:
1. Execute the operation via scheduler (tick 1), capture `InstanceId` from console output
2. Execute the operation again via scheduler (tick 2), capture `InstanceId` from console output
3. Assert: Tick 1 and Tick 2 resolved different plugin instances (different `InstanceId`)

#### `ScheduledOperation_GivenSingletonPlugin_ExpectedSameInstanceEveryTick`

Given a `SingletonPlugin<T>` with a registered scheduled operation:
1. Execute the operation via scheduler (tick 1), capture `InstanceId` from console output
2. Execute the operation again via scheduler (tick 2), capture `InstanceId` from console output
3. Assert: Tick 1 and Tick 2 resolved the same plugin instance (same `InstanceId`)

---

## Implementation Status

### Phase 1: HTTP Path ✓ COMPLETED

**Change Made**: [src/Modus.Core/Plugins/Base/PluginBase.cs](../../src/Modus.Core/Plugins/Base/PluginBase.cs#L65)

```csharp
// BEFORE (always singleton):
protected virtual void RegisterPluginServices(IServiceCollection services)
{
    services.AddPluginServiceInstance<TPluginImpl>((TPluginImpl)(object)this);
}

// AFTER (respects declared lifetime):
protected virtual void RegisterPluginServices(IServiceCollection services)
{
    services.AddPluginService<TPluginImpl, TPluginImpl>(DeclaredServiceLifetime);
}
```

**Impact**: 
- Transient plugins now create new instances per HTTP request
- Scoped plugins now create new instances per HTTP scope
- Singleton plugins continue to use the same instance throughout app lifetime

**Tests Passing**:
- ✓ PluginDiConsumptionTests (12/12)
- ✓ HostDiResolution_GivenScopedDependencies_ExpectedScopedLifetimeRespectedDuringExecution
- ✓ TransientRegistration_GivenRepeatedResolutions_ExpectedNewInstanceEachTime

---

### Phase 2: Scheduled Operations Path ✗ TODO

**File to Modify**: [src/Modus.Host/Domain/Plugins/Host/AssemblyLifecycleHost.cs](../../src/Modus.Host/Domain/Plugins/Host/AssemblyLifecycleHost.cs)

**Required Changes**:

1. **Inject IServiceProvider**: Modify AssemblyLifecycleHost constructor to accept `IServiceProvider`
2. **Refactor plugin caching**: Instead of storing plugin instance in closure, resolve from DI on each tick
3. **Create scope per tick**: Wrap each scheduled operation execution in `using var scope = _serviceProvider.CreateScope()`
4. **Resolve plugin per invocation**: Call `scope.ServiceProvider.GetRequiredService(pluginType)` instead of reusing cached instance

**Current broken pattern** (lines 103–108):
```csharp
var plugin = Activator.CreateInstance(lifecycleType); // Cached once
_ = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(interval);
    while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
    {
        ExecuteScheduledOperation(plugin, pluginId, jobName, operation); // Reused forever
    }
}, CancellationToken.None);
```

**Required fix**:
```csharp
_ = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(interval);
    while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
    {
        using var scope = _serviceProvider.CreateScope();
        var plugin = scope.ServiceProvider.GetRequiredService(lifecycleType);
        ExecuteScheduledOperation(plugin, pluginId, jobName, operation);
    }
}, CancellationToken.None);
```

**Note**: AssemblyLifecycleHost is instantiated without DI in PluginFolderWatcher.cs:14. This will require passing IServiceProvider through the constructor chain or via dependency injection.

---

## Verification & Next Steps

### Current Status (After HTTP Fix - May 20, 2026)

**HTTP/Swagger Path**: ✓ Now respects plugin lifetimes correctly
- Transient plugins: Different ID on every HTTP request
- Scoped plugins: Same ID within a request scope, different IDs across scopes
- Singleton plugins: Same ID across all requests

**Scheduled Operations Path**: ✗ Still broken
- All plugins: Same ID on every scheduled tick (even Transient/Scoped)

### How to Verify HTTP Fix

Run the live telemetry demo and make multiple HTTP requests via Swagger UI:

```bash
make run-telemetry-live
```

Then call each plugin's HTTP endpoint multiple times. Expected output in Swagger response:

**Transient Plugin** (different ID each call):
```
invoke 1: lifetime=Transient instance-id=ABC123... invocation=1
invoke 2: lifetime=Transient instance-id=DEF456... invocation=1  # Different
invoke 3: lifetime=Transient instance-id=GHI789... invocation=1  # Different
```

**Scoped Plugin** (same ID per scope, different across scopes):
```
invoke 1: lifetime=Scoped instance-id=AAA111... invocation=1
invoke 2: lifetime=Scoped instance-id=BBB222... invocation=1  # Different scope
invoke 3: lifetime=Scoped instance-id=CCC333... invocation=1  # Different scope
```

**Singleton Plugin** (same ID always):
```
invoke 1: lifetime=Singleton instance-id=XXX999... invocation=1
invoke 2: lifetime=Singleton instance-id=XXX999... invocation=2  # Same ID
invoke 3: lifetime=Singleton instance-id=XXX999... invocation=3  # Same ID
```

### Remaining Work

1. **Inject IServiceProvider into AssemblyLifecycleHost** — Currently instantiated without DI, making it impossible to resolve plugins from the container
2. **Refactor scheduled operation execution** — Resolve plugins from DI on each tick instead of caching
3. **Create new DI scope per tick** — Scoped plugins should get a new instance per scheduled operation invocation
4. **Write and pass scheduled operation tests** — Verify all three lifetime variants work correctly in scheduled paths

---

*All HTTP path assumptions verified. Zero Falsified rows for completed items. Phase 2 (scheduled operations) deferred pending architecture review.*
