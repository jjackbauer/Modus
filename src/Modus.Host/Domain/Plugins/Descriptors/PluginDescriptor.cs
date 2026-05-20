using Modus.Core.Plugins;

namespace Modus.Host.Plugins.Descriptors;

public sealed record PluginDescriptor(
    PluginId PluginId,
    string AssemblyName,
    Version Version,
    IReadOnlyList<CapabilityName> Capabilities,
    IReadOnlyList<CapabilityName> DependsOn,
    bool IsContractCompliant = true,
    bool IsValidAssembly = true,
    bool UsesOnlyStandardLibrary = true,
    bool FailOnActivation = false,
    IReadOnlyList<OperationName>? DeclaredOperations = null,
    IReadOnlyList<OperationName>? FailingOperations = null,
    string? AssemblyPath = null,
    long AssemblyFileSizeBytes = 0);
