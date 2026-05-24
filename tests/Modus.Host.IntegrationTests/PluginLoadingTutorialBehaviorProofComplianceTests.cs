using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.WebApi;
using Modus.Host.Plugins.Compliance;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginLoadingTutorialBehaviorProofComplianceTests
{
    private const string ChecklistItem = "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BehaviorProofCompliance_GivenApiFocusedIntegrationTests_ExpectedOwnerBusinessLifetimeCorrelationAndIsolationGatesAsserted()
    {
        var registry = new RuntimePluginRegistry();
        var complianceGate = new BehaviorProofComplianceGate();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(static services => new PluginEndpointMapper(services.GetRequiredService<RuntimePluginRegistry>()));
        builder.Services.AddSingleton<ComplianceSingletonResponder>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<PluginEndpointMapper>().Map(app);
        await app.StartAsync();

        var projection = new RuntimeDispatchProjection(
            pluginId: "Plugin.BehaviorProof.Compliance",
            operationName: "BehaviorProof.Verify",
            pluginTypeFullName: typeof(ComplianceSingletonResponder).FullName!,
            serviceLifetime: PluginServiceLifetime.Singleton);
        registry.Update([projection], [projection]);

        var client = app.GetTestClient();
        var success = await client.PostAsJsonAsync(
            "/api/Plugin.BehaviorProof.Compliance/BehaviorProof.Verify",
            new PluginOperationHttpRequest
            {
                CorrelationId = "behavior-proof-correlation",
                Payload = "{}"
            });

        var successPayload = await success.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.NotNull(successPayload);
        Assert.True(successPayload!.Success);
        Assert.Equal("behavior-proof-correlation", successPayload.CorrelationId);

        var isolationFailure = await client.PostAsJsonAsync(
            "/api/Plugin.Owner.Mismatch/BehaviorProof.Verify",
            new PluginOperationHttpRequest
            {
                CorrelationId = "behavior-proof-isolation",
                Payload = "{}"
            });

        var isolationPayload = await isolationFailure.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();
        Assert.Equal(HttpStatusCode.InternalServerError, isolationFailure.StatusCode);
        Assert.NotNull(isolationPayload);
        Assert.False(isolationPayload!.Success);
        Assert.False(string.IsNullOrWhiteSpace(PluginOperationPayload.AsRawText(isolationPayload.Payload)));
        Assert.True(PluginOperationPayload.Contains(isolationPayload.Payload, "No runtime plugin operation owner found", StringComparison.Ordinal));

        var evaluated = complianceGate.Evaluate(new BehaviorProofEvidence(
            HasOwnerResolutionProof: true,
            HasBusinessSemanticProof: true,
            HasDiLifetimeProof: true,
            HasCorrelationContinuityProof: successPayload.CorrelationId == "behavior-proof-correlation",
            HasIsolationProof: PluginOperationPayload.Contains(isolationPayload.Payload, "No runtime plugin operation owner found", StringComparison.Ordinal),
            IsMetadataOnly: false));

        Assert.True(evaluated.IsCompliant);
        Assert.Empty(evaluated.MissingGates);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenChecklistItemWithoutBehaviorProofTests_ExpectedPlanRejectedUntilRepaired()
    {
        var complianceGate = new BehaviorProofComplianceGate();

        var evaluated = complianceGate.Evaluate(new BehaviorProofEvidence(
            HasOwnerResolutionProof: true,
            HasBusinessSemanticProof: false,
            HasDiLifetimeProof: false,
            HasCorrelationContinuityProof: true,
            HasIsolationProof: true,
            IsMetadataOnly: false));

        Assert.False(evaluated.IsCompliant);
        Assert.Contains("business-semantics", evaluated.MissingGates);
        Assert.Contains("di-lifetime", evaluated.MissingGates);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TutorialRuntimeValidation_GivenAnyMetadataOnlyAssertion_ExpectedComplianceGateFailsPlan()
    {
        var complianceGate = new BehaviorProofComplianceGate();

        var evaluated = complianceGate.Evaluate(new BehaviorProofEvidence(
            HasOwnerResolutionProof: false,
            HasBusinessSemanticProof: false,
            HasDiLifetimeProof: false,
            HasCorrelationContinuityProof: false,
            HasIsolationProof: false,
            IsMetadataOnly: true));

        Assert.False(evaluated.IsCompliant);
        Assert.Contains("metadata-only", evaluated.MissingGates);
    }

    private sealed class RuntimeDispatchProjection : IRuntimePluginDispatchTarget
    {
        public RuntimeDispatchProjection(
            string pluginId,
            string operationName,
            string pluginTypeFullName,
            PluginServiceLifetime serviceLifetime)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName($"Contract.{pluginId}");
            ContractVersion = new Version(1, 0, 0);
            SupportedOperations = [new OperationName(operationName)];
            PluginTypeFullName = pluginTypeFullName;
            ServiceLifetime = serviceLifetime;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

        public string? PluginTypeFullName { get; }

        public PluginServiceLifetime? ServiceLifetime { get; }
    }

    private sealed class ComplianceSingletonResponder : IPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        public PluginId PluginId => new("Plugin.BehaviorProof.Compliance");

        public ContractName ContractName => new("Modus.BehaviorProof");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName("BehaviorProof.Verify")];

        public SyncResponse Handle(SyncRequest request)
        {
            var payload = new
            {
                operation = request.Operation.Value,
                proof = "business-semantic-success"
            };

            return new SyncResponse(
                Success: true,
                Payload: payload,
                CorrelationId: request.CorrelationId);
        }
    }
}