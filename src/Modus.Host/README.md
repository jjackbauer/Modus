# Modus.Host

Host runtime for the [Modus](https://github.com/jackbauer/Modus) modular monolith framework. Discovers, validates, composes, and activates plugins with failure isolation and deterministic lifecycle management.

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
- Scheduled and timer-driven plugins are supported via core lifecycle hooks

## Related packages

- **Modus.Core** — contracts and extension points for plugin authors
