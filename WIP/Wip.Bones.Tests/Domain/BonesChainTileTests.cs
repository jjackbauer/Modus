using System.Text.Json;
using Wip.Bones.Domain;
using Xunit;

namespace Wip.Bones.Tests.Domain;

public sealed class BonesChainTileTests
{
    private const string ChecklistItem = BonesBoardOrientationRequirementsChecklistItems.ChainTileType;

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData(1, 3, 3, 1)]
    [InlineData(1, 3, 1, 3)]
    [InlineData(3, 3, 3, 3)]
    [InlineData(0, 6, 0, 6)]
    [InlineData(2, 5, 5, 2)]
    public void BonesChainTile_GivenIdentityAndFacings_ExpectedCanonicalTileUnchanged(
        int lowPip,
        int highPip,
        int chainLeftPip,
        int chainRightPip)
    {
        var identity = new BonesTile(new BonesPipCount(lowPip), new BonesPipCount(highPip));
        var chainTile = new BonesChainTile(
            identity,
            new BonesPipCount(chainLeftPip),
            new BonesPipCount(chainRightPip));

        Assert.Equal(new BonesPipCount(lowPip), chainTile.Tile.LowPip);
        Assert.Equal(new BonesPipCount(highPip), chainTile.Tile.HighPip);
        Assert.Equal(new BonesPipCount(chainLeftPip), chainTile.ChainLeftPip);
        Assert.Equal(new BonesPipCount(chainRightPip), chainTile.ChainRightPip);

        var json = JsonSerializer.Serialize(chainTile);
        var roundTrip = JsonSerializer.Deserialize<BonesChainTile>(json);

        Assert.Equal(chainTile, roundTrip);
    }
}