using System.Text.Json;
using Wip.Bones.Agents.Play;

namespace Wip.Bones.ModelProviders.DeepSeek;

public static class BonesPlayTurnResponseParser
{
    public static string? Parse(string content, IReadOnlyList<BonesPlayTurnOption> allowedMoves)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(allowedMoves);

        if (allowedMoves.Count == 0)
            return null;

        var allowedIds = new HashSet<string>(
            allowedMoves.Select(option => option.MoveId.Value),
            StringComparer.Ordinal);

        var jsonMoveId = TryParseJsonMoveId(content);
        if (jsonMoveId is not null && allowedIds.Contains(jsonMoveId))
            return jsonMoveId;

        var trimmed = content.Trim();
        if (allowedIds.Contains(trimmed))
            return trimmed;

        if (IsPassOnlyLegalSet(allowedMoves) &&
            string.Equals(trimmed, "pass", StringComparison.OrdinalIgnoreCase))
            return allowedMoves[0].MoveId.Value;

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (allowedIds.Contains(line))
                return line;

            if (IsPassOnlyLegalSet(allowedMoves) &&
                string.Equals(line, "pass", StringComparison.OrdinalIgnoreCase))
                return allowedMoves[0].MoveId.Value;
        }

        return null;
    }

    private static bool IsPassOnlyLegalSet(IReadOnlyList<BonesPlayTurnOption> allowedMoves) =>
        allowedMoves.Count == 1 &&
        allowedMoves[0].Description.Contains("pass", StringComparison.OrdinalIgnoreCase);

    private static string? TryParseJsonMoveId(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content.Trim());
            if (document.RootElement.TryGetProperty("moveId", out var moveIdElement) &&
                moveIdElement.ValueKind == JsonValueKind.String)
            {
                return moveIdElement.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}