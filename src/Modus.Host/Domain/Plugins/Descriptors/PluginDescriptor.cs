namespace Modus.Host.Plugins.Descriptors;

public sealed record PluginDescriptor(
    string PluginId,
    string AssemblyName,
    Version Version,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> DependsOn,
    bool IsContractCompliant = true,
    bool IsValidAssembly = true,
    bool UsesOnlyStandardLibrary = true,
    bool FailOnActivation = false,
    IReadOnlyList<string>? DeclaredOperations = null,
    IReadOnlyList<string>? FailingOperations = null,
    string? AssemblyPath = null,
    long AssemblyFileSizeBytes = 0);
