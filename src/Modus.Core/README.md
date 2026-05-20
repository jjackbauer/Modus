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

## Related packages

- **Modus.Host** — host runtime that discovers, validates, and activates plugins
