# Modus.Core

Core contracts, plugin interfaces, and extension points for the [Modus](https://github.com/jackbauer/Modus) modular monolith framework.

## What's in this package

- **Plugin contracts** — stable interfaces that plugins implement (`IPlugin`, `IPluginDescriptor`, lifecycle hooks)
- **Extension points** — abstractions for registering capabilities, operations, and scheduled work
- **Domain events** — base types for inter-module communication without direct coupling
- **Messaging abstractions** — event bus contracts for decoupled module communication
- **Hosting abstractions** — contracts consumed by the host runtime during discovery, validation, and activation

## Usage

Reference this package from your plugin projects:

```sh
dotnet add package Modus.Core
```

Implement a plugin by satisfying the core contracts:

```csharp
public class MyPlugin : IPlugin
{
    public PluginDescriptor Descriptor => new("MyPlugin", "1.0.0");

    public void Register(IPluginRegistrationContext context) { ... }
    public void Activate(IPluginActivationContext context) { ... }
}
```

## Design principles

- Contracts are stable and versioned — plugins depend on `Modus.Core`, never on host internals
- No runtime dependencies beyond `Microsoft.Extensions.DependencyInjection.Abstractions`
- All extension points are interface-based for testability and isolation

Contracts are stable and versioned; plugins depend on Modus.Core contracts, never on Modus.Host internals.

## Public API Reference

### `IPluginContract`

- Intent: define stable identity and contract versioning for every plugin capability.
- Usage: implement `PluginId`, `ContractName`, and `ContractVersion` on plugin contracts.
- Constraints: values must be deterministic and safe to compare with ordinal semantics.

### `IPluginLifecycle`

- Intent: provide deterministic runtime hooks used by host activation flow.
- Usage: implement `Load`, `Start`, `Stop`, and `Unload` for startup and shutdown stages.
- Constraints: each hook must validate input context and avoid hidden cross-plugin coupling.

### `IPluginDependencyRegister`

- Intent: register plugin dependencies through DI without leaking host internals.
- Usage: call `services.AddPluginService<TService, TImplementation>(...)` from `Register`.
- Constraints: registrations must be idempotent and consistent with declared plugin lifetime.

### `IPluginOperationCatalog`

- Intent: expose the deterministic operation set owned by a plugin capability.
- Usage: return `SupportedOperations` as an immutable, stable set of operation names.
- Constraints: operation names should be non-empty, unique, and stable across process restarts.

### `IPluginScheduledEvents` and `IPluginScheduler`

- Intent: define timer and schedule integration points for recurring or point-in-time plugin operations.
- Usage: call `ScheduleRecurring` or `ScheduleAt` inside `RegisterSchedules(IPluginScheduler scheduler)`.
- Constraints: job names and operation names should be deterministic so diagnostics remain comparable.

## Related packages

- **Modus.Host** — host runtime that discovers, validates, and activates plugins
