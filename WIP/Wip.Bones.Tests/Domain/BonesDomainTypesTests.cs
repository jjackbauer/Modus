using System.Text.Json;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Domain;

public sealed class BonesDomainTypesTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.DomainTypes;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesTileId_GivenValidValue_ConstructsAndExposesValue()
    {
        const string value = "3-5";
        var tileId = new BonesTileId(value);

        Assert.Equal(value, tileId.Value);
        Assert.Equal(value, tileId.ToString());
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BonesTileId_GivenWhitespace_ThrowsArgumentException(string? value)
    {
        var exception = Assert.Throws<ArgumentException>(() => new BonesTileId(value!));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesTile_GivenTwoPipEnds_EqualityComparesByLowHighPipOrdering()
    {
        var fromLowHigh = new BonesTile(new BonesPipCount(3), new BonesPipCount(5));
        var fromHighLow = new BonesTile(new BonesPipCount(5), new BonesPipCount(3));

        Assert.Equal(fromLowHigh, fromHighLow);
        Assert.Equal(new BonesPipCount(3), fromLowHigh.LowPip);
        Assert.Equal(new BonesPipCount(5), fromLowHigh.HighPip);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesTableConfig_GivenDefault_ExpectedFixesPlayerCountAtFourSeats()
    {
        var config = BonesTableConfig.Default;

        Assert.Equal(4, config.PlayerCount);
        Assert.Equal(BonesTableConfig.FixedPlayerCount, config.PlayerCount);

        var exception = Assert.Throws<ArgumentException>(() => config.EnsurePlayerCount(3));
        Assert.Equal("playerCount", exception.ParamName);
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BonesPlayerId_GivenValidSeat_ConstructsAndExposesSeat(int seat)
    {
        var playerId = new BonesPlayerId(seat);

        Assert.Equal(seat, playerId.Seat);
        Assert.Equal(seat.ToString(), playerId.ToString());
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void BonesPlayerId_GivenInvalidSeat_ThrowsArgumentOutOfRangeException(int seat)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BonesPlayerId(seat));
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void BonesStringIdentifiers_GivenWhitespace_ThrowArgumentException(string? value)
    {
        AssertIdentifierThrows(value, static input => new BonesMoveId(input!));
        AssertIdentifierThrows(value, static input => new BonesGameId(input!));
        AssertIdentifierThrows(value, static input => new BonesStrategyId(input!));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesPipCount_GivenValidValue_ConstructsAndExposesValue()
    {
        var pipCount = new BonesPipCount(6);

        Assert.Equal(6, pipCount.Value);
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData(-1)]
    [InlineData(7)]
    public void BonesPipCount_GivenOutOfRangeValue_ThrowsArgumentOutOfRangeException(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BonesPipCount(value));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardEnd_GivenPipValue_ExposesOpenEndPip()
    {
        var boardEnd = new BonesBoardEnd(new BonesPipCount(4));

        Assert.Equal(new BonesPipCount(4), boardEnd.Pip);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesHand_GivenTiles_ExposesImmutableCopyAndTotalPips()
    {
        var tiles = new[]
        {
            new BonesTile(new BonesPipCount(0), new BonesPipCount(1)),
            new BonesTile(new BonesPipCount(2), new BonesPipCount(3)),
        };

        var hand = new BonesHand(tiles);

        Assert.Equal(2, hand.Tiles.Length);
        Assert.Equal(6, hand.TotalPips);

        tiles[0] = new BonesTile(new BonesPipCount(6), new BonesPipCount(6));
        Assert.NotEqual(tiles[0], hand.Tiles[0]);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDomainTypes_GivenSerializationRoundTrip_PreservesValues()
    {
        var tile = new BonesTile(new BonesPipCount(2), new BonesPipCount(5));
        var hand = new BonesHand([tile]);
        var boardEnd = new BonesBoardEnd(new BonesPipCount(5));
        var config = BonesTableConfig.Default;

        var tileJson = JsonSerializer.Serialize(tile);
        var handJson = JsonSerializer.Serialize(hand);
        var boardEndJson = JsonSerializer.Serialize(boardEnd);
        var configJson = JsonSerializer.Serialize(config);

        var roundTripTile = JsonSerializer.Deserialize<BonesTile>(tileJson);
        var roundTripHand = JsonSerializer.Deserialize<BonesHand>(handJson);
        var roundTripBoardEnd = JsonSerializer.Deserialize<BonesBoardEnd>(boardEndJson);
        var roundTripConfig = JsonSerializer.Deserialize<BonesTableConfig>(configJson);

        Assert.Equal(tile, roundTripTile);
        Assert.Equal(hand.Tiles.Length, roundTripHand!.Tiles.Length);
        Assert.Equal(hand.Tiles[0], roundTripHand.Tiles[0]);
        Assert.Equal(hand.TotalPips, roundTripHand.TotalPips);
        Assert.Equal(boardEnd, roundTripBoardEnd);
        Assert.Equal(config, roundTripConfig);
    }

    private static void AssertIdentifierThrows(string? value, Func<string?, object> factory)
    {
        var exception = Assert.Throws<ArgumentException>(() => factory(value));
        Assert.Equal("value", exception.ParamName);
    }
}