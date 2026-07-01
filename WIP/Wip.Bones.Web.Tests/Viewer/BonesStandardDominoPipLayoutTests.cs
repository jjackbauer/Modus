using System.Text.RegularExpressions;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests.Viewer;

public sealed class BonesStandardDominoPipLayoutTests
{
    private static readonly IReadOnlyDictionary<int, int[]> ExpectedLayouts = new Dictionary<int, int[]>
    {
        [0] = [],
        [1] = [4],
        [2] = [2, 6],
        [3] = [2, 4, 6],
        [4] = [0, 2, 6, 8],
        [5] = [0, 2, 4, 6, 8],
        [6] = [0, 3, 6, 2, 5, 8],
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.StandardPipLayoutSync)]
    public void BonesStandardDominoPipLayout_GivenPipValue_ExpectedGridPositionsMatchStandardDoubleSix(int pip)
    {
        Assert.Equal(ExpectedLayouts[pip], BonesStandardDominoPipLayout.GetGridPositions(pip));
    }

    [Fact]
    public void BonesStandardDominoPipLayout_GivenOutOfRangePipValue_ExpectedArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BonesStandardDominoPipLayout.GetGridPositions(7));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void BonesDominoTileMarkup_GivenHandTileHalf_ExpectedRenderedPipPositionsMatchStandardLayout(int pip)
    {
        var tile = new BonesHandTileView { LowPip = pip, HighPip = 0, OwnerSeat = 1 };
        var markup = BonesDominoTileMarkup.RenderHandTile(tile, new Dictionary<int, string> { [1] = "#e63946" });

        var halfPattern = "data-pip=\"" + pip + "\"";
        var halfStart = markup.IndexOf(halfPattern, StringComparison.Ordinal);
        Assert.True(halfStart >= 0);

        var halfEnd = markup.IndexOf("</div>", halfStart, StringComparison.Ordinal);
        var halfMarkup = markup[halfStart..halfEnd];
        var positions = Regex.Matches(halfMarkup, "data-position=\"(\\d+)\"")
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(ExpectedLayouts[pip], positions);
    }

    [Fact]
    public async Task BonesViewerCss_GivenPipGridPositions_ExpectedAllNineGridCellsDefined()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var css = await (await client.GetAsync("/viewer/viewer.css")).Content.ReadAsStringAsync();

        for (var position = 0; position <= 8; position++)
            Assert.Contains(".pip[data-position=\"" + position + "\"]", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0: []")]
    [InlineData("1: [4]")]
    [InlineData("2: [2, 6]")]
    [InlineData("3: [2, 4, 6]")]
    [InlineData("4: [0, 2, 6, 8]")]
    [InlineData("5: [0, 2, 4, 6, 8]")]
    [InlineData("6: [0, 3, 6, 2, 5, 8]")]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.StandardPipLayoutSync)]
    public async Task BonesViewerScript_GivenPipPositions_ExpectedMatchesStandardDominoLayout(string expectedEntry)
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();
        Assert.Contains(expectedEntry, script, StringComparison.Ordinal);
    }
}