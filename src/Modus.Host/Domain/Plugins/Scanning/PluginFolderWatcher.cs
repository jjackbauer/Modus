using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Plugins.Descriptors;
using Modus.Host.Plugins.Host;
using Modus.Host.Plugins.Lifecycle;
using PluginProjectDescriptorFactory = Modus.Host.Plugins.PluginProjectDescriptorFactory;

namespace Modus.Host.Plugins.Scanning;

public sealed class PluginFolderWatcher
{
    private readonly PluginProjectDescriptorFactory _descriptorFactory = new();
    private readonly PluginLoader _loader = new();
    private readonly InMemoryHostRuntime _runtime = new();
    private readonly HostStatusSnapshotBuilder _statusSnapshotBuilder = new();
    private readonly IServiceProvider? _serviceProvider;
    private readonly HostStatusRegistry? _statusRegistry;
    private RuntimePluginRegistry? _runtimePluginRegistry;
    private IReadOnlyList<IPluginContract> _baseRuntimeContracts = [];
    private IReadOnlyList<IPluginOperationCatalog> _baseRuntimeCatalogs = [];
    private bool _baseRuntimeSnapshotCaptured;
    private bool _runtimeRegistryUnavailable;
    private readonly AssemblyLifecycleHost _assemblyLifecycleHost;
    private readonly HashSet<string> _processedProjectPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pluginIdByProjectPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeRegistryPluginProjection> _runtimeProjectionsByPluginId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activePluginIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedPluginIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _publishedRuntimePluginIds = new(StringComparer.Ordinal);
    private long _discoverySequence;
    private string? _pluginsPath;
    private bool _started;

    public PluginFolderWatcher()
        : this(serviceProvider: null)
    {
    }

    internal PluginFolderWatcher(IServiceProvider? serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _statusRegistry = serviceProvider?.GetService<HostStatusRegistry>();
        _assemblyLifecycleHost = serviceProvider is null
            ? new AssemblyLifecycleHost()
            : new AssemblyLifecycleHost(serviceProvider);
    }

