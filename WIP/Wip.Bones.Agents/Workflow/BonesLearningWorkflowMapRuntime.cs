using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Runtime.Runtime;

namespace Wip.Bones.Agents.Workflow;

internal enum BonesStageKind
{
    Ponder,
    Play,
    Enhance,
}

public static class BonesLearningWorkflowMapRuntime
{
    internal static BonesPonderRequest CreatePonderRequest(
        BonesObserveGamesResult observe,
        WorkflowContext context)
    {
        var parameters = RequireParameters(context);
        var modelId = GetStageModelId(parameters, BonesStageKind.Ponder);
        return modelId is null
            ? new BonesPonderRequest(
                parameters.LearningPlayerId,
                context.RepositoryPath,
                AllSeatsLearning: parameters.AllSeatsLearning,
                ObservedGameIds: observe.ObservedGameIds)
            : new BonesPonderRequest(
                parameters.LearningPlayerId,
                context.RepositoryPath,
                modelId,
                parameters.AllSeatsLearning,
                observe.ObservedGameIds);
    }

    internal static BonesPlayMatchRequest CreatePlayMatchRequest(
        BonesPonderResult ponder,
        WorkflowContext context)
    {
        var parameters = RequireParameters(context);
        var modelId = GetStageModelId(parameters, BonesStageKind.Play);
        return modelId is null
            ? new BonesPlayMatchRequest(
                parameters.Seed,
                parameters.TargetScore,
                context.RepositoryPath,
                AllSeatsLearning: parameters.AllSeatsLearning)
            : new BonesPlayMatchRequest(
                parameters.Seed,
                parameters.TargetScore,
                context.RepositoryPath,
                ModelId: modelId,
                AllSeatsLearning: parameters.AllSeatsLearning);
    }

    internal static BonesEnhanceStrategyRequest CreateEnhanceRequest(
        BonesPlayMatchResult play,
        WorkflowContext context)
    {
        var parameters = RequireParameters(context);
        var modelId = GetStageModelId(parameters, BonesStageKind.Enhance);
        return modelId is null
            ? new BonesEnhanceStrategyRequest(
                parameters.LearningPlayerId,
                context.RepositoryPath,
                play.GameId,
                EvaluationTargetScore: parameters.EvaluationTargetScore,
                AllSeatsLearning: parameters.AllSeatsLearning)
            : new BonesEnhanceStrategyRequest(
                parameters.LearningPlayerId,
                context.RepositoryPath,
                play.GameId,
                ModelId: modelId,
                EvaluationTargetScore: parameters.EvaluationTargetScore,
                AllSeatsLearning: parameters.AllSeatsLearning);
    }

    internal static BonesObserveGamesRequest CreateInitialRequest(WorkflowContext context)
    {
        var parameters = RequireParameters(context);
        var observers = Enumerable
            .Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount)
            .Select(seat => new BonesPlayerId(seat))
            .ToArray();

        return new BonesObserveGamesRequest(
            parameters.GameCount,
            parameters.Seed,
            parameters.TargetScore,
            observers,
            context.RepositoryPath);
    }
    private static BonesLearningWorkflowParameters RequireParameters(WorkflowContext context)
    {
        if (!BonesLearningWorkflowParameters.TryParse(context.Task, out var parameters))
        {
            throw new InvalidOperationException(
                $"Session '{context.SessionId.Value}' is missing bones learning workflow parameters in TaskDescription.");
        }

        return parameters;
    }

    private static string? GetStageModelId(BonesLearningWorkflowParameters parameters, BonesStageKind stage)
    {
        var stageModelId = stage switch
        {
            BonesStageKind.Ponder => parameters.PonderModelId,
            BonesStageKind.Play => parameters.PlayModelId,
            BonesStageKind.Enhance => parameters.EnhanceModelId,
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(stageModelId))
            return stageModelId.Trim();

        if (!string.IsNullOrWhiteSpace(parameters.ModelId))
            return parameters.ModelId.Trim();

        return null;
    }
}
