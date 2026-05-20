using Modus.Core.Events;

namespace Modus.Core.Plugins;

public sealed class DeterministicPluginRegistrationPolicy : IPluginRegistrationPolicy
{
    public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        var steps = new List<PluginRegistrationStep>();
        var sequence = 1;

        if (plugin is IPluginOperationCatalog operationCatalog)
        {
            var operations = operationCatalog.SupportedOperations
                .Select(x => x.Value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            foreach (var operation in operations)
            {
                steps.Add(new PluginRegistrationStep(sequence++, PluginRegistrationStepKind.RegisterOperation, $"operation:{operation}"));
            }
        }

        if (plugin is IEventSubscriber)
        {
            steps.Add(new PluginRegistrationStep(sequence++, PluginRegistrationStepKind.SubscribeEvents, "events:subscribe"));
        }

        if (plugin is IPluginScheduledEvents)
        {
            steps.Add(new PluginRegistrationStep(sequence++, PluginRegistrationStepKind.RegisterSchedules, "schedules:register"));
        }

        return steps;
    }
}