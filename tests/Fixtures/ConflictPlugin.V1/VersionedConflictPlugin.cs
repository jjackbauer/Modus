using Conflict.Dep;
using Modus.Core.Plugins;
using Wip.Modus.Hosting;

namespace Conflict.Plugin.V1;

public sealed class VersionedConflictPlugin : IWipHostPluginContract, IPluginOperationCatalog
{
    public PluginId PluginId => new("conflict.plugin.v1");

    public ContractName ContractName => new("Wip.HostPluginContract");

    public Version ContractVersion => new(1, 0, 0);

    public IReadOnlyCollection<OperationName> SupportedOperations =>
        [new OperationName($"conflict.operation.v{DepMarker.Version}")];
}