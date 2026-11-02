---
kind: rule
mode: agent
description: Before asserting JSON property values in tests, read the JsonSerializerOptions used by the API. WhenWritingNull means null-valued properties are absent from the JSON, not present as "null".
---

# Rule: Check JSON Serialization Options Before Testing

## The Problem

API tests that parse JSON responses with `JsonDocument` and call `GetProperty("field")` will throw `KeyNotFoundException` when the field is null *and* the serializer uses `JsonIgnoreCondition.WhenWritingNull`. The property simply isn't in the JSON.

Common pattern that breaks:
```csharp
// This works when serializer includes nulls
Assert.Equal("null", doc.RootElement.GetProperty("predecessorId").GetString());
// Throws KeyNotFoundException when WhenWritingNull is active
```

## The Rule

**Before writing JSON assertions**, read the `JsonSerializerOptions` used by the API. If `WhenWritingNull` is active, use `TryGetProperty` for optional/nullable fields:

```csharp
// Correct for WhenWritingNull
if (doc.RootElement.TryGetProperty("predecessorId", out var pred))
    Assert.Equal("expected", pred.GetString());
else
    Assert.True(true); // Field absent = null, which is expected
```

Or, alternatively, `Assert.False(doc.RootElement.TryGetProperty("predecessorId", out _))` when you expect the field to be absent.

## Detection

`System.Collections.Generic.KeyNotFoundException` thrown from `JsonElement.GetProperty(string)` on a field that should be present. The stack trace points at the test assertion, not the API code.

## Where to find the options

```csharp
// BonesWebHost.cs
public static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,  // ← this
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
};
```

## Failure Pattern (this session)

```csharp
// Test expected "null" string in JSON
Assert.Equal("null", doc.RootElement.GetProperty("predecessorId").GetString());
// → KeyNotFoundException: The given key was not present in the dictionary
// Root cause: WhenWritingNull suppressed the null-valued field entirely
```