    public PluginWatcherStartResult Start(string pluginsPath)
    {
        if (string.IsNullOrWhiteSpace(pluginsPath))
        {
            var snapshot = _statusSnapshotBuilder.Build(
                hostHealthy: false,
                descriptors: [],
                activatedPluginIds: [],
                failedPluginIds: [],
                capabilityOwners: null);
            _statusRegistry?.Update(snapshot, ["stage=startup outcome=failure reason=plugins path missing"]);

            return new PluginWatcherStartResult(
                HostHealthy: false,
                WatcherRegistered: false,
                PluginsPath: string.Empty,
                PluginsDirectoryExists: false,
                Diagnostics: ["stage=startup outcome=failure reason=plugins path missing"])
            {
                StatusSnapshot = snapshot
            };
        }

        _pluginsPath = Path.GetFullPath(pluginsPath);
        _started = true;

        var pluginsDirectoryExists = Directory.Exists(_pluginsPath);
        var diagnostics = new List<string>
        {
            "stage=startup pipeline=plugin-loader outcome=initialized",
            $"stage=startup outcome=success watcher=registered path={_pluginsPath}",
        };
        var hostHealthy = true;
        IReadOnlyList<PluginDescriptor> startupDescriptors = [];
        IReadOnlyCollection<string> activatedPluginIds = [];
        IReadOnlyCollection<string> failedPluginIds = [];
        IReadOnlyDictionary<string, string>? capabilityOwners = null;

        if (!pluginsDirectoryExists)
        {
            hostHealthy = false;
            diagnostics.Add($"stage=startup outcome=failure reason=plugins directory missing path={_pluginsPath}");
        }
        else
        {
            var scan = _loader.ScanRuntimeAssemblies(_pluginsPath);
            startupDescriptors = scan.Descriptors;
            diagnostics.AddRange(scan.Diagnostics);

            if (scan.Descriptors.Count > 0)
            {
                var startupLoad = _runtime.Start(scan.Descriptors);
                activatedPluginIds = startupLoad.ActivatedPluginIds;
                failedPluginIds = startupLoad.FailedPluginIds;
                capabilityOwners = startupLoad.CapabilityOwners;
                foreach (var pluginId in startupLoad.ActivatedPluginIds)
                {
                    _activePluginIds.Add(pluginId);
                    _failedPluginIds.Remove(pluginId);
                }

                foreach (var pluginId in startupLoad.FailedPluginIds)
                {
                    _failedPluginIds.Add(pluginId);
                }

                diagnostics.AddRange(startupLoad.Diagnostics);
                diagnostics.AddRange(_assemblyLifecycleHost.StartActivatedPlugins(scan.Descriptors, startupLoad.ActivatedPluginIds));

                var activatedSet = startupLoad.ActivatedPluginIds.ToHashSet(StringComparer.Ordinal);
                foreach (var descriptor in scan.Descriptors.Where(descriptor => activatedSet.Contains(descriptor.PluginId.Value)))
                {
                    UpsertRuntimeProjection(descriptor);
                }
            }

            foreach (var existingProjectPath in Directory
                .EnumerateFiles(_pluginsPath, "*.csproj", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var onboarding = OnProjectCreated(existingProjectPath);
                diagnostics.AddRange(onboarding.Diagnostics);
            }
        }

        diagnostics.AddRange(PublishRuntimeRegistrySnapshot());

        var statusSnapshot = _statusSnapshotBuilder.Build(
            hostHealthy,
            startupDescriptors,
            activatedPluginIds,
            failedPluginIds,
            capabilityOwners);
        _statusRegistry?.Update(statusSnapshot, diagnostics);

        return new PluginWatcherStartResult(
            HostHealthy: hostHealthy,
            WatcherRegistered: true,
            PluginsPath: _pluginsPath,
            PluginsDirectoryExists: pluginsDirectoryExists,
            Diagnostics: diagnostics)
        {
            StatusSnapshot = statusSnapshot
        };
    }

    public PluginOnboardingResult OnProjectCreated(string csprojPath)
    {
        if (!_started || string.IsNullOrWhiteSpace(_pluginsPath))
        {
            return new PluginOnboardingResult(
                HostHealthy: false,
                EventAccepted: false,
                PluginActivated: false,
                PluginId: null,
                ActivePluginIds: Snapshot(_activePluginIds),
                FailedPluginIds: Snapshot(_failedPluginIds),
                Diagnostics: ["stage=discovery outcome=failure reason=watcher not started"]);
        }

        var sequence = NextDiscoverySequence();

        if (string.IsNullOrWhiteSpace(csprojPath))
        {
            return CreateIgnoredResult($"stage=discovery sequence={sequence:D4} outcome=ignored reason=project path missing");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(csprojPath);
        }
        catch (Exception)
        {
            return CreateIgnoredResult($"stage=discovery sequence={sequence:D4} outcome=ignored reason=invalid project path");
        }

        if (!IsUnderPluginsPath(fullPath))
        {
            return CreateIgnoredResult($"stage=discovery sequence={sequence:D4} outcome=ignored reason=outside plugins path path={fullPath}");
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return CreateIgnoredResult($"stage=discovery sequence={sequence:D4} outcome=ignored reason=non-csproj file path={fullPath}");
        }

        if (!IsInScopePluginProject(fullPath))
        {
            return CreateIgnoredResult($"stage=discovery sequence={sequence:D4} outcome=ignored reason=out-of-scope plugin project path={fullPath}");
        }

        if (!_processedProjectPaths.Add(fullPath))
        {
            return CreateIgnoredResult($"stage=discovery sequence={sequence:D4} outcome=ignored reason=duplicate file notification path={fullPath}");
        }

        var discoveryDiagnostics = new List<string>
        {
            $"stage=discovery sequence={sequence:D4} outcome=accepted path={fullPath}",
        };

        PluginDescriptor descriptor;
        try
        {
            descriptor = _descriptorFactory.Create(fullPath);
        }
        catch (Exception ex)
        {
            _failedPluginIds.Add(Path.GetFileNameWithoutExtension(fullPath));
            return new PluginOnboardingResult(
                HostHealthy: true,
                EventAccepted: true,
                PluginActivated: false,
                PluginId: new PluginId(Path.GetFileNameWithoutExtension(fullPath)!),
                ActivePluginIds: Snapshot(_activePluginIds),
                FailedPluginIds: Snapshot(_failedPluginIds),
                Diagnostics: [.. discoveryDiagnostics, $"stage=descriptor outcome=failure reason={ex.Message}"]);
        }

        if (_activePluginIds.Contains(descriptor.PluginId.Value))
        {
            return CreateIgnoredResult($"stage=discovery sequence={sequence:D4} outcome=ignored reason=plugin already active plugin={descriptor.PluginId}");
        }

        _pluginIdByProjectPath[fullPath] = descriptor.PluginId.Value;

        var hostResult = _runtime.Start([descriptor]);
        foreach (var pluginId in hostResult.ActivatedPluginIds)
        {
            _activePluginIds.Add(pluginId);
            _failedPluginIds.Remove(pluginId);
        }

        foreach (var pluginId in hostResult.FailedPluginIds)
        {
            _failedPluginIds.Add(pluginId);
        }

        var activated = hostResult.ActivatedPluginIds.Contains(descriptor.PluginId.Value, StringComparer.Ordinal);
        if (activated)
        {
            UpsertRuntimeProjection(descriptor);
        }

        var registryDiagnostics = PublishRuntimeRegistrySnapshot();

        return new PluginOnboardingResult(
            HostHealthy: hostResult.Started,
            EventAccepted: true,
            PluginActivated: activated,
            PluginId: descriptor.PluginId,
            ActivePluginIds: Snapshot(_activePluginIds),
            FailedPluginIds: Snapshot(_failedPluginIds),
            Diagnostics: [.. discoveryDiagnostics, .. hostResult.Diagnostics, .. registryDiagnostics]);
    }

    public PluginOnboardingResult OnProjectDeleted(string csprojPath)
    {
        if (!_started || string.IsNullOrWhiteSpace(_pluginsPath))
        {
            return new PluginOnboardingResult(
                HostHealthy: false,
                EventAccepted: false,
                PluginActivated: false,
                PluginId: null,
                ActivePluginIds: Snapshot(_activePluginIds),
                FailedPluginIds: Snapshot(_failedPluginIds),
                Diagnostics: ["stage=unload outcome=failure reason=watcher not started"]);
        }

        var sequence = NextDiscoverySequence();
        if (string.IsNullOrWhiteSpace(csprojPath))
        {
            return CreateIgnoredResult($"stage=unload sequence={sequence:D4} outcome=ignored reason=project path missing");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(csprojPath);
        }
        catch (Exception)
        {
            return CreateIgnoredResult($"stage=unload sequence={sequence:D4} outcome=ignored reason=invalid project path");
        }

        if (!IsUnderPluginsPath(fullPath))
        {
            return CreateIgnoredResult($"stage=unload sequence={sequence:D4} outcome=ignored reason=outside plugins path path={fullPath}");
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return CreateIgnoredResult($"stage=unload sequence={sequence:D4} outcome=ignored reason=non-csproj file path={fullPath}");
        }

        if (!IsInScopePluginProject(fullPath))
        {
            return CreateIgnoredResult($"stage=unload sequence={sequence:D4} outcome=ignored reason=out-of-scope plugin project path={fullPath}");
        }

        if (!_processedProjectPaths.Remove(fullPath))
        {
            return CreateIgnoredResult($"stage=unload sequence={sequence:D4} outcome=ignored reason=project not tracked path={fullPath}");
        }

        if (!_pluginIdByProjectPath.Remove(fullPath, out var pluginId))
        {
            pluginId = Path.GetFileNameWithoutExtension(fullPath);
        }

        _activePluginIds.Remove(pluginId);
        _failedPluginIds.Remove(pluginId);
        _runtimeProjectionsByPluginId.Remove(pluginId);
        var registryDiagnostics = PublishRuntimeRegistrySnapshot();

        return new PluginOnboardingResult(
            HostHealthy: true,
            EventAccepted: true,
            PluginActivated: false,
            PluginId: new PluginId(pluginId),
            ActivePluginIds: Snapshot(_activePluginIds),
            FailedPluginIds: Snapshot(_failedPluginIds),
            Diagnostics: [$"stage=unload sequence={sequence:D4} plugin={pluginId} outcome=success path={fullPath}", .. registryDiagnostics]);
    }

    private long NextDiscoverySequence()
    {
        _discoverySequence++;
        return _discoverySequence;
    }

    private PluginOnboardingResult CreateIgnoredResult(string diagnostic)
    {
        return new PluginOnboardingResult(
            HostHealthy: true,
            EventAccepted: false,
            PluginActivated: false,
            PluginId: null,
            ActivePluginIds: Snapshot(_activePluginIds),
            FailedPluginIds: Snapshot(_failedPluginIds),
            Diagnostics: [diagnostic]);
    }

    private bool IsUnderPluginsPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(_pluginsPath))
        {
            return false;
        }

        var pluginsRoot = _pluginsPath.EndsWith(Path.DirectorySeparatorChar)
            ? _pluginsPath
            : _pluginsPath + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(pluginsRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, _pluginsPath, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<PluginId> Snapshot(HashSet<string> source)
    {
        return source.OrderBy(x => x, StringComparer.Ordinal).Select(x => new PluginId(x)).ToArray();
    }

    private static bool IsInScopePluginProject(string fullPath)
    {
        var projectName = Path.GetFileNameWithoutExtension(fullPath);
        return projectName.StartsWith("Plugin.", StringComparison.OrdinalIgnoreCase);
    }

    private void UpsertRuntimeProjection(PluginDescriptor descriptor)
    {
        var projection = RuntimeRegistryPluginProjection.FromDescriptor(descriptor);
        if (projection is null)
        {
            _runtimeProjectionsByPluginId.Remove(descriptor.PluginId.Value);
            return;
        }

        _runtimeProjectionsByPluginId[descriptor.PluginId.Value] = projection;
    }

    private IReadOnlyList<string> PublishRuntimeRegistrySnapshot()
    {
        EnsureRuntimeRegistryInitialized();

        if (_runtimePluginRegistry is null)
        {
            return [];
        }

        var projections = _runtimeProjectionsByPluginId.Values
            .OrderBy(static projection => projection.PluginId.Value, StringComparer.Ordinal)
            .ToArray();

        var snapshotContracts = _baseRuntimeContracts.Concat<IPluginContract>(projections).ToArray();
        var snapshotCatalogs = _baseRuntimeCatalogs.Concat<IPluginOperationCatalog>(projections).ToArray();
        var currentPluginIds = snapshotContracts
            .Select(static contract => contract.PluginId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static pluginId => pluginId, StringComparer.Ordinal)
            .ToArray();

        _runtimePluginRegistry.Update(
            contracts: snapshotContracts,
            catalogs: snapshotCatalogs);

        var addedPluginIds = currentPluginIds.Except(_publishedRuntimePluginIds, StringComparer.Ordinal).ToArray();
        var removedPluginIds = _publishedRuntimePluginIds.Except(currentPluginIds, StringComparer.Ordinal).ToArray();

        _publishedRuntimePluginIds.Clear();
        foreach (var pluginId in currentPluginIds)
        {
            _publishedRuntimePluginIds.Add(pluginId);
        }

        if (addedPluginIds.Length == 0 && removedPluginIds.Length == 0)
        {
            return [];
        }

        return [$"stage=registry-update outcome=success added={string.Join(',', addedPluginIds)} removed={string.Join(',', removedPluginIds)} total={currentPluginIds.Length}"];
    }

    private void EnsureRuntimeRegistryInitialized()
    {
        if (_runtimeRegistryUnavailable)
        {
            return;
        }

        if (_runtimePluginRegistry is null && _serviceProvider is not null)
        {
            try
            {
                _runtimePluginRegistry = _serviceProvider.GetService<RuntimePluginRegistry>();
            }
            catch (InvalidOperationException)
            {
                _runtimeRegistryUnavailable = true;
                return;
            }
        }

        if (_baseRuntimeSnapshotCaptured || _runtimePluginRegistry is null)
        {
            return;
        }

        var snapshot = _runtimePluginRegistry.GetSnapshot();
        _baseRuntimeContracts = snapshot.Contracts.ToArray();
        _baseRuntimeCatalogs = snapshot.Catalogs.ToArray();
        _baseRuntimeSnapshotCaptured = true;
    }

    private sealed class RuntimeRegistryPluginProjection : IRuntimePluginDispatchTarget
    {
        private RuntimeRegistryPluginProjection(
            PluginId pluginId,
            ContractName contractName,
            Version contractVersion,
            IReadOnlyCollection<OperationName> supportedOperations,
            string? pluginTypeFullName,
            PluginServiceLifetime? serviceLifetime)
        {
            PluginId = pluginId;
            ContractName = contractName;
            ContractVersion = contractVersion;
            SupportedOperations = supportedOperations;
            PluginTypeFullName = pluginTypeFullName;
            ServiceLifetime = serviceLifetime;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

        public string? PluginTypeFullName { get; }

        public PluginServiceLifetime? ServiceLifetime { get; }

        public static RuntimeRegistryPluginProjection? FromDescriptor(PluginDescriptor descriptor)
        {
            var operations = (descriptor.DeclaredOperations ?? Array.Empty<OperationName>())
                .Where(static operation => !string.IsNullOrWhiteSpace(operation.Value))
                .DistinctBy(static operation => operation.Value, StringComparer.Ordinal)
                .OrderBy(static operation => operation.Value, StringComparer.Ordinal)
                .ToArray();

            if (operations.Length == 0)
            {
                return null;
            }

            return new RuntimeRegistryPluginProjection(
                pluginId: descriptor.PluginId,
                contractName: new ContractName(descriptor.AssemblyName),
                contractVersion: descriptor.Version,
                supportedOperations: operations,
                pluginTypeFullName: descriptor.RuntimePluginTypeFullName,
                serviceLifetime: descriptor.DeclaredServiceLifetime);
        }
    }
}