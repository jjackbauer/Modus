using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Workflow;

public static class BonesLearningWorkflowIds
{
    public static readonly WorkflowId WorkflowId = new("workflow.bones.learning");
}

public sealed record BonesLearningWorkflowRequest(
    int GameCount,
    int Seed,
    int TargetScore,
    BonesPlayerId LearningPlayerId,
    string RepositoryPath);

public sealed record BonesLearningWorkflowResult(
    int GamesObserved,
    BonesStrategyId StrategyId,
    BonesGameId MatchGameId,
    BonesStrategyId EnhancedStrategyId);

public sealed record BonesLearningWorkflowParameters(
    int GameCount,
    int Seed,
    int TargetScore,
    BonesPlayerId LearningPlayerId,
    string? ModelId = null,
    bool AllSeatsLearning = false,
    string? PonderModelId = null,
    string? PlayModelId = null,
    string? EnhanceModelId = null)
{
    public const string TaskDescriptionPrefix = "bones-learning";

    public static string FormatTaskDescription(
        int gameCount,
        int seed,
        int targetScore,
        BonesPlayerId learningPlayerId,
        string? modelId = null,
        bool allSeatsLearning = false,
        string? ponderModelId = null,
        string? playModelId = null,
        string? enhanceModelId = null)
    {
        var description =
            $"{TaskDescriptionPrefix};gameCount={gameCount};seed={seed};targetScore={targetScore};learningPlayerId={learningPlayerId.Seat}";

        if (!string.IsNullOrWhiteSpace(modelId))
            description += $";modelId={modelId.Trim()}";

        if (allSeatsLearning)
            description += ";allSeatsLearning=true";

        if (!string.IsNullOrWhiteSpace(ponderModelId))
            description += $";ponderModelId={ponderModelId.Trim()}";

        if (!string.IsNullOrWhiteSpace(playModelId))
            description += $";playModelId={playModelId.Trim()}";

        if (!string.IsNullOrWhiteSpace(enhanceModelId))
            description += $";enhanceModelId={enhanceModelId.Trim()}";

        return description;
    }

    public static bool TryParse(SessionSnapshot session, out BonesLearningWorkflowParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(session);

        parameters = default!;
        if (string.IsNullOrWhiteSpace(session.TaskDescription)
            || !session.TaskDescription.StartsWith(TaskDescriptionPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var values = session.TaskDescription
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        if (!values.TryGetValue("gameCount", out var gameCountValue)
            || !int.TryParse(gameCountValue, out var gameCount)
            || !values.TryGetValue("seed", out var seedValue)
            || !int.TryParse(seedValue, out var seed)
            || !values.TryGetValue("targetScore", out var targetScoreValue)
            || !int.TryParse(targetScoreValue, out var targetScore))
        {
            return false;
        }

        // Parse AllSeatsLearning flag (default false for backward compat)
        var allSeatsLearning = false;
        if (values.TryGetValue("allSeatsLearning", out var allSeatsLearningValue)
            && bool.TryParse(allSeatsLearningValue, out var parsedAllSeatsLearning))
        {
            allSeatsLearning = parsedAllSeatsLearning;
        }

        // Parse learningPlayerId: required when AllSeatsLearning is false, optional when true
        if (!values.TryGetValue("learningPlayerId", out var learningPlayerIdValue)
            || !int.TryParse(learningPlayerIdValue, out var learningPlayerSeat))
        {
            if (!allSeatsLearning)
                return false; // learningPlayerId is required in single-seat mode

            learningPlayerSeat = default; // dummy value when AllSeatsLearning is true
        }

        values.TryGetValue("modelId", out var modelIdValue);
        var modelId = string.IsNullOrWhiteSpace(modelIdValue) ? null : modelIdValue.Trim();

        values.TryGetValue("ponderModelId", out var ponderModelIdValue);
        var ponderModelId = string.IsNullOrWhiteSpace(ponderModelIdValue) ? null : ponderModelIdValue.Trim();

        values.TryGetValue("playModelId", out var playModelIdValue);
        var playModelId = string.IsNullOrWhiteSpace(playModelIdValue) ? null : playModelIdValue.Trim();

        values.TryGetValue("enhanceModelId", out var enhanceModelIdValue);
        var enhanceModelId = string.IsNullOrWhiteSpace(enhanceModelIdValue) ? null : enhanceModelIdValue.Trim();

        parameters = new BonesLearningWorkflowParameters(
            gameCount,
            seed,
            targetScore,
            new BonesPlayerId(learningPlayerSeat),
            modelId,
            allSeatsLearning,
            ponderModelId,
            playModelId,
            enhanceModelId);
        return true;
    }
}