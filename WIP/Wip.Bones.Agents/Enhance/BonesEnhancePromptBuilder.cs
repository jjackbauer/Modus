using System.Text;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Enhance;

internal static class BonesEnhancePromptBuilder
{
    internal const string PriorScriptHeader = "## Prior script";
    internal const string MatchOutcomeSummaryHeader = "## Match outcome summary";
    internal const string OpponentStrategySummaryHeader = "## Opponent strategy summaries";
    internal const string StrategyLineageHeader = "## Strategy lineage";
    internal const string RefinementGuidanceHeader = "## Refinement guidance";
    internal const string ScriptOutputFormatHeader = "## Required output format";
    private const int OpponentSourceExcerptMaxLength = 500;

    internal static BonesStrategyEnhancementRequest BuildEnhancementRequest(
        BonesPlayerId playerId,
        BonesStrategyDocument priorStrategy,
        IReadOnlyList<BonesMatchHistoryTranscript> matchHistories,
        IReadOnlyList<string> matchHistoryArtifactPaths,
        IReadOnlyDictionary<BonesPlayerId, BonesStrategyArtifact>? opponentStrategies = null,
        IReadOnlyList<BonesStrategyLineageEntry>? strategyLineage = null)
    {
        ArgumentNullException.ThrowIfNull(priorStrategy);
        ArgumentNullException.ThrowIfNull(matchHistories);
        ArgumentNullException.ThrowIfNull(matchHistoryArtifactPaths);

        var systemPrompt = BuildSystemPrompt(playerId, priorStrategy.StrategyId);
        var userPrompt = BuildUserPrompt(playerId, priorStrategy, matchHistories, opponentStrategies, strategyLineage);

        return new BonesStrategyEnhancementRequest(
            Messages:
            [
                new BonesStrategyEnhancementMessage("system", systemPrompt),
                new BonesStrategyEnhancementMessage("user", userPrompt),
            ],
            MatchHistoryArtifactPaths: matchHistoryArtifactPaths,
            PriorStrategyId: priorStrategy.StrategyId);
    }

    private static string BuildSystemPrompt(BonesPlayerId playerId, BonesStrategyId priorStrategyId)
    {
        return $"""
            You are refining Block Dominoes tactics for seat {playerId.Seat} by revising a C# strategy script.
            Output exactly one compilable C# source file: a public sealed class implementing {BonesStrategyScriptApiReference.InterfaceName}.
            Review the prior script (strategy id {priorStrategyId.Value}) and match outcomes, then produce an enhanced script.
            Preserve incumbent ChooseMove logic that correlates with wins; revise heuristics where outcomes were losses or score differential was negative.
            {BonesStrategyScriptApiReference.MoveSelectionConstraint}
            Do not output markdown rules, commentary outside the code fence, or multiple classes.
            """;
    }

