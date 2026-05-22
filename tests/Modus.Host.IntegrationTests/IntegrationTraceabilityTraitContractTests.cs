using System.Reflection;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class IntegrationTraceabilityTraitContractTests
{
    [Fact]
    [Trait("ChecklistItem", "Verify each assurance test includes checklist trace and audit artifact traits for evidence-based review [mandatory - traceability]")]
    [Trait("AuditArtifact", "iterative-implementation-assurance-traceability-contract-2026-05-22")]
    public void IntegrationTests_GivenVerifiabilityChecklist_ExpectedAssuranceTestsHaveChecklistItemAndAuditArtifactTraits()
    {
        var assuranceTests = new[]
        {
            new AssuranceTestSpec(typeof(PluginFolderWatcherRuntimeRegistryTests), nameof(PluginFolderWatcherRuntimeRegistryTests.PluginFolderWatcher_GivenInScopeProjectCreated_ExpectedSnapshotMutationAndApiDispatchSuccess)),
            new AssuranceTestSpec(typeof(PluginFolderWatcherRuntimeRegistryTests), nameof(PluginFolderWatcherRuntimeRegistryTests.PluginFolderWatcher_GivenOnboardedProjectDeleted_ExpectedSnapshotEvictionDiagnosticsAndDispatchMiss)),
            new AssuranceTestSpec(typeof(PluginUploadEndpointTests), nameof(PluginUploadEndpointTests.RuntimeLifecycleDiagnostics_GivenHotLoadRunAndUnloadFlows_ExpectedDeterministicStageOutcomeTokensForMandatoryLifecycleStages)),
            new AssuranceTestSpec(typeof(PluginUploadEndpointTests), nameof(PluginUploadEndpointTests.StartPluginUpload_GivenValidSignedPackage_ExpectedRegistryMutationBeforeCompletedStatus)),
            new AssuranceTestSpec(typeof(PluginUploadEndpointTests), nameof(PluginUploadEndpointTests.StartPluginUpload_GivenValidSignedPackage_ExpectedUploadedOperationDispatchWithCorrelationAndPayloadContract)),
            new AssuranceTestSpec(typeof(PluginUploadEndpointTests), nameof(PluginUploadEndpointTests.StartPluginUpload_GivenArchiveWithoutAssemblies_ExpectedRegistrySnapshotUnchangedAndUploadedOperationNonDispatchable)),
            new AssuranceTestSpec(typeof(PluginUploadEndpointTests), nameof(PluginUploadEndpointTests.GetPluginUploadOperationStatus_GivenAsyncUploadPipeline_ExpectedMonotonicPollingTransitionsThroughTerminalState)),
            new AssuranceTestSpec(typeof(PluginUploadEndpointTests), nameof(PluginUploadEndpointTests.GetPluginUploadOperationStatus_GivenOutOfOrderStoreUpdates_ExpectedEndpointSequenceRemainsMonotonicAndTerminalStateSticks)),
            new AssuranceTestSpec(typeof(PluginUploadEndpointTests), nameof(PluginUploadEndpointTests.RuntimeResolver_GivenRuntimeAddedDispatchTargets_ExpectedApiDispatchHonorsSingletonScopedAndTransientLifetimes)),
            new AssuranceTestSpec(typeof(RuntimeAddedScheduledJobsAbsoluteGatesTests), nameof(RuntimeAddedScheduledJobsAbsoluteGatesTests.ScheduledJobs_GivenRuntimeAddedTimerPlugin_ExpectedDeterministicRegistrationAndAbsoluteCadenceGates)),
            new AssuranceTestSpec(typeof(HostTelemetryScheduledPluginWorkflowTests), nameof(HostTelemetryScheduledPluginWorkflowTests.TelemetryPluginHostStartup_GivenServiceProviderCannotResolveScheduledPluginType_ExpectedHostHealthyAndDeterministicUnresolvableDiagnostic)),
        };

        foreach (var assuranceTest in assuranceTests)
        {
            var method = assuranceTest.TestType.GetMethod(assuranceTest.MethodName, BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(method);

            var traits = method!.CustomAttributes
                .Where(attribute => attribute.AttributeType == typeof(TraitAttribute))
                .Select(attribute => new
                {
                    Name = attribute.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value as string : null,
                    Value = attribute.ConstructorArguments.Count > 1 ? attribute.ConstructorArguments[1].Value as string : null,
                })
                .ToArray();

            Assert.Contains(
                traits,
                trait => string.Equals(trait.Name, "ChecklistItem", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(trait.Value));

            Assert.Contains(
                traits,
                trait => string.Equals(trait.Name, "AuditArtifact", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(trait.Value));
        }
    }

    private readonly record struct AssuranceTestSpec(Type TestType, string MethodName);
}