using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// A behavioural profile for an opponent constructed from match history analysis.
/// Tracks frequencies of preferred pip counts, double play tendencies, and pass rates.
/// Used to inject opponent modelling into enhancement prompts.
/// </summary>
public sealed record BonesOpponentProfile
{
    /// <summary>
    /// Maps pip counts to observed frequency (0.0-1.0) of the opponent playing tiles
    /// with that many pips.
    /// </summary>
    public IReadOnlyDictionary<int, double> PreferredPipsFrequencies { get; init; } =
        new Dictionary<int, double>();

    /// <summary>
    /// Frequency with which the opponent plays doubles (0.0-1.0) relative to
    /// total moves made.
    /// </summary>
    public double DoublePlayFrequency { get; init; }

    /// <summary>
    /// How often the opponent passes instead of playing a tile (0.0-1.0).
    /// </summary>
    public double PassRate { get; init; }

    /// <summary>
    /// Total number of moves analyzed from the opponent's match history.
    /// </summary>
    public int TotalMovesAnalyzed { get; init; }

    /// <summary>
    /// Computes a BonesOpponentProfile from a match history transcript markdown.
    /// The markdown is parsed for per-turn events:
    ///   - "Plays [X|Y]" (tile play, with pipe-separated pips)
    ///   - "Plays double [X|X]" (double tile play)
    ///   - "Passes" (pass event)
    /// </summary>
    public static BonesOpponentProfile FromTranscriptMarkdown(BonesPlayerId opponentId, string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var pipCounts = new Dictionary<int, int>();
        var doubleCount = 0;
        var passCount = 0;
        var totalMoves = 0;

        var lines = markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Match lines like "Seat 2: Plays [3|5]" or "Seat 2: Plays double [4|4]"
            if (!trimmed.StartsWith($"Seat {opponentId.Seat}:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (trimmed.Contains("Passes", StringComparison.OrdinalIgnoreCase))
            {
                passCount++;
                totalMoves++;
                continue;
            }

            // Extract tile pip values from [X|Y] pattern
            var pipMatch = System.Text.RegularExpressions.Regex.Match(
                trimmed,
                @"\[(\d+)\|(\d+)\]");

            if (!pipMatch.Success)
                continue;

            var isDouble = trimmed.Contains("double", StringComparison.OrdinalIgnoreCase);
            if (isDouble)
                doubleCount++;

            if (int.TryParse(pipMatch.Groups[1].Value, out var pip1) &&
                int.TryParse(pipMatch.Groups[2].Value, out var pip2))
            {
                var totalPips = pip1 + pip2;
                pipCounts.TryGetValue(totalPips, out var existing);
                pipCounts[totalPips] = existing + 1;
            }

            totalMoves++;
        }

        if (totalMoves == 0)
        {
            return new BonesOpponentProfile
            {
                TotalMovesAnalyzed = 0,
            };
        }

        var frequencies = pipCounts.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value / (double)totalMoves);

        return new BonesOpponentProfile
        {
            PreferredPipsFrequencies = frequencies,
            DoublePlayFrequency = doubleCount / (double)totalMoves,
            PassRate = passCount / (double)totalMoves,
            TotalMovesAnalyzed = totalMoves,
        };
    }

    /// <summary>
    /// Builds a formatted markdown summary of the opponent profile for inclusion
    /// in enhancement prompts.
    /// </summary>
    public string ToPromptSummary(BonesPlayerId opponentId)
    {
        if (TotalMovesAnalyzed == 0)
            return $"- **Seat {opponentId.Seat}**: No move data available.";

        var topPips = PreferredPipsFrequencies
            .OrderByDescending(kvp => kvp.Value)
            .Take(3)
            .Select(kvp => $"  - {kvp.Key} pips: {kvp.Value:P1}")
            .ToList();

        var topPipsSection = topPips.Count > 0
            ? "\n" + string.Join("\n", topPips)
            : string.Empty;

        return $"- **Seat {opponentId.Seat}** ({TotalMovesAnalyzed} moves analyzed):\n" +
            $"  - Double play frequency: {DoublePlayFrequency:P1}\n" +
            $"  - Pass rate: {PassRate:P1}\n" +
            $"  - Top preferred pip counts:{topPipsSection}";
    }
}
