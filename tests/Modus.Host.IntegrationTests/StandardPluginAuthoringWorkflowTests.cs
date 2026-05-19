using Modus.Core.Events;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Plugins;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class StandardPluginAuthoringWorkflowTests
{
    [Fact]
    public void StandardPluginAuthoringWorkflow_GivenContractLifecycleAndOperationsImplemented_ExpectedRuntimeValidationAndActivationSucceed()
    {
        var plugin = new StandardWorkflowPlugin(
            pluginId: "Plugin.Standard.Compliant",
            supportedOperations: ["Ops.Alpha", "Ops.Beta"]);

        var validation = PluginContractValidator.Validate(
            plugin,
            new PluginContractValidationPolicy
            {
                RequireScheduledEventsCapability = false,
                RequireDeterministicRegistrationLifecycle = true,
            });

        var runtime = new InMemoryHostRuntime();
        var descriptor = new PluginDescriptor(
            PluginId: plugin.PluginId,
            AssemblyName: plugin.PluginId,
            Version: new Version(1, 0, 0),
            Capabilities: ["Cap.Standard"],
            DependsOn: [],
            DeclaredOperations: plugin.SupportedOperations.ToList());

        var result = runtime.Start([descriptor]);

        Assert.True(validation.IsValid);
        Assert.Empty(validation.MissingCapabilities);
        Assert.Contains(plugin.PluginId, result.ActivatedPluginIds);
        Assert.Contains($"stage=activation plugin={plugin.PluginId} outcome=success", result.Diagnostics, StringComparer.Ordinal);
        Assert.Contains($"stage=operation plugin={plugin.PluginId} operation=Ops.Alpha outcome=success", result.Diagnostics, StringComparer.Ordinal);
    }

    [Fact]
    public void StandardPluginAuthoringWorkflow_GivenDuplicateOrAmbiguousOperations_ExpectedOperationResolutionRemainsDeterministic()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-standard-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var projectPath = Path.Combine(root, "Plugin.Standard.Ordered.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ModusOperations>Ops.Zeta; Ops.Alpha; Ops.Alpha; Ops.Beta</ModusOperations></PropertyGroup></Project>");

            var descriptorFactory = new PluginProjectDescriptorFactory();
            var descriptor = descriptorFactory.Create(projectPath);
            var runtime = new InMemoryHostRuntime();

            var firstRun = runtime.Start([descriptor]);
            var secondRun = runtime.Start([descriptor]);

            var firstOperationDiagnostic = Assert.Single(
                firstRun.Diagnostics.Where(x => x.StartsWith("stage=operation plugin=Plugin.Standard.Ordered", StringComparison.Ordinal)));
            var secondOperationDiagnostic = Assert.Single(
                secondRun.Diagnostics.Where(x => x.StartsWith("stage=operation plugin=Plugin.Standard.Ordered", StringComparison.Ordinal)));

            Assert.Equal(["Ops.Alpha", "Ops.Beta", "Ops.Zeta"], descriptor.DeclaredOperations);
            Assert.Equal(
                "stage=operation plugin=Plugin.Standard.Ordered operation=Ops.Alpha outcome=success",
                firstOperationDiagnostic);
            Assert.Equal(firstOperationDiagnostic, secondOperationDiagnostic);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StandardWorkflowPlugin :
        IPluginContract,
        IPluginLifecycle,
        IPluginOperationCatalog,
        IEventSubscriber,
        ISyncResponder,
        IPluginRegistrationPolicy
    {
        public StandardWorkflowPlugin(string pluginId, IReadOnlyCollection<string> supportedOperations)
        {
            PluginId = pluginId;
            SupportedOperations = supportedOperations;
        }

        public string PluginId { get; }

        public string ContractName => "Modus.PluginContract";

        public Version ContractVersion => new(1, 0);

        public IReadOnlyCollection<string> SupportedOperations { get; }

        public void Load(PluginLoadContext context)
        {
        }

        public void Start(PluginStartContext context)
        {
        }

        public void Stop(PluginStopContext context)
        {
        }

        public void Unload(PluginUnloadContext context)
        {
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(Success: true, Payload: "ok");
        }

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
        {
            var steps = SupportedOperations
                .OrderBy(x => x, StringComparer.Ordinal)
                .Distinct(StringComparer.Ordinal)
                .Select((operation, index) => new PluginRegistrationStep(index + 1, PluginRegistrationStepKind.RegisterOperation, operation))
                .ToArray();

            return steps;
        }
    }
}
