using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Workflows;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class TypedLinearWorkflowBuilderTests
{
    private const string ChecklistItem = "Complete typed linear workflow builder path (StartWith/Then/UseTool/ValidateWith/Map/RequireHumanApproval) without collapsing public contracts to object payloads [depends on SDK promotion]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TypedLinearWorkflowBuilder_GivenFluentLinearPath_CompilesExpectedStageAndMapContracts()
    {
        var compilation = TypedLinearWorkflowBuilder
            .StartWith<PlanStageRequest, PlanStageResult>(WorkflowStageKind.Plan)
            .UseTool<WorkflowRequest, WorkflowResult>()
            .Map<PlanStageResult, WorkflowRequest>()
            .ValidateWith<ValidateStageRequest, ValidateStageResult>()
            .Map<WorkflowResult, ValidateStageRequest>()
            .Then<ReviewStageRequest, ReviewStageResult>(WorkflowStageKind.Review)
            .Map<ValidateStageResult, ReviewStageRequest>()
            .RequireHumanApproval<RequireApprovalStageRequest, RequireApprovalStageResult>()
            .Map<ReviewStageResult, RequireApprovalStageRequest>()
            .Then<MergeStageRequest, MergeStageResult>(WorkflowStageKind.Merge)
            .Map<RequireApprovalStageResult, MergeStageRequest>()
            .Build();

        Assert.Collection(
            compilation.StageDescriptors,
            stage => Assert.Equal((WorkflowStageKind.Plan, typeof(PlanStageRequest), typeof(PlanStageResult)), (stage.Stage, stage.RequestType, stage.ResultType)),
            stage => Assert.Equal((WorkflowStageKind.Run, typeof(WorkflowRequest), typeof(WorkflowResult)), (stage.Stage, stage.RequestType, stage.ResultType)),
            stage => Assert.Equal((WorkflowStageKind.Validate, typeof(ValidateStageRequest), typeof(ValidateStageResult)), (stage.Stage, stage.RequestType, stage.ResultType)),
            stage => Assert.Equal((WorkflowStageKind.Review, typeof(ReviewStageRequest), typeof(ReviewStageResult)), (stage.Stage, stage.RequestType, stage.ResultType)),
            stage => Assert.Equal((WorkflowStageKind.RequireApproval, typeof(RequireApprovalStageRequest), typeof(RequireApprovalStageResult)), (stage.Stage, stage.RequestType, stage.ResultType)),
            stage => Assert.Equal((WorkflowStageKind.Merge, typeof(MergeStageRequest), typeof(MergeStageResult)), (stage.Stage, stage.RequestType, stage.ResultType)));

        Assert.Collection(
            compilation.MapAdapters,
            map => Assert.Equal((WorkflowStageKind.Plan, WorkflowStageKind.Run, typeof(PlanStageResult), typeof(WorkflowRequest)), (map.FromStage, map.ToStage, map.SourceType, map.TargetType)),
            map => Assert.Equal((WorkflowStageKind.Run, WorkflowStageKind.Validate, typeof(WorkflowResult), typeof(ValidateStageRequest)), (map.FromStage, map.ToStage, map.SourceType, map.TargetType)),
            map => Assert.Equal((WorkflowStageKind.Validate, WorkflowStageKind.Review, typeof(ValidateStageResult), typeof(ReviewStageRequest)), (map.FromStage, map.ToStage, map.SourceType, map.TargetType)),
            map => Assert.Equal((WorkflowStageKind.Review, WorkflowStageKind.RequireApproval, typeof(ReviewStageResult), typeof(RequireApprovalStageRequest)), (map.FromStage, map.ToStage, map.SourceType, map.TargetType)),
            map => Assert.Equal((WorkflowStageKind.RequireApproval, WorkflowStageKind.Merge, typeof(RequireApprovalStageResult), typeof(MergeStageRequest)), (map.FromStage, map.ToStage, map.SourceType, map.TargetType)));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TypedLinearWorkflowBuilder_GivenObjectPayloadContract_ThrowsDeterministicContractError()
    {
        var startException = Assert.Throws<InvalidOperationException>(
            () => TypedLinearWorkflowBuilder.StartWith(WorkflowStageKind.Plan, typeof(object), typeof(PlanStageResult)));

        Assert.Contains("rejects object payload contracts", startException.Message, StringComparison.Ordinal);

        var mapException = Assert.Throws<InvalidOperationException>(() =>
            TypedLinearWorkflowBuilder
                .StartWith<PlanStageRequest, PlanStageResult>(WorkflowStageKind.Plan)
                .UseTool<WorkflowRequest, WorkflowResult>()
                .Map(typeof(object), typeof(WorkflowRequest)));

        Assert.Contains("rejects object payload contracts", mapException.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void CompileLinear_GivenWorkflowRegistration_UsesTypedFluentPathWithNoObjectContracts()
    {
        var workflow = new WorkflowRegistration(
            new WorkflowId("workflow.typed-linear"),
            typeof(TypedLinearWorkflow),
            typeof(WorkflowRequest),
            typeof(WorkflowResult),
            new WorkflowDescriptor<WorkflowRequest, WorkflowResult>(new WorkflowId("workflow.typed-linear"), "Typed linear"));

        var compilation = WorkflowBuilderStageCompiler.CompileLinear(workflow);

        Assert.All(compilation.StageDescriptors, stage =>
        {
            Assert.NotEqual(typeof(object), stage.RequestType);
            Assert.NotEqual(typeof(object), stage.ResultType);
        });

        Assert.All(compilation.MapAdapters, map =>
        {
            Assert.NotEqual(typeof(object), map.SourceType);
            Assert.NotEqual(typeof(object), map.TargetType);
        });
    }

    private sealed record WorkflowRequest(string Task);

    private sealed record WorkflowResult(string Summary);

    private sealed class TypedLinearWorkflow : Wip.Abstractions.Workflows.IWorkflow<WorkflowRequest, WorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.typed-linear");

        public ValueTask<WorkflowResult> ExecuteAsync(
            WorkflowRequest request,
            Wip.Abstractions.Workflows.WorkflowContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new WorkflowResult(request.Task));
    }
}
