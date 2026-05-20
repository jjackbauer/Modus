---
applyTo: "**/*.cs"
---

# Rule: No Primitive Obsession for Semantic Identifiers

## Context

`Modus.Core` defines typed readonly record structs for every semantic identifier in the plugin
framework. Using raw `string` (or other primitives) in place of these types is a violation of this
rule and must be corrected or rejected in code review.

## Canonical Value Object Types

| Concept | Type | Location |
|---|---|---|
| Plugin identifier | `PluginId` | `Modus.Core/Plugins/Types/` |
| Operation name | `OperationName` | `Modus.Core/Plugins/Types/` |
| Contract name | `ContractName` | `Modus.Core/Plugins/Types/` |
| Scheduled job name | `JobName` | `Modus.Core/Plugins/Types/` |
| Capability name | `CapabilityName` | `Modus.Core/Plugins/Types/` |
| Correlation identifier | `CorrelationId` | `Modus.Core/Messaging/Types/` |
| Operation response status | `SyncResponseStatus` (enum) | `Modus.Core/Messaging/` |

## Prohibited Patterns

The following usages are prohibited whenever the value carries semantic identity:

```csharp
// PROHIBITED — use PluginId instead
string PluginId { get; }
string pluginId

// PROHIBITED — use OperationName instead
string Operation { get; }
IReadOnlyCollection<string> SupportedOperations { get; }
string operation

// PROHIBITED — use ContractName instead
string ContractName { get; }

// PROHIBITED — use JobName instead
string jobName

// PROHIBITED — use CapabilityName instead
IReadOnlyList<string> Capabilities { get; }
IReadOnlyList<string> DependsOn { get; }
IReadOnlyList<string> MissingCapabilities { get; }

// PROHIBITED — use CorrelationId? instead
string? CorrelationId { get; }

// PROHIBITED — use SyncResponseStatus? instead
string? Status { get; }

// PROHIBITED — use typed generic instead
void Validate(object candidate)
```

## Required Patterns

```csharp
// Correct identifier properties
PluginId PluginId { get; }
OperationName Operation { get; }
ContractName ContractName { get; }
CorrelationId? CorrelationId { get; }

// Correct collections
IReadOnlyCollection<OperationName> SupportedOperations { get; }
IReadOnlyList<CapabilityName> Capabilities { get; }
IReadOnlyList<CapabilityName> DependsOn { get; }
IReadOnlyList<CapabilityName> MissingCapabilities { get; }
IReadOnlyList<OperationName> DeclaredOperations { get; }
IReadOnlyList<PluginId> ActivePluginIds { get; }

// Correct scheduler parameters
void ScheduleRecurring(JobName jobName, TimeSpan interval, OperationName operation);
void ScheduleAt(JobName jobName, DateTimeOffset at, OperationName operation);

// Correct generic validator
TResult Validate<T>(T candidate) where T : class;

// Correct status field
SyncResponseStatus? Status { get; }
```

## Adding a New Semantic Identifier

When a new concept needs an identifier, create a `readonly record struct` following this template.
Do **not** use `string` directly in any interface, record, or DTO.

```csharp
namespace Modus.Core.Plugins.Types; // or Modus.Core.Messaging.Types for messaging concepts

public readonly record struct NewConceptName
{
    public string Value { get; }

    public NewConceptName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}
```

Place the file under `Modus.Core/Plugins/Types/` (plugin domain) or `Modus.Core/Messaging/Types/`
(messaging domain), then register it in the table above.

## Enforcement Scope

This rule applies to:

- All interfaces in `Modus.Core/Plugins/Contracts/`
- All lifecycle context records in `Modus.Core/Plugins/Lifecycle/`
- All messaging types in `Modus.Core/Messaging/`
- All domain types in `Modus.Host/Domain/`
- All plugin base classes in `Modus.Core/Plugins/Base/`
- Any new DTO or record that flows across module or plugin boundaries

This rule does **not** apply to:

- Internal implementation details that never cross a module boundary
- Test helper strings used only within a single test method scope
- Log message interpolation where the typed value is already in scope
