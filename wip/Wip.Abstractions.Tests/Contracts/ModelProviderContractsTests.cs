using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Xunit;

namespace Wip.Abstractions.Tests.Contracts;

public sealed class ModelProviderContractsTests
{
    [Fact]
    public async Task ModelProviderContracts_GivenTypedExecutionRequest_ExpectedStronglyTypedResultContractReturned()
    {
        var provider = new StubModelProvider();
        var request = new ModelProviderRequest<PlanPromptRequest>(
            payload: new PlanPromptRequest("add provider integration"),
            modelId: "deepseek-chat",
            correlationId: "corr-typed");
        var context = new CapabilityContext(new SessionId("session-typed"), "C:/repo/modus/.wip/worktree");

        var response = await provider.ExecuteAsync(request, context, CancellationToken.None);

        Assert.Equal(1, provider.ExecutionCount);
        Assert.Equal("deepseek-chat", response.ModelId);
        Assert.Equal("draft:add provider integration", response.Payload.Markdown);
        Assert.Equal(42, response.Usage!.InputTokens);
        Assert.Equal(15, response.Usage.OutputTokens);
    }

    [Fact]
    public async Task ModelProviderContracts_GivenMismatchedRequestResultTypes_ExpectedDeterministicContractMismatchFailure()
    {
        var provider = new StubModelProvider();
        object mismatchedRequest = new ModelProviderRequest<string>(
            payload: "not-a-plan-prompt",
            modelId: "deepseek-chat",
            correlationId: "corr-mismatch");
        var context = new CapabilityContext(new SessionId("session-mismatch"), "C:/repo/modus/.wip/worktree");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ModelProviderExecutionRuntime.ExecuteAsync<PlanPromptRequest, PlanDraftResult>(
                provider,
                mismatchedRequest,
                context,
                CancellationToken.None));

        Assert.Equal(0, provider.ExecutionCount);
        Assert.Equal(
            "Model provider request type mismatch. Expected request type 'Wip.Abstractions.Tests.Contracts.ModelProviderContractsTests+PlanPromptRequest' but received 'System.String'.",
            exception.Message);
    }

    [Fact]
    public void ModelProviderContracts_GivenCapabilityKindModelProvider_ExpectedDescriptorMetadataPreservesRequestResultTypes()
    {
        var descriptor = CapabilityDescriptor.For<
            StubModelProvider,
            ModelProviderRequest<PlanPromptRequest>,
            ModelProviderResponse<PlanDraftResult>>(
            capabilityId: new CapabilityId("provider.deepseek"),
            displayName: "DeepSeek model provider",
            kind: CapabilityKind.ModelProvider);

        Assert.Equal(CapabilityKind.ModelProvider, descriptor.Kind);
        Assert.Equal(typeof(ModelProviderRequest<PlanPromptRequest>), descriptor.RequestType);
        Assert.Equal(typeof(ModelProviderResponse<PlanDraftResult>), descriptor.ResultType);
    }

    private sealed record PlanPromptRequest(string Prompt);

    private sealed record PlanDraftResult(string Markdown);

    private sealed class StubModelProvider : IModelProvider<PlanPromptRequest, PlanDraftResult>
    {
        public int ExecutionCount { get; private set; }

        public ValueTask<ModelProviderResponse<PlanDraftResult>> ExecuteAsync(
            ModelProviderRequest<PlanPromptRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;

            return ValueTask.FromResult(
                new ModelProviderResponse<PlanDraftResult>(
                    payload: new PlanDraftResult($"draft:{request.Payload.Prompt}"),
                    providerId: "deepseek",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(42, 15),
                    correlationId: request.CorrelationId));
        }
    }
}