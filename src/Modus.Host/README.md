# Modus.Host

Host runtime for the [Modus](https://github.com/jackbauer/Modus) modular monolith framework. Discovers, validates, composes, and activates plugins with failure isolation and deterministic lifecycle management.

## Built-in HTTP Surface

Every plugin operation is mapped to `POST /api/{pluginId}/{operation}` by `PluginEndpointMapper`. The host also registers OpenAPI through `AddOpenApi()` and `app.MapOpenApi()`, which means `/openapi/v1.json` and Swagger UI are available as soon as the host starts.

## Embedded Host Integration

Use `AddModusPluginHosting` when Modus is embedded inside another application or when you want the host to discover plugin assemblies and wire runtime services for you.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Modus.Host.Hosting;

var services = new ServiceCollection();
services.AddModusPluginHosting(options =>
{
	options.PluginsPath = "plugins";
	options.RunOnce = false;
});
```

The host honors the plugin lifetime declarations exposed by `SingletonPlugin<T>`, `ScopedPlugin<T>`, and `TransientPlugin<T>`. Plugin contract interfaces that extend `IPluginContract` are registered automatically, and `RegisterPluginServices(IServiceCollection services)` remains available for any extra plugin-only dependencies.

## Installation

```sh
dotnet tool install -g Modus.Host
```

## Usage

Point the host at a folder containing compiled plugin assemblies:

```sh
modus plugins/
```

The host will:
1. **Discover** — scan the folder for assemblies that expose plugin descriptors
2. **Validate** — enforce contract requirements and capability constraints
3. **Register** — wire plugin dependencies into the DI container
4. **Activate** — start plugins in dependency order
5. **Isolate** — contain faults so a failing plugin does not halt healthy ones

## Plugin authoring

Install [Modus.Core](https://www.nuget.org/packages/Modus.Core) in your plugin project and implement the plugin contracts:

```sh
dotnet add package Modus.Core
```

Drop the compiled plugin assembly into the plugins folder and restart the host (or trigger a hot-load if enabled).

## Architecture

```
plugins/
├── MyPlugin.dll        ← implements Modus.Core contracts
└── AnotherPlugin.dll

modus plugins/          ← discovers, validates, activates
```

- Plugins communicate through domain events, never by direct assembly reference
- The host is the only composition root — plugins never reach into host internals
- Scheduled and timer-driven plugins are supported via `IPluginScheduledEvents.RegisterSchedules(IPluginScheduler scheduler)` and host-visible diagnostics

The host runtime composes dependencies, orchestrates lifecycle stages, and enforces boundary validation before activation.

## Lifecycle and Runtime Extension Points

### Registration and composition flow

1. `AddModusPluginHosting` initializes hosting options and runtime composition.
2. `AddPluginHostingCore` registers portability contracts and host-core defaults.
3. `AddDiscoveredPlugins` wires plugin registrars and capability contracts into DI.
4. `AddModusPluginHostingRuntime` adds discovery, validation, watcher, and host runner services.

### Runtime lifecycle hooks

`IPluginLifecycle` hooks are executed as deterministic runtime stages:

1. `Load(PluginLoadContext)`
2. `Start(PluginStartContext)`
3. `Stop(PluginStopContext)`
4. `Unload(PluginUnloadContext)`

### REST endpoint mapping and OpenAPI

`PluginEndpointMapper` joins plugin contract metadata and supported operations to register HTTP endpoints for the live host. Every declared operation is exposed as `POST /api/{pluginId}/{operation}`, and the same pipeline makes the generated OpenAPI document visible at `/openapi/v1.json` with Swagger UI at `/swagger`.

### Runtime resolution helpers

- `HostRunner.StartAsync` starts runtime scanning and watcher registration for the configured plugins path.
- `TryResolvePluginsByContractInterfaceName` resolves active plugin instances by contract interface full name.
- `TryResolvePluginByTypeName` resolves a plugin instance by concrete type full name.

## Diagnostics and Troubleshooting

Use runtime stages as the primary troubleshooting spine:

- discovery: verify plugin path and descriptor onboarding inputs
- validation: inspect contract, capability, and operation catalog failures
- activate: inspect lifecycle hook failures for the plugin identifier in error output

Failure isolation is intentional: one plugin fault should not terminate healthy plugin activation. Prefer fixing or disabling only the failing plugin and re-running host startup validation.

## Related packages

- **Modus.Core** — contracts and extension points for plugin authors
