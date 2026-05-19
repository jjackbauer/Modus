namespace Modus.Core.Hosting;

public interface IPluginHostPortabilityContract
{
    string ContractName { get; }

    Version ContractVersion { get; }
}