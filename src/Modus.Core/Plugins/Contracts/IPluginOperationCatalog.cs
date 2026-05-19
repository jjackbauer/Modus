namespace Modus.Core.Plugins;

public interface IPluginOperationCatalog
{
    IReadOnlyCollection<string> SupportedOperations { get; }
}