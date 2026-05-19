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
    private readonly AssemblyLifecycleHost _assemblyLifecycleHost = new();
    private readonly HashSet<string> _processedProjectPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activePluginIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedPluginIds = new(StringComparer.Ordinal);
    private long _discoverySequence;
    private string? _pluginsPath;
    private bool _started;

    public PluginWatcherStartResult Start(string pluginsPath)
    {
        if (string.IsNullOrWhiteSpace(pluginsPath))
        {
            return new PluginWatcherStartResult(
                HostHealthy: false,
                WatcherRegistered: false,
                PluginsPath: string.Empty,
                PluginsDirectoryExists: false,
                Diagnostics: ["stage=startup outcome=failure reason=plugins path missing"]);
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

        if (!pluginsDirectoryExists)
        {
            hostHealthy = false;
            diagnostics.Add($"stage=startup outcome=failure reason=plugins directory missing path={_pluginsPath}");
        }
        else
        {
            var scan = _loader.ScanRuntimeAssemblies(_pluginsPath);
            diagnostics.AddRange(scan.Diagnostics);

            if (scan.Descriptors.Count > 0)
            {
                var startupLoad = _runtime.Start(scan.Descriptors);
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
            }

            foreach (var existingProjectPath in Directory
                .EnumerateFiles(_pluginsPath, "*.csproj", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var onboarding = OnProjectCreated(existingProjectPath);
                diagnostics.AddRange(onboarding.Diagnostics);
            }
        }

        return new PluginWatcherStartResult(
            HostHealthy: hostHealthy,
            WatcherRegistered: true,
            PluginsPath: _pluginsPath,
            PluginsDirectoryExists: pluginsDirectoryExists,
            Diagnostics: diagnostics);
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
                PluginId: Path.GetFileNameWithoutExtension(fullPath),
                ActivePluginIds: Snapshot(_activePluginIds),
                FailedPluginIds: Snapshot(_failedPluginIds),
                Diagnostics: [.. discoveryDiagnostics, $"stage=descriptor outcome=failure reason={ex.Message}"]);
        }

        if (_activePluginIds.Contains(descriptor.PluginId))
        {
            return CreateIgnoredResult($"stage=discovery sequence={sequence:D4} outcome=ignored reason=plugin already active plugin={descriptor.PluginId}");
        }

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

        var activated = hostResult.ActivatedPluginIds.Contains(descriptor.PluginId, StringComparer.Ordinal);

        return new PluginOnboardingResult(
            HostHealthy: hostResult.Started,
            EventAccepted: true,
            PluginActivated: activated,
            PluginId: descriptor.PluginId,
            ActivePluginIds: Snapshot(_activePluginIds),
            FailedPluginIds: Snapshot(_failedPluginIds),
            Diagnostics: [.. discoveryDiagnostics, .. hostResult.Diagnostics]);
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

    private static IReadOnlyList<string> Snapshot(HashSet<string> source)
    {
        return source.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static bool IsInScopePluginProject(string fullPath)
    {
        var projectName = Path.GetFileNameWithoutExtension(fullPath);
        return projectName.StartsWith("Plugin.", StringComparison.OrdinalIgnoreCase);
    }
}