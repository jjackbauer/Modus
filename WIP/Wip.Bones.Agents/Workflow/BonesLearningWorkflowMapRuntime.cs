using Wip.Abstractions.Sessions;
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
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;

        WorkflowStageMapAdapterRuntime.RegisterInitialRequestFactory(TryCreateInitialStageRequest);

        // Observe -> Ponder
        WorkflowStageMapAdapterRuntime.RegisterMapBinding<BonesObserveGamesResult, BonesPonderRequest>(
            static (observe, session) =>
            {
                var parameters = RequireParameters(session);
                var modelId = GetStageModelId(parameters, BonesStageKind.Ponder);
                return modelId is null
                    ? new BonesPonderRequest(
                        parameters.LearningPlayerId,
                        session.RepositoryPath,
                        AllSeatsLearning: parameters.AllSeatsLearning)
                    : new BonesPonderRequest(
                        parameters.LearningPlayerId,
                        session.RepositoryPath,
                        modelId,
                        parameters.AllSeatsLearning);
            });

        // Ponder -> PlayMatch
        WorkflowStageMapAdapterRuntime.RegisterMapBinding<BonesPonderResult, BonesPlayMatchRequest>(
            static (ponder, session) =>
            {
                var parameters = RequireParameters(session);
                var modelId = GetStageModelId(parameters, BonesStageKind.Play);
                return modelId is null
                    ? new BonesPlayMatchRequest(
                        parameters.Seed,
                        parameters.TargetScore,
                        session.RepositoryPath,
                        AllSeatsLearning: parameters.AllSeatsLearning)
                    : new BonesPlayMatchRequest(
                        parameters.Seed,
                        parameters.TargetScore,
                        session.RepositoryPath,
                        ModelId: modelId,
                        AllSeatsLearning: parameters.AllSeatsLearning);
            });

        // PlayMatch -> Enhance
        WorkflowStageMapAdapterRuntime.RegisterMapBinding<BonesPlayMatchResult, BonesEnhanceStrategyRequest>(
            static (play, session) =>
            {
                var parameters = RequireParameters(session);
                var modelId = GetStageModelId(parameters, BonesStageKind.Enhance);
                return modelId is null
                    ? new BonesEnhanceStrategyRequest(
                        parameters.LearningPlayerId,
                        session.RepositoryPath,
                        play.GameId,
                        EvaluationTargetScore: parameters.TargetScore,
                        AllSeatsLearning: parameters.AllSeatsLearning)
                    : new BonesEnhanceStrategyRequest(
                        parameters.LearningPlayerId,
                        session.RepositoryPath,
                        play.GameId,
                        ModelId: modelId,
                        EvaluationTargetScore: parameters.TargetScore,
                        AllSeatsLearning: parameters.AllSeatsLearning);
            });
    }

    public static void ResetRegistrationForTests()
        => Interlocked.Exchange(ref _registered, 0);

    private static object? TryCreateInitialStageRequest(Type requestType, SessionSnapshot session)
    {
        if (requestType != typeof(BonesObserveGamesRequest))
            return null;

        var parameters = RequireParameters(session);
        var observers = Enumerable
            .Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount)
            .Select(seat => new BonesPlayerId(seat))
            .ToArray();

        return new BonesObserveGamesRequest(
            parameters.GameCount,
            parameters.Seed,
            parameters.TargetScore,
            observers,
            session.RepositoryPath);
    }

    private static BonesLearningWorkflowParameters RequireParameters(SessionSnapshot session)
    {
        if (!BonesLearningWorkflowParameters.TryParse(session, out var parameters))
        {
            throw new InvalidOperationException(
                $"Session '{session.SessionId.Value}' is missing bones learning workflow parameters in TaskDescription.");
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
