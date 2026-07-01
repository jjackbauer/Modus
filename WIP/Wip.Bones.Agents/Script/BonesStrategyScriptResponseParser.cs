using Wip.Bones.Engine;

namespace Wip.Bones.Agents.Script;

public static class BonesStrategyScriptResponseParser
{
    public static string? TryParseScriptSource(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        foreach (var fencedBlock in ExtractFencedCSharpBlocks(content))
        {
            if (ContainsPlayerSlotImplementation(fencedBlock))
                return fencedBlock;
        }

        return null;
    }

    private static IEnumerable<string> ExtractFencedCSharpBlocks(string content)
    {
        const string fenceOpen = "```";
        var searchStart = 0;

        while (searchStart < content.Length)
        {
            var fenceIndex = content.IndexOf(fenceOpen, searchStart, StringComparison.Ordinal);
            if (fenceIndex < 0)
                yield break;

            var languageStart = fenceIndex + fenceOpen.Length;
            var lineEnd = content.IndexOf('\n', languageStart);
            if (lineEnd < 0)
                yield break;

            var language = content[languageStart..lineEnd].Trim();
            if (!IsCSharpFenceLanguage(language))
            {
                searchStart = lineEnd + 1;
                continue;
            }

            var blockStart = lineEnd + 1;
            var closingFenceIndex = content.IndexOf(fenceOpen, blockStart, StringComparison.Ordinal);
            if (closingFenceIndex < 0)
                yield break;

            var block = content[blockStart..closingFenceIndex].Trim();
            if (block.Length > 0)
                yield return block;

            searchStart = closingFenceIndex + fenceOpen.Length;
        }
    }

    private static bool IsCSharpFenceLanguage(string language)
        => string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(language, "cs", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsPlayerSlotImplementation(string source)
        => source.Contains(BonesStrategyScriptApiReference.InterfaceName, StringComparison.Ordinal);
}