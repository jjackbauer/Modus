using System.Globalization;
using System.Text.RegularExpressions;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Web.Viewer;

internal static partial class BonesMatchTranscriptParser
{
    [GeneratedRegex(@"^Game:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex GameLineRegex();

    [GeneratedRegex(
        @"^- Turn (\d+): seat (\d+) played \((\d+), (\d+)\) on (Left|Right)$",
        RegexOptions.Multiline)]
    private static partial Regex PlayEventRegex();

    [GeneratedRegex(@"^- Turn (\d+): seat (\d+) passed$", RegexOptions.Multiline)]
    private static partial Regex PassEventRegex();

    [GeneratedRegex(@"^- Seat (\d+): (\d+) points$", RegexOptions.Multiline)]
    private static partial Regex FinalScoreRegex();

    [GeneratedRegex(@"^Winner seat: (\d+)$", RegexOptions.Multiline)]
    private static partial Regex WinnerSeatRegex();

    public static ParsedTranscript Parse(string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var gameMatch = GameLineRegex().Match(markdown);
        if (!gameMatch.Success)
            throw new FormatException("Transcript is missing a Game line.");

        var gameId = new BonesGameId(gameMatch.Groups[1].Value.Trim());
        var events = new List<BonesEvent>();

        foreach (Match playMatch in PlayEventRegex().Matches(markdown))
        {
            var turnIndex = int.Parse(playMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var seat = int.Parse(playMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var lowPip = int.Parse(playMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            var highPip = int.Parse(playMatch.Groups[4].Value, CultureInfo.InvariantCulture);
            var side = Enum.Parse<BonesBoardSide>(playMatch.Groups[5].Value, ignoreCase: false);

            events.Add(new BonesEvent(
                turnIndex,
                new BonesPlayerId(seat),
                BonesEventKind.Play,
                new BonesTile(new BonesPipCount(lowPip), new BonesPipCount(highPip)),
                side));
        }

        foreach (Match passMatch in PassEventRegex().Matches(markdown))
        {
            var turnIndex = int.Parse(passMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var seat = int.Parse(passMatch.Groups[2].Value, CultureInfo.InvariantCulture);

            events.Add(new BonesEvent(
                turnIndex,
                new BonesPlayerId(seat),
                BonesEventKind.Pass,
                null,
                null));
        }

        events.Sort(static (left, right) => left.TurnIndex.CompareTo(right.TurnIndex));

        var cumulativeScores = new Dictionary<BonesPlayerId, int>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            cumulativeScores[new BonesPlayerId(seat)] = 0;

        foreach (Match scoreMatch in FinalScoreRegex().Matches(markdown))
        {
            var seat = int.Parse(scoreMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var points = int.Parse(scoreMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            cumulativeScores[new BonesPlayerId(seat)] = points;
        }

        BonesPlayerId? winner = null;
        var winnerMatch = WinnerSeatRegex().Match(markdown);
        if (winnerMatch.Success)
            winner = new BonesPlayerId(int.Parse(winnerMatch.Groups[1].Value, CultureInfo.InvariantCulture));

        return new ParsedTranscript(gameId, events, cumulativeScores, winner);
    }

    internal sealed record ParsedTranscript(
        BonesGameId GameId,
        IReadOnlyList<BonesEvent> Events,
        IReadOnlyDictionary<BonesPlayerId, int> CumulativeScores,
        BonesPlayerId? Winner);
}