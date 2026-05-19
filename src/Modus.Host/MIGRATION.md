# Modus.Host Migration Notes

This guide shows how to move from the console host entrypoint to embedded host wiring while preserving startup semantics and diagnostics.

## Hosting Plugins Using Extensions

Use extension-based registration as the default hosting model for both CLI and embedded applications.

### CLI host (continuous)

Run the host and keep it alive until process cancellation:

```bash
dotnet run --project src/Modus.Host/Modus.Host.csproj -- plugins
```

- Pass the plugins path as the first non-flag argument.
- Omit `--run-once` to keep the watcher and host process active.
- Stop with Ctrl+C.

### CLI host (single-run startup validation)

Run startup and exit with the resulting health status:

```bash
dotnet run --project src/Modus.Host/Modus.Host.csproj -- plugins --run-once
```

### Embedded host in another .NET app

Use the same extension surface as the console host:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Modus.Host.Hosting;
using Modus.Host.Plugins;

var services = new ServiceCollection();
services.AddModusPluginHosting(options =>
{
	options.PluginsPath = "plugins";
	options.RunOnce = false;
});

await using var provider = services.BuildServiceProvider();
var runner = provider.GetRequiredService<HostRunner>();
var result = await runner.StartAsync(CancellationToken.None);

foreach (var diagnostic in result.Diagnostics)
{
	Console.WriteLine(diagnostic);
}
```

### Extension API summary

- `AddPluginHostingCore(IServiceCollection)` registers portability contracts and options in Core.
- `AddModusPluginHosting(IServiceCollection, Action<PluginHostingOptions>?)` wires host runtime services and options delegates.
- `HostRunner.StartAsync(CancellationToken)` uses configured options from DI.
- `HostRunner.StartAsync(string pluginsPath, CancellationToken)` supports explicit path overrides per call.

## Plugins Folder Refactoring Structure

The Plugins domain was reorganized from a flat folder into concern-focused subfolders to keep host composition code easier to navigate and maintain.

| Subfolder | Primary Classes | Rationale |
|---|---|---|
| Host/ | HostRunner, HostStartResult, AssemblyLifecycleHost | Keep host startup entry points and high-level orchestration together. |
| Scanning/ | PluginDiscoveryService, InMemoryPluginDiscoveryService, PluginFolderWatcher, PluginLoader, InMemoryPluginLoader | Isolate discovery and loading flow from runtime lifecycle concerns. |
| Descriptors/ | PluginDescriptor, PluginSpec, PluginOnboardingResult, PluginWatcherStartResult | Group plugin metadata and onboarding result contracts in one location. |
| Validation/ | PluginValidationService, PluginIsolationBoundary, PluginQuarantineStore | Centralize plugin safety gates, isolation checks, and quarantine boundaries. |
| Lifecycle/ | PluginLifecycleOrchestrator, PluginUnloadCoordinator, PluginRollbackCoordinator, InMemoryLifecycleEngine, InMemoryHostRuntime, PluginRetryPolicy, RegistrationTransactionLog | Keep activation, retry, rollback, and runtime lifecycle transitions cohesive. |
| Results/ | EventDispatchResult, LifecycleResult | Separate result models from orchestration and side-effecting behavior. |

### Navigation Guidance

- Start in Host/ when tracing startup behavior and process-level health results.
- Follow Scanning/ then Validation/ when investigating discovery and admissibility issues.
- Use Descriptors/ when reasoning about plugin metadata shape and onboarding outcomes.
- Use Lifecycle/ when debugging activation, retries, unload, rollback, or runtime coordination.
- Use Results/ to inspect strongly typed outcomes returned by host lifecycle operations.

## Migration Guide: Console Host to Embedded Host

Use the same plugin path and startup contract in both approaches.

| Console host usage | Embedded host usage |
|---|---|
| ```bash
| dotnet run --project src/Modus.Host/Modus.Host.csproj -- plugins
| ``` | ```csharp
| using Microsoft.Extensions.DependencyInjection;
| using Modus.Host.Hosting;
|
| var services = new ServiceCollection();
| var pluginsPath = "plugins";
| services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);
| await using var provider = services.BuildServiceProvider();
| var runner = provider.GetRequiredService<HostRunner>();
| var result = await runner.StartAsync(CancellationToken.None);
| ``` |

Equivalent behavior notes:

- Both flows register watcher startup and plugin runtime activation through the same extension APIs.
- Both flows honor plugin path overrides and produce deterministic startup diagnostics.
- Both flows report healthy or unhealthy startup using the same HostRunner start result shape.

## Troubleshooting Startup Failures

If startup reports unhealthy, inspect deterministic diagnostics first.

Expected failure marker examples:

- stage=startup outcome=failure reason=plugins directory missing path=<resolved path>
- stage=startup pipeline=plugin-loader outcome=initialized

Troubleshooting checklist:

1. Verify the path passed to AddModusPluginHosting exists before startup.
2. Confirm the embedded app resolves HostRunner from the same service provider where AddModusPluginHosting was called.
3. If using a relative path, normalize against the process working directory and compare with the resolved diagnostics path.
4. Keep run-once and cancellation semantics unchanged while migrating to avoid hiding startup failures.

## Recommended Validation Commands

After wiring or migration updates, run:

```bash
dotnet build src/Modus.Host/Modus.Host.csproj
dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --no-build
```
