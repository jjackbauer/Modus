using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wip.Bones.Domain;

[JsonConverter(typeof(BonesPipCountJsonConverter))]
public readonly record struct BonesPipCount
{
    public const int MinValue = 0;
    public const int MaxValue = 6;

    public BonesPipCount(int value)
    {
        if (value < MinValue || value > MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Pip count must be between {MinValue} and {MaxValue}.");

        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString();
}

[JsonConverter(typeof(BonesTileJsonConverter))]
public readonly record struct BonesTile
{
    public BonesTile(BonesPipCount firstPip, BonesPipCount secondPip)
    {
        if (firstPip.Value <= secondPip.Value)
        {
            LowPip = firstPip;
            HighPip = secondPip;
        }
        else
        {
            LowPip = secondPip;
            HighPip = firstPip;
        }
    }

    public BonesPipCount LowPip { get; }

    public BonesPipCount HighPip { get; }

    public int TotalPips => LowPip.Value + HighPip.Value;

    public bool IsDouble => LowPip.Value == HighPip.Value;
}

[JsonConverter(typeof(BonesChainTileJsonConverter))]
public readonly record struct BonesChainTile
{
    public BonesChainTile(BonesTile tile, BonesPipCount chainLeftPip, BonesPipCount chainRightPip)
    {
        if (!ContainsPip(tile, chainLeftPip) || !ContainsPip(tile, chainRightPip))
            throw new ArgumentException("Chain facings must be pips of the tile identity.", nameof(chainLeftPip));

        if (tile.IsDouble)
        {
            if (chainLeftPip.Value != chainRightPip.Value)
                throw new ArgumentException("Double tiles must have equal chain facings.", nameof(chainRightPip));
        }
        else if (chainLeftPip.Value == chainRightPip.Value)
        {
            throw new ArgumentException("Non-double tiles must have distinct chain facings.", nameof(chainRightPip));
        }

        Tile = tile;
        ChainLeftPip = chainLeftPip;
        ChainRightPip = chainRightPip;
    }

    public BonesTile Tile { get; }

    public BonesPipCount ChainLeftPip { get; }

    public BonesPipCount ChainRightPip { get; }

    private static bool ContainsPip(BonesTile tile, BonesPipCount pip) =>
        pip.Value == tile.LowPip.Value || pip.Value == tile.HighPip.Value;
}

[JsonConverter(typeof(BonesBoardEndJsonConverter))]
public readonly record struct BonesBoardEnd
{
    public BonesBoardEnd(BonesPipCount pip)
    {
        Pip = pip;
    }

    public BonesPipCount Pip { get; }
}

[JsonConverter(typeof(BonesHandJsonConverter))]
public sealed record BonesHand
{
    public BonesHand(IReadOnlyList<BonesTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        Tiles = tiles.ToImmutableArray();
    }

    public ImmutableArray<BonesTile> Tiles { get; }

    public int TotalPips => Tiles.Sum(static tile => tile.TotalPips);
}

public sealed record BonesTableConfig
{
    public const int FixedPlayerCount = 4;

    public static BonesTableConfig Default { get; } = new();

    public int PlayerCount => FixedPlayerCount;

    public void EnsurePlayerCount(int playerCount)
    {
        if (playerCount != FixedPlayerCount)
            throw new ArgumentException($"Player count must be {FixedPlayerCount}.", nameof(playerCount));
    }
}

internal sealed class BonesPipCountJsonConverter : JsonConverter<BonesPipCount>
{
    public override BonesPipCount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetInt32());

    public override void Write(Utf8JsonWriter writer, BonesPipCount value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Value);
}

internal sealed class BonesTileJsonConverter : JsonConverter<BonesTile>
{
    public override BonesTile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of object.");

        var lowPip = default(BonesPipCount);
        var highPip = default(BonesPipCount);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new BonesTile(lowPip, highPip);

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected property name.");

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "LowPip":
                    lowPip = JsonSerializer.Deserialize<BonesPipCount>(ref reader, options);
                    break;
                case "HighPip":
                    highPip = JsonSerializer.Deserialize<BonesPipCount>(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Expected end of object.");
    }

    public override void Write(Utf8JsonWriter writer, BonesTile value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("LowPip");
        JsonSerializer.Serialize(writer, value.LowPip, options);
        writer.WritePropertyName("HighPip");
        JsonSerializer.Serialize(writer, value.HighPip, options);
        writer.WriteEndObject();
    }
}

internal sealed class BonesChainTileJsonConverter : JsonConverter<BonesChainTile>
{
    public override BonesChainTile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of object.");

        var tile = default(BonesTile);
        var chainLeftPip = default(BonesPipCount);
        var chainRightPip = default(BonesPipCount);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new BonesChainTile(tile, chainLeftPip, chainRightPip);

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected property name.");

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "Tile":
                    tile = JsonSerializer.Deserialize<BonesTile>(ref reader, options);
                    break;
                case "ChainLeftPip":
                    chainLeftPip = JsonSerializer.Deserialize<BonesPipCount>(ref reader, options);
                    break;
                case "ChainRightPip":
                    chainRightPip = JsonSerializer.Deserialize<BonesPipCount>(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Expected end of object.");
    }

    public override void Write(Utf8JsonWriter writer, BonesChainTile value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Tile");
        JsonSerializer.Serialize(writer, value.Tile, options);
        writer.WritePropertyName("ChainLeftPip");
        JsonSerializer.Serialize(writer, value.ChainLeftPip, options);
        writer.WritePropertyName("ChainRightPip");
        JsonSerializer.Serialize(writer, value.ChainRightPip, options);
        writer.WriteEndObject();
    }
}

internal sealed class BonesBoardEndJsonConverter : JsonConverter<BonesBoardEnd>
{
    public override BonesBoardEnd Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of object.");

        var pip = default(BonesPipCount);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new BonesBoardEnd(pip);

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected property name.");

            var propertyName = reader.GetString();
            reader.Read();

            if (propertyName == "Pip")
                pip = JsonSerializer.Deserialize<BonesPipCount>(ref reader, options);
            else
                reader.Skip();
        }

        throw new JsonException("Expected end of object.");
    }

    public override void Write(Utf8JsonWriter writer, BonesBoardEnd value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Pip");
        JsonSerializer.Serialize(writer, value.Pip, options);
        writer.WriteEndObject();
    }
}

internal sealed class BonesHandJsonConverter : JsonConverter<BonesHand>
{
    public override BonesHand Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var tiles = JsonSerializer.Deserialize<List<BonesTile>>(ref reader, options) ?? [];
        return new BonesHand(tiles);
    }

    public override void Write(Utf8JsonWriter writer, BonesHand value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value.Tiles, options);
}