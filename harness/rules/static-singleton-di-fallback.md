---
kind: rule
mode: agent
description: When DI is opaque (framework-created instances, type-based registrations), a static singleton on the target class is the most reliable integration pattern. Use it when the framework resists constructor injection.
---

# Rule: Static Singleton as DI Fallback

## When to Use

Use a `public static TypeName? Instance` property when:

1. A framework (WipBuilder, ASP.NET, etc.) creates instances via `Services.AddSingleton(type)` (type-based registration) which ignores factory registrations.
2. The framework might use `ActivatorUtilities.CreateInstance` which bypasses DI entirely.
3. You've tried constructor injection and the runtime proves the dependency is null.
4. The class is a true singleton in the process (only one instance exists).

## Pattern

```csharp
public sealed class StrategyPool
{
    /// <summary>
    /// The singleton pool instance for the current process.
    /// Set by DI during startup. Use this when constructor DI is unavailable.
    /// </summary>
    public static StrategyPool? Instance { get; private set; }

    public StrategyPool()
    {
        Instance = this;
    }
}
```

Then in the consumer:

```csharp
var pool = StrategyPool.Instance;
if (pool is not null)
{
    pool.Add(entry);
}
```

## Why This Works

- The property is set in the constructor — the moment DI creates the singleton
- Any code anywhere in the process can access it, regardless of DI chain
- No circular dependency risk
- No framework-specific workarounds needed

## Caveats

- Only for true singletons. Don't use for scoped/transient services.
- Don't use in library code that may be used in multiple processes/appdomains.
- Prefer constructor injection when it actually works. This is a fallback.
