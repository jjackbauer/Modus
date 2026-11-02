---
kind: rule
mode: agent
description: When frameworks register types via AddSingleton(typeof(T)), a factory registration added before it is silently overridden. Always register AFTER the framework, or use a different pattern entirely.
---

# Rule: DI Factory Registrations Are Overridden by Type-Based Ones

## The Problem

In .NET DI, when you have:

```csharp
// Your factory registration (first)
services.AddSingleton<MyService>(sp => new MyService(dep));

// Framework's type-based registration (second)
services.AddSingleton(typeof(MyService));
```

The framework's type-based registration **overrides** your factory. The DI container uses the **last** registration for a given service type. The framework will resolve `MyService` using its parameterless (or longest-satisfiable) constructor, and your factory will never be called.

## This Happens With

- `builder.AddAgent<T>()` → internally calls `Services.AddSingleton(typeof(T))`
- `builder.AddTool<T>()` → internally calls `Services.AddSingleton(typeof(T))`
- Any framework that registers capabilities by type

## Detection

If you've added a factory registration and the dependency is always null at runtime, the type-based registration is overriding you.

To verify, add a `Console.WriteLine` in both your factory lambda and the class constructor. Only one will fire.

## Solutions (in order of reliability)

1. **Static singleton** — set `Instance = this` in the constructor, access via `ClassName.Instance` everywhere. No DI chain dependency.

2. **Post-registration** — call your factory registration AFTER the framework registration. This only works if you control the startup order.

3. **IServiceProvider injection** — inject `IServiceProvider` and resolve lazily. But this is a service locator anti-pattern.

4. **Remove the framework registration and replace it** — if you control the registration code, remove the framework's `AddAgent` call and register your own.
