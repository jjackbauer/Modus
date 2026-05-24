using System.Text.Json;

namespace Modus.Host.IntegrationTests;

internal static class PluginOperationPayload
{
    public static JsonElement AsJsonElement(object? payload)
    {
        if (payload is null)
        {
            throw new InvalidOperationException("Expected non-null payload.");
        }

        if (payload is JsonElement jsonElement)
        {
            return jsonElement;
        }

        return JsonSerializer.SerializeToElement(payload);
    }

    public static string AsRawText(object? payload)
    {
        if (payload is null)
        {
            return string.Empty;
        }

        if (payload is string text)
        {
            return text;
        }

        if (payload is JsonElement jsonElement)
        {
            return jsonElement.GetRawText();
        }

        return JsonSerializer.Serialize(payload);
    }

    public static string? AsStringValue(object? payload)
    {
        if (payload is null)
        {
            return null;
        }

        if (payload is string text)
        {
            return text;
        }

        if (payload is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.String
                ? jsonElement.GetString()
                : jsonElement.GetRawText();
        }

        return JsonSerializer.Serialize(payload);
    }

    public static bool Contains(object? payload, string expectedSubstring, StringComparison comparison)
    {
        return AsRawText(payload).Contains(expectedSubstring, comparison);
    }
}
