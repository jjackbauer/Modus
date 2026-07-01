using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests;

public sealed class BonesLearningWorkflowMapRuntimePerStageModelTests
{
    private const string StageKindEnumItem =
        "Add internal `BonesStageKind` enum (`Ponder`, `Play`, `Enhance`) in `BonesLearningWorkflowMapRuntime.cs` [foundation]";

    private const string GetStageModelIdOverrideItem =
        "Add `GetStageModelId(parameters, stageKind)` static method that returns: stage-specific model if set, else global `ModelId` if set, else `null` [depends on StageKind enum, parameters fields]";

    private const string PonderBindingItem =
        "Update the Observe→Ponder map binding to use `GetStageModelId(parameters, Ponder)` [depends on GetStageModelId]";

    private const string PlayBindingItem =
        "Update the Ponder→Play map binding to use `GetStageModelId(parameters, Play)` [depends on GetStageModelId]";

    private const string EnhanceBindingItem =
        "Update the Play→Enhance map binding to use `GetStageModelId(parameters, Enhance)` [depends on GetStageModelId] [mandatory - per-stage routing]";

    [Fact]
    [Trait("ChecklistItem", StageKindEnumItem)]
    public void BonesLearningWorkflowMapRuntime_GivenPonderModelIdSet_ExpectedPonderRequestUsesPonderModel()
    {
        BonesLearningWorkflowMapRuntime.ResetRegistrationForTests();
        BonesLearningWorkflowMapRuntime.Register();

        var description = BonesLearningWorkflowParameters.FormatTaskDescription(
            gameCount: 1,
            seed: 42,
            targetScore: 10,
            learningPlayerId: new BonesPlayerId(1),
            modelId: "deepseek-chat",
            allSeatsLearning: false,
            ponderModelId: "deepseek-v4-pro",
            playModelId: null,
            enhanceModelId: null);

        var session = new SessionSnapshot(
            SessionId: new SessionId("session-ponder-override"),
            WorkflowId: BonesLearningWorkflowIds.WorkflowId,
            State: SessionState.Editing,
            RepositoryPath: "/tmp/repo",
            WorktreePath: "/tmp/worktree",
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: description);

        Assert.True(BonesLearningWorkflowParameters.TryParse(session, out var parameters));

        // Verify per-stage model ID is set on parameters
        Assert.Equal("deepseek-v4-pro", parameters.PonderModelId);
        // Global ModelId is still set for fallback
        Assert.Equal("deepseek-chat", parameters.ModelId);
        // Other stage models are null
        Assert.Null(parameters.PlayModelId);
        Assert.Null(parameters.EnhanceModelId);
    }

    [Fact]
    [Trait("ChecklistItem", GetStageModelIdOverrideItem)]
    public void BonesLearningWorkflowMapRuntime_GivenPonderModelIdNullButGlobalModelIdSet_ExpectedFallsBackToGlobal()
    {
        var description = BonesLearningWorkflowParameters.FormatTaskDescription(
            gameCount: 1,
            seed: 42,
            targetScore: 10,
            learningPlayerId: new BonesPlayerId(1),
            modelId: "deepseek-chat",
            allSeatsLearning: false);

        var session = new SessionSnapshot(
            SessionId: new SessionId("session-fallback"),
            WorkflowId: BonesLearningWorkflowIds.WorkflowId,
            State: SessionState.Editing,
            RepositoryPath: "/tmp/repo",
            WorktreePath: "/tmp/worktree",
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: description);

        Assert.True(BonesLearningWorkflowParameters.TryParse(session, out var parameters));

        // When per-stage model is null, parameters should have null per-stage and fall back to global
        Assert.Null(parameters.PonderModelId);
        Assert.Null(parameters.PlayModelId);
        Assert.Null(parameters.EnhanceModelId);
        Assert.Equal("deepseek-chat", parameters.ModelId);
    }

    [Fact]
    [Trait("ChecklistItem", GetStageModelIdOverrideItem)]
    public void BonesLearningWorkflowMapRuntime_GivenAllModelsNull_ExpectedReturnsNull()
    {
        var description = BonesLearningWorkflowParameters.FormatTaskDescription(
            gameCount: 1,
            seed: 42,
            targetScore: 10,
            learningPlayerId: new BonesPlayerId(1));

        var session = new SessionSnapshot(
            SessionId: new SessionId("session-null"),
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
        Assert.Null(parameters.ModelId);
    }

    [Fact]
    [Trait("ChecklistItem", PlayBindingItem)]
    public void BonesLearningWorkflowMapRuntime_GivenPlayModelIdSet_ExpectedPlayParametersReflectPlayModel()
    {
        var description = BonesLearningWorkflowParameters.FormatTaskDescription(
            gameCount: 1,
            seed: 42,
            targetScore: 10,
            learningPlayerId: new BonesPlayerId(1),
            modelId: "deepseek-chat",
            allSeatsLearning: false,
            ponderModelId: null,
            playModelId: "deepseek-v4-flash",
            enhanceModelId: null);

        var session = new SessionSnapshot(
            SessionId: new SessionId("session-play"),
            WorkflowId: BonesLearningWorkflowIds.WorkflowId,
            State: SessionState.Editing,
            RepositoryPath: "/tmp/repo",
            WorktreePath: "/tmp/worktree",
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: description);

        Assert.True(BonesLearningWorkflowParameters.TryParse(session, out var parameters));

        Assert.Null(parameters.PonderModelId);
        Assert.Equal("deepseek-v4-flash", parameters.PlayModelId);
        Assert.Null(parameters.EnhanceModelId);
        Assert.Equal("deepseek-chat", parameters.ModelId);
    }

    [Fact]
    [Trait("ChecklistItem", EnhanceBindingItem)]
    public void BonesLearningWorkflowMapRuntime_GivenEnhanceModelIdSet_ExpectedEnhanceParametersReflectEnhanceModel()
    {
        var description = BonesLearningWorkflowParameters.FormatTaskDescription(
            gameCount: 1,
            seed: 42,
            targetScore: 10,
            learningPlayerId: new BonesPlayerId(1),
            modelId: "deepseek-chat",
            allSeatsLearning: false,
            ponderModelId: null,
            playModelId: null,
            enhanceModelId: "deepseek-v4-pro");

        var session = new SessionSnapshot(
            SessionId: new SessionId("session-enhance"),
            WorkflowId: BonesLearningWorkflowIds.WorkflowId,
            State: SessionState.Editing,
            RepositoryPath: "/tmp/repo",
            WorktreePath: "/tmp/worktree",
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: description);

        Assert.True(BonesLearningWorkflowParameters.TryParse(session, out var parameters));

        Assert.Null(parameters.PonderModelId);
        Assert.Null(parameters.PlayModelId);
        Assert.Equal("deepseek-v4-pro", parameters.EnhanceModelId);
        Assert.Equal("deepseek-chat", parameters.ModelId);
    }
}
