using System.Text;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Play;

internal static class BonesPlayMatchPromptBuilder
{
    internal const string AllowedMovesHeader = "## Allowed moves";

    internal static BonesPlayTurnRequest BuildTurnRequest(
        BonesPlayerId activeSeat,
        BonesStrategyDocument strategy,
        BonesRoundState state,
        IReadOnlyList<BonesMove> legalMoves,
        int retryAttempt = 0)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(legalMoves);

        var options = legalMoves
            .Select(move => new BonesPlayTurnOption(move.MoveId, DescribeMove(move)))
            .ToArray();

        var systemPrompt = BuildSystemPrompt(activeSeat, strategy.StrategyId);
        var userPrompt = BuildUserPrompt(activeSeat, strategy, state, options, retryAttempt);

        return new BonesPlayTurnRequest(
            Messages:
            [
                new BonesPlayTurnMessage("system", systemPrompt),
                new BonesPlayTurnMessage("user", userPrompt),
            ],
            AllowedMoves: options,
            ActiveSeat: activeSeat,
            StrategyId: strategy.StrategyId);
    }

    internal static string DescribeMove(BonesMove move)
    {
        if (move.IsPass)
            return $"pass (move id: {move.MoveId.Value})";

        return $"play {move.Tile} on {move.Side} (move id: {move.MoveId.Value})";
    }

    private static string BuildSystemPrompt(BonesPlayerId activeSeat, BonesStrategyId strategyId)
    {
        return $"""
            You are seat {activeSeat.Seat} in a four-player Block Dominoes match.
            Strategy artifact: {strategyId.Value}.
            Choose exactly one allowed move id from the enumerated options.
            Use GetLegalMoves semantics: only listed move ids are valid.
            """;
    }

    private static string BuildUserPrompt(
        BonesPlayerId activeSeat,
        BonesStrategyDocument strategy,
        BonesRoundState state,
        IReadOnlyList<BonesPlayTurnOption> allowedMoves,
        int retryAttempt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Play turn request");
        builder.AppendLine();
        builder.AppendLine($"Active seat: {activeSeat.Seat}");
        builder.AppendLine($"Game: {state.GameId.Value}");
        builder.AppendLine($"Turn index: {state.EventLog.Length}");
        builder.AppendLine($"Board empty: {state.Board.IsEmpty}");

        if (!state.Board.IsEmpty)
        {
            builder.AppendLine($"Open ends: Left={state.Board.LeftEnd!.Value.Pip}, Right={state.Board.RightEnd!.Value.Pip}");
        }

        builder.AppendLine();
        builder.AppendLine("## Strategy excerpt");
        builder.AppendLine(strategy.Markdown);
        builder.AppendLine();
        builder.AppendLine(AllowedMovesHeader);
        builder.AppendLine("Select one move id from this enumerated list:");

        foreach (var option in allowedMoves)
            builder.AppendLine($"- {option.MoveId.Value}: {option.Description}");

        if (retryAttempt > 0)
        {
            builder.AppendLine();
            builder.AppendLine(
                $"Previous selection was rejected (attempt {retryAttempt}). Choose a valid move id from the list above.");
        }

        return builder.ToString();
    }
}