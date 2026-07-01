using System.Text;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Play;

internal static class BonesMatchTranscriptBuilder
{
    public static string BuildFullTranscript(BonesMatchResult matchResult)
    {
        ArgumentNullException.ThrowIfNull(matchResult);

        var builder = new StringBuilder();
        builder.AppendLine("# Bones Play Match Transcript");
        builder.AppendLine();
        builder.AppendLine($"Game: {matchResult.GameId.Value}");
        builder.AppendLine($"Winner seat: {matchResult.Winner.Seat}");
        builder.AppendLine($"Rounds played: {matchResult.Rounds.Length}");
        builder.AppendLine($"Total turns: {matchResult.Transcript.Length}");
        builder.AppendLine();

        foreach (var round in matchResult.Rounds)
        {
            builder.AppendLine($"## Round {round.RoundNumber}");
            builder.AppendLine(
                $"Outcome: {round.Score.Outcome}; winner seat {round.Score.Winner.Seat}; +{round.Score.Points} points");
            builder.AppendLine();

            foreach (var roundEvent in round.EventLog)
                builder.AppendLine(FormatEvent(roundEvent));

            builder.AppendLine();
        }

        builder.AppendLine("## Final cumulative scores");
        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            builder.AppendLine($"- Seat {seat}: {matchResult.CumulativeScores[playerId]} points");
        }

        return builder.ToString();
    }

    public static string BuildPlayerMatchHistory(BonesPlayerId playerId, BonesMatchResult matchResult)
    {
        ArgumentNullException.ThrowIfNull(matchResult);

        var builder = new StringBuilder();
        builder.AppendLine("# Bones Match History");
        builder.AppendLine();
        builder.AppendLine($"Game: {matchResult.GameId.Value}");
        builder.AppendLine($"Player seat: {playerId.Seat}");
        builder.AppendLine($"Match winner seat: {matchResult.Winner.Seat}");
        builder.AppendLine($"Final score for seat {playerId.Seat}: {matchResult.CumulativeScores[playerId]}");
        builder.AppendLine();

        foreach (var roundEvent in matchResult.Transcript)
        {
            if (roundEvent.PlayerId == playerId)
                builder.AppendLine(FormatEvent(roundEvent));
        }

        return builder.ToString();
    }

    private static string FormatEvent(BonesEvent roundEvent)
    {
        return roundEvent.Kind switch
        {
            BonesEventKind.Play =>
                $"- Turn {roundEvent.TurnIndex}: seat {roundEvent.PlayerId.Seat} played {roundEvent.Tile} on {roundEvent.Side}",
            BonesEventKind.Pass =>
                $"- Turn {roundEvent.TurnIndex}: seat {roundEvent.PlayerId.Seat} passed",
            _ => $"- Turn {roundEvent.TurnIndex}: seat {roundEvent.PlayerId.Seat} event",
        };
    }
}