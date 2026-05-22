using System.Reflection;
using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Plugins.Descriptors;

namespace Modus.Host.Plugins.Host;

internal sealed class AssemblyLifecycleHost
{
    private readonly CancellationTokenSource _lifecycleCts = new();
    private readonly IServiceProvider? _serviceProvider;
    private readonly IServiceScopeFactory? _scopeFactory;

    public AssemblyLifecycleHost()
    {
    }

    public AssemblyLifecycleHost(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
        _scopeFactory = serviceProvider.GetService<IServiceScopeFactory>();
    }

    public IReadOnlyList<string> StartActivatedPlugins(
        IReadOnlyList<PluginDescriptor> descriptors,
        IReadOnlyCollection<string> activatedPluginIds)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(activatedPluginIds);

        var diagnostics = new LiveDiagnosticBuffer();
        var activatedSet = activatedPluginIds.ToHashSet(StringComparer.Ordinal);

        foreach (var descriptor in descriptors.OrderBy(x => x.PluginId.Value, StringComparer.Ordinal))
        {
            if (!activatedSet.Contains(descriptor.PluginId.Value))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(descriptor.AssemblyPath) || !File.Exists(descriptor.AssemblyPath))
            {
                continue;
            }

            try
            {
                var assembly = Assembly.LoadFrom(descriptor.AssemblyPath);
                var lifecycleTypes = assembly
                    .GetTypes()
                    .Where(type =>
                        type is { IsAbstract: false, IsInterface: false }
                        && typeof(IPluginLifecycle).IsAssignableFrom(type)
                        && CanActivateLifecycleType(type))
                    .OrderBy(type => type.FullName, StringComparer.Ordinal)
                    .ToArray();
                var scheduledLifecycleTypeCount = lifecycleTypes.Count(static type => typeof(IPluginScheduledEvents).IsAssignableFrom(type));

                foreach (var lifecycleType in lifecycleTypes)
                {
                    using var activationScope = CreateScope();
                    var plugin = ResolveActivationLifecyclePluginInstance(lifecycleType, activationScope?.ServiceProvider);
                    if (plugin is null)
                    {
                        if (ShouldEmitUnresolvableActivationDiagnostic(descriptor, lifecycleType, scheduledLifecycleTypeCount))
                        {
                            diagnostics.Add(
                                CreateUnresolvableScheduledLifecycleDiagnostic(
                                    lifecycleType,
                                    descriptor.PluginId.Value,
                                    "registration",
                                    "unresolved"));
                        }

                        continue;
                    }

                    var pluginIdString = (plugin as IPluginContract)?.PluginId.Value
                        ?? lifecycleType.FullName
                        ?? lifecycleType.Name;
                    var pluginId = new PluginId(pluginIdString);

                    plugin.Load(new PluginLoadContext(pluginId, _lifecycleCts.Token));
                    plugin.Start(new PluginStartContext(pluginId, _lifecycleCts.Token));

                    diagnostics.Add($"stage=lifecycle plugin={pluginId} outcome=started source={descriptor.PluginId}");
                    if (plugin is IPluginScheduledEvents scheduledPlugin)
                    {
                        diagnostics.AddRange(RegisterAndRunSchedules(scheduledPlugin, lifecycleType, pluginId.Value, diagnostics));
                    }
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add($"stage=lifecycle plugin={descriptor.PluginId} outcome=failure reason={ex.Message}");
            }
        }

