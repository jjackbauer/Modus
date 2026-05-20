using Modus.Core.Events;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Plugins;

public sealed class PluginRegistrationPolicyTests
{
    [Fact]
    public void RegistrationPolicy_GivenCatalogWithDuplicateAndUnorderedOperations_ExpectedDeterministicPlan()
    {
        var plugin = new PluginWithEventsAndSchedules(
            pluginId: "Registration.Plugin",
            operations: [" warmup ", "ping", "PING", "rebuild"]);
        IPluginRegistrationPolicy policy = new DeterministicPluginRegistrationPolicy();

        var firstPlan = policy.BuildRegistrationPlan(plugin);
        var secondPlan = policy.BuildRegistrationPlan(plugin);

        Assert.Equal(firstPlan, secondPlan);
        Assert.Equal(5, firstPlan.Count);

        Assert.Equal(new PluginRegistrationStep(1, PluginRegistrationStepKind.RegisterOperation, "operation:ping"), firstPlan[0]);
        Assert.Equal(new PluginRegistrationStep(2, PluginRegistrationStepKind.RegisterOperation, "operation:rebuild"), firstPlan[1]);
        Assert.Equal(new PluginRegistrationStep(3, PluginRegistrationStepKind.RegisterOperation, "operation:warmup"), firstPlan[2]);
        Assert.Equal(new PluginRegistrationStep(4, PluginRegistrationStepKind.SubscribeEvents, "events:subscribe"), firstPlan[3]);
        Assert.Equal(new PluginRegistrationStep(5, PluginRegistrationStepKind.RegisterSchedules, "schedules:register"), firstPlan[4]);
    }

    [Fact]
    public void RegistrationPolicy_GivenPluginWithoutOptionalCapabilities_ExpectedOperationsOnlyPlan()
    {
        var plugin = new PluginWithCatalogOnly(
            pluginId: "CatalogOnly.Plugin",
            operations: [" status ", "health"]);
        IPluginRegistrationPolicy policy = new DeterministicPluginRegistrationPolicy();

        var plan = policy.BuildRegistrationPlan(plugin);

        Assert.Equal(2, plan.Count);
        Assert.Equal(new PluginRegistrationStep(1, PluginRegistrationStepKind.RegisterOperation, "operation:health"), plan[0]);
        Assert.Equal(new PluginRegistrationStep(2, PluginRegistrationStepKind.RegisterOperation, "operation:status"), plan[1]);
    }

    [Fact]
    public void ContractValidator_GivenDeterministicRegistrationRequiredAndPluginOmitsPolicy_ExpectedValidationFailure()
    {
        var plugin = new PluginMissingRegistrationPolicy(
            pluginId: "MissingRegistration.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));
        var validationPolicy = new PluginContractValidationPolicy
        {
            RequireDeterministicRegistrationLifecycle = true,
        };

        var result = PluginContractValidator.Validate(plugin, validationPolicy);

        Assert.False(result.IsValid);
        Assert.Contains(new CapabilityName(nameof(IPluginRegistrationPolicy)), result.MissingCapabilities);
    }

    private sealed class PluginWithEventsAndSchedules : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IEventSubscriber, ISyncResponder, IPluginScheduledEvents
    {
        public PluginWithEventsAndSchedules(string pluginId, IReadOnlyCollection<string> operations)
        {
            PluginId = new PluginId(pluginId);
            SupportedOperations = operations.Select(op => new OperationName(op)).ToArray();
            ContractName = new ContractName("Modus.PluginContract");
            ContractVersion = new Version(1, 0);
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

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

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
        }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(Success: true, Payload: "ok");
        }
    }

    private sealed class PluginWithCatalogOnly : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, ISyncResponder
    {
        public PluginWithCatalogOnly(string pluginId, IReadOnlyCollection<string> operations)
        {
            PluginId = new PluginId(pluginId);
            SupportedOperations = operations.Select(op => new OperationName(op)).ToArray();
            ContractName = new ContractName("Modus.PluginContract");
            ContractVersion = new Version(1, 0);
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

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

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(Success: true, Payload: "ok");
        }
    }

    private sealed class PluginMissingRegistrationPolicy : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IEventSubscriber, ISyncResponder
    {
        public PluginMissingRegistrationPolicy(string pluginId, string contractName, Version contractVersion)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName(contractName);
            ContractVersion = contractVersion;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; } = [new OperationName("ping")];

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
            return new SyncResponse(Success: true, Payload: "pong");
        }
    }
}
