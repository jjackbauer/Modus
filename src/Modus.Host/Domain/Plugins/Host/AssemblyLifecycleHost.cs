using System.Reflection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Plugins.Descriptors;

namespace Modus.Host.Plugins.Host;

internal sealed class AssemblyLifecycleHost
{
    private readonly CancellationTokenSource _lifecycleCts = new();

    public IReadOnlyList<string> StartActivatedPlugins(
        IReadOnlyList<PluginDescriptor> descriptors,
        IReadOnlyCollection<string> activatedPluginIds)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(activatedPluginIds);

        var diagnostics = new List<string>();
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
                        && type.GetConstructor(Type.EmptyTypes) is not null)
                    .OrderBy(type => type.FullName, StringComparer.Ordinal)
                    .ToArray();

                foreach (var lifecycleType in lifecycleTypes)
                {
                    if (Activator.CreateInstance(lifecycleType) is not IPluginLifecycle plugin)
                    {
                        continue;
                    }

                    var pluginIdString = (plugin as IPluginContract)?.PluginId.Value
                        ?? lifecycleType.FullName
                        ?? lifecycleType.Name;
                    var pluginId = new PluginId(pluginIdString);

                    plugin.Load(new PluginLoadContext(pluginId, _lifecycleCts.Token));
                    plugin.Start(new PluginStartContext(pluginId, _lifecycleCts.Token));

                    diagnostics.Add($"stage=lifecycle plugin={pluginId} outcome=started source={descriptor.PluginId}");
                    diagnostics.AddRange(RegisterAndRunSchedules(plugin, pluginId.Value));
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add($"stage=lifecycle plugin={descriptor.PluginId} outcome=failure reason={ex.Message}");
            }
        }

        return diagnostics;
    }

    private IReadOnlyList<string> RegisterAndRunSchedules(IPluginLifecycle plugin, string pluginId)
    {
        var diagnostics = new List<string>();

        if (plugin is not IPluginScheduledEvents scheduledPlugin)
        {
            return diagnostics;
        }

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
            diagnostics.AddRange(ExecuteScheduledOperation(plugin, pluginId, recurring.JobName, recurring.Operation));

            var token = _lifecycleCts.Token;
            var interval = recurring.Interval;
            var jobName = recurring.JobName;
            var operation = recurring.Operation;
            _ = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(interval);
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    ExecuteScheduledOperation(plugin, pluginId, jobName, operation);
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
            diagnostics.AddRange(ExecuteScheduledOperation(plugin, pluginId, oneTime.JobName, oneTime.Operation));
        }

        return diagnostics;
    }

    private static IReadOnlyList<string> ExecuteScheduledOperation(
        IPluginLifecycle plugin,
        string pluginId,
        string jobName,
        string operation)
    {
        if (plugin is not ISyncResponder responder)
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
}
