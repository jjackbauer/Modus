using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests;

public sealed class BonesLearningWorkflowParametersPerStageModelTests
{
    private const string FormatTaskDescriptionItem =
        "Update `FormatTaskDescription` to include per-stage model IDs in the task description string (e.g. `;ponderModelId=deepseek-v4-pro`) when non-null [depends on fields]";

    private const string TryParseItem =
        "Update `TryParse` to parse per-stage model IDs from the task description (round-trip compatible) [depends on FormatTaskDescription]";

    private const string BackwardCompatItem =
        "Add `string? PonderModelId = null`, `string? PlayModelId = null`, `string? EnhanceModelId = null` to `BonesLearningWorkflowParameters` record [foundation]";

    [Fact]
    [Trait("ChecklistItem", FormatTaskDescriptionItem)]
    public void BonesLearningWorkflowParameters_GivenPerStageModelsSet_ExpectedFormatTaskDescriptionIncludesThem()
    {
        var description = BonesLearningWorkflowParameters.FormatTaskDescription(
            gameCount: 1,
            seed: 42,
            targetScore: 10,
            learningPlayerId: new BonesPlayerId(1),
            modelId: null,
            allSeatsLearning: false,
            ponderModelId: "deepseek-v4-pro",
            playModelId: "deepseek-v4-flash",
            enhanceModelId: null);

        Assert.Contains(";ponderModelId=deepseek-v4-pro", description, StringComparison.Ordinal);
        Assert.Contains(";playModelId=deepseek-v4-flash", description, StringComparison.Ordinal);
        Assert.DoesNotContain("enhanceModelId", description, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", TryParseItem)]
    public void BonesLearningWorkflowParameters_GivenTaskDescriptionWithPerStageModels_ExpectedTryParseReconstructsThem()
    {
        var description = BonesLearningWorkflowParameters.FormatTaskDescription(
            gameCount: 3,
            seed: 99,
            targetScore: 15,
            learningPlayerId: new BonesPlayerId(2),
            modelId: "deepseek-chat",
            allSeatsLearning: true,
            ponderModelId: "deepseek-v4-pro",
            playModelId: "deepseek-v4-flash",
            enhanceModelId: "deepseek-v4-pro");

        var session = new SessionSnapshot(
            SessionId: new SessionId("session-roundtrip"),
            WorkflowId: BonesLearningWorkflowIds.WorkflowId,
            State: SessionState.Editing,
            RepositoryPath: "/tmp/repo",
            WorktreePath: "/tmp/worktree",
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: description);

        Assert.True(BonesLearningWorkflowParameters.TryParse(session, out var parameters));
        Assert.Equal(3, parameters.GameCount);
        Assert.Equal(99, parameters.Seed);
        Assert.Equal(15, parameters.TargetScore);
        Assert.Equal(2, parameters.LearningPlayerId.Seat);
        Assert.Equal("deepseek-chat", parameters.ModelId);
        Assert.True(parameters.AllSeatsLearning);
        Assert.Equal("deepseek-v4-pro", parameters.PonderModelId);
        Assert.Equal("deepseek-v4-flash", parameters.PlayModelId);
        Assert.Equal("deepseek-v4-pro", parameters.EnhanceModelId);
    }

    [Fact]
    [Trait("ChecklistItem", BackwardCompatItem)]
    public void BonesLearningWorkflowParameters_GivenLegacyTaskDescription_ExpectedPerStageModelsDefaultToNull()
    {
        var description = BonesLearningWorkflowParameters.FormatTaskDescription(
            gameCount: 1,
            seed: 1,
            targetScore: 10,
            learningPlayerId: new BonesPlayerId(1),
            modelId: "deepseek-chat",
            allSeatsLearning: false);

        var session = new SessionSnapshot(
            SessionId: new SessionId("session-legacy"),
            WorkflowId: BonesLearningWorkflowIds.WorkflowId,
            State: SessionState.Editing,
            RepositoryPath: "/tmp/repo",
            WorktreePath: "/tmp/worktree",
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: description);

        Assert.True(BonesLearningWorkflowParameters.TryParse(session, out var parameters));
        Assert.Null(parameters.PonderModelId);
        Assert.Null(parameters.PlayModelId);
        Assert.Null(parameters.EnhanceModelId);
    }

    [Fact]
    [Trait("ChecklistItem", BackwardCompatItem)]
    public void BonesLearningWorkflowParameters_GivenDefaultConstruction_ExpectedPerStageModelsDefaultToNull()
    {
        var parameters = new BonesLearningWorkflowParameters(
            GameCount: 1,
            Seed: 42,
            TargetScore: 10,
            LearningPlayerId: new BonesPlayerId(1));

        Assert.Null(parameters.PonderModelId);
        Assert.Null(parameters.PlayModelId);
        Assert.Null(parameters.EnhanceModelId);
        Assert.Null(parameters.ModelId);
        Assert.False(parameters.AllSeatsLearning);
    }
}
