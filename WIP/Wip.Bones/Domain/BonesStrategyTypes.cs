using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wip.Bones.Domain;

[JsonConverter(typeof(BonesStrategyKindJsonConverter))]
public enum BonesStrategyKind
{
    Script = 1,
    Markdown = 2,
}

[JsonConverter(typeof(BonesPromotionStatusJsonConverter))]
public enum BonesPromotionStatus
{
    Active = 1,
    Candidate = 2,
    Rejected = 3,
    Superseded = 4,
}

public static class BonesStrategyKindParser
{
    public static bool TryParse(string? value, out BonesStrategyKind kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            kind = default;
            return false;
        }

        if (!Enum.TryParse(value, ignoreCase: false, out kind) || !Enum.IsDefined(kind))
        {
            kind = default;
            return false;
        }

        return true;
    }

    public static bool TryParse(int value, out BonesStrategyKind kind)
    {
        if (!Enum.IsDefined(typeof(BonesStrategyKind), value))
        {
            kind = default;
            return false;
        }

        kind = (BonesStrategyKind)value;
        return true;
    }
}

public static class BonesPromotionStatusParser
{
    public static bool TryParse(string? value, out BonesPromotionStatus status)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            status = default;
            return false;
        }

        if (!Enum.TryParse(value, ignoreCase: false, out status) || !Enum.IsDefined(status))
        {
            status = default;
            return false;
        }

        return true;
    }

    public static bool TryParse(int value, out BonesPromotionStatus status)
    {
        if (!Enum.IsDefined(typeof(BonesPromotionStatus), value))
        {
            status = default;
            return false;
        }

        status = (BonesPromotionStatus)value;
        return true;
    }
}

internal sealed class BonesStrategyKindJsonConverter : JsonConverter<BonesStrategyKind>
{
    public override BonesStrategyKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (BonesStrategyKindParser.TryParse(text, out var kind))
                return kind;

            throw new JsonException($"Unknown BonesStrategyKind value '{text}'.");
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            if (BonesStrategyKindParser.TryParse(number, out var kind))
                return kind;

            throw new JsonException($"Unknown BonesStrategyKind value '{number}'.");
        }

        throw new JsonException("Expected string or number for BonesStrategyKind.");
    }

    public override void Write(Utf8JsonWriter writer, BonesStrategyKind value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

internal sealed class BonesPromotionStatusJsonConverter : JsonConverter<BonesPromotionStatus>
{
    public override BonesPromotionStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (BonesPromotionStatusParser.TryParse(text, out var status))
                return status;

            throw new JsonException($"Unknown BonesPromotionStatus value '{text}'.");
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            if (BonesPromotionStatusParser.TryParse(number, out var status))
                return status;

            throw new JsonException($"Unknown BonesPromotionStatus value '{number}'.");
        }

        throw new JsonException("Expected string or number for BonesPromotionStatus.");
    }

    public override void Write(Utf8JsonWriter writer, BonesPromotionStatus status, JsonSerializerOptions options)
        => writer.WriteStringValue(status.ToString());
}