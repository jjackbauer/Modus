using Modus.Core.Plugins;
using Modus.Core.Messaging;
using System.Globalization;
using Xunit;

namespace Modus.Core.Tests.Plugins;

public sealed class TimerPluginLifecycleTests
{
    [Fact]
    public void PluginBase_GivenNullLoadContext_ExpectedArgumentNullException()
    {
        var plugin = new TestPlugin();

        Assert.Throws<ArgumentNullException>(() => plugin.Load(null!));
    }

    [Fact]
    public void PluginBase_GivenNullStartContext_ExpectedArgumentNullException()
    {
        var plugin = new TestPlugin();

        Assert.Throws<ArgumentNullException>(() => plugin.Start(null!));
    }

    [Fact]
    public void PluginBase_GivenNullStopContext_ExpectedArgumentNullException()
    {
        var plugin = new TestPlugin();

        Assert.Throws<ArgumentNullException>(() => plugin.Stop(null!));
    }

    [Fact]
    public void PluginBase_GivenNullUnloadContext_ExpectedArgumentNullException()
    {
        var plugin = new TestPlugin();

        Assert.Throws<ArgumentNullException>(() => plugin.Unload(null!));
    }

    [Fact]
    public void PluginBase_GivenNullSchedulerInRegisterSchedules_ExpectedArgumentNullException()
    {
        var plugin = new TestPlugin();

        Assert.Throws<ArgumentNullException>(() => plugin.RegisterSchedules(null!));
    }

    [Fact]
    public void TimerPlugin_GivenContractMetadata_ExpectedPluginIdAndVersionAreDeterministic()
    {
        var plugin = new TimerPlugin();

        Assert.Equal(new PluginId("Plugin.Timer"), plugin.PluginId);
        Assert.Equal(new ContractName("Modus.PluginContract"), plugin.ContractName);
        Assert.Equal(new Version(1, 0, 0), plugin.ContractVersion);
    }

    [Fact]
    public void TimerPlugin_GivenInstanceCreation_ExpectedImplementsAllLifetimePluginInterfaces()
    {
        var plugin = new TimerPlugin();

        Assert.IsAssignableFrom<IPluginContract>(plugin);
        Assert.IsAssignableFrom<IPluginLifecycle>(plugin);
        Assert.IsAssignableFrom<IPluginOperationCatalog>(plugin);
        Assert.IsAssignableFrom<IPluginScheduledEvents>(plugin);
    }

    [Fact]
    public void TimerPlugin_GivenScheduledTaskExtensionsConstructor_ExpectedSupportsSingleOrMultipleExtensions()
    {
        var single = new TimerPlugin(new StubScheduledTimerTaskExtension("Timer.Custom.One"));
        var multiple = new TimerPlugin(
            new StubScheduledTimerTaskExtension("Timer.Custom.One"),
            new StubScheduledTimerTaskExtension("Timer.Custom.Two"));

        Assert.NotNull(single);
        Assert.NotNull(multiple);
    }

    [Fact]
    public void TimerPlugin_GivenNullScheduledTaskExtensions_ExpectedThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TimerPlugin((IScheduledTimerTaskExtension[])null!));
    }

    [Fact]
    public void TimerPlugin_GivenEmptyScheduledTaskExtensions_ExpectedThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new TimerPlugin(Array.Empty<IScheduledTimerTaskExtension>()));
    }

    [Fact]
    public void TimerPlugin_GivenScheduledTaskExtensionsContainingNull_ExpectedThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new TimerPlugin(new IScheduledTimerTaskExtension[] { new StubScheduledTimerTaskExtension("Timer.Custom.One"), null! }));
    }

    [Fact]
    public void TimerPlugin_GivenCustomExtensionOperations_ExpectedSupportedOperationsComesFromExtensions()
    {
        var plugin = new TimerPlugin(new StubScheduledTimerTaskExtension("Timer.Custom.One"));

        Assert.Equal([new OperationName("Timer.Custom.One")], plugin.SupportedOperations);
    }

    [Fact]
    public void TimerPlugin_GivenMultipleExtensionsWithUniqueOperations_ExpectedSupportedOperationsContainsAllInOrdinalOrder()
    {
        var plugin = new TimerPlugin(
            new MultiOperationScheduledTimerTaskExtension("Timer.Zulu", "Timer.Alpha"),
            new MultiOperationScheduledTimerTaskExtension("Timer.Beta"));

        Assert.Equal([new OperationName("Timer.Alpha"), new OperationName("Timer.Beta"), new OperationName("Timer.Zulu")], plugin.SupportedOperations);
    }

    [Fact]
    public void TimerPlugin_GivenDuplicateOperationsAcrossExtensions_ExpectedSupportedOperationsDeDuplicatesUsingOrdinalComparerAndDeterministicOrder()
    {
        var plugin = new TimerPlugin(
            new MultiOperationScheduledTimerTaskExtension("Timer.Beta", "Timer.Alpha", "Timer.Beta", "timer.alpha"),
            new MultiOperationScheduledTimerTaskExtension("Timer.Alpha", "Timer.Gamma"));

        Assert.Equal([new OperationName("Timer.Alpha"), new OperationName("Timer.Beta"), new OperationName("Timer.Gamma"), new OperationName("timer.alpha")], plugin.SupportedOperations);
    }

    [Fact]
    public void TimerPlugin_GivenInheritedBaseDefaults_ExpectedLifecycleMethodsRemainCallableWithoutHostInternals()
    {
        var plugin = new TimerPlugin();
        using var cts = new CancellationTokenSource();

        plugin.Load(new PluginLoadContext(plugin.PluginId, cts.Token));
        plugin.Start(new PluginStartContext(plugin.PluginId, cts.Token));
        plugin.Stop(new PluginStopContext(plugin.PluginId, cts.Token));
        plugin.Unload(new PluginUnloadContext(
            plugin.PluginId,
            PluginUnloadReason.GracefulShutdown,
            DateTimeOffset.UtcNow.AddMinutes(1),
            cts.Token));
    }

    [Fact]
    public void TimerPlugin_GivenSupportedOperations_ExpectedContainsOnlyTimerWriteCurrentTime()
    {
        var plugin = new TimerPlugin();

        Assert.Equal([new OperationName("Timer.WriteCurrentTime")], plugin.SupportedOperations);
    }

    [Fact]
    public void TimerPlugin_GivenRegisterSchedules_ExpectedScheduleRecurringCalledWithFiveSecondInterval()
    {
        var plugin = new TimerPlugin();
        var scheduler = new RecordingScheduler();

        plugin.RegisterSchedules(scheduler);

        var recurring = Assert.Single(scheduler.RecurringSchedules);
        Assert.Equal(TimeSpan.FromSeconds(5), recurring.Interval);
        Assert.Equal(new OperationName("Timer.WriteCurrentTime"), recurring.Operation);
    }

    [Fact]
    public void TimerPlugin_GivenRegisterSchedules_ExpectedJobNameAndOperationMatchCatalog()
    {
        var plugin = new TimerPlugin();
        var scheduler = new RecordingScheduler();

        plugin.RegisterSchedules(scheduler);

        var recurring = Assert.Single(scheduler.RecurringSchedules);
        Assert.Equal(new JobName("Timer.WriteCurrentTime.Every5Seconds"), recurring.JobName);
        Assert.Contains(recurring.Operation, plugin.SupportedOperations);
    }

    [Fact]
    public void TimerPlugin_GivenCustomExtensions_ExpectedRegisterSchedulesDelegatesToExtensionsWithoutHardcodedOperation()
    {
        var plugin = new TimerPlugin(new StubScheduledTimerTaskExtension("Timer.Custom.One"));
        var scheduler = new RecordingScheduler();

        plugin.RegisterSchedules(scheduler);

        var recurring = Assert.Single(scheduler.RecurringSchedules);
        Assert.Equal(new JobName("Timer.Custom.One.EverySecond"), recurring.JobName);
        Assert.Equal(new OperationName("Timer.Custom.One"), recurring.Operation);
    }

    [Fact]
    public void TimerPlugin_GivenDefaultAndExplicitDefaultExtensionComposition_ExpectedScheduleAndOperationExecutionParity()
    {
        var fixedTimestamp = new DateTimeOffset(2026, 05, 17, 12, 34, 56, TimeSpan.Zero);
        var defaultWrites = new List<string>();
        var explicitWrites = new List<string>();

        var defaultPlugin = new TimerPlugin(
            utcNowProvider: () => fixedTimestamp,
            writeLine: defaultWrites.Add,
            recurringInterval: TimeSpan.FromSeconds(5));
        var explicitPlugin = new TimerPlugin(
            new FiveSecondIntervalsTimerPrint(
                utcNowProvider: () => fixedTimestamp,
                writeLine: explicitWrites.Add,
                recurringInterval: TimeSpan.FromSeconds(5)));

        var defaultScheduler = new RecordingScheduler();
        var explicitScheduler = new RecordingScheduler();

        defaultPlugin.RegisterSchedules(defaultScheduler);
        explicitPlugin.RegisterSchedules(explicitScheduler);

        var defaultSchedule = Assert.Single(defaultScheduler.RecurringSchedules);
        var explicitSchedule = Assert.Single(explicitScheduler.RecurringSchedules);
        Assert.Equal(defaultSchedule, explicitSchedule);
        Assert.Equal(defaultPlugin.SupportedOperations, explicitPlugin.SupportedOperations);

        var defaultResponse = defaultPlugin.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.WriteCurrentTime"), new CorrelationId("corr-default")));
        var explicitResponse = explicitPlugin.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.WriteCurrentTime"), new CorrelationId("corr-explicit")));

        Assert.True(defaultResponse.Success);
        Assert.True(explicitResponse.Success);
        Assert.NotNull(defaultResponse.Payload.TimestampUtcIso8601);
        Assert.NotNull(explicitResponse.Payload.TimestampUtcIso8601);
        Assert.Equal(defaultResponse.Payload.TimestampUtcIso8601, explicitResponse.Payload.TimestampUtcIso8601);
        Assert.Equal([defaultResponse.Payload.TimestampUtcIso8601!], defaultWrites);
        Assert.Equal([explicitResponse.Payload.TimestampUtcIso8601!], explicitWrites);
    }

    [Fact]
    public void TimerPlugin_GivenCustomScheduledTaskExtension_ExpectedSchedulesAndHandlesCustomOperationWithoutPluginCodeChanges()
    {
        var extension = new RoutingAwareScheduledTimerTaskExtension("Timer.Custom.Composite");
        var plugin = new TimerPlugin(extension);
        var scheduler = new RecordingScheduler();

        plugin.RegisterSchedules(scheduler);
        var recurring = Assert.Single(scheduler.RecurringSchedules);
        Assert.Equal(new JobName("Timer.Custom.Composite.EverySecond"), recurring.JobName);
        Assert.Equal(new OperationName("Timer.Custom.Composite"), recurring.Operation);

        var response = plugin.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.Custom.Composite"), new CorrelationId("corr-custom")));

        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal("handled:Timer.Custom.Composite", response.Payload.TimestampUtcIso8601);
        Assert.Equal(new CorrelationId("corr-custom"), response.CorrelationId);
        Assert.Equal(1, extension.HandleCallCount);
    }

    [Fact]
    public void TimerPlugin_GivenMultipleLoadedExtensions_ExpectedDelegatesRegistrationOncePerExtensionAndPreservesDeterministicJobNames()
    {
        var first = new RecordingScheduledTimerTaskExtension(
            recurringSchedules:
            [
                ("Timer.Custom.First.Every15Seconds", TimeSpan.FromSeconds(15), "Timer.Custom.First"),
                ("Timer.Custom.First.EveryMinute", TimeSpan.FromMinutes(1), "Timer.Custom.First")
            ],
            oneTimeSchedules:
            [
                ("Timer.Custom.First.Bootstrap", new DateTimeOffset(2026, 05, 17, 0, 0, 0, TimeSpan.Zero), "Timer.Custom.First")
            ],
            supportedOperations: ["Timer.Custom.First"]);

        var second = new RecordingScheduledTimerTaskExtension(
            recurringSchedules:
            [
                ("Timer.Custom.Second.Every5Seconds", TimeSpan.FromSeconds(5), "Timer.Custom.Second")
            ],
            oneTimeSchedules:
            [
                ("Timer.Custom.Second.Warmup", new DateTimeOffset(2026, 05, 17, 0, 5, 0, TimeSpan.Zero), "Timer.Custom.Second")
            ],
            supportedOperations: ["Timer.Custom.Second"]);

        var plugin = new TimerPlugin(first, second);
        var scheduler = new RecordingScheduler();

        plugin.RegisterSchedules(scheduler);

        Assert.Equal(1, first.RegisterSchedulesCallCount);
        Assert.Equal(1, second.RegisterSchedulesCallCount);

        Assert.Equal(
            [
                new JobName("Timer.Custom.First.Every15Seconds"),
                new JobName("Timer.Custom.First.EveryMinute"),
                new JobName("Timer.Custom.Second.Every5Seconds")
            ],
            scheduler.RecurringSchedules.Select(static schedule => schedule.JobName).ToArray());

        Assert.Equal(
            [
                new JobName("Timer.Custom.First.Bootstrap"),
                new JobName("Timer.Custom.Second.Warmup")
            ],
            scheduler.OneTimeSchedules.Select(static schedule => schedule.JobName).ToArray());
    }

    [Fact]
    public async Task TimerPlugin_GivenLifecycleStart_ExpectedWritesTimestampWithoutHostOperationCall()
    {
        var writes = new List<string>();
        var plugin = new TimerPlugin(
            utcNowProvider: () => DateTimeOffset.UtcNow,
            writeLine: timestamp =>
            {
                lock (writes)
                {
                    writes.Add(timestamp);
                }
            },
            recurringInterval: TimeSpan.FromMilliseconds(20));

        plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None));

        var wroteAtLeastTwice = SpinWait.SpinUntil(
            () =>
            {
                lock (writes)
                {
                    return writes.Count >= 2;
                }
            },
            1000);

        Assert.True(wroteAtLeastTwice);

        plugin.Stop(new PluginStopContext(plugin.PluginId, CancellationToken.None));

        int countAfterStop;
        lock (writes)
        {
            countAfterStop = writes.Count;
        }

        await Task.Delay(80);

        lock (writes)
        {
            Assert.Equal(countAfterStop, writes.Count);
        }
    }

    [Fact]
    public void TimerPlugin_GivenAutonomousLoopWithMultipleExtensions_ExpectedInvokesDefaultExtensionOperationViaHandleDispatch()
    {
        var defaultExtension = new RoutingAwareScheduledTimerTaskExtension("Timer.Zulu.Default");
        var secondaryExtension = new RoutingAwareScheduledTimerTaskExtension("Timer.Alpha.Secondary");
        var plugin = new TimerPlugin(defaultExtension, secondaryExtension);

        plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None));

        var dispatchObserved = SpinWait.SpinUntil(
            () => defaultExtension.HandleCallCount > 0 || secondaryExtension.HandleCallCount > 0,
            7000);

        plugin.Stop(new PluginStopContext(plugin.PluginId, CancellationToken.None));

        Assert.True(dispatchObserved);
        Assert.True(defaultExtension.HandleCallCount > 0);
        Assert.Equal(0, secondaryExtension.HandleCallCount);
    }

    [Fact]
    public void TimerPlugin_GivenNonPositiveRecurringInterval_ExpectedArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimerPlugin(
                utcNowProvider: () => DateTimeOffset.UtcNow,
                writeLine: _ => { },
                recurringInterval: TimeSpan.Zero));
    }

    [Fact]
    public void TimerOperation_GivenExecution_ExpectedWritesCurrentTimestamp()
    {
        var fixedTimestamp = new DateTimeOffset(2026, 05, 17, 12, 34, 56, TimeSpan.Zero);
        var writes = new List<string>();
        var plugin = new TimerPlugin(() => fixedTimestamp, writes.Add);

        var response = plugin.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.WriteCurrentTime"), new CorrelationId("corr-1")));

        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal(new CorrelationId("corr-1"), response.CorrelationId);
        Assert.Equal(fixedTimestamp.ToString("O", CultureInfo.InvariantCulture), response.Payload.TimestampUtcIso8601);
        Assert.Equal([response.Payload.TimestampUtcIso8601!], writes);
    }

    [Fact]
    public void TimerOperation_GivenMultipleRuns_ExpectedWritesNewTimestampPerInvocation()
    {
        var first = new DateTimeOffset(2026, 05, 17, 12, 34, 56, TimeSpan.Zero);
        var second = first.AddSeconds(5);
        var sequence = new Queue<DateTimeOffset>([first, second]);
        var writes = new List<string>();
        var plugin = new TimerPlugin(() => sequence.Dequeue(), writes.Add);

        var firstResponse = plugin.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.WriteCurrentTime")));
        var secondResponse = plugin.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.WriteCurrentTime")));

        var firstPayload = firstResponse.Payload.TimestampUtcIso8601;
        var secondPayload = secondResponse.Payload.TimestampUtcIso8601;
        Assert.NotNull(firstPayload);
        Assert.NotNull(secondPayload);
        Assert.NotEqual(firstPayload, secondPayload);
        Assert.Equal(2, writes.Count);
        Assert.Equal(firstPayload, writes[0]);
        Assert.Equal(secondPayload, writes[1]);
    }

    [Fact]
    public void TimerPlugin_GivenMultipleExtensions_ExpectedHandleRoutesToOwningExtension()
    {
        var alpha = new RoutingAwareScheduledTimerTaskExtension("Timer.Custom.Alpha");
        var beta = new RoutingAwareScheduledTimerTaskExtension("Timer.Custom.Beta");
        var plugin = new TimerPlugin(alpha, beta);

        var response = plugin.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.Custom.Beta"), new CorrelationId("corr-route")));

        Assert.True(response.Success);
        Assert.Equal("handled:Timer.Custom.Beta", response.Payload.TimestampUtcIso8601);
        Assert.Equal(0, alpha.HandleCallCount);
        Assert.Equal(1, beta.HandleCallCount);
    }

    [Fact]
    public void TimerPlugin_GivenUnknownOperation_ExpectedReturnsRejectedUnsupportedOperationResponse()
    {
        var alpha = new RoutingAwareScheduledTimerTaskExtension("Timer.Custom.Alpha");
        var plugin = new TimerPlugin(alpha);

        var response = plugin.Handle(SyncRequest.ForStandardPath(new OperationName("Timer.Custom.Unknown"), new CorrelationId("corr-unknown")));

        Assert.False(response.Success);
        Assert.Equal(SyncResponseStatus.Rejected, response.Status);
        Assert.NotNull(response.Payload.Error);
        Assert.Equal("unsupported-operation", response.Payload.Error!.Code);
        Assert.Equal(new CorrelationId("corr-unknown"), response.CorrelationId);
        Assert.Equal(0, alpha.HandleCallCount);
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

    private sealed class TestPlugin : PluginBase
    {
    }

    private sealed class StubScheduledTimerTaskExtension : IScheduledTimerTaskExtension
    {
        private readonly string _operation;

        public StubScheduledTimerTaskExtension(string operation)
        {
            _operation = operation;
        }

        public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName(_operation)];

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(scheduler);
            scheduler.ScheduleRecurring(new JobName($"{_operation}.EverySecond"), TimeSpan.FromSeconds(1), new OperationName(_operation));
        }

        public SyncResponse<ISyncPayload> Handle(SyncRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return new SyncResponse<ISyncPayload>(
                Success: true,
                Payload: new TimerWriteCurrentTimeResult(_operation),
                Status: SyncResponseStatus.Success,
                ServedFromFallback: false,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class RoutingAwareScheduledTimerTaskExtension : IScheduledTimerTaskExtension
    {
        private readonly string _operation;

        public RoutingAwareScheduledTimerTaskExtension(string operation)
        {
            _operation = operation;
        }

        public int HandleCallCount { get; private set; }

        public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName(_operation)];

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(scheduler);
            scheduler.ScheduleRecurring(new JobName($"{_operation}.EverySecond"), TimeSpan.FromSeconds(1), new OperationName(_operation));
        }

        public SyncResponse<ISyncPayload> Handle(SyncRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!string.Equals(request.Operation.Value, _operation, StringComparison.Ordinal))
            {
                return new SyncResponse<ISyncPayload>(
                    Success: false,
                    Payload: new SyncErrorPayload(
                        Code: "unsupported-operation",
                        Message: $"Operation '{request.Operation.Value}' is not supported by '{_operation}'."),
                    Status: SyncResponseStatus.Rejected,
                    ServedFromFallback: false,
                    CorrelationId: request.CorrelationId);
            }

            HandleCallCount++;
            return new SyncResponse<ISyncPayload>(
                Success: true,
                Payload: new TimerWriteCurrentTimeResult($"handled:{_operation}"),
                Status: SyncResponseStatus.Success,
                ServedFromFallback: false,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class MultiOperationScheduledTimerTaskExtension : IScheduledTimerTaskExtension
    {
        private readonly IReadOnlyCollection<OperationName> _supportedOperations;

        public MultiOperationScheduledTimerTaskExtension(params string[] supportedOperations)
        {
            _supportedOperations = supportedOperations.Select(op => new OperationName(op)).ToArray();
        }

        public IReadOnlyCollection<OperationName> SupportedOperations => _supportedOperations;

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(scheduler);
        }

        public SyncResponse<ISyncPayload> Handle(SyncRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return new SyncResponse<ISyncPayload>(
                Success: true,
                Payload: new TimerWriteCurrentTimeResult(request.Operation.Value),
                Status: SyncResponseStatus.Success,
                ServedFromFallback: false,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class RecordingScheduledTimerTaskExtension : IScheduledTimerTaskExtension
    {
        private readonly IReadOnlyCollection<(string JobName, TimeSpan Interval, string Operation)> _recurringSchedules;
        private readonly IReadOnlyCollection<(string JobName, DateTimeOffset RunAt, string Operation)> _oneTimeSchedules;
        private readonly IReadOnlyCollection<OperationName> _supportedOperations;

        public RecordingScheduledTimerTaskExtension(
            IReadOnlyCollection<(string JobName, TimeSpan Interval, string Operation)> recurringSchedules,
            IReadOnlyCollection<(string JobName, DateTimeOffset RunAt, string Operation)> oneTimeSchedules,
            IReadOnlyCollection<string> supportedOperations)
        {
            _recurringSchedules = recurringSchedules;
            _oneTimeSchedules = oneTimeSchedules;
            _supportedOperations = supportedOperations.Select(op => new OperationName(op)).ToArray();
        }

        public int RegisterSchedulesCallCount { get; private set; }

        public IReadOnlyCollection<OperationName> SupportedOperations => _supportedOperations;

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(scheduler);

            RegisterSchedulesCallCount++;
            foreach (var schedule in _recurringSchedules)
            {
                scheduler.ScheduleRecurring(new JobName(schedule.JobName), schedule.Interval, new OperationName(schedule.Operation));
            }

            foreach (var schedule in _oneTimeSchedules)
            {
                scheduler.ScheduleAt(new JobName(schedule.JobName), schedule.RunAt, new OperationName(schedule.Operation));
            }
        }

        public SyncResponse<ISyncPayload> Handle(SyncRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            return new SyncResponse<ISyncPayload>(
                Success: true,
                Payload: new TimerWriteCurrentTimeResult(request.Operation.Value),
                Status: SyncResponseStatus.Success,
                ServedFromFallback: false,
                CorrelationId: request.CorrelationId);
        }
    }
}
