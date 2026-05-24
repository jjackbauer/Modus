using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.WebApi;
using Modus.Host.Plugins.Compliance;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginLoadingTutorialBehaviorProofComplianceTests
{
    private const string ChecklistItem = "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
    private const string RequirementsDocRelativePath = ".github/requirements/all the projects.md";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ValidatePlannedIntegrationTests_GivenAllProjectsRequirements_ExpectedAtLeastOneRuntimeProofPathPerItem()
    {
        var document = ReadRepositoryFile(RequirementsDocRelativePath);
        var testPlanSection = GetTestPlanSection(document);
        var plannedTestCount = 0;

        for (var index = 0; index < testPlanSection.Length; index++)
        {
            var line = testPlanSection[index];
            if (!Regex.IsMatch(line, @"^\d+\.\s+`.+`", RegexOptions.CultureInvariant))
            {
                continue;
            }

            plannedTestCount++;
            var assumptionLine = FindAssumptionLine(testPlanSection, index + 1);

            Assert.False(string.IsNullOrWhiteSpace(assumptionLine));
            Assert.True(HasBehaviorProofSignal(assumptionLine!), $"Assumption missing behavior-proof signal: {assumptionLine}");
        }

        Assert.True(plannedTestCount > 0);
    }

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

    private static string[] GetTestPlanSection(string document)
    {
        var lines = document.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var testPlanStart = Array.FindIndex(lines, static line => string.Equals(line.Trim(), "## Test Plan", StringComparison.Ordinal));
        Assert.True(testPlanStart >= 0);

        var nextSection = Array.FindIndex(lines, testPlanStart + 1, static line => line.StartsWith("---", StringComparison.Ordinal));
        if (nextSection < 0)
        {
            nextSection = lines.Length;
        }

        var length = nextSection - (testPlanStart + 1);
        return lines.Skip(testPlanStart + 1).Take(length).ToArray();
    }

    private static string? FindAssumptionLine(string[] lines, int startIndex)
    {
        for (var index = startIndex; index < lines.Length; index++)
        {
            var current = lines[index].Trim();
            if (current.StartsWith("### ", StringComparison.Ordinal)
                || Regex.IsMatch(current, @"^\d+\.\s+`.+`", RegexOptions.CultureInvariant))
            {
                return null;
            }

            if (current.StartsWith("*Assumption*:", StringComparison.Ordinal))
            {
                return current;
            }
        }

        return null;
    }

    private static bool HasBehaviorProofSignal(string assumptionLine)
    {
        if (assumptionLine.Contains("metadata-only", StringComparison.OrdinalIgnoreCase)
            && (assumptionLine.Contains("reject", StringComparison.OrdinalIgnoreCase)
                || assumptionLine.Contains("cannot", StringComparison.OrdinalIgnoreCase)
                || assumptionLine.Contains("noncompliant", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var tokens = new[]
        {
            "runtime",
            "live",
            "execution",
            "lifecycle",
            "api",
            "integration",
            "dispatch",
            "endpoint",
            "scope",
            "schedule",
            "deterministic",
            "rejection",
            "correlation",
            "behavior",
            "proof",
            "gate"
        };

        return tokens.Any(token => assumptionLine.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            var filePath = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(solutionPath) && File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root containing Modus.slnx and {relativePath}.");
    }
}