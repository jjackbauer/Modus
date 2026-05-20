namespace Modus.Core.Plugins;

public interface IPluginContract
{
    PluginId PluginId { get; }
    ContractName ContractName { get; }
    Version ContractVersion { get; }
}
