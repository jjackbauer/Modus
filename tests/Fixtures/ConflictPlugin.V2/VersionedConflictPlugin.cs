using Conflict.Dep;
using Modus.Core.Plugins;
using Wip.Modus.Hosting;

namespace Conflict.Plugin.V2;

public sealed class VersionedConflictPlugin : IWipHostPluginContract, IPluginOperationCatalog
{
    public PluginId PluginId => new("conflict.plugin.v2");

    public ContractName ContractName => new("Wip.HostPluginContract");

    public Version ContractVersion => new(1, 0, 0);

    public IReadOnlyCollection<OperationName> SupportedOperations =>
        [new OperationName($"conflict.operation.v{DepMarker.Version}")];
}