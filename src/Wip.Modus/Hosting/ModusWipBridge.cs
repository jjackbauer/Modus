using System.Reflection;
using Modus.Core.Plugins;
using Wip.Builder;

namespace Wip.Modus.Hosting;

public sealed record PluginManifestEntry(
    string PluginId,
    string PluginName,
    string PluginVersion,
    string AssemblyName,
    string AssemblyVersion,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> RequiredPermissions);

public sealed record WorkflowManifestEntry(
    string WorkflowId,
    string DisplayName,
    string RequestType,
    string ResultType);

public sealed record RunManifest(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<PluginManifestEntry> Plugins,
    IReadOnlyList<WorkflowManifestEntry> Workflows);

public sealed record ModusDebugLogEntry(
    DateTimeOffset TimestampUtc,
    string Level,
    string Source,
    string Message,
    string RunCorrelationId);

public sealed record ModusDebugLogSnapshot(
    string RunCorrelationId,
    DateTimeOffset RunStartedAtUtc,
    IReadOnlyList<ModusDebugLogEntry> Entries);

public interface IModusWipDebugChannel
{
    void BeginHostRun(string runCorrelationId, DateTimeOffset runStartedAtUtc);

    ModusDebugLogSnapshot GetDebugLogSnapshot();
}

public interface IModusWipBridge
{
    ValueTask<int> LoadPluginsAsync(CancellationToken cancellationToken);

    ValueTask StopPluginsAsync(CancellationToken cancellationToken);

    RunManifest GetRunManifest();

    IReadOnlyList<string> GetLoadDiagnostics();
}

public sealed class ModusWipBridge : IModusWipBridge, IModusWipDebugChannel
{
    private const string DiscoveryStage = "discovery";
    private const string AssemblyLoadStage = "assembly-load";
    private const string ActivationStage = "activation";
    private const string LifecycleStartStage = "lifecycle-start";

    private readonly IReadOnlyList<string> _pluginDiscoveryPaths;
    private readonly IReadOnlyList<WorkflowRegistration> _workflowRegistrations;
    private readonly List<PluginRuntime> _loadedPlugins = [];
    private readonly List<string> _loadDiagnostics = [];
    private readonly List<ModusDebugLogEntry> _debugLogs = [];
    private RunManifest _runManifest;
    private string _currentRunCorrelationId = "run-unassigned";
    private DateTimeOffset _currentRunStartedAtUtc = DateTimeOffset.MinValue;
    private int _loaded;

    public ModusWipBridge(string pluginsPath, IReadOnlyList<WorkflowRegistration>? workflowRegistrations = null)
        : this([pluginsPath], workflowRegistrations)
    {
    }

    public ModusWipBridge(
        string repositoryPluginsPath,
        string userPluginsPath,
        IReadOnlyList<WorkflowRegistration>? workflowRegistrations = null)
        : this([repositoryPluginsPath, userPluginsPath], workflowRegistrations)
    {
    }

    private ModusWipBridge(IReadOnlyList<string> pluginDiscoveryPaths, IReadOnlyList<WorkflowRegistration>? workflowRegistrations)
    {
        ArgumentNullException.ThrowIfNull(pluginDiscoveryPaths);
        _pluginDiscoveryPaths = NormalizePluginDiscoveryPaths(pluginDiscoveryPaths);
        _workflowRegistrations = workflowRegistrations ?? Array.Empty<WorkflowRegistration>();
        _runManifest = CreateUnloadedRunManifest();
    }

