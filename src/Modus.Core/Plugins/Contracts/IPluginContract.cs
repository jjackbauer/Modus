namespace Modus.Core.Plugins;

public interface IPluginContract
{
    string PluginId { get; }
    string ContractName { get; }
    Version ContractVersion { get; }
}
