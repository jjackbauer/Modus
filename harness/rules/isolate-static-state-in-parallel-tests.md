---
kind: rule
mode: agent
description: When multiple test classes mutate a static singleton like StrategyPool.Instance, xUnit parallel execution causes non-deterministic interference. Use [Collection] to serialize access.
---

# Rule: Isolate Static State in Parallel Tests

## The Problem

xUnit runs different test classes in parallel by default. If multiple test classes read or write the same static singleton (e.g., `StrategyPool.Instance`), tests interfere non-deterministically. Symptoms include:

- `BuildPoolSnapshot` returning entries from a completely different test class
- `Assert.Equal(0, ...)` failing because another test populated the pool
- Flaky failures that pass when run individually but fail in the full suite

## The Rule

**Every test class that touches a shared static singleton must be in the same `[Collection]`.** This forces xUnit to run them sequentially.

### Minimum implementation

```csharp
// Collection definition file (once per solution)
[CollectionDefinition("StrategyPool")]
public class StrategyPoolTestCollection { }

// Every test class that touches StrategyPool.Instance
[Collection("StrategyPool")]
public sealed class StrategyPoolJsonServiceTests { ... }

[Collection("StrategyPool")]
public sealed class BonesPoolApiEndpointTests { ... }

[Collection("StrategyPool")]
public sealed class BonesPoolPageRenderTests { ... }

[Collection("StrategyPool")]
public sealed class BonesPoolClientJsTests { ... }
```

### Additional guard: save/restore in IDisposable

```csharp
public sealed class TestClass : IDisposable
{
    private StrategyPool? _saved;

    public TestClass()
    {
        _saved = StrategyPool.Instance;
        StrategyPool.Instance = null;  // clean slate
    }

    public void Dispose()
    {
        StrategyPool.Instance = _saved;
    }
}
```

## Detection

- Test that passes when run alone (`--filter "FullyQualifiedName~TestName"`) but fails in full suite
- `Assert.Empty()` failure showing entries from a different test class's pool
- Test assertion state that clearly belongs to another test

## Failure Pattern (this session)

```
StrategyPoolJsonServiceTests.BuildPoolSnapshot_GivenEmptyPool_ReturnsEmptyEntriesAndStats:
  Assert.Empty() Failure: Collection was not empty
  Collection: [PoolEntryViewModel { Id = s6 ... } ← from BonesPoolApiEndpointTests!
```
