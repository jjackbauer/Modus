using Modus.Core.Plugins;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Workflows;
using Wip.Builder;
using Wip.Modus.Hosting;
using Wip.Shell.Interactive;
using Wip.ShellHost.Hosting;
using Wip.Abstractions.Sessions;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.ShellHost.Tests.Hosting;

public sealed class WipShellHostPluginDiagnosticsTests : IDisposable
{
    private readonly List<string> _tempRoots = [];

    [Fact]
    public async Task LoadPluginsAsync_GivenAssemblyActivationAndLifecycleStartFailures_RecordsStageScopedDiagnostics()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        WriteBrokenAssemblyFile(pluginsPath, "broken-stage-assembly.dll");

        var bridge = new ModusWipBridge(pluginsPath);

        await bridge.LoadPluginsAsync(CancellationToken.None);

        var diagnostics = bridge.GetLoadDiagnostics();

        Assert.Contains(diagnostics, static line =>
            line.Contains("[assembly-load]", StringComparison.Ordinal)
            && line.Contains("broken-stage-assembly.dll", StringComparison.Ordinal));

        Assert.Contains(diagnostics, static line =>
            line.Contains("[activation]", StringComparison.Ordinal)
            && line.Contains(typeof(ActivationFailurePlugin).FullName!, StringComparison.Ordinal));

        Assert.Contains(diagnostics, static line =>
            line.Contains("[lifecycle-start]", StringComparison.Ordinal)
            && line.Contains("tests.lifecycle-start.failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadPluginsAsync_GivenPureModusPluginType_ExpectedPluginRejectedByWipOwnedInterfaceFilter()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        var bridge = new ModusWipBridge(pluginsPath);

        await bridge.LoadPluginsAsync(CancellationToken.None);

        var pluginIds = bridge.GetRunManifest().Plugins.Select(static plugin => plugin.PluginId).ToArray();
        Assert.DoesNotContain("tests.plain.modus", pluginIds, StringComparer.Ordinal);
    }

    [Fact]
    public async Task LoadPluginsAsync_GivenWipOwnedPluginType_ExpectedPluginAcceptedAndManifested()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        var bridge = new ModusWipBridge(pluginsPath);

        await bridge.LoadPluginsAsync(CancellationToken.None);

        var pluginIds = bridge.GetRunManifest().Plugins.Select(static plugin => plugin.PluginId).ToArray();
        Assert.Contains("tests.manifest.alpha", pluginIds, StringComparer.Ordinal);
    }

    [Fact]
    public async Task LoadPluginsAsync_GivenMixedAssembly_ExpectedOnlyWipOwnedPluginsContributeToLoadCount()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        var bridge = new ModusWipBridge(pluginsPath);

        var loadedCount = await bridge.LoadPluginsAsync(CancellationToken.None);
        var pluginIds = bridge.GetRunManifest().Plugins.Select(static plugin => plugin.PluginId).ToArray();

        Assert.Equal(pluginIds.Length, loadedCount);
        Assert.DoesNotContain("tests.plain.modus", pluginIds, StringComparer.Ordinal);
        Assert.Contains("tests.manifest.alpha", pluginIds, StringComparer.Ordinal);
        Assert.Contains("tests.manifest.zulu", pluginIds, StringComparer.Ordinal);
    }

    [Fact]
    public async Task PluginsCommand_GivenCapturedRuntimeFailures_RendersBridgeDiagnosticsInCommandOutput()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        WriteBrokenAssemblyFile(pluginsPath, "broken-command-assembly.dll");

        var options = new WipShellHostOptions(
            PluginsPath: pluginsPath,
            EffectiveConfig: new WipShellHostEffectiveConfig(
                SourceFile: "(tests)",
                PluginsPath: pluginsPath,
                WorkspaceRoot: pluginsPath,
                PolicyId: "test",
                ValidationCommands: ["dotnet test"],
                PluginStartupMode: WipShellPluginStartupMode.AutoLoadPlugins));

        using var input = new StringReader("plugins\nexit\n");
        using var output = new StringWriter();
        await using var host = WipShellHostFactory.CreateDefault(options, input, output);

        var exitCode = await host.RunAsync(CancellationToken.None);
        var shellOutput = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("Plugin diagnostics:", shellOutput, StringComparison.Ordinal);
        Assert.Contains("[assembly-load]", shellOutput, StringComparison.Ordinal);
        Assert.Contains("broken-command-assembly.dll", shellOutput, StringComparison.Ordinal);
        Assert.Contains("[activation]", shellOutput, StringComparison.Ordinal);
        Assert.Contains(typeof(ActivationFailurePlugin).FullName!, shellOutput, StringComparison.Ordinal);
        Assert.Contains("[lifecycle-start]", shellOutput, StringComparison.Ordinal);
        Assert.Contains("tests.lifecycle-start.failure", shellOutput, StringComparison.Ordinal);

        Assert.Contains("Loaded plugins:", shellOutput, StringComparison.Ordinal);
        Assert.Contains("tests.manifest.alpha [ManifestMetadataAlphaPlugin] v2.0.0", shellOutput, StringComparison.Ordinal);
        Assert.Contains("capabilities: cap.alpha", shellOutput, StringComparison.Ordinal);
        Assert.Contains("permissions: RegisterOperation", shellOutput, StringComparison.Ordinal);
        Assert.Contains("tests.manifest.zulu [ManifestMetadataZuluPlugin] v1.2.3", shellOutput, StringComparison.Ordinal);
        Assert.Contains("capabilities: cap.alpha, cap.zulu", shellOutput, StringComparison.Ordinal);
        Assert.Contains("permissions: RegisterOperation, RegisterSchedules", shellOutput, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", shellOutput);
    }

    [Fact]
    public async Task RunAsync_GivenExplicitCommandOnlyStartupMode_ExpectedPromptWithoutImplicitPluginLoad()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();

        var options = new WipShellHostOptions(
            PluginsPath: pluginsPath,
            EffectiveConfig: new WipShellHostEffectiveConfig(
                SourceFile: "(tests)",
                PluginsPath: pluginsPath,
                WorkspaceRoot: pluginsPath,
                PolicyId: "test",
                ValidationCommands: ["dotnet test"],
                PluginStartupMode: WipShellPluginStartupMode.ExplicitCommandOnly));

        using var input = new StringReader("plugins\nexit\n");
        using var output = new StringWriter();
        await using var host = WipShellHostFactory.CreateDefault(options, input, output);

        var exitCode = await host.RunAsync(CancellationToken.None);
        var shellOutput = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("wip> ", shellOutput, StringComparison.Ordinal);
        Assert.Contains("No plugins are currently loaded.", shellOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded plugins:", shellOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("tests.manifest.alpha", shellOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("tests.manifest.zulu", shellOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PluginsLoadCommand_GivenPluginsNotLoaded_ExpectedBridgeLoadInvokedAndManifestPopulated()
    {
        var bridge = new LifecycleTrackingBridge();

        using var input = new StringReader("plugins\nplugins load\nplugins\nexit\n");
        using var output = new StringWriter();
        var loop = new WipShellCommandLoop(CreateOrchestrator(), input, output, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var rendered = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, bridge.LoadCallCount);
        Assert.Contains("No plugins are currently loaded.", rendered, StringComparison.Ordinal);
        Assert.Contains("Plugins loaded: 1.", rendered, StringComparison.Ordinal);
        Assert.Contains("Loaded plugins:", rendered, StringComparison.Ordinal);
        Assert.Contains("tests.lifecycle.command [LifecycleCommandPlugin] v1.0.0", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PluginsLoadCommand_GivenPluginsAlreadyLoaded_ExpectedIdempotentResponseWithoutDuplicateActivation()
    {
        var bridge = new LifecycleTrackingBridge();

        using var input = new StringReader("plugins load\nplugins load\nexit\n");
        using var output = new StringWriter();
        var loop = new WipShellCommandLoop(CreateOrchestrator(), input, output, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var rendered = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, bridge.LoadCallCount);
        Assert.Contains("Plugins loaded: 1.", rendered, StringComparison.Ordinal);
        Assert.Contains("Plugins are already loaded for this host container lifetime.", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PluginsUnloadCommand_GivenLoadedPlugins_ExpectedStopInvokedAndManifestReturnsToEmptyState()
    {
        var bridge = new LifecycleTrackingBridge();

        using var input = new StringReader("plugins load\nplugins unload\nplugins\nexit\n");
        using var output = new StringWriter();
        var loop = new WipShellCommandLoop(CreateOrchestrator(), input, output, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var rendered = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, bridge.LoadCallCount);
        Assert.Equal(1, bridge.StopCallCount);
        Assert.Contains("Plugins unloaded.", rendered, StringComparison.Ordinal);
        Assert.Contains("No plugins are currently loaded.", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("tests.lifecycle.command [LifecycleCommandPlugin] v1.0.0", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PluginsLoadCommand_GivenLoadThenUnloadThenSecondLoad_ExpectedSecondLoadRejectedForHostContainerLifetime()
    {
        var bridge = new LifecycleTrackingBridge();

        using var input = new StringReader("plugins load\nplugins unload\nplugins load\nplugins\nexit\n");
        using var output = new StringWriter();
        var loop = new WipShellCommandLoop(CreateOrchestrator(), input, output, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var rendered = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, bridge.LoadCallCount);
        Assert.Equal(1, bridge.StopCallCount);
        Assert.Contains("Plugins loaded: 1.", rendered, StringComparison.Ordinal);
        Assert.Contains("Plugins unloaded.", rendered, StringComparison.Ordinal);
        Assert.Contains("Plugins are already loaded for this host container lifetime.", rendered, StringComparison.Ordinal);
        Assert.Contains("No plugins are currently loaded.", rendered, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(rendered, "Plugins loaded: 1."));
    }

    [Fact]
    public async Task IdlePrompt_GivenNoExplicitLoad_ExpectedZeroRecurringPluginOutputsWithinBoundedWindow()
    {
        using var output = new StringWriter();
        using var input = new DelayedSequenceTextReader([
            (TimeSpan.FromMilliseconds(250), "exit")
        ]);
        var bridge = new RecurringOutputBridge(output, "[scheduled-noise]");
        var loop = new WipShellCommandLoop(CreateOrchestrator(), input, output, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var rendered = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(0, bridge.LoadCallCount);
        Assert.Equal(0, bridge.StopCallCount);
        Assert.DoesNotContain("[scheduled-noise]", rendered, StringComparison.Ordinal);
        Assert.Contains("wip> ", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdlePrompt_GivenExplicitLoad_ExpectedRecurringOutputsAppearOnlyAfterLoadCommand()
    {
        using var output = new StringWriter();
        using var input = new DelayedSequenceTextReader([
            (TimeSpan.Zero, "plugins load"),
            (TimeSpan.FromMilliseconds(220), "plugins unload"),
            (TimeSpan.Zero, "exit")
        ]);
        var bridge = new RecurringOutputBridge(output, "[scheduled-noise]");
        var loop = new WipShellCommandLoop(CreateOrchestrator(), input, output, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var rendered = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, bridge.LoadCallCount);
        Assert.Equal(1, bridge.StopCallCount);
        Assert.Contains("Plugins loaded: 1.", rendered, StringComparison.Ordinal);
        Assert.Contains("Plugins unloaded.", rendered, StringComparison.Ordinal);
        Assert.Contains("[scheduled-noise]", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRunManifest_GivenSuccessfulPluginLoad_ExpectedManifestContainsRuntimeIdentityVersionCapabilitiesAndPermissions()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        var bridge = new ModusWipBridge(pluginsPath);

        await bridge.LoadPluginsAsync(CancellationToken.None);

        var manifestPlugins = bridge
            .GetRunManifest()
            .Plugins
            .Where(static plugin => plugin.PluginId.StartsWith("tests.manifest.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, manifestPlugins.Length);

        var alpha = Assert.Single(manifestPlugins, static plugin => plugin.PluginId == "tests.manifest.alpha");
        Assert.Equal("ManifestMetadataAlphaPlugin", alpha.PluginName);
        Assert.Equal("2.0.0", alpha.PluginVersion);
        Assert.NotEmpty(alpha.AssemblyName);
        Assert.NotEmpty(alpha.AssemblyVersion);
        Assert.Equal(new[] { "cap.alpha" }, alpha.Capabilities);
        Assert.Equal(new[] { "RegisterOperation" }, alpha.RequiredPermissions);

        var zulu = Assert.Single(manifestPlugins, static plugin => plugin.PluginId == "tests.manifest.zulu");
        Assert.Equal("ManifestMetadataZuluPlugin", zulu.PluginName);
        Assert.Equal("1.2.3", zulu.PluginVersion);
        Assert.NotEmpty(zulu.AssemblyName);
        Assert.NotEmpty(zulu.AssemblyVersion);
        Assert.Equal(new[] { "cap.alpha", "cap.zulu" }, zulu.Capabilities);
        Assert.Equal(new[] { "RegisterOperation", "RegisterSchedules" }, zulu.RequiredPermissions);
    }

    [Fact]
    public async Task GetRunManifest_GivenSuccessfulPluginLoad_ExpectedEntryPublishedForEverySuccessfullyLoadedPlugin()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        var bridge = new ModusWipBridge(pluginsPath);

        var loadedCount = await bridge.LoadPluginsAsync(CancellationToken.None);
        var manifestPlugins = bridge.GetRunManifest().Plugins;

        Assert.Equal(loadedCount, manifestPlugins.Count);

        foreach (var plugin in manifestPlugins)
        {
            Assert.False(string.IsNullOrWhiteSpace(plugin.PluginId));
            Assert.False(string.IsNullOrWhiteSpace(plugin.PluginName));
            Assert.False(string.IsNullOrWhiteSpace(plugin.PluginVersion));
            Assert.False(string.IsNullOrWhiteSpace(plugin.AssemblyName));
            Assert.False(string.IsNullOrWhiteSpace(plugin.AssemblyVersion));

            Assert.Equal(
                plugin.Capabilities.Distinct(StringComparer.Ordinal).OrderBy(static item => item, StringComparer.Ordinal),
                plugin.Capabilities);
            Assert.Equal(
                plugin.RequiredPermissions.Distinct(StringComparer.Ordinal).OrderBy(static item => item, StringComparer.Ordinal),
                plugin.RequiredPermissions);
        }
    }

    [Fact]
    public async Task GetRunManifest_GivenEquivalentPluginSetAcrossRuns_ExpectedDeterministicOrderingAndFieldValues()
    {
        var firstPluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        var secondPluginsPath = CreatePluginsFolderWithCurrentTestAssembly();

        var firstBridge = new ModusWipBridge(firstPluginsPath);
        var secondBridge = new ModusWipBridge(secondPluginsPath);

        await firstBridge.LoadPluginsAsync(CancellationToken.None);
        await secondBridge.LoadPluginsAsync(CancellationToken.None);

        var firstManifest = firstBridge
            .GetRunManifest()
            .Plugins
            .Where(static plugin => plugin.PluginId.StartsWith("tests.manifest.", StringComparison.Ordinal))
            .ToArray();
        var secondManifest = secondBridge
            .GetRunManifest()
            .Plugins
            .Where(static plugin => plugin.PluginId.StartsWith("tests.manifest.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(new[] { "tests.manifest.alpha", "tests.manifest.zulu" }, firstManifest.Select(static plugin => plugin.PluginId).ToArray());
        Assert.Equal(new[] { "tests.manifest.alpha", "tests.manifest.zulu" }, secondManifest.Select(static plugin => plugin.PluginId).ToArray());

        var firstProjection = firstManifest
            .Select(static plugin =>
                $"{plugin.PluginId}|{plugin.PluginName}|{plugin.PluginVersion}|{plugin.AssemblyName}|{plugin.AssemblyVersion}|{string.Join(',', plugin.Capabilities)}|{string.Join(',', plugin.RequiredPermissions)}")
            .ToArray();
        var secondProjection = secondManifest
            .Select(static plugin =>
                $"{plugin.PluginId}|{plugin.PluginName}|{plugin.PluginVersion}|{plugin.AssemblyName}|{plugin.AssemblyVersion}|{string.Join(',', plugin.Capabilities)}|{string.Join(',', plugin.RequiredPermissions)}")
            .ToArray();

        Assert.Equal(firstProjection, secondProjection);
    }

    [Fact]
    public async Task PluginsCommand_GivenPluginStdoutNoiseDuringLoad_ExpectedDiagnosticsBlockRemainsStructuredAndNonInterleaved()
    {
        var bridge = new DeterministicBridge(
            manifest: new RunManifest(
                DateTimeOffset.Parse("2026-05-24T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                Plugins:
                [
                    new PluginManifestEntry(
                        PluginId: "tests.manifest.zulu",
                        PluginName: "ManifestMetadataZuluPlugin",
                        PluginVersion: "1.2.3",
                        AssemblyName: "Tests.Zulu",
                        AssemblyVersion: "1.0.0.0",
                        Capabilities: ["cap.zulu", "cap.alpha"],
                        RequiredPermissions: ["RegisterSchedules", "RegisterOperation"]),
                    new PluginManifestEntry(
                        PluginId: "tests.manifest.alpha",
                        PluginName: "ManifestMetadataAlphaPlugin",
                        PluginVersion: "2.0.0",
                        AssemblyName: "Tests.Alpha",
                        AssemblyVersion: "1.0.0.0",
                        Capabilities: ["cap.alpha"],
                        RequiredPermissions: ["RegisterOperation"]),
                ],
                Workflows: Array.Empty<WorkflowManifestEntry>()),
            diagnostics: ["[lifecycle-start] z-line", "[activation] a-line"]);

        using var input = new StringReader("plugins\nexit\n");
        using var output = new NoiseInjectingWriter("[plugin-noise]");
        var loop = new WipShellCommandLoop(CreateOrchestrator(), input, output, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var rendered = output.ToString();

        Assert.Equal(0, exitCode);
        var manifestIndex = rendered.IndexOf("Loaded plugins:", StringComparison.Ordinal);
        var diagnosticsHeaderIndex = rendered.IndexOf("Plugin diagnostics:", StringComparison.Ordinal);
        Assert.True(manifestIndex >= 0);
        Assert.True(diagnosticsHeaderIndex > manifestIndex);

        var pluginBlock = rendered.Substring(manifestIndex, diagnosticsHeaderIndex - manifestIndex);
        Assert.DoesNotContain("[plugin-noise]", pluginBlock, StringComparison.Ordinal);

        Assert.Contains("- tests.manifest.alpha [ManifestMetadataAlphaPlugin] v2.0.0", rendered, StringComparison.Ordinal);
        Assert.Contains("- tests.manifest.zulu [ManifestMetadataZuluPlugin] v1.2.3", rendered, StringComparison.Ordinal);
        Assert.True(
            rendered.IndexOf("- tests.manifest.alpha [ManifestMetadataAlphaPlugin] v2.0.0", StringComparison.Ordinal)
            < rendered.IndexOf("- tests.manifest.zulu [ManifestMetadataZuluPlugin] v1.2.3", StringComparison.Ordinal));

        Assert.True(
            rendered.IndexOf("- [activation] a-line", StringComparison.Ordinal)
            < rendered.IndexOf("- [lifecycle-start] z-line", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkflowsCommand_GivenConcurrentPluginLogging_ExpectedWorkflowDiagnosticsRemainDeterministic()
    {
        var bridge = new DeterministicBridge(
            manifest: new RunManifest(
                DateTimeOffset.Parse("2026-05-24T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                Plugins: Array.Empty<PluginManifestEntry>(),
                Workflows:
                [
                    new WorkflowManifestEntry("workflow.zulu", "Zulu Workflow", "Req.Zulu", "Res.Zulu"),
                    new WorkflowManifestEntry("workflow.alpha", "Alpha Workflow", "Req.Alpha", "Res.Alpha"),
                ]),
            diagnostics: Array.Empty<string>());

        using var input = new StringReader("workflows\nexit\n");
        using var output = new NoiseInjectingWriter("[plugin-noise]");
        var loop = new WipShellCommandLoop(CreateOrchestrator(), input, output, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var rendered = output.ToString();

        Assert.Equal(0, exitCode);
        var headerIndex = rendered.IndexOf("Registered workflows:", StringComparison.Ordinal);
        var firstWorkflowIndex = rendered.IndexOf("- workflow.alpha [Alpha Workflow]", StringComparison.Ordinal);
        var secondWorkflowIndex = rendered.IndexOf("- workflow.zulu [Zulu Workflow]", StringComparison.Ordinal);

        Assert.True(headerIndex >= 0);
        Assert.True(firstWorkflowIndex > headerIndex);
        Assert.True(secondWorkflowIndex > firstWorkflowIndex);

        var workflowBlock = rendered.Substring(headerIndex, secondWorkflowIndex - headerIndex);
        Assert.DoesNotContain("[plugin-noise]", workflowBlock, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkflowsCommand_GivenExplicitLoadAndUnload_ExpectedWorkflowDiagnosticsTrackBridgeRuntimeState()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        var bridge = new ModusWipBridge(pluginsPath, CreateWorkflowRegistrations());

        using var input = new StringReader("workflows\nplugins load\nworkflows\nplugins unload\nworkflows\nexit\n");
        using var output = new StringWriter();
        var loop = new WipShellCommandLoop(CreateOrchestrator(), input, output, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var rendered = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(2, CountOccurrences(rendered, "No workflows are currently registered."));
        Assert.Contains("Registered workflows:", rendered, StringComparison.Ordinal);
        Assert.Contains("workflow.runtime.alpha [Runtime Alpha Workflow]", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DebugLogsCommand_GivenModusBridgeEmitsDebugEvents_ExpectedHostReturnsRunCorrelatedDebugEntries()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        WriteBrokenAssemblyFile(pluginsPath, "broken-debug-assembly.dll");

        var options = new WipShellHostOptions(
            PluginsPath: pluginsPath,
            EffectiveConfig: new WipShellHostEffectiveConfig(
                SourceFile: "(tests)",
                PluginsPath: pluginsPath,
                WorkspaceRoot: pluginsPath,
                PolicyId: "test",
                ValidationCommands: ["dotnet test"],
                PluginStartupMode: WipShellPluginStartupMode.AutoLoadPlugins));

        using var input = new StringReader("debug-logs\nexit\n");
        using var output = new StringWriter();
        await using var host = WipShellHostFactory.CreateDefault(options, input, output);

        var exitCode = await host.RunAsync(CancellationToken.None);
        var shellOutput = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("Host run correlation:", shellOutput, StringComparison.Ordinal);
        Assert.Contains("Modus debug logs:", shellOutput, StringComparison.Ordinal);
        Assert.Contains("[debug] plugin-load:assembly-load :: Failed to load assembly", shellOutput, StringComparison.Ordinal);
        Assert.Contains("broken-debug-assembly.dll", shellOutput, StringComparison.Ordinal);
        Assert.Contains("(run=", shellOutput, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", shellOutput);
    }

    [Fact]
    public async Task DebugLogsCommand_GivenNoDebugEvents_ExpectedDeterministicEmptyDebugStateMessage()
    {
        var bridge = new DeterministicDebugBridge(
            manifest: new RunManifest(
                DateTimeOffset.Parse("2026-05-24T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                Plugins: Array.Empty<PluginManifestEntry>(),
                Workflows: Array.Empty<WorkflowManifestEntry>()),
            diagnostics: Array.Empty<string>(),
            debugSnapshot: new ModusDebugLogSnapshot(
                RunCorrelationId: "run-empty-debug",
                RunStartedAtUtc: DateTimeOffset.Parse("2026-05-24T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                Entries: Array.Empty<ModusDebugLogEntry>()));

        using var input = new StringReader("debug-logs\nexit\n");
        using var output = new StringWriter();
        var loop = new WipShellCommandLoop(CreateOrchestrator(), input, output, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var rendered = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("Host run correlation: run-empty-debug", rendered, StringComparison.Ordinal);
        Assert.Contains("No Modus debug logs were emitted for the current host run.", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRunManifest_GivenStopAfterSuccessfulLoad_ExpectedPluginsAndWorkflowsClearedFromRuntimeSnapshot()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        var bridge = new ModusWipBridge(pluginsPath, CreateWorkflowRegistrations());

        var loadedCount = await bridge.LoadPluginsAsync(CancellationToken.None);
        await bridge.StopPluginsAsync(CancellationToken.None);
        var manifestAfterStop = bridge.GetRunManifest();

        Assert.True(loadedCount > 0);
        Assert.Empty(manifestAfterStop.Plugins);
        Assert.Empty(manifestAfterStop.Workflows);
    }

    [Fact]
    public async Task DebugLogsCommand_GivenPluginLoadFailure_ExpectedFailureDiagnosticAndDebugEntryShareSameCorrelation()
    {
        var pluginsPath = CreatePluginsFolderWithCurrentTestAssembly();
        WriteBrokenAssemblyFile(pluginsPath, "broken-correlation-assembly.dll");

        var options = new WipShellHostOptions(
            PluginsPath: pluginsPath,
            EffectiveConfig: new WipShellHostEffectiveConfig(
                SourceFile: "(tests)",
                PluginsPath: pluginsPath,
                WorkspaceRoot: pluginsPath,
                PolicyId: "test",
                ValidationCommands: ["dotnet test"],
                PluginStartupMode: WipShellPluginStartupMode.AutoLoadPlugins));

        using var input = new StringReader("plugins\ndebug-logs\nexit\n");
        using var output = new StringWriter();
        await using var host = WipShellHostFactory.CreateDefault(options, input, output);

        var exitCode = await host.RunAsync(CancellationToken.None);
        var shellOutput = output.ToString();

        Assert.Equal(0, exitCode);

        var runCorrelation = ExtractLineValue(shellOutput, "Host run correlation: ");
        Assert.False(string.IsNullOrWhiteSpace(runCorrelation));
        Assert.Contains($"[run:{runCorrelation}]", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"(run={runCorrelation})", shellOutput, StringComparison.Ordinal);
        Assert.Contains("broken-correlation-assembly.dll", shellOutput, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", shellOutput);
    }

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private string CreatePluginsFolderWithCurrentTestAssembly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-shellhost-diagnostics-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");

        Directory.CreateDirectory(pluginsPath);
        _tempRoots.Add(root);

        var sourceAssemblyPath = typeof(WipShellHostPluginDiagnosticsTests).Assembly.Location;
        var copiedAssemblyPath = Path.Combine(pluginsPath, Path.GetFileName(sourceAssemblyPath));
        File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);

        return pluginsPath;
    }

    private static void WriteBrokenAssemblyFile(string pluginsPath, string fileName)
    {
        var brokenAssemblyPath = Path.Combine(pluginsPath, fileName);
        File.WriteAllText(brokenAssemblyPath, "not a managed assembly");
    }

    private static WipRuntimeOrchestrator CreateOrchestrator()
        => new(new NoOpStore(), new NoOpPublisher());

    private static IReadOnlyList<WorkflowRegistration> CreateWorkflowRegistrations()
        =>
        [
            new WorkflowRegistration(
                new WorkflowId("workflow.runtime.alpha"),
                typeof(RuntimeAlphaWorkflow),
                typeof(RuntimeAlphaRequest),
                typeof(RuntimeAlphaResult),
                new WorkflowDescriptor<RuntimeAlphaRequest, RuntimeAlphaResult>(
                    new WorkflowId("workflow.runtime.alpha"),
                    "Runtime Alpha Workflow"))
        ];

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class DeterministicBridge : IModusWipBridge
    {
        private readonly RunManifest _manifest;
        private readonly IReadOnlyList<string> _diagnostics;

        public DeterministicBridge(RunManifest manifest, IReadOnlyList<string> diagnostics)
        {
            _manifest = manifest;
            _diagnostics = diagnostics;
        }

        public ValueTask<int> LoadPluginsAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(0);

        public ValueTask StopPluginsAsync(CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public RunManifest GetRunManifest() => _manifest;

        public IReadOnlyList<string> GetLoadDiagnostics() => _diagnostics;
    }

    private sealed class LifecycleTrackingBridge : IModusWipBridge
    {
        private static readonly PluginManifestEntry[] LoadedPlugins =
        [
            new PluginManifestEntry(
                PluginId: "tests.lifecycle.command",
                PluginName: "LifecycleCommandPlugin",
                PluginVersion: "1.0.0",
                AssemblyName: "Tests.Lifecycle",
                AssemblyVersion: "1.0.0.0",
                Capabilities: ["cap.lifecycle"],
                RequiredPermissions: ["RegisterSchedules"]),
        ];

        private int _loaded;

        public int LoadCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public ValueTask<int> LoadPluginsAsync(CancellationToken cancellationToken)
        {
            LoadCallCount++;
            if (Interlocked.CompareExchange(ref _loaded, 1, 0) != 0)
                return ValueTask.FromResult(0);

            return ValueTask.FromResult(LoadedPlugins.Length);
        }

        public ValueTask StopPluginsAsync(CancellationToken cancellationToken)
        {
            StopCallCount++;
            Interlocked.Exchange(ref _loaded, 0);
            return ValueTask.CompletedTask;
        }

        public RunManifest GetRunManifest()
            => new(
                CapturedAtUtc: DateTimeOffset.Parse("2026-05-24T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                Plugins: Volatile.Read(ref _loaded) == 1 ? LoadedPlugins : Array.Empty<PluginManifestEntry>(),
                Workflows: Array.Empty<WorkflowManifestEntry>());

        public IReadOnlyList<string> GetLoadDiagnostics()
            => Array.Empty<string>();
    }

    private sealed class DeterministicDebugBridge : IModusWipBridge, IModusWipDebugChannel
    {
        private readonly RunManifest _manifest;
        private readonly IReadOnlyList<string> _diagnostics;
        private ModusDebugLogSnapshot _debugSnapshot;

        public DeterministicDebugBridge(
            RunManifest manifest,
            IReadOnlyList<string> diagnostics,
            ModusDebugLogSnapshot debugSnapshot)
        {
            _manifest = manifest;
            _diagnostics = diagnostics;
            _debugSnapshot = debugSnapshot;
        }

        public ValueTask<int> LoadPluginsAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(0);

        public ValueTask StopPluginsAsync(CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public RunManifest GetRunManifest() => _manifest;

        public IReadOnlyList<string> GetLoadDiagnostics() => _diagnostics;

        public void BeginHostRun(string runCorrelationId, DateTimeOffset runStartedAtUtc)
        {
            _debugSnapshot = _debugSnapshot with
            {
                RunCorrelationId = runCorrelationId,
                RunStartedAtUtc = runStartedAtUtc,
            };
        }

        public ModusDebugLogSnapshot GetDebugLogSnapshot() => _debugSnapshot;
    }

    private sealed class RecurringOutputBridge : IModusWipBridge
    {
        private static readonly PluginManifestEntry[] LoadedPlugins =
        [
            new PluginManifestEntry(
                PluginId: "tests.idle.scheduled",
                PluginName: "ScheduledNoisePlugin",
                PluginVersion: "1.0.0",
                AssemblyName: "Tests.ScheduledNoise",
                AssemblyVersion: "1.0.0.0",
                Capabilities: ["cap.scheduled-noise"],
                RequiredPermissions: ["RegisterSchedules"]),
        ];

        private readonly TextWriter _hostOutput;
        private readonly string _noiseToken;
        private int _loaded;
        private CancellationTokenSource? _emissionCts;
        private Task? _emissionTask;

        public RecurringOutputBridge(TextWriter hostOutput, string noiseToken)
        {
            _hostOutput = hostOutput;
            _noiseToken = noiseToken;
        }

        public int LoadCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public ValueTask<int> LoadPluginsAsync(CancellationToken cancellationToken)
        {
            LoadCallCount++;
            if (Interlocked.CompareExchange(ref _loaded, 1, 0) != 0)
                return ValueTask.FromResult(0);

            _emissionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _emissionTask = EmitRecurringOutputAsync(_emissionCts.Token);
            return ValueTask.FromResult(LoadedPlugins.Length);
        }

        public async ValueTask StopPluginsAsync(CancellationToken cancellationToken)
        {
            StopCallCount++;
            if (Interlocked.Exchange(ref _loaded, 0) == 0)
                return;

            var emissionCts = Interlocked.Exchange(ref _emissionCts, null);
            var emissionTask = Interlocked.Exchange(ref _emissionTask, null);

            if (emissionCts is not null)
            {
                emissionCts.Cancel();
                emissionCts.Dispose();
            }

            if (emissionTask is not null)
            {
                try
                {
                    await emissionTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        public RunManifest GetRunManifest()
            => new(
                CapturedAtUtc: DateTimeOffset.Parse("2026-05-24T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                Plugins: Volatile.Read(ref _loaded) == 1 ? LoadedPlugins : Array.Empty<PluginManifestEntry>(),
                Workflows: Array.Empty<WorkflowManifestEntry>());

        public IReadOnlyList<string> GetLoadDiagnostics()
            => Array.Empty<string>();

        private async Task EmitRecurringOutputAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _hostOutput.WriteLineAsync(_noiseToken);
                await _hostOutput.FlushAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(35), cancellationToken);
            }
        }
    }

    private sealed class DelayedSequenceTextReader : TextReader
    {
        private readonly Queue<(TimeSpan Delay, string? Line)> _steps;

        public DelayedSequenceTextReader(IReadOnlyList<(TimeSpan Delay, string? Line)> steps)
        {
            _steps = new Queue<(TimeSpan Delay, string? Line)>(steps);
        }

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            if (_steps.Count == 0)
                return null;

            var (delay, line) = _steps.Dequeue();
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            return line;
        }
    }

    private sealed class NoOpStore : ISessionStore
    {
        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask<SessionSnapshot?> LoadAsync(Wip.Abstractions.Identifiers.SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult<SessionSnapshot?>(null);
    }

    private sealed class NoOpPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class NoiseInjectingWriter : StringWriter
    {
        private readonly string _noiseToken;

        public NoiseInjectingWriter(string noiseToken)
        {
            _noiseToken = noiseToken;
        }

        public override Task WriteAsync(string? value)
        {
            base.WriteLine(_noiseToken);
            return base.WriteAsync(value);
        }
    }

    private static string ExtractLineValue(string content, string prefix)
    {
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            var prefixIndex = line.IndexOf(prefix, StringComparison.Ordinal);
            if (prefixIndex >= 0)
                return line[(prefixIndex + prefix.Length)..].Trim();
        }

        throw new Xunit.Sdk.XunitException($"Expected a line with prefix '{prefix}' in shell output.");
    }

    public sealed class ActivationFailurePlugin : IWipHostPluginContract
    {
        public ActivationFailurePlugin()
        {
            throw new InvalidOperationException("Activation exploded.");
        }

        public PluginId PluginId => new("tests.activation.failure");

        public ContractName ContractName => new("Tests.ActivationFailure");

        public Version ContractVersion => new(1, 0, 0);
    }

    public sealed class LifecycleStartFailurePlugin : IWipHostPluginContract, IPluginLifecycle
    {
        public PluginId PluginId => new("tests.lifecycle-start.failure");

        public ContractName ContractName => new("Tests.LifecycleStartFailure");

        public Version ContractVersion => new(1, 0, 0);

        public void Load(PluginLoadContext context)
        {
        }

        public void Start(PluginStartContext context)
        {
            throw new InvalidOperationException("Lifecycle start exploded.");
        }

        public void Stop(PluginStopContext context)
        {
        }

        public void Unload(PluginUnloadContext context)
        {
        }
    }

    public sealed class ManifestMetadataZuluPlugin : IWipHostPluginContract, IPluginOperationCatalog, IPluginRegistrationPolicy
    {
        public PluginId PluginId => new("tests.manifest.zulu");

        public ContractName ContractName => new("Tests.ManifestMetadataZulu");

        public Version ContractVersion => new(1, 2, 3);

        public IReadOnlyCollection<OperationName> SupportedOperations =>
            new[]
            {
                new OperationName("cap.zulu"),
                new OperationName("cap.alpha"),
                new OperationName("cap.zulu"),
            };

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
            =>
            [
                new PluginRegistrationStep(0, PluginRegistrationStepKind.RegisterSchedules, "schedule-zulu"),
                new PluginRegistrationStep(1, PluginRegistrationStepKind.RegisterOperation, "operation-zulu"),
                new PluginRegistrationStep(2, PluginRegistrationStepKind.RegisterSchedules, "schedule-zulu-dup"),
            ];
    }

    public sealed class ManifestMetadataAlphaPlugin : IWipHostPluginContract, IPluginOperationCatalog, IPluginRegistrationPolicy
    {
        public PluginId PluginId => new("tests.manifest.alpha");

        public ContractName ContractName => new("Tests.ManifestMetadataAlpha");

        public Version ContractVersion => new(2, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations =>
            new[]
            {
                new OperationName("cap.alpha"),
            };

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
            =>
            [
                new PluginRegistrationStep(0, PluginRegistrationStepKind.RegisterOperation, "operation-alpha"),
            ];
    }

    public sealed class PlainModusOnlyPlugin : IPluginContract
    {
        public PluginId PluginId => new("tests.plain.modus");

        public ContractName ContractName => new("Tests.PlainModusOnly");

        public Version ContractVersion => new(1, 0, 0);
    }

    private sealed class RuntimeAlphaWorkflow;

    private sealed record RuntimeAlphaRequest(string Value);

    private sealed record RuntimeAlphaResult(string Value);
}
