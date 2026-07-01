using System.Text;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Observe;

internal static class BonesObservationTranscriptBuilder
{
    public static string BuildRedactedTranscript(BonesPlayerId observerId, BonesMatchResult matchResult)
    {
        ArgumentNullException.ThrowIfNull(matchResult);

        var builder = new StringBuilder();
        builder.AppendLine("# Bones Observation Transcript");
        builder.AppendLine();
        builder.AppendLine($"Game: {matchResult.GameId.Value}");
        builder.AppendLine($"Observer seat: {observerId.Seat}");
        builder.AppendLine("Other players' private hand contents are redacted.");
        builder.AppendLine();
        builder.AppendLine("## Match summary");
        builder.AppendLine($"Winner seat: {matchResult.Winner.Seat}");
        builder.AppendLine($"Rounds played: {matchResult.Rounds.Length}");
        builder.AppendLine($"Public events recorded: {matchResult.Transcript.Length}");
        builder.AppendLine();

        foreach (var round in matchResult.Rounds)
        {
            builder.AppendLine($"## Round {round.RoundNumber}");
            builder.AppendLine(
                $"Outcome: {round.Score.Outcome}; winner seat {round.Score.Winner.Seat}; +{round.Score.Points} points");
            builder.AppendLine();

            foreach (var roundEvent in round.EventLog)
            {
                builder.AppendLine(FormatPublicEvent(roundEvent));
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Final cumulative scores");
        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            var score = matchResult.CumulativeScores[playerId];
            var visibility = seat == observerId.Seat ? "observer" : "redacted-hand";
            builder.AppendLine($"- Seat {seat}: {score} points ({visibility})");
        }

        return builder.ToString();
    }

    private static string FormatPublicEvent(BonesEvent roundEvent)
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