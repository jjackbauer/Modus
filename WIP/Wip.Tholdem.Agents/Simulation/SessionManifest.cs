using System.Text.Json;

namespace Wip.Tholdem.Agents.Simulation;

/// <summary>
/// Read model of the canonical session manifest written by <see cref="MatchSimulator"/>. A manifest
/// written before the multiway change has no <c>seats</c> member; it parses as the recorded two-seat
/// session, so old session directories stay readable without a migration.
/// </summary>
public sealed record SessionManifest(
    string Schema,
    string SessionId,
    ulong Seed,
    int Hands,
    int Seats,
    long StartingStack,
    IReadOnlyList<long> StartingStacks,
    long SmallBlind,
    long BigBlind,
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> HandHashes)
{
    public const string SchemaV1 = "tholdem.session.v1";

    public const int LegacySeats = 2;

    public static SessionManifest Parse(string json)
    {
        if (json is null)
        {
            throw new FormatException("session-json-null: the session manifest text is null.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException(
                "session-json-invalid: the session manifest is not valid JSON: " + exception.Message,
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("session-json-object: the session manifest must be a JSON object.");
            }

            var schema = RequiredString(root, "schema");
            if (!string.Equals(schema, SchemaV1, StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"session-schema: expected schema '{SchemaV1}', observed '{schema}'.");
            }

            var seats = root.TryGetProperty("seats", out var seatsElement)
                ? ReadInt32(seatsElement, "seats")
                : LegacySeats;
            if (seats is < 2 or > 6)
            {
                throw new FormatException("session-field-range: field 'seats' must be between 2 and 6.");
            }

            var startingStack = RequiredInt64(root, "startingStack");
            var startingStacks = root.TryGetProperty("startingStacks", out var stacksElement)
                ? ReadLongs(stacksElement, "startingStacks", seats)
                : Repeat(startingStack, seats);
            var agents = RequiredStrings(root, "agents");
            if (agents.Count != seats)
            {
                throw new FormatException(
                    $"session-field-range: field 'agents' must hold exactly {seats} names; "
                    + $"observed {agents.Count}.");
            }

            var handHashes = RequiredStrings(root, "handHashes");
            var hands = RequiredInt32(root, "hands");
            if (handHashes.Count != hands)
            {
                throw new FormatException(
                    $"session-field-range: field 'handHashes' must hold exactly {hands} hashes; "
                    + $"observed {handHashes.Count}.");
            }

            return new SessionManifest(
                schema,
                RequiredString(root, "sessionId"),
                RequiredUInt64(root, "seed"),
                hands,
                seats,
                startingStack,
                startingStacks,
                RequiredInt64(root, "smallBlind"),
                RequiredInt64(root, "bigBlind"),
                agents,
                handHashes);
        }
    }

    private static long[] Repeat(long value, int count)
    {
        var values = new long[count];
        Array.Fill(values, value);
        return values;
    }

    private static IReadOnlyList<long> ReadLongs(JsonElement element, string name, int expectedCount)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != expectedCount)
        {
            throw new FormatException(
                $"session-field-range: field '{name}' must hold exactly {expectedCount} integers.");
        }

        var values = new long[expectedCount];
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out var value))
            {
                throw new FormatException($"session-field-type: {name}[{index}] must be a 64-bit integer.");
            }

            values[index] = value;
            index++;
        }

        return values;
    }

    private static IReadOnlyList<string> RequiredStrings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"session-field-type: field '{name}' must be an array of strings.");
        }

        var values = new string[element.GetArrayLength()];
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new FormatException($"session-field-type: {name}[{index}] must be a non-empty string.");
            }

            values[index] = item.GetString()!;
            index++;
        }

        return values;
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"session-field-type: field '{name}' must be a string.");
        }

        return element.GetString()!;
    }

    private static int RequiredInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            throw new FormatException($"session-field-missing: required field '{name}' is missing.");
        }

        return ReadInt32(element, name);
    }

    private static int ReadInt32(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw new FormatException($"session-field-type: field '{name}' must be a 32-bit integer.");
        }

        return value;
    }

    private static long RequiredInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var value))
        {
            throw new FormatException($"session-field-type: field '{name}' must be a 64-bit integer.");
        }

        return value;
    }

    private static ulong RequiredUInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetUInt64(out var value))
        {
            throw new FormatException($"session-field-type: field '{name}' must be an unsigned 64-bit integer.");
        }

        return value;
    }
}
