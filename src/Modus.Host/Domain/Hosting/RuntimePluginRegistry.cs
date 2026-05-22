using Modus.Core.Plugins;

namespace Modus.Host.Domain.Hosting;

public sealed class RuntimePluginRegistry
{
    private readonly object _gate = new();
    private RuntimePluginSnapshot _snapshot = new([], []);

    public event EventHandler<RuntimePluginRegistryChangedEventArgs>? Changed;

    public RuntimePluginRegistry()
    {
    }

    public RuntimePluginRegistry(
        IEnumerable<IPluginContract> contracts,
        IEnumerable<IPluginOperationCatalog> catalogs)
    {
        Update(contracts, catalogs);
    }

    public RuntimePluginSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    public void Update(
        IEnumerable<IPluginContract> contracts,
        IEnumerable<IPluginOperationCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(catalogs);

        var normalizedContracts = contracts.ToArray();
        var normalizedCatalogs = catalogs.ToArray();

        RuntimePluginRegistryChangedEventArgs? change = null;

        lock (_gate)
        {
            var previousPluginIds = _snapshot.Contracts
                .Select(static contract => contract.PluginId.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var currentPluginIds = normalizedContracts
                .Select(static contract => contract.PluginId.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            _snapshot = new RuntimePluginSnapshot(normalizedContracts, normalizedCatalogs);

            var removedPluginIds = previousPluginIds
                .Except(currentPluginIds, StringComparer.Ordinal)
                .ToArray();
            var addedPluginIds = currentPluginIds
                .Except(previousPluginIds, StringComparer.Ordinal)
                .ToArray();

            if (removedPluginIds.Length > 0 || addedPluginIds.Length > 0)
            {
                change = new RuntimePluginRegistryChangedEventArgs(_snapshot, addedPluginIds, removedPluginIds);
            }
        }

        if (change is not null)
        {
            Changed?.Invoke(this, change);
        }
    }
}

public sealed record RuntimePluginSnapshot(
    IReadOnlyList<IPluginContract> Contracts,
    IReadOnlyList<IPluginOperationCatalog> Catalogs);

public sealed class RuntimePluginRegistryChangedEventArgs : EventArgs
{
    public RuntimePluginRegistryChangedEventArgs(
        RuntimePluginSnapshot snapshot,
        IReadOnlyList<string> addedPluginIds,
        IReadOnlyList<string> removedPluginIds)
    {
        Snapshot = snapshot;
        AddedPluginIds = addedPluginIds;
        RemovedPluginIds = removedPluginIds;
    }

    public RuntimePluginSnapshot Snapshot { get; }

    public IReadOnlyList<string> AddedPluginIds { get; }

    public IReadOnlyList<string> RemovedPluginIds { get; }
}