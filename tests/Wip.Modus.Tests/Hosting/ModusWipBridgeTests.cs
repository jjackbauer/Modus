using Modus.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Samples.TodoApp.WipAgents;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Workflows;
using Wip.Builder;
using Wip.Modus.Hosting;
using Xunit;

namespace Wip.Modus.Tests.Hosting;

public sealed class ModusWipBridgeTests
{
    [Fact]
    public void ExternalPluginBuild_GivenSeparateProjectUsingBuilderApis_BuildsWithoutShellHostCodeChanges()
    {
        IServiceCollection services = new TestServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddTodoAppWipAgents();

        var agentDescriptor = Assert.Single(builder.CapabilityDescriptors, static descriptor => descriptor.CapabilityId.Value == "todoapp.agent.plan");
        var validatorDescriptor = Assert.Single(builder.CapabilityDescriptors, static descriptor => descriptor.CapabilityId.Value == "todoapp.validator.result");
        var workflow = Assert.Single(builder.WorkflowRegistrations, static registration => registration.WorkflowId.Value == "todoapp.workflow.delivery");

        Assert.Equal(typeof(TodoPlanRequest), agentDescriptor.RequestType);
        Assert.Equal(typeof(TodoPlanResult), agentDescriptor.ResultType);
        Assert.Equal(typeof(TodoValidationRequest), validatorDescriptor.RequestType);
        Assert.Equal(typeof(TodoValidationResult), validatorDescriptor.ResultType);
        Assert.Equal(typeof(TodoWorkflowRequest), workflow.RequestType);
        Assert.Equal(typeof(TodoWorkflowResult), workflow.ResultType);

        Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(TodoPlanAgent));
        Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(TodoResultValidator));
        Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(TodoAppWorkflow));
    }

    [Fact]
    public async Task ShellDiscovery_GivenExternalPluginAssembly_ListsRegisteredAgentValidatorAndWorkflow()
    {
        IServiceCollection services = new TestServiceCollection();
        var builder = new WipBuilder(services);
        builder.AddTodoAppWipAgents();

        var pluginsPath = CreatePluginsFolderWithAssembly(typeof(TodoAppWipPlugin).Assembly.Location);

        try
        {
            var bridge = new ModusWipBridge(pluginsPath, builder.WorkflowRegistrations);

            var count = await bridge.LoadPluginsAsync(CancellationToken.None);
            var manifest = bridge.GetRunManifest();
            var plugin = Assert.Single(manifest.Plugins, static entry => entry.PluginId == "samples.todoapp.wipagents");
            var workflow = Assert.Single(manifest.Workflows, static entry => entry.WorkflowId == "todoapp.workflow.delivery");

            Assert.Equal(1, count);
            Assert.Equal(new[] { "todoapp.agent.plan", "todoapp.validator.result" }, plugin.Capabilities);
            Assert.Equal(new[] { "RegisterOperation", "SubscribeEvents" }, plugin.RequiredPermissions);
            Assert.Equal(typeof(TodoWorkflowRequest).FullName, workflow.RequestType);
            Assert.Equal(typeof(TodoWorkflowResult).FullName, workflow.ResultType);
        }
        finally
        {
            if (Directory.Exists(pluginsPath))
                Directory.Delete(pluginsPath, recursive: true);
        }
    }

    [Fact]
    public async Task PluginLoader_GivenPluginsInConfiguredFolders_LoadsCapabilitiesOncePerShellProcess()
    {
        var pluginsPath = CreatePluginsFolderWithAssembly(typeof(ModusWipBridgeTests).Assembly.Location);

        try
        {
            var bridge = new ModusWipBridge(pluginsPath, Array.Empty<WorkflowRegistration>());

            var firstCount = await bridge.LoadPluginsAsync(CancellationToken.None);
            var secondCount = await bridge.LoadPluginsAsync(CancellationToken.None);
            var manifest = bridge.GetRunManifest();

            Assert.True(firstCount > 0);
            Assert.Equal(firstCount, secondCount);
            Assert.Equal(firstCount, manifest.Plugins.Count);
        }
        finally
        {
            if (Directory.Exists(pluginsPath))
                Directory.Delete(pluginsPath, recursive: true);
        }
    }

    [Fact]
    public async Task RunManifestBuilder_GivenLoadedPlugins_CapturesPluginAssemblyVersionCapabilitiesAndPermissions()
    {
        var pluginsPath = CreatePluginsFolderWithAssembly(typeof(ModusWipBridgeTests).Assembly.Location);

        try
        {
            var workflowRegistrations = new[]
            {
                new WorkflowRegistration(
                    WorkflowId: new WorkflowId("workflow.demo"),
                    WorkflowType: typeof(DemoWorkflow),
                    RequestType: typeof(DemoWorkflowRequest),
                    ResultType: typeof(DemoWorkflowResult),
                    Descriptor: new WorkflowDescriptor<DemoWorkflowRequest, DemoWorkflowResult>(new WorkflowId("workflow.demo"), "Demo Workflow"))
            };

            var bridge = new ModusWipBridge(pluginsPath, workflowRegistrations);

            await bridge.LoadPluginsAsync(CancellationToken.None);

            var manifest = bridge.GetRunManifest();
            var plugin = manifest.Plugins.Single(static entry => entry.PluginId == "bridge-test.plugin");
            var workflow = Assert.Single(manifest.Workflows);

            Assert.Equal("BridgeTestPlugin", plugin.PluginName);
            Assert.Equal("2.1.0", plugin.PluginVersion);
            Assert.False(string.IsNullOrWhiteSpace(plugin.AssemblyName));
            Assert.False(string.IsNullOrWhiteSpace(plugin.AssemblyVersion));
            Assert.Equal(new[] { "bridge.test.execute", "bridge.test.validate" }, plugin.Capabilities);
            Assert.Equal(new[] { "RegisterOperation", "SubscribeEvents" }, plugin.RequiredPermissions);

            Assert.Equal("workflow.demo", workflow.WorkflowId);
            Assert.Equal("Demo Workflow", workflow.DisplayName);
            Assert.Equal(typeof(DemoWorkflowRequest).FullName, workflow.RequestType);
            Assert.Equal(typeof(DemoWorkflowResult).FullName, workflow.ResultType);
        }
        finally
        {
            if (Directory.Exists(pluginsPath))
                Directory.Delete(pluginsPath, recursive: true);
        }
    }

    private static string CreatePluginsFolderWithAssembly(string assemblyPath)
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-wip-modus-tests-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        var copiedAssemblyPath = Path.Combine(pluginsPath, Path.GetFileName(assemblyPath));
        File.Copy(assemblyPath, copiedAssemblyPath, overwrite: true);

        return pluginsPath;
    }

    public sealed class BridgeTestPlugin : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IPluginRegistrationPolicy
    {
        public PluginId PluginId => new("bridge-test.plugin");

        public ContractName ContractName => new("BridgeTest.Contract");

        public Version ContractVersion => new(2, 1, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations =>
        [
            new("bridge.test.execute"),
            new("bridge.test.validate")
        ];

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

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
        {
            return
            [
                new PluginRegistrationStep(1, PluginRegistrationStepKind.RegisterOperation, "operation:bridge.test.execute"),
                new PluginRegistrationStep(2, PluginRegistrationStepKind.SubscribeEvents, "events:subscribe")
            ];
        }
    }

    public sealed record DemoWorkflowRequest(string Task);

    public sealed record DemoWorkflowResult(string Summary);

    public sealed class DemoWorkflow : IWorkflow<DemoWorkflowRequest, DemoWorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.demo");

        public ValueTask<DemoWorkflowResult> ExecuteAsync(
            DemoWorkflowRequest request,
            WorkflowContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new DemoWorkflowResult(request.Task));
    }

    private sealed class TestServiceCollection : List<ServiceDescriptor>, IServiceCollection;
}