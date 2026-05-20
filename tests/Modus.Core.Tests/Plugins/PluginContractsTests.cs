using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Events;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Plugins;

public sealed class PluginContractsTests
{
    [Fact]
    public void LifecycleContract_GivenCoreAssembly_ExpectedIPluginLifecycleExists()
    {
        var lifecycleType = typeof(IPluginContract).Assembly.GetType("Modus.Core.Plugins.IPluginLifecycle");

        Assert.NotNull(lifecycleType);
    }

    [Fact]
    public void ContractsMove_GivenGenericPluginContracts_ExpectedTypesRemainPubliclyResolvable()
    {
        var assembly = typeof(IPluginContract).Assembly;
        var expectedContractTypeNames = new[]
        {
            "Modus.Core.Plugins.IPluginContract",
            "Modus.Core.Plugins.IPluginDependencyRegister",
            "Modus.Core.Plugins.IPluginLifecycle",
            "Modus.Core.Plugins.IPluginOperationCatalog",
            "Modus.Core.Plugins.IPluginRegistrationPolicy",
            "Modus.Core.Plugins.IPluginScheduledEvents",
            "Modus.Core.Plugins.IPluginScheduler",
            "Modus.Core.Plugins.IScheduledTimerTaskExtension"
        };

        foreach (var fullName in expectedContractTypeNames)
        {
            var contractType = assembly.GetType(fullName);
            Assert.NotNull(contractType);
            Assert.True(contractType!.IsPublic, $"Expected '{fullName}' to remain publicly visible.");
            Assert.Equal("Modus.Core.Plugins", contractType.Namespace);
        }
    }

    [Fact]
    public void ContractsMove_GivenBuildAfterMove_ExpectedNoMissingTypeErrorsForCoreContracts()
    {
        var plugin = new PluginWithScheduledEvents(
            pluginId: "Contracts.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));
        var register = new DummyDependencyRegister();
        var scheduler = new RecordingScheduler();

        Assert.IsAssignableFrom<IPluginContract>(plugin);
        Assert.IsAssignableFrom<IPluginLifecycle>(plugin);
        Assert.IsAssignableFrom<IPluginOperationCatalog>(plugin);
        Assert.IsAssignableFrom<IPluginRegistrationPolicy>(plugin);
        Assert.IsAssignableFrom<IPluginScheduledEvents>(plugin);
        Assert.IsAssignableFrom<IPluginScheduler>(scheduler);
        Assert.IsAssignableFrom<IPluginDependencyRegister>(register);

        plugin.RegisterSchedules(scheduler);

        var recurring = Assert.Single(scheduler.RecurringSchedules);
        var oneTime = Assert.Single(scheduler.OneTimeSchedules);
        Assert.Equal(new JobName("rebuild-index"), recurring.JobName);
        Assert.Equal(new JobName("run-warmup"), oneTime.JobName);
    }

    [Fact]
    public void PluginContract_GivenCoreAssembly_ExpectedIdentityAndVersionMembersDefined()
    {
        var contractType = typeof(IPluginContract);

        Assert.Equal(typeof(PluginId), contractType.GetProperty(nameof(IPluginContract.PluginId))?.PropertyType);
        Assert.Equal(typeof(ContractName), contractType.GetProperty(nameof(IPluginContract.ContractName))?.PropertyType);
        Assert.Equal(typeof(Version), contractType.GetProperty(nameof(IPluginContract.ContractVersion))?.PropertyType);
    }

    [Fact]
    public void DependencyRegisterContract_GivenCoreAssembly_ExpectedRegisterMethodAcceptsServiceCollection()
    {
        var registerType = typeof(IPluginDependencyRegister);
        var registerMethod = registerType.GetMethod(nameof(IPluginDependencyRegister.Register));

        Assert.NotNull(registerMethod);
        Assert.Equal(typeof(void), registerMethod!.ReturnType);
        Assert.Equal(typeof(IServiceCollection), registerMethod.GetParameters().Single().ParameterType);

        var register = new DummyDependencyRegister();
        Assert.IsAssignableFrom<IPluginDependencyRegister>(register);
    }

    [Fact]
    public void LifecycleContract_GivenCompliantPlugin_ExpectedLoadStartStopUnloadOperationsExposed()
    {
        var plugin = new LifecycleAwarePlugin(
            pluginId: "Lifecycle.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));

        using var cts = new CancellationTokenSource();

        plugin.Load(new PluginLoadContext(plugin.PluginId, cts.Token));
        plugin.Start(new PluginStartContext(plugin.PluginId, cts.Token));
        plugin.Stop(new PluginStopContext(plugin.PluginId, cts.Token));
        plugin.Unload(new PluginUnloadContext(
            plugin.PluginId,
            PluginUnloadReason.GracefulShutdown,
            DateTimeOffset.UtcNow.AddMinutes(1),
            cts.Token));

        Assert.Equal(new[] { "Load", "Start", "Stop", "Unload" }, plugin.ExecutedOperations);
    }

    [Fact]
    public void LifecycleContract_GivenMissingStartOperation_ExpectedValidationFailureBeforeRegistration()
    {
        var plugin = new MissingStartPlugin(
            pluginId: "PartialLifecycle.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));

        var result = PluginContractValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(new CapabilityName(nameof(IPluginLifecycle)), result.MissingCapabilities);
    }

    [Fact]
    public void LifecycleContext_GivenLoadOperation_ExpectedContextContainsPluginIdentityAndCancellation()
    {
        var assembly = typeof(IPluginContract).Assembly;
        var loadContextType = assembly.GetType("Modus.Core.Plugins.PluginLoadContext");

        Assert.NotNull(loadContextType);
        Assert.Equal(typeof(PluginId), loadContextType!.GetProperty("PluginId")?.PropertyType);
        Assert.Equal(typeof(CancellationToken), loadContextType.GetProperty("CancellationToken")?.PropertyType);

        using var cts = new CancellationTokenSource();
        var loadContext = Activator.CreateInstance(loadContextType, new PluginId("Orders.Plugin"), cts.Token);

        Assert.NotNull(loadContext);
        Assert.Equal(new PluginId("Orders.Plugin"), loadContextType.GetProperty("PluginId")!.GetValue(loadContext));
        Assert.Equal(cts.Token, loadContextType.GetProperty("CancellationToken")!.GetValue(loadContext));

        var loadMethod = typeof(IPluginLifecycle).GetMethod("Load", [loadContextType]);
        Assert.NotNull(loadMethod);
    }

    [Fact]
    public void LifecycleContext_GivenUnloadOperation_ExpectedContextContainsReasonAndDeadline()
    {
        var assembly = typeof(IPluginContract).Assembly;
        var unloadContextType = assembly.GetType("Modus.Core.Plugins.PluginUnloadContext");
        var unloadReasonType = assembly.GetType("Modus.Core.Plugins.PluginUnloadReason");

        Assert.NotNull(unloadContextType);
        Assert.NotNull(unloadReasonType);
        Assert.Equal(typeof(PluginId), unloadContextType!.GetProperty("PluginId")?.PropertyType);
        Assert.Equal(unloadReasonType, unloadContextType.GetProperty("UnloadReason")?.PropertyType);
        Assert.Equal(typeof(DateTimeOffset), unloadContextType.GetProperty("Deadline")?.PropertyType);
        Assert.Equal(typeof(CancellationToken), unloadContextType.GetProperty("CancellationToken")?.PropertyType);

        using var cts = new CancellationTokenSource();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var gracefulShutdown = Enum.Parse(unloadReasonType!, "GracefulShutdown");
        var unloadContext = Activator.CreateInstance(unloadContextType, new PluginId("Orders.Plugin"), gracefulShutdown, deadline, cts.Token);

        Assert.NotNull(unloadContext);
        Assert.Equal(new PluginId("Orders.Plugin"), unloadContextType.GetProperty("PluginId")!.GetValue(unloadContext));
        Assert.Equal(gracefulShutdown, unloadContextType.GetProperty("UnloadReason")!.GetValue(unloadContext));
        Assert.Equal(deadline, unloadContextType.GetProperty("Deadline")!.GetValue(unloadContext));
        Assert.Equal(cts.Token, unloadContextType.GetProperty("CancellationToken")!.GetValue(unloadContext));

        var unloadMethod = typeof(IPluginLifecycle).GetMethod("Unload", [unloadContextType]);
        Assert.NotNull(unloadMethod);
    }

    [Fact]
    public void PluginContract_GivenValidClassLibraryPlugin_ExpectedMandatoryCapabilitiesExposed()
    {
        var plugin = new ValidPlugin(
            pluginId: "Sample.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));

        var result = PluginContractValidator.Validate(plugin);

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingCapabilities);
        Assert.Equal(new PluginId("Sample.Plugin"), plugin.PluginId);
        Assert.Equal(new ContractName("Modus.PluginContract"), plugin.ContractName);
        Assert.Equal(new Version(1, 0), plugin.ContractVersion);
    }

    [Fact]
    public void PluginContract_GivenVersionMismatch_ExpectedValidationFailureBeforeRegistration()
    {
        var plugin = new ValidPlugin(
            pluginId: "Legacy.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(0, 9));

        var result = PluginVersionValidator.Validate(
            plugin.ContractVersion,
            requiredVersion: new Version(1, 0));

        Assert.False(result.IsCompatible);
        Assert.Equal("Contract version mismatch.", result.Error);
    }

    [Fact]
    public void PluginContract_GivenInvalidMetadata_ExpectedValidationFailureBeforeRegistration()
    {
        var plugin = new InvalidContractMetadataPlugin(
            pluginId: default,
            contractName: default,
            contractVersion: null!);

        var result = PluginContractValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(new CapabilityName("IPluginContract.PluginId"), result.MissingCapabilities);
        Assert.Contains(new CapabilityName("IPluginContract.ContractName"), result.MissingCapabilities);
        Assert.Contains(new CapabilityName("IPluginContract.ContractVersion"), result.MissingCapabilities);
    }

    [Fact]
    public void EventContract_GivenPublishedDomainEvent_ExpectedSubscribedHandlersInvoked()
    {
        var bus = new InMemoryEventBus();
        var plugin = new ValidPlugin(
            pluginId: "Events.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));
        plugin.Subscribe(bus);

        bus.Publish(new DomainEvent("OrderCreated"));

        var received = Assert.Single(plugin.ReceivedEvents);
        Assert.Equal("OrderCreated", received.Name);
    }

    [Fact]
    public void SyncFallbackContract_GivenExplicitFallbackRequest_ExpectedSynchronousResponseReturned()
    {
        var plugin = new ValidPlugin(
            pluginId: "Sync.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));

        var fallbackRequest = SyncRequest.ForExplicitFallback(
            operation: new OperationName("ping"),
            fallbackReason: SyncFallbackReason.CapabilityUnavailable,
            fallbackReasonCode: "ASYNC_HANDLER_UNAVAILABLE",
            correlationId: new CorrelationId("corr-sync-1"));
        var response = plugin.Handle(fallbackRequest);

        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.True(response.ServedFromFallback);
        Assert.Equal(new CorrelationId("corr-sync-1"), response.CorrelationId);
        Assert.Equal("pong", response.Payload);
    }

    [Fact]
    public void SyncFallbackContract_GivenStandardPathRequest_ExpectedRejectedSynchronousResponseReturned()
    {
        var plugin = new ValidPlugin(
            pluginId: "Sync.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));

        var standardRequest = SyncRequest.ForStandardPath(
            operation: new OperationName("ping"),
            correlationId: new CorrelationId("corr-sync-2"));
        var response = plugin.Handle(standardRequest);

        Assert.False(response.Success);
        Assert.Equal(SyncResponseStatus.Rejected, response.Status);
        Assert.False(response.ServedFromFallback);
        Assert.Equal(new CorrelationId("corr-sync-2"), response.CorrelationId);
        Assert.Equal("fallback-not-explicit", response.Payload);
    }

    [Fact]
    public void ContractValidator_GivenPluginWithoutLifecycle_ExpectedMissingCapabilitiesIncludesIPluginLifecycle()
    {
        var plugin = new PluginWithoutLifecycle(
            pluginId: "NoLifecycle.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));

        var result = PluginContractValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(new CapabilityName("IPluginLifecycle"), result.MissingCapabilities);
    }

    [Fact]
    public void ContractValidator_GivenPluginWithoutOperationCatalog_ExpectedMissingCapabilitiesIncludesIPluginOperationCatalog()
    {
        var plugin = new PluginWithLifecycleWithoutOperationCatalog(
            pluginId: "NoCatalog.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));

        var result = PluginContractValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(new CapabilityName("IPluginOperationCatalog"), result.MissingCapabilities);
    }

    [Fact]
    public void ContractValidator_GivenPolicyRequiresSchedulingAndPluginOmitsIt_ExpectedSchedulingCapabilityFailure()
    {
        var plugin = new PluginWithLifecycleAndCatalogWithoutScheduling(
            pluginId: "NoScheduling.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));

        var validatorType = typeof(PluginContractValidator);
        var policyType = typeof(IPluginContract).Assembly.GetType("Modus.Core.Plugins.PluginContractValidationPolicy");

        Assert.NotNull(policyType);

        var validateMethod = validatorType.GetMethods()
            .Where(m => m.Name == "Validate" && m.IsGenericMethod && m.GetParameters().Length == 2)
            .Select(m => m.MakeGenericMethod(plugin.GetType()))
            .FirstOrDefault();
        Assert.NotNull(validateMethod);

        var policy = Activator.CreateInstance(policyType!);
        var requireScheduledEventsCapability = policyType!.GetProperty("RequireScheduledEventsCapability");
        Assert.NotNull(requireScheduledEventsCapability);
        requireScheduledEventsCapability!.SetValue(policy, true);

        var result = (ContractValidationResult?)validateMethod!.Invoke(null, [plugin, policy]);
        Assert.NotNull(result);
        Assert.False(result!.IsValid);
        Assert.Contains(new CapabilityName("IPluginScheduledEvents"), result.MissingCapabilities);
    }

    [Fact]
    public void OperationCatalog_GivenDuplicateOperationNames_ExpectedValidationFailure()
    {
        var plugin = new PluginWithDuplicateOperationCatalog(
            pluginId: "DuplicateCatalog.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));

        var result = PluginContractValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(new CapabilityName("IPluginOperationCatalog.SupportedOperations"), result.MissingCapabilities);
    }

    [Fact]
    public void OperationCatalog_GivenNoOperations_ExpectedValidationFailure()
    {
        var plugin = new PluginWithEmptyOperationCatalog(
            pluginId: "EmptyCatalog.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));

        var result = PluginContractValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(new CapabilityName("IPluginOperationCatalog.SupportedOperations"), result.MissingCapabilities);
    }

    [Fact]
    public void ScheduledEvents_GivenRecurringScheduleDefinition_ExpectedSchedulerRegistersOperationAtInterval()
    {
        var plugin = new PluginWithScheduledEvents(
            pluginId: "Scheduling.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));
        var scheduler = new RecordingScheduler();

        plugin.RegisterSchedules(scheduler);

        var recurring = Assert.Single(scheduler.RecurringSchedules);
        Assert.Equal(new JobName("rebuild-index"), recurring.JobName);
        Assert.Equal(TimeSpan.FromMinutes(5), recurring.Interval);
        Assert.Equal(new OperationName("rebuild"), recurring.Operation);
    }

    [Fact]
    public void ScheduledEvents_GivenOneTimeScheduleDefinition_ExpectedSchedulerRegistersOperationAtSpecificInstant()
    {
        var plugin = new PluginWithScheduledEvents(
            pluginId: "Scheduling.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));
        var scheduler = new RecordingScheduler();

        plugin.RegisterSchedules(scheduler);

        var oneTime = Assert.Single(scheduler.OneTimeSchedules);
        Assert.Equal(new JobName("run-warmup"), oneTime.JobName);
        Assert.Equal(new DateTimeOffset(2030, 01, 01, 00, 00, 00, TimeSpan.Zero), oneTime.RunAt);
        Assert.Equal(new OperationName("warmup"), oneTime.Operation);
    }

    [Fact]
    public void PluginContracts_GivenCompliantPlugin_ExpectedValidatorPassesAllMandatoryCapabilities()
    {
        var plugin = new PluginWithScheduledEvents(
            pluginId: "Compliant.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));
        var policy = new PluginContractValidationPolicy { RequireScheduledEventsCapability = true };

        var result = PluginContractValidator.Validate(plugin, policy);

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingCapabilities);
    }

    [Fact]
    public void PluginContracts_GivenPartiallyImplementedPlugin_ExpectedValidatorReportsAllMissingCapabilities()
    {
        var plugin = new PartiallyImplementedPlugin(
            pluginId: "Partial.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));
        var policy = new PluginContractValidationPolicy { RequireScheduledEventsCapability = true };

        var result = PluginContractValidator.Validate(plugin, policy);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                new CapabilityName(nameof(IPluginLifecycle)),
                new CapabilityName(nameof(IEventSubscriber)),
                new CapabilityName(nameof(ISyncResponder)),
                new CapabilityName(nameof(IPluginOperationCatalog)),
                new CapabilityName(nameof(IPluginScheduledEvents)),
                new CapabilityName(nameof(IPluginRegistrationPolicy)),
            ],
            result.MissingCapabilities);
    }

    [Fact]
    public void PluginContractValidator_Validate_GivenNullCandidate_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PluginContractValidator.Validate<object>(null!));
    }

    [Fact]
    public void PluginContractValidator_Validate_GivenBareObject_ReturnsAllCapabilitiesMissing()
    {
        var candidate = new object();
        var result = PluginContractValidator.Validate(candidate);

        Assert.False(result.IsValid);
        Assert.Contains(new CapabilityName(nameof(IPluginContract)), result.MissingCapabilities);
        Assert.Contains(new CapabilityName(nameof(IPluginLifecycle)), result.MissingCapabilities);
        Assert.Contains(new CapabilityName(nameof(IEventSubscriber)), result.MissingCapabilities);
        Assert.Contains(new CapabilityName(nameof(ISyncResponder)), result.MissingCapabilities);
        Assert.Contains(new CapabilityName(nameof(IPluginOperationCatalog)), result.MissingCapabilities);
    }

    [Fact]
    public void PluginContractValidator_Validate_GivenFullyCompliantPlugin_ReturnsIsValidTrue()
    {
        var plugin = new PluginWithScheduledEvents(
            pluginId: "Generic.Plugin",
            contractName: "Modus.PluginContract",
            contractVersion: new Version(1, 0));

        var result = PluginContractValidator.Validate(plugin);

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingCapabilities);
    }

    private sealed class InMemoryEventBus : IEventPublisher
    {
        private readonly List<Action<DomainEvent>> _handlers = new();

        public void Publish(DomainEvent @event)
        {
            foreach (var handler in _handlers)
            {
                handler(@event);
            }
        }

        public void Subscribe(Action<DomainEvent> handler)
        {
            _handlers.Add(handler);
        }
    }

    private sealed class ValidPlugin : IPluginContract, IEventSubscriber, ISyncResponder, IPluginLifecycle, IPluginOperationCatalog, IPluginRegistrationPolicy
    {
        public ValidPlugin(string pluginId, string contractName, Version contractVersion)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName(contractName);
            ContractVersion = contractVersion;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public List<DomainEvent> ReceivedEvents { get; } = new();

        public IReadOnlyCollection<OperationName> SupportedOperations { get; } = [new OperationName("ping")];

        public SyncResponse Handle(SyncRequest request)
        {
            if (!request.IsFallbackExplicit)
            {
                return new SyncResponse(
                    Success: false,
                    Payload: "fallback-not-explicit",
                    Status: SyncResponseStatus.Rejected,
                    ServedFromFallback: false,
                    CorrelationId: request.CorrelationId);
            }

            return request.Operation == new OperationName("ping")
                ? new SyncResponse(
                    Success: true,
                    Payload: "pong",
                    Status: SyncResponseStatus.Success,
                    ServedFromFallback: true,
                    CorrelationId: request.CorrelationId)
                : new SyncResponse(
                    Success: false,
                    Payload: "unsupported-operation",
                    Status: SyncResponseStatus.Rejected,
                    ServedFromFallback: true,
                    CorrelationId: request.CorrelationId);
        }

        public void Subscribe(IEventPublisher publisher)
        {
            if (publisher is InMemoryEventBus inMemoryBus)
            {
                inMemoryBus.Subscribe(e => ReceivedEvents.Add(e));
            }
        }

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
        {
            return new DeterministicPluginRegistrationPolicy().BuildRegistrationPlan(plugin);
        }

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
    }

    private sealed class LifecycleAwarePlugin : IPluginContract, IEventSubscriber, ISyncResponder, IPluginLifecycle, IPluginOperationCatalog, IPluginRegistrationPolicy
    {
        public LifecycleAwarePlugin(string pluginId, string contractName, Version contractVersion)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName(contractName);
            ContractVersion = contractVersion;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public List<string> ExecutedOperations { get; } = new();

        public IReadOnlyCollection<OperationName> SupportedOperations { get; } = [new OperationName("load"), new OperationName("start"), new OperationName("stop"), new OperationName("unload")];

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(Success: true, Payload: request.Operation.Value);
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
        {
            return new DeterministicPluginRegistrationPolicy().BuildRegistrationPlan(plugin);
        }

        public void Load(PluginLoadContext context)
        {
            ExecutedOperations.Add("Load");
        }

        public void Start(PluginStartContext context)
        {
            ExecutedOperations.Add("Start");
        }

        public void Stop(PluginStopContext context)
        {
            ExecutedOperations.Add("Stop");
        }

        public void Unload(PluginUnloadContext context)
        {
            ExecutedOperations.Add("Unload");
        }
    }

    private sealed class MissingStartPlugin : IPluginContract, IEventSubscriber, ISyncResponder
    {
        public MissingStartPlugin(string pluginId, string contractName, Version contractVersion)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName(contractName);
            ContractVersion = contractVersion;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(Success: true, Payload: request.Operation.Value);
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public void Load()
        {
        }

        public void Stop()
        {
        }

        public void Unload()
        {
        }
    }

    private sealed class PluginWithoutLifecycle : IPluginContract, IEventSubscriber, ISyncResponder
    {
        public PluginWithoutLifecycle(string pluginId, string contractName, Version contractVersion)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName(contractName);
            ContractVersion = contractVersion;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public SyncResponse Handle(SyncRequest request)
        {
            return request.IsFallbackExplicit
                ? new SyncResponse(Success: true, Payload: "pong")
                : new SyncResponse(Success: false, Payload: "fallback-not-explicit");
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }
    }

    private sealed class InvalidContractMetadataPlugin : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IEventSubscriber, ISyncResponder, IPluginRegistrationPolicy
    {
        public InvalidContractMetadataPlugin(PluginId pluginId, ContractName contractName, Version contractVersion)
        {
            PluginId = pluginId;
            ContractName = contractName;
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

        public SyncResponse Handle(SyncRequest request)
        {
            return request.IsFallbackExplicit
                ? new SyncResponse(Success: true, Payload: "pong")
                : new SyncResponse(Success: false, Payload: "fallback-not-explicit");
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
        {
            return new DeterministicPluginRegistrationPolicy().BuildRegistrationPlan(plugin);
        }
    }

    private sealed class PluginWithLifecycleWithoutOperationCatalog : IPluginContract, IPluginLifecycle, IEventSubscriber, ISyncResponder
    {
        public PluginWithLifecycleWithoutOperationCatalog(string pluginId, string contractName, Version contractVersion)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName(contractName);
            ContractVersion = contractVersion;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

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
            return request.IsFallbackExplicit
                ? new SyncResponse(Success: true, Payload: "pong")
                : new SyncResponse(Success: false, Payload: "fallback-not-explicit");
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }
    }

    private sealed class PluginWithLifecycleAndCatalogWithoutScheduling : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IEventSubscriber, ISyncResponder, IPluginRegistrationPolicy
    {
        public PluginWithLifecycleAndCatalogWithoutScheduling(string pluginId, string contractName, Version contractVersion)
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

        public SyncResponse Handle(SyncRequest request)
        {
            return request.IsFallbackExplicit
                ? new SyncResponse(Success: true, Payload: "pong")
                : new SyncResponse(Success: false, Payload: "fallback-not-explicit");
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
        {
            return new DeterministicPluginRegistrationPolicy().BuildRegistrationPlan(plugin);
        }
    }

    private sealed class PluginWithDuplicateOperationCatalog : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IEventSubscriber, ISyncResponder, IPluginRegistrationPolicy
    {
        public PluginWithDuplicateOperationCatalog(string pluginId, string contractName, Version contractVersion)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName(contractName);
            ContractVersion = contractVersion;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; } = [new OperationName("ping"), new OperationName("PING")];

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
            return request.IsFallbackExplicit
                ? new SyncResponse(Success: true, Payload: "pong")
                : new SyncResponse(Success: false, Payload: "fallback-not-explicit");
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
        {
            return new DeterministicPluginRegistrationPolicy().BuildRegistrationPlan(plugin);
        }
    }

    private sealed class PluginWithEmptyOperationCatalog : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IEventSubscriber, ISyncResponder, IPluginRegistrationPolicy
    {
        public PluginWithEmptyOperationCatalog(string pluginId, string contractName, Version contractVersion)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName(contractName);
            ContractVersion = contractVersion;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; } = [];

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
            return request.IsFallbackExplicit
                ? new SyncResponse(Success: true, Payload: "pong")
                : new SyncResponse(Success: false, Payload: "fallback-not-explicit");
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
        {
            return new DeterministicPluginRegistrationPolicy().BuildRegistrationPlan(plugin);
        }
    }

    private sealed class PluginWithScheduledEvents : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IPluginScheduledEvents, IEventSubscriber, ISyncResponder, IPluginRegistrationPolicy
    {
        public PluginWithScheduledEvents(string pluginId, string contractName, Version contractVersion)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName(contractName);
            ContractVersion = contractVersion;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; } = [new OperationName("ping"), new OperationName("rebuild"), new OperationName("warmup")];

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
            return request.IsFallbackExplicit
                ? new SyncResponse(Success: true, Payload: "pong")
                : new SyncResponse(Success: false, Payload: "fallback-not-explicit");
        }

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            scheduler.ScheduleRecurring(new JobName("rebuild-index"), TimeSpan.FromMinutes(5), new OperationName("rebuild"));
            scheduler.ScheduleAt(new JobName("run-warmup"), new DateTimeOffset(2030, 01, 01, 00, 00, 00, TimeSpan.Zero), new OperationName("warmup"));
        }

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
        {
            return new DeterministicPluginRegistrationPolicy().BuildRegistrationPlan(plugin);
        }
    }

    private sealed class PartiallyImplementedPlugin : IPluginContract
    {
        public PartiallyImplementedPlugin(string pluginId, string contractName, Version contractVersion)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName(contractName);
            ContractVersion = contractVersion;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }
    }

    private sealed class DummyDependencyRegister : IPluginDependencyRegister
    {
        public void Register(IServiceCollection services)
        {
        }
    }

    private sealed class RecordingScheduler : IPluginScheduler
    {
        public List<(JobName JobName, TimeSpan Interval, OperationName Operation)> RecurringSchedules { get; } = new();

        public List<(JobName JobName, DateTimeOffset RunAt, OperationName Operation)> OneTimeSchedules { get; } = new();

        public void ScheduleRecurring(JobName jobName, TimeSpan interval, OperationName operation)
        {
            RecurringSchedules.Add((jobName, interval, operation));
        }

        public void ScheduleAt(JobName jobName, DateTimeOffset runAt, OperationName operation)
        {
            OneTimeSchedules.Add((jobName, runAt, operation));
        }
    }

    [Fact]
    public void IPluginContract_PluginId_PropertyType_IsPluginId()
    {
        var property = typeof(IPluginContract).GetProperty(nameof(IPluginContract.PluginId));
        Assert.NotNull(property);
        Assert.Equal(typeof(PluginId), property!.PropertyType);
    }

    [Fact]
    public void IPluginContract_ContractName_PropertyType_IsContractName()
    {
        var property = typeof(IPluginContract).GetProperty(nameof(IPluginContract.ContractName));
        Assert.NotNull(property);
        Assert.Equal(typeof(ContractName), property!.PropertyType);
    }

    [Fact]
    public void IPluginOperationCatalog_SupportedOperations_PropertyType_IsOperationNameCollection()
    {
        var property = typeof(IPluginOperationCatalog).GetProperty(nameof(IPluginOperationCatalog.SupportedOperations));
        Assert.NotNull(property);
        Assert.Equal(typeof(IReadOnlyCollection<OperationName>), property!.PropertyType);
    }

    [Fact]
    public void IPluginScheduler_ScheduleRecurring_AcceptsJobNameAndOperationName()
    {
        var method = typeof(IPluginScheduler).GetMethod(nameof(IPluginScheduler.ScheduleRecurring));
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(JobName), parameters[0].ParameterType);
        Assert.Equal(typeof(TimeSpan), parameters[1].ParameterType);
        Assert.Equal(typeof(OperationName), parameters[2].ParameterType);
    }

    [Fact]
    public void PluginLoadContext_PluginId_PropertyType_IsPluginId()
    {
        var property = typeof(PluginLoadContext).GetProperty(nameof(PluginLoadContext.PluginId));
        Assert.NotNull(property);
        Assert.Equal(typeof(PluginId), property!.PropertyType);
    }

    [Fact]
    public void PluginStartContext_PluginId_PropertyType_IsPluginId()
    {
        var property = typeof(PluginStartContext).GetProperty(nameof(PluginStartContext.PluginId));
        Assert.NotNull(property);
        Assert.Equal(typeof(PluginId), property!.PropertyType);
    }

    [Fact]
    public void PluginStopContext_PluginId_PropertyType_IsPluginId()
    {
        var property = typeof(PluginStopContext).GetProperty(nameof(PluginStopContext.PluginId));
        Assert.NotNull(property);
        Assert.Equal(typeof(PluginId), property!.PropertyType);
    }

    [Fact]
    public void PluginUnloadContext_PluginId_PropertyType_IsPluginId()
    {
        var property = typeof(PluginUnloadContext).GetProperty(nameof(PluginUnloadContext.PluginId));
        Assert.NotNull(property);
        Assert.Equal(typeof(PluginId), property!.PropertyType);
    }
}