    private static string BuildUserPrompt(
        BonesPlayerId playerId,
        BonesStrategyDocument priorStrategy,
        IReadOnlyList<BonesMatchHistoryTranscript> matchHistories,
        IReadOnlyDictionary<BonesPlayerId, BonesStrategyArtifact>? opponentStrategies = null,
        IReadOnlyList<BonesStrategyLineageEntry>? strategyLineage = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Strategy script enhancement request");
        builder.AppendLine();
        builder.AppendLine($"Player seat: {playerId.Seat}");
        builder.AppendLine($"Prior strategy id: {priorStrategy.StrategyId.Value}");
        builder.AppendLine($"Match history count: {matchHistories.Count}");
        builder.AppendLine();
        builder.AppendLine(BonesStrategyScriptApiReference.ContractExcerpt);
        builder.AppendLine();

        if (strategyLineage is { Count: > 0 })
        {
            builder.AppendLine(StrategyLineageHeader);
            builder.AppendLine();
            foreach (var entry in strategyLineage)
            {
                builder.AppendLine($"- v{entry.PredecessorStrategyId.Value} → v{entry.SuccessorStrategyId.Value}: {entry.PromotionOutcome}");
                builder.AppendLine($"  Diff: {entry.DiffSummary}");
                builder.AppendLine($"  At: {entry.RecordedAtUtc:O}");
                builder.AppendLine();
            }

            builder.AppendLine();
        }

        builder.AppendLine(PriorScriptHeader);
        builder.AppendLine(priorStrategy.Markdown);
        builder.AppendLine();
        builder.AppendLine(MatchOutcomeSummaryHeader);

        var hasWin = false;
        var hasLossOrNegativeDifferential = false;

        if (matchHistories.Count == 0)
        {
            builder.AppendLine("No match history transcripts were loaded for this player.");
        }
        else
        {
            foreach (var matchHistory in matchHistories)
            {
                var outcome = AppendMatchOutcomeSummary(builder, playerId, matchHistory);
                if (outcome == MatchOutcomeKind.Win)
                    hasWin = true;
                if (outcome is MatchOutcomeKind.Loss or MatchOutcomeKind.NegativeScoreDifferential)
                    hasLossOrNegativeDifferential = true;

                builder.AppendLine();
            }
        }

        builder.AppendLine(RefinementGuidanceHeader);
        if (hasWin)
        {
            builder.AppendLine(
                "When Outcome is Win, preserve effective heuristics and control flow from the prior script unless a specific loss pattern contradicts them.");
        }

        if (hasLossOrNegativeDifferential)
        {
            builder.AppendLine(
                "When Outcome is Loss or score differential vs winner is negative, revise ChooseMove logic to address the observed loss patterns.");
        }

        if (!hasWin && !hasLossOrNegativeDifferential)
        {
            builder.AppendLine(
                "No decisive outcomes were loaded; refine the prior script conservatively while keeping compilable structure.");
        }

        if (opponentStrategies is { Count: > 0 })
        {
            builder.AppendLine();
            AppendOpponentStrategySummaries(builder, opponentStrategies);
        }

        builder.AppendLine();
        builder.AppendLine(ScriptOutputFormatHeader);
        builder.AppendLine("Reply with a single fenced ```csharp block containing the complete revised class source.");
        builder.AppendLine($"The class must implement {BonesStrategyScriptApiReference.InterfaceName} and compile against the allowed namespaces.");

        builder.AppendLine();
        builder.AppendLine("## Match history transcripts");
        if (matchHistories.Count == 0)
        {
            builder.AppendLine("No match history transcripts were loaded for this player.");
        }
        else
        {
            foreach (var matchHistory in matchHistories)
            {
                builder.AppendLine($"### Game {matchHistory.GameId.Value}");
                builder.AppendLine(matchHistory.Markdown);
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private enum MatchOutcomeKind
    {
        Neutral,
        Win,
        Loss,
        NegativeScoreDifferential,
    }

    private static MatchOutcomeKind AppendMatchOutcomeSummary(
        StringBuilder builder,
        BonesPlayerId playerId,
        BonesMatchHistoryTranscript matchHistory)
    {
        var winnerSeat = ExtractIntField(matchHistory.Markdown, "Match winner seat:");
        var playerScore = ExtractIntField(matchHistory.Markdown, $"Final score for seat {playerId.Seat}:");
        var outcome = winnerSeat == playerId.Seat ? "Win" : "Loss";
        var scoreDifferential = winnerSeat.HasValue && playerScore.HasValue
            ? playerScore.Value - ExtractWinnerScore(matchHistory.Markdown, winnerSeat.Value)
            : (int?)null;

        builder.AppendLine($"### Game {matchHistory.GameId.Value}");
        builder.AppendLine($"Outcome: {outcome}");
        if (winnerSeat.HasValue)
            builder.AppendLine($"Match winner seat: {winnerSeat.Value}");
        if (playerScore.HasValue)
            builder.AppendLine($"Final score for seat {playerId.Seat}: {playerScore.Value}");
        if (scoreDifferential.HasValue)
            builder.AppendLine($"Score differential vs winner: {scoreDifferential.Value}");

        if (winnerSeat == playerId.Seat)
            return MatchOutcomeKind.Win;

        if (winnerSeat.HasValue)
            return scoreDifferential is < 0
                ? MatchOutcomeKind.NegativeScoreDifferential
                : MatchOutcomeKind.Loss;

        if (scoreDifferential is < 0)
            return MatchOutcomeKind.NegativeScoreDifferential;

        return MatchOutcomeKind.Neutral;
    }

    internal static BonesStrategyEnhancementRequest AppendCompileRetryMessage(
        BonesStrategyEnhancementRequest priorRequest,
        string failureReason,
        string priorAssistantResponse)
    {
        ArgumentNullException.ThrowIfNull(priorRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        ArgumentException.ThrowIfNullOrWhiteSpace(priorAssistantResponse);

        var retryUserPrompt = new StringBuilder();
        retryUserPrompt.AppendLine("# Compile retry request");
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine("The previous C# script failed compilation. Fix the script and reply with a single fenced ```csharp block.");
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine("## Compiler diagnostics");
        retryUserPrompt.AppendLine(failureReason);
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine(BonesStrategyScriptApiReference.ContractExcerpt);
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine(ScriptOutputFormatHeader);
        retryUserPrompt.AppendLine($"The class must implement {BonesStrategyScriptApiReference.InterfaceName} and compile against the allowed namespaces.");

        var messages = priorRequest.Messages
            .Append(new BonesStrategyEnhancementMessage("assistant", priorAssistantResponse))
            .Append(new BonesStrategyEnhancementMessage("user", retryUserPrompt.ToString()))
            .ToArray();

        return priorRequest with { Messages = messages };
    }

    private static int ExtractWinnerScore(string markdown, int winnerSeat)
        => ExtractIntField(markdown, $"Final score for seat {winnerSeat}:") ?? 0;

    private static void AppendOpponentStrategySummaries(
        StringBuilder builder,
        IReadOnlyDictionary<BonesPlayerId, BonesStrategyArtifact> opponentStrategies)
    {
        builder.AppendLine(OpponentStrategySummaryHeader);
        builder.AppendLine();

        foreach (var (opponentId, artifact) in opponentStrategies.OrderBy(kvp => kvp.Key.Seat))
        {
            var kindLabel = artifact.Kind == BonesStrategyKind.Script ? "Script" : "Markdown";
            var excerpt = artifact.Source.Length <= OpponentSourceExcerptMaxLength
                ? artifact.Source
                : artifact.Source[..OpponentSourceExcerptMaxLength] + "...";

            builder.AppendLine($"### Opponent seat {opponentId.Seat} ({kindLabel})");
            builder.AppendLine($"Strategy id: {artifact.StrategyId.Value}");
            builder.AppendLine($"Promotion status: {artifact.PromotionStatus}");
            builder.AppendLine();
            builder.AppendLine("```");
            builder.AppendLine(excerpt);
            builder.AppendLine("```");
            builder.AppendLine();
        }
    }

    private static int? ExtractIntField(string markdown, string label)
    {
        foreach (var line in markdown.Split('\n'))
        {
            if (!line.StartsWith(label, StringComparison.Ordinal))
                continue;

            var valueText = line[label.Length..].Trim();
            if (int.TryParse(valueText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
                return value;
        }

        return null;
    }
}
