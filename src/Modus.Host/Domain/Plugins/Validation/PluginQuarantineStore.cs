using Modus.Host.Plugins.Lifecycle;

namespace Modus.Host.Plugins.Validation;

public sealed class PluginQuarantineStore
{
    private readonly Dictionary<string, int> _consecutiveFailures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _quarantinedPluginIds = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> QuarantinedPluginIds => _quarantinedPluginIds
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToArray();

    public bool IsQuarantined(string pluginId)
    {
        return _quarantinedPluginIds.Contains(pluginId);
    }

    public int GetConsecutiveFailureCount(string pluginId)
    {
        return _consecutiveFailures.TryGetValue(pluginId, out var count) ? count : 0;
    }

    public void RegisterFailure(string pluginId, PluginRetryPolicy retryPolicy)
    {
        var nextCount = GetConsecutiveFailureCount(pluginId) + 1;
        _consecutiveFailures[pluginId] = nextCount;

        if (retryPolicy.ShouldQuarantine(nextCount))
        {
            _quarantinedPluginIds.Add(pluginId);
        }
    }

    public void RegisterSuccess(string pluginId)
    {
        _consecutiveFailures.Remove(pluginId);
        _quarantinedPluginIds.Remove(pluginId);
    }
}