    public ValueTask<int> LoadPluginsAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _loaded, 1, 0) != 0)
        {
            return ValueTask.FromResult(_loadedPlugins.Count);
        }

        var discoveredAnyPath = false;
        foreach (var pluginDiscoveryPath in _pluginDiscoveryPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(pluginDiscoveryPath))
            {
                AddLoadDiagnostic(DiscoveryStage, $"Plugin path '{pluginDiscoveryPath}' does not exist.");
                continue;
            }

            discoveredAnyPath = true;

            foreach (var assemblyPath in Directory
                         .EnumerateFiles(pluginDiscoveryPath, "*.dll", SearchOption.AllDirectories)
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryLoadAssembly(assemblyPath, out var assembly, out var loadError))
                {
                    AddLoadDiagnostic(AssemblyLoadStage, $"Failed to load assembly '{assemblyPath}': {loadError}");
                    continue;
                }

                foreach (var pluginType in ResolvePluginTypes(assembly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!TryActivatePlugin(pluginType, out var plugin, out var activationError))
                    {
                        AddLoadDiagnostic(ActivationStage, $"Failed to activate plugin type '{pluginType.FullName}': {activationError}");
                        continue;
                    }

                    var lifecycle = plugin as IPluginLifecycle;

                    try
                    {
                        lifecycle?.Load(new PluginLoadContext(plugin.PluginId, cancellationToken));
                        lifecycle?.Start(new PluginStartContext(plugin.PluginId, cancellationToken));

                        var metadata = BuildPluginMetadata(plugin, pluginType.Assembly);
                        _loadedPlugins.Add(new PluginRuntime(plugin, lifecycle, metadata));
                    }
                    catch (Exception ex)
                    {
                        AddLoadDiagnostic(LifecycleStartStage, $"Plugin '{plugin.PluginId.Value}' failed during lifecycle start: {ex.Message}");
                    }
                }
            }
        }

        if (!discoveredAnyPath)
        {
            _runManifest = CreateUnloadedRunManifest();
            return ValueTask.FromResult(0);
        }

        var pluginEntries = _loadedPlugins
            .Select(static runtime => runtime.Metadata)
            .OrderBy(static entry => entry.PluginId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.PluginVersion, StringComparer.Ordinal)
            .ThenBy(static entry => entry.PluginName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.AssemblyName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.AssemblyVersion, StringComparer.Ordinal)
            .ToArray();

        _runManifest = CreateLoadedRunManifest(pluginEntries);
        return ValueTask.FromResult(_loadedPlugins.Count);
    }

    public ValueTask StopPluginsAsync(CancellationToken cancellationToken)
    {
        var unloadDeadline = DateTimeOffset.UtcNow.AddSeconds(5);

        for (var index = _loadedPlugins.Count - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var runtime = _loadedPlugins[index];
            if (runtime.Lifecycle is null)
                continue;

            runtime.Lifecycle.Stop(new PluginStopContext(runtime.Plugin.PluginId, cancellationToken));
            runtime.Lifecycle.Unload(new PluginUnloadContext(
                runtime.Plugin.PluginId,
                PluginUnloadReason.GracefulShutdown,
                unloadDeadline,
                cancellationToken));
        }

        _loadedPlugins.Clear();
        _runManifest = CreateUnloadedRunManifest();

        return ValueTask.CompletedTask;
    }

    public RunManifest GetRunManifest() => _runManifest;

    public IReadOnlyList<string> GetLoadDiagnostics() => _loadDiagnostics;

    public void BeginHostRun(string runCorrelationId, DateTimeOffset runStartedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(runCorrelationId))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(runCorrelationId));

        _currentRunCorrelationId = runCorrelationId;
        _currentRunStartedAtUtc = runStartedAtUtc;
        _debugLogs.Clear();
    }

    public ModusDebugLogSnapshot GetDebugLogSnapshot()
        => new(
            RunCorrelationId: _currentRunCorrelationId,
            RunStartedAtUtc: _currentRunStartedAtUtc,
            Entries: _debugLogs
                .Where(entry => string.Equals(entry.RunCorrelationId, _currentRunCorrelationId, StringComparison.Ordinal))
                .OrderBy(entry => entry.TimestampUtc)
                .ThenBy(entry => entry.Source, StringComparer.Ordinal)
                .ThenBy(entry => entry.Message, StringComparer.Ordinal)
                .ToArray());

    private void AddLoadDiagnostic(string stage, string message)
    {
        _loadDiagnostics.Add($"[{stage}][run:{_currentRunCorrelationId}] {message}");
        _debugLogs.Add(new ModusDebugLogEntry(
            TimestampUtc: DateTimeOffset.UtcNow,
            Level: "debug",
            Source: $"plugin-load:{stage}",
            Message: message,
            RunCorrelationId: _currentRunCorrelationId));
    }

    private IReadOnlyList<WorkflowManifestEntry> BuildWorkflowEntries()
    {
        return _workflowRegistrations
            .Select(static registration => new WorkflowManifestEntry(
                registration.WorkflowId.Value,
                registration.Descriptor.DisplayName,
                registration.RequestType.FullName ?? registration.RequestType.Name,
                registration.ResultType.FullName ?? registration.ResultType.Name))
            .ToArray();
    }

    private RunManifest CreateLoadedRunManifest(IReadOnlyList<PluginManifestEntry> pluginEntries)
        => new(
            DateTimeOffset.UtcNow,
            pluginEntries,
            pluginEntries.Count == 0 ? Array.Empty<WorkflowManifestEntry>() : BuildWorkflowEntries());

    private RunManifest CreateUnloadedRunManifest()
        => new(DateTimeOffset.UtcNow, Array.Empty<PluginManifestEntry>(), Array.Empty<WorkflowManifestEntry>());

    private static bool TryLoadAssembly(string assemblyPath, out Assembly assembly, out string? error)
    {
        try
        {
            assembly = Assembly.LoadFrom(assemblyPath);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            assembly = null!;
            error = ex.Message;
            return false;
        }
    }

    private static bool TryActivatePlugin(Type pluginType, out IWipHostPluginContract plugin, out string? error)
    {
        try
        {
            if (Activator.CreateInstance(pluginType) is not IWipHostPluginContract instance)
            {
                plugin = null!;
                error = "Type does not implement IWipHostPluginContract.";
                return false;
            }

            plugin = instance;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            var effective = ex is TargetInvocationException tie && tie.InnerException is Exception inner
                ? inner
                : ex;

            plugin = null!;
            error = effective.Message;
            return false;
        }
    }

    private static IEnumerable<Type> ResolvePluginTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(static type => type is not null).Cast<Type>().ToArray();
        }

        return types
            .Where(static type =>
                type is { IsClass: true, IsAbstract: false } &&
                typeof(IWipHostPluginContract).IsAssignableFrom(type) &&
                type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal);
    }

    private static PluginManifestEntry BuildPluginMetadata(IPluginContract plugin, Assembly assembly)
    {
        var assemblyName = assembly.GetName();
        var capabilities = plugin is IPluginOperationCatalog operationCatalog
            ? operationCatalog.SupportedOperations
                .Select(static operation => operation.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static capability => capability, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();

        var requiredPermissions = plugin is IPluginRegistrationPolicy registrationPolicy
            ? registrationPolicy
                .BuildRegistrationPlan(plugin)
                .Select(static step => step.Kind.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static permission => permission, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();

        return new PluginManifestEntry(
            PluginId: plugin.PluginId.Value,
            PluginName: plugin.GetType().Name,
            PluginVersion: plugin.ContractVersion.ToString(),
            AssemblyName: assemblyName.Name ?? plugin.GetType().Assembly.GetName().Name ?? "unknown",
            AssemblyVersion: (assemblyName.Version ?? new Version(0, 0, 0, 0)).ToString(),
            Capabilities: capabilities,
            RequiredPermissions: requiredPermissions);
    }

    private sealed record PluginRuntime(IWipHostPluginContract Plugin, IPluginLifecycle? Lifecycle, PluginManifestEntry Metadata);

    private static IReadOnlyList<string> NormalizePluginDiscoveryPaths(IReadOnlyList<string> pluginDiscoveryPaths)
    {
        var normalized = pluginDiscoveryPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
            throw new ArgumentException("At least one plugin discovery path must be provided.", nameof(pluginDiscoveryPaths));

        return normalized;
    }
}