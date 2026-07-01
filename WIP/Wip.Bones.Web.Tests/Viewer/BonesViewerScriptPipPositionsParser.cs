using System.Globalization;
using System.Text.RegularExpressions;

namespace Wip.Bones.Web.Tests.Viewer;

internal static class BonesViewerScriptPipPositionsParser
{
    private static readonly Regex PipPositionsBlockRegex = new(
        @"const pipPositions = \{([\s\S]*?)\n  \};",
        RegexOptions.Compiled);

    private static readonly Regex PipEntryRegex = new(
        @"(\d+):\s*\[([^\]]*)\]",
        RegexOptions.Compiled);

    public static IReadOnlyDictionary<int, int[]> Parse(string viewerScript)
    {
        var blockMatch = PipPositionsBlockRegex.Match(viewerScript);
        if (!blockMatch.Success)
        {
            throw new InvalidOperationException("pipPositions table not found in viewer.js.");
        }

        var entries = new Dictionary<int, int[]>();
        foreach (Match entry in PipEntryRegex.Matches(blockMatch.Groups[1].Value))
        {
            var count = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
            var positionsText = entry.Groups[2].Value.Trim();
            var positions = positionsText.Length == 0
                ? []
                : positionsText
                    .Split(',')
                    .Select(position => int.Parse(position.Trim(), CultureInfo.InvariantCulture))
                    .ToArray();
            entries[count] = positions;
        }

        return entries;
    }
}