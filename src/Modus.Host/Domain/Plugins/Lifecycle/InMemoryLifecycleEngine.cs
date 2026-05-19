using Modus.Core.Events;
using Modus.Core.Plugins;
using Modus.Host.Plugins.Descriptors;
using Modus.Host.Plugins.Results;
using Modus.Host.Plugins.Validation;

namespace Modus.Host.Plugins.Lifecycle;

public sealed class InMemoryLifecycleEngine
{
    private readonly Dictionary<string, PluginSpec> _activePlugins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _receivedEventCount = new(StringComparer.Ordinal);
    private readonly PluginLifecycleOrchestrator _lifecycleOrchestrator = new();
    private readonly PluginUnloadCoordinator _unloadCoordinator = new();
    private readonly PluginRetryPolicy _retryPolicy;
    private readonly PluginQuarantineStore _quarantineStore = new();

    public InMemoryLifecycleEngine(int quarantineThreshold = 3)
    {
        _retryPolicy = new PluginRetryPolicy(quarantineThreshold);
    }

    public bool HostHealthy { get; private set; } = true;

    public IReadOnlyCollection<string> ActivePluginIds => _activePlugins.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();

    public IReadOnlyCollection<string> QuarantinedPluginIds => _quarantineStore.QuarantinedPluginIds;

    public LifecycleResult HotLoad(PluginSpec spec)
    {
        var orchestration = _lifecycleOrchestrator.OrchestrateHotLoad(spec, _quarantineStore.IsQuarantined(spec.PluginId));

        if (orchestration.RegisterFailure)
        {
            _quarantineStore.RegisterFailure(spec.PluginId, _retryPolicy);
        }

        if (orchestration.Activated)
        {
            _activePlugins[spec.PluginId] = spec;
            _quarantineStore.RegisterSuccess(spec.PluginId);

            if (!_receivedEventCount.ContainsKey(spec.PluginId))
            {
                _receivedEventCount[spec.PluginId] = 0;
            }
        }

        return new LifecycleResult(
            orchestration.Transitions,
            orchestration.Diagnostics,
            HostHealthy,
            Quarantined: orchestration.Quarantined || _quarantineStore.IsQuarantined(spec.PluginId));
    }

    public LifecycleResult HotUnload(string pluginId)
    {
        var wasActive = _activePlugins.Remove(pluginId);
        return _unloadCoordinator.OrchestrateHotUnload(pluginId, wasActive, HostHealthy);
    }

    public EventDispatchResult Publish(DomainEvent @event)
    {
        var delivered = 0;

        foreach (var pluginId in _activePlugins.Keys)
        {
            _receivedEventCount[pluginId]++;
            delivered++;
        }

        _ = @event;
        return new EventDispatchResult(delivered);
    }

    public int GetReceivedEvents(string pluginId)
    {
        return _receivedEventCount.TryGetValue(pluginId, out var count) ? count : 0;
    }

    public int GetFailureCount(string pluginId)
    {
        return _quarantineStore.GetConsecutiveFailureCount(pluginId);
    }
}