        return diagnostics;
    }

    private IReadOnlyList<string> RegisterAndRunSchedules(
        IPluginScheduledEvents scheduledPlugin,
        Type lifecycleType,
        string pluginId,
        LiveDiagnosticBuffer liveDiagnostics)
    {
        var diagnostics = new List<string>();

        var scheduler = new RecordingPluginScheduler();

        try
        {
            scheduledPlugin.RegisterSchedules(scheduler);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"stage=scheduling plugin={pluginId} outcome=failure reason={ex.Message}");
            return diagnostics;
        }

        foreach (var recurring in scheduler.RecurringSchedules
            .OrderBy(x => x.JobName, StringComparer.Ordinal)
            .ThenBy(x => x.Operation, StringComparer.Ordinal)
            .ThenBy(x => x.Interval))
        {
            diagnostics.Add(
                $"stage=scheduling plugin={pluginId} job={recurring.JobName} intervalMs={recurring.Interval.TotalMilliseconds:F0} operation={recurring.Operation} outcome=registered");
            diagnostics.AddRange(ExecuteScheduledOperation(lifecycleType, pluginId, recurring.JobName, recurring.Operation));

            var token = _lifecycleCts.Token;
            var interval = recurring.Interval;
            var jobName = recurring.JobName;
            var operation = recurring.Operation;
            _ = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(interval);
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    liveDiagnostics.AddRange(ExecuteScheduledOperation(lifecycleType, pluginId, jobName, operation));
                }
            }, CancellationToken.None);
        }

        foreach (var oneTime in scheduler.OneTimeSchedules
            .OrderBy(x => x.JobName, StringComparer.Ordinal)
            .ThenBy(x => x.Operation, StringComparer.Ordinal)
            .ThenBy(x => x.RunAt))
        {
            diagnostics.Add(
                $"stage=scheduling plugin={pluginId} job={oneTime.JobName} runAt={oneTime.RunAt:O} operation={oneTime.Operation} outcome=registered");
            diagnostics.AddRange(ExecuteScheduledOperation(lifecycleType, pluginId, oneTime.JobName, oneTime.Operation));
        }

        return diagnostics;
    }

    private IReadOnlyList<string> ExecuteScheduledOperation(
        Type lifecycleType,
        string pluginId,
        string jobName,
        string operation)
    {
        using var scope = CreateScope();
        if (ResolveScheduledLifecyclePluginInstance(lifecycleType, scope?.ServiceProvider) is IPluginLifecycle runtimePlugin)
        {
            return ExecuteScheduledOperationOnResolvedPlugin(runtimePlugin, pluginId, jobName, operation);
        }

        return [CreateUnresolvableScheduledLifecycleDiagnostic(lifecycleType, pluginId, jobName, operation)];
    }

    private static string CreateUnresolvableScheduledLifecycleDiagnostic(
        Type lifecycleType,
        string pluginId,
        string jobName,
        string operation)
    {
        var lifecycleTypeName = lifecycleType.FullName ?? lifecycleType.Name;
        return $"stage=operation plugin={pluginId} operation={operation} source=scheduled job={jobName} outcome=ignored reason=unresolvable-via-di lifecycleType={lifecycleTypeName}";
    }

    private static IReadOnlyList<string> ExecuteScheduledOperationOnResolvedPlugin(
        IPluginLifecycle runtimePlugin,
        string pluginId,
        string jobName,
        string operation)
    {
        if (runtimePlugin is not ISyncResponder responder)
        {
            return [$"stage=operation plugin={pluginId} operation={operation} source=scheduled job={jobName} outcome=ignored reason=sync responder missing"];
        }

        try
        {
            var response = responder.Handle(SyncRequest.ForStandardPath(new OperationName(operation), correlationId: new CorrelationId($"scheduled:{jobName}")));
            if (!response.Success)
            {
                return [$"stage=operation plugin={pluginId} operation={operation} source=scheduled job={jobName} outcome=failure reason={response.Payload}"];
            }

            return [$"stage=operation plugin={pluginId} operation={operation} source=scheduled job={jobName} outcome=success payload={response.Payload}"];
        }
        catch (Exception ex)
        {
            return [$"stage=operation plugin={pluginId} operation={operation} source=scheduled job={jobName} outcome=failure reason={ex.Message}"];
        }
    }

    private IPluginLifecycle? ResolveActivationLifecyclePluginInstance(Type lifecycleType, IServiceProvider? provider)
    {
        if (ResolveLifecyclePluginFromProvider(provider, lifecycleType) is IPluginLifecycle resolvedFromScope)
        {
            return resolvedFromScope;
        }

        if (ResolveLifecyclePluginFromProvider(_serviceProvider, lifecycleType) is IPluginLifecycle resolvedFromRoot)
        {
            return resolvedFromRoot;
        }

        if (provider is not null || _serviceProvider is not null)
        {
            return null;
        }

        return Activator.CreateInstance(lifecycleType) as IPluginLifecycle;
    }

    private IPluginLifecycle? ResolveScheduledLifecyclePluginInstance(Type lifecycleType, IServiceProvider? provider)
    {
        var resolutionProvider = provider ?? _serviceProvider;
        return ResolveLifecyclePluginFromProvider(resolutionProvider, lifecycleType);
    }

    private bool CanActivateLifecycleType(Type lifecycleType)
    {
        if (_serviceProvider is not null)
        {
            return true;
        }

        return lifecycleType.GetConstructor(Type.EmptyTypes) is not null;
    }

    private static IPluginLifecycle? ResolveLifecyclePluginFromProvider(IServiceProvider? provider, Type lifecycleType)
    {
        return provider?.GetService(lifecycleType) as IPluginLifecycle;
    }

    private bool ShouldEmitUnresolvableActivationDiagnostic(
        PluginDescriptor descriptor,
        Type lifecycleType,
        int scheduledLifecycleTypeCount)
    {
        if (_serviceProvider is null || !typeof(IPluginScheduledEvents).IsAssignableFrom(lifecycleType))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.RuntimePluginTypeFullName))
        {
            return string.Equals(
                descriptor.RuntimePluginTypeFullName,
                lifecycleType.FullName,
                StringComparison.Ordinal);
        }

        // Without an explicit runtime type hint, only emit this activation-time unresolved
        // diagnostic when the assembly has a single scheduled lifecycle type.
        return scheduledLifecycleTypeCount == 1;
    }

    private IServiceScope? CreateScope()
    {
        if (_scopeFactory is null)
        {
            return null;
        }

        return _scopeFactory.CreateScope();
    }


    private sealed class RecordingPluginScheduler : IPluginScheduler
    {
        public List<RecurringSchedule> RecurringSchedules { get; } = [];

        public List<OneTimeSchedule> OneTimeSchedules { get; } = [];

        public void ScheduleRecurring(JobName jobName, TimeSpan interval, OperationName operation)
        {
            RecurringSchedules.Add(new RecurringSchedule(jobName.Value, interval, operation.Value));
        }

        public void ScheduleAt(JobName jobName, DateTimeOffset runAt, OperationName operation)
        {
            OneTimeSchedules.Add(new OneTimeSchedule(jobName.Value, runAt, operation.Value));
        }

        public sealed record RecurringSchedule(string JobName, TimeSpan Interval, string Operation);

        public sealed record OneTimeSchedule(string JobName, DateTimeOffset RunAt, string Operation);
    }

    private sealed class LiveDiagnosticBuffer : IReadOnlyList<string>
    {
        private readonly object _gate = new();
        private readonly List<string> _items = [];

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _items.Count;
                }
            }
        }

        public string this[int index]
        {
            get
            {
                lock (_gate)
                {
                    return _items[index];
                }
            }
        }

        public void Add(string item)
        {
            lock (_gate)
            {
                _items.Add(item);
            }
        }

        public void AddRange(IEnumerable<string> items)
        {
            lock (_gate)
            {
                _items.AddRange(items);
            }
        }

        public IEnumerator<string> GetEnumerator()
        {
            lock (_gate)
            {
                return ((IEnumerable<string>)_items.ToArray()).GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
