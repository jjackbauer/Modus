using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Plugins.Descriptors;
using Modus.Host.Plugins.Host;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class RuntimeAddedScheduledJobsAbsoluteGatesTests
{
    [Fact]
    [Trait("ChecklistItem", "Verify timer-plugin scheduled jobs from runtime-added plugins register deterministically and execute with `source=scheduled` success diagnostics that satisfy absolute cadence gates [mandatory - scheduled jobs assurance]")]
    [Trait("AuditArtifact", "iterative-implementation-runtime-added-scheduled-jobs-absolute-gates-2026-05-22")]
    public async Task ScheduledJobs_GivenRuntimeAddedTimerPlugin_ExpectedDeterministicRegistrationAndAbsoluteCadenceGates()
    {
        ScheduledExecutionProbe.Reset();

        var recurringInterval = TimeSpan.FromMilliseconds(300);
        var observationWindow = TimeSpan.FromMilliseconds(1800);
        var minimumRecurringExecutions = Math.Max(2, (int)Math.Floor(observationWindow.TotalMilliseconds / recurringInterval.TotalMilliseconds) - 1);
        var tolerance = TimeSpan.FromMilliseconds(Math.Max(250, recurringInterval.TotalMilliseconds * 0.20d));

        var services = new ServiceCollection();
        services.AddSingleton<RuntimeAddedScheduledJobsPlugin>();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var host = new AssemblyLifecycleHost(provider);
        var descriptor = new PluginDescriptor(
            PluginId: new PluginId(RuntimeAddedScheduledJobsPlugin.PluginIdValue),
            AssemblyName: "Plugin.Tests.RuntimeAdded.Scheduled.AbsoluteGates",
            Version: new Version(1, 0, 0),
            Capabilities: [new CapabilityName("Cap.Scheduled.AbsoluteGates")],
            DependsOn: [],
            DeclaredOperations:
            [
                new OperationName(RuntimeAddedScheduledJobsPlugin.RecurringOperation),
                new OperationName(RuntimeAddedScheduledJobsPlugin.OneTimeOperation)
            ],
            AssemblyPath: typeof(RuntimeAddedScheduledJobsPlugin).Assembly.Location,
            RuntimePluginTypeFullName: typeof(RuntimeAddedScheduledJobsPlugin).FullName,
            DeclaredServiceLifetime: PluginServiceLifetime.Singleton);

        var diagnostics = host.StartActivatedPlugins([descriptor], [RuntimeAddedScheduledJobsPlugin.PluginIdValue]);

        await WaitUntilAsync(
            condition: () => ScheduledExecutionProbe.GetRecurringExecutionTimes().Length >= minimumRecurringExecutions,
            timeout: observationWindow + TimeSpan.FromSeconds(1));

        var recurringExecutionTimes = ScheduledExecutionProbe.GetRecurringExecutionTimes();
        var oneTimeExecutionTimes = ScheduledExecutionProbe.GetOneTimeExecutionTimes();

        Assert.True(
            recurringExecutionTimes.Length >= minimumRecurringExecutions,
            $"Expected at least {minimumRecurringExecutions} recurring executions in window {observationWindow} for interval {recurringInterval}, but observed {recurringExecutionTimes.Length}.");

        for (var index = 1; index < recurringExecutionTimes.Length; index++)
        {
            var delta = recurringExecutionTimes[index] - recurringExecutionTimes[index - 1];
            var deviation = Math.Abs((delta - recurringInterval).TotalMilliseconds);
            Assert.True(
                deviation <= tolerance.TotalMilliseconds,
                $"Recurring cadence outside tolerance. Expected interval {recurringInterval.TotalMilliseconds}ms +/- {tolerance.TotalMilliseconds}ms but observed {delta.TotalMilliseconds}ms between run {index - 1} and {index}.");
        }

        Assert.Single(oneTimeExecutionTimes);

        await WaitUntilAsync(
            condition: () => CountRecurringSuccessDiagnostics(diagnostics) >= recurringExecutionTimes.Length,
            timeout: TimeSpan.FromSeconds(2));

        var registrationDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.StartsWith("stage=scheduling plugin=Plugin.Runtime.Added.Scheduled.AbsoluteGates", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(
            registrationDiagnostics,
            diagnostic => diagnostic.Contains("job=Absolute.Recurring.Every300Ms", StringComparison.Ordinal)
                && diagnostic.Contains("intervalMs=300", StringComparison.Ordinal)
                && diagnostic.Contains("operation=Absolute.Recurring.Execute", StringComparison.Ordinal)
                && diagnostic.Contains("outcome=registered", StringComparison.Ordinal));
        Assert.Contains(
            registrationDiagnostics,
            diagnostic => diagnostic.Contains("job=Absolute.OneTime.Once", StringComparison.Ordinal)
                && diagnostic.Contains("operation=Absolute.OneTime.Execute", StringComparison.Ordinal)
                && diagnostic.Contains("outcome=registered", StringComparison.Ordinal));

        var recurringSuccessDiagnostics = diagnostics
            .Where(diagnostic =>
                diagnostic.Contains("stage=operation plugin=Plugin.Runtime.Added.Scheduled.AbsoluteGates", StringComparison.Ordinal)
                && diagnostic.Contains("operation=Absolute.Recurring.Execute", StringComparison.Ordinal)
                && diagnostic.Contains("job=Absolute.Recurring.Every300Ms", StringComparison.Ordinal)
                && diagnostic.Contains("source=scheduled", StringComparison.Ordinal)
                && diagnostic.Contains("outcome=success", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            recurringSuccessDiagnostics.Length >= recurringExecutionTimes.Length,
            $"Each recurring execution must emit a source=scheduled success diagnostic. Observed executions={recurringExecutionTimes.Length}, diagnostics={recurringSuccessDiagnostics.Length}.");

        var oneTimeSuccessDiagnostics = diagnostics
            .Where(diagnostic =>
                diagnostic.Contains("stage=operation plugin=Plugin.Runtime.Added.Scheduled.AbsoluteGates", StringComparison.Ordinal)
                && diagnostic.Contains("operation=Absolute.OneTime.Execute", StringComparison.Ordinal)
                && diagnostic.Contains("job=Absolute.OneTime.Once", StringComparison.Ordinal)
                && diagnostic.Contains("source=scheduled", StringComparison.Ordinal)
                && diagnostic.Contains("outcome=success", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(oneTimeSuccessDiagnostics);
    }

    private static int CountRecurringSuccessDiagnostics(IReadOnlyList<string> diagnostics)
    {
        return diagnostics.Count(diagnostic =>
            diagnostic.Contains("stage=operation plugin=Plugin.Runtime.Added.Scheduled.AbsoluteGates", StringComparison.Ordinal)
            && diagnostic.Contains("operation=Absolute.Recurring.Execute", StringComparison.Ordinal)
            && diagnostic.Contains("job=Absolute.Recurring.Every300Ms", StringComparison.Ordinal)
            && diagnostic.Contains("source=scheduled", StringComparison.Ordinal)
            && diagnostic.Contains("outcome=success", StringComparison.Ordinal));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20, cts.Token);
        }

        throw new TimeoutException($"Condition was not met within timeout '{timeout}'.");
    }

    private sealed class RuntimeAddedScheduledJobsPlugin : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IPluginScheduledEvents, ISyncResponder
    {
        public const string PluginIdValue = "Plugin.Runtime.Added.Scheduled.AbsoluteGates";
        public const string RecurringOperation = "Absolute.Recurring.Execute";
        public const string OneTimeOperation = "Absolute.OneTime.Execute";

        public PluginId PluginId => new(PluginIdValue);

        public ContractName ContractName => new("Modus.RuntimeAdded.Scheduled.AbsoluteGates");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations =>
        [
            new OperationName(RecurringOperation),
            new OperationName(OneTimeOperation)
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

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            scheduler.ScheduleRecurring(
                new JobName("Absolute.Recurring.Every300Ms"),
                TimeSpan.FromMilliseconds(300),
                new OperationName(RecurringOperation));

            scheduler.ScheduleAt(
                new JobName("Absolute.OneTime.Once"),
                DateTimeOffset.UtcNow.AddMilliseconds(750),
                new OperationName(OneTimeOperation));
        }

        public SyncResponse Handle(SyncRequest request)
        {
            if (string.Equals(request.Operation.Value, RecurringOperation, StringComparison.Ordinal))
            {
                ScheduledExecutionProbe.RecordRecurringExecution();
                return new SyncResponse(
                    Success: true,
                    Payload: "recurring-ok",
                    CorrelationId: request.CorrelationId);
            }

            if (string.Equals(request.Operation.Value, OneTimeOperation, StringComparison.Ordinal))
            {
                ScheduledExecutionProbe.RecordOneTimeExecution();
                return new SyncResponse(
                    Success: true,
                    Payload: "one-time-ok",
                    CorrelationId: request.CorrelationId);
            }

            return new SyncResponse(
                Success: false,
                Payload: $"unsupported-operation:{request.Operation.Value}",
                CorrelationId: request.CorrelationId,
                Status: SyncResponseStatus.Failed);
        }
    }

    private static class ScheduledExecutionProbe
    {
        private static readonly object Gate = new();
        private static readonly List<DateTimeOffset> RecurringExecutionTimes = [];
        private static readonly List<DateTimeOffset> OneTimeExecutionTimes = [];

        public static void Reset()
        {
            lock (Gate)
            {
                RecurringExecutionTimes.Clear();
                OneTimeExecutionTimes.Clear();
            }
        }

        public static void RecordRecurringExecution()
        {
            lock (Gate)
            {
                RecurringExecutionTimes.Add(DateTimeOffset.UtcNow);
            }
        }

        public static void RecordOneTimeExecution()
        {
            lock (Gate)
            {
                OneTimeExecutionTimes.Add(DateTimeOffset.UtcNow);
            }
        }

        public static DateTimeOffset[] GetRecurringExecutionTimes()
        {
            lock (Gate)
            {
                return RecurringExecutionTimes.ToArray();
            }
        }

        public static DateTimeOffset[] GetOneTimeExecutionTimes()
        {
            lock (Gate)
            {
                return OneTimeExecutionTimes.ToArray();
            }
        }
    }
}
