---
kind: rule
mode: agent
description: Before adding a new service type to a route handler lambda, read the host's DI registration to confirm the type is registered. The Web project's AddBonesViewerServices may not be called by the running host.
---

# Rule: Audit Host DI Before Adding Service Parameters

## The Problem

ASP.NET Core minimal API route handlers can accept DI-injected parameters by type. If the type isn't registered in the host's DI container, the delegate factory infers it as `Body (Inferred)` and throws at startup — breaking every route in the application, not just yours.

The error is cryptic:
```
Body was inferred but the method does not allow inferred body parameters.
Parameter: service | Source: Body (Inferred)
```

This happens when the Web project registers the service (`AddBonesViewerServices`) but the actual host process uses its own `AddBonesHostServices` which registers viewer services manually and doesn't call the shared method.

## The Rule

When adding a new `IServiceCollection`-injected parameter to a route handler:

1. **Read the host's service registration method** (e.g., `AddBonesHostServices`, `ConfigureServices`)
2. **Confirm the type is registered** or add it
3. **Never assume** the Web project's `AddBonesViewerServices` is called by the host

## Detection

At startup, `InvalidOperationException: Body was inferred but the method does not allow inferred body parameters`. The parameter named `service` appears as `Body (Inferred)` in the endpoint data source build.

## Failure Pattern (this session)

```csharp
// BonesWebHost.cs — registered here ✓
services.AddSingleton<StrategyPoolJsonService>();

// MapPoolRoutes handler — references it ✓
app.MapGet("/bones/pool", (StrategyPoolJsonService service) => { ... });

// BonesHostApplication.cs — calls MapPoolRoutes ✓
BonesWebHost.MapPoolRoutes(app);

// BonesHostServiceCollectionExtensions.cs — MISSING registration ✗
// service is NOT in AddBonesHostServices
// → every route in the app breaks with 500
```

## Correct Approach

```csharp
// Before writing the route handler, read the host's service registration:
// Read: WIP/Wip.Bones.Host/BonesHostServiceCollectionExtensions.cs
// → Find: services.AddSingleton<StrategyPoolJsonService>() ← add if missing
```
