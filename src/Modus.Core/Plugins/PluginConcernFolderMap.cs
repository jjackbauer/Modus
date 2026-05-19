namespace Modus.Core.Plugins;

public static class PluginConcernFolderMap
{
    public const string CanonicalPluginNamespace = "Modus.Core.Plugins";

    public static IReadOnlyList<string> ForbiddenFolderDerivedNamespacePrefixes { get; } =
    [
        "Modus.Core.Plugins.Contracts",
        "Modus.Core.Plugins.Lifecycle",
        "Modus.Core.Plugins.Validation",
        "Modus.Core.Plugins.Registration",
        "Modus.Core.Plugins.ServiceLifetime",
        "Modus.Core.Plugins.Base",
        "Modus.Core.Plugins.Implementation",
        "Modus.Core.Plugins.Extensions"
    ];

    public static IReadOnlyList<string> TargetFolders { get; } =
    [
        "Contracts",
        "Contracts/Specialized",
        "Lifecycle",
        "Validation",
        "Registration",
        "ServiceLifetime",
        "Base",
        "Implementation",
        "Extensions"
    ];

    public static IReadOnlyList<string> CurrentPluginRootFiles { get; } =
    [
        "ContractValidationResult.cs",
        "DeterministicPluginRegistrationPolicy.cs",
        "FiveSecondIntervalsTimerPrint.cs",
        "IHostTelemetryPluginContract.cs",
        "IMachineTelemetryPluginContract.cs",
        "IPluginContract.cs",
        "IPluginDependencyRegister.cs",
        "IPluginLifecycle.cs",
        "IPluginOperationCatalog.cs",
        "IPluginRegistrationPolicy.cs",
        "IPluginScheduledEvents.cs",
        "IPluginScheduler.cs",
        "IScheduledTimerTaskExtension.cs",
        "PluginBase.cs",
        "PluginContractValidationPolicy.cs",
        "PluginContractValidator.cs",
        "PluginDependencyRegisterServiceCollectionExtensions.cs",
        "PluginLifecycleContexts.cs",
        "PluginRegistrationStep.cs",
        "PluginRuntimeState.cs",
        "PluginRuntimeStateTransitions.cs",
        "PluginServiceLifetime.cs",
        "PluginServiceLifetimeMapping.cs",
        "PluginVersionValidator.cs",
        "TimerPlugin.cs",
        "VersionValidationResult.cs"
    ];

    public static IReadOnlyDictionary<string, string> CurrentRootFileToTargetFolder { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IPluginContract.cs"] = "Contracts",
            ["IPluginDependencyRegister.cs"] = "Contracts",
            ["IPluginLifecycle.cs"] = "Contracts",
            ["IPluginOperationCatalog.cs"] = "Contracts",
            ["IPluginRegistrationPolicy.cs"] = "Contracts",
            ["IPluginScheduledEvents.cs"] = "Contracts",
            ["IPluginScheduler.cs"] = "Contracts",
            ["IScheduledTimerTaskExtension.cs"] = "Contracts",
            ["IHostTelemetryPluginContract.cs"] = "Contracts/Specialized",
            ["IMachineTelemetryPluginContract.cs"] = "Contracts/Specialized",
            ["PluginLifecycleContexts.cs"] = "Lifecycle",
            ["PluginRuntimeState.cs"] = "Lifecycle",
            ["PluginRuntimeStateTransitions.cs"] = "Lifecycle",
            ["ContractValidationResult.cs"] = "Validation",
            ["PluginContractValidationPolicy.cs"] = "Validation",
            ["PluginContractValidator.cs"] = "Validation",
            ["PluginVersionValidator.cs"] = "Validation",
            ["VersionValidationResult.cs"] = "Validation",
            ["DeterministicPluginRegistrationPolicy.cs"] = "Registration",
            ["PluginRegistrationStep.cs"] = "Registration",
            ["PluginServiceLifetime.cs"] = "ServiceLifetime",
            ["PluginServiceLifetimeMapping.cs"] = "ServiceLifetime",
            ["PluginBase.cs"] = "Base",
            ["FiveSecondIntervalsTimerPrint.cs"] = "Implementation",
            ["TimerPlugin.cs"] = "Implementation",
            ["PluginDependencyRegisterServiceCollectionExtensions.cs"] = "Extensions"
        };
}