namespace Modus.Core.Plugins;

public interface IPluginOperationCatalog
{
    IReadOnlyCollection<OperationName> SupportedOperations { get; }
}