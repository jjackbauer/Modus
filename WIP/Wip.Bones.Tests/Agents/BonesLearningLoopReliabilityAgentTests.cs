using System.Collections.Immutable;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesLearningLoopReliabilityAgentTests
{
    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.TargetScoreParity)]
    public void BonesLearningWorkflowMapRuntime_GivenSessionTargetScoreTen_ExpectedEnhanceRequestEvaluationTargetScoreTen()
    {
        WorkflowStageMapAdapterRuntime.ResetRegisteredBindingsForTests();
        BonesLearningWorkflowMapRuntime.ResetRegistrationForTests();
        BonesLearningWorkflowMapRuntime.Register();

        var compilation = BonesLearningWorkflow.CompileLinearStages();
        var enhanceDescriptor = compilation.StageDescriptors.Single(
            stage => stage.RequestType == typeof(BonesEnhanceStrategyRequest));

        var repositoryPath = Path.Combine(Path.GetTempPath(), $"bones-map-{Guid.NewGuid():N}");
        var sessionId = new SessionId($"bones-map-{Guid.NewGuid():N}");
        var taskDescription = BonesLearningWorkflowParameters.FormatTaskDescription(
            gameCount: 1,
            seed: 4242,
            targetScore: 10,
            new BonesPlayerId(1));

        var session = new SessionSnapshot(
            SessionId: sessionId,
            WorkflowId: BonesLearningWorkflowIds.WorkflowId,
            State: SessionState.Created,
            RepositoryPath: repositoryPath,
            WorktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "bones-map"),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: taskDescription);

        var playResult = CreateMinimalPlayResult();

        var mapped = WorkflowStageMapAdapterRuntime.ApplyMappedStageInput(
            enhanceDescriptor,
            compilation,
            session,
            playResult);

        var enhanceRequest = Assert.IsType<BonesEnhanceStrategyRequest>(mapped.MappedInputPayload);
        Assert.Equal(10, enhanceRequest.EvaluationTargetScore);
    }

    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.SharedPlayerSlotFactory)]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.PromotionEvaluatorMarkdownParity)]
    public void BonesStrategyPlayerSlotFactory_GivenMarkdownOpponent_ExpectedSameSlotTypeAsPlayToolResolver()
    {
        var playProvider = new StubPlayTurnModelProvider();
        var factory = new BonesStrategyPlayerSlotFactory(playModelProvider: playProvider);

        var markdown = new BonesStrategyArtifact(
            new BonesStrategyId("opponent-markdown-v1"),
            new BonesPlayerId(2),
            BonesStrategyKind.Markdown,
            "# Opponent markdown strategy",
            BonesPromotionStatus.Active);

        Assert.Equal(
            BonesStrategyPlayerSlotDispatchKind.MarkdownLlm,
            factory.ResolveDispatchKind(markdown));

        var context = new CapabilityContext(
            new SessionId("bones-promotion-eval"),
            Path.Combine(Path.GetTempPath(), "bones-promotion-eval"));
        var slot = factory.CreatePlayerSlot(
            markdown,
            new BonesStrategyPlayerSlotFactoryContext(context));

        Assert.IsType<BonesMarkdownStrategyPlayerSlot>(slot);
        Assert.IsNotType<BonesFirstLegalMovePlayerSlot>(slot);
    }

    private static BonesPlayMatchResult CreateMinimalPlayResult()
    {
        var gameId = new BonesGameId("play-map-test");
        var descriptor = new ArtifactDescriptor(
            new ArtifactId("bones-play-artifact"),
            new SessionId("bones-map-session"),
            ArtifactKind.Json,
            ".wip/sessions/bones-map/artifacts/bones-play-match-execution.json",
            "test",
            "1.0.0",
            DateTimeOffset.UtcNow);

        return new BonesPlayMatchResult(
            gameId,
            new BonesPlayerId(1),
            ImmutableDictionary<BonesPlayerId, int>.Empty,
            Array.Empty<BonesEvent>(),
            0,
            descriptor,
            descriptor,
            ImmutableDictionary<BonesPlayerId, ArtifactDescriptor>.Empty);
    }

    private sealed class StubPlayTurnModelProvider : IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>
    {
        public ValueTask<ModelProviderResponse<BonesPlayTurnResult>> ExecuteAsync(
            ModelProviderRequest<BonesPlayTurnRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesPlayTurnResult>(
                    payload: new BonesPlayTurnResult(request.Payload.AllowedMoves[0].MoveId.Value),
                    providerId: "stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(1, 1),
                    correlationId: request.CorrelationId));
    }
}