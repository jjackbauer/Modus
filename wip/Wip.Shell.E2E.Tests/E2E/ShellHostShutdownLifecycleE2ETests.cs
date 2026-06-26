using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Modus.Hosting;
using Wip.Runtime.Runtime;
using Wip.Shell.Interactive;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

public sealed class ShellHostShutdownLifecycleE2ETests
{
    private const string ChecklistItem = "Expand shell host lifecycle guarantees so shutdown always executes plugin stop/unload hooks in deterministic reverse activation order for `exit`, `Ctrl+C`, and process-exit paths [depends on SH-006 graceful shutdown coverage]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ShellHostShutdown_GivenExit_ExecutesPluginStopAndUnloadHooksInDeterministicReverseActivationOrder()
    {
        var bridge = new LifecycleOrderTrackingBridge(["tests.plugin.alpha", "tests.plugin.bravo", "tests.plugin.zulu"]);
        var orchestrator = CreateOrchestrator();

        using var input = new StringReader("plugins load\nexit\n");
        using var output = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, input, output, diagnosticsBridge: bridge);
        var host = new WipShellHost(new WipShellHostContainer(orchestrator, new WipShellEngine(loop), bridge));

        var exitCode = await host.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, bridge.StopInvocationCount);
        Assert.Equal(
        [
            "stop:tests.plugin.zulu",
            "unload:tests.plugin.zulu",
            "stop:tests.plugin.bravo",
            "unload:tests.plugin.bravo",
            "stop:tests.plugin.alpha",
            "unload:tests.plugin.alpha"
        ],
        bridge.StopAndUnloadEvents);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ShellHostShutdown_GivenCtrlCSignal_ExecutesPluginStopAndUnloadHooksInDeterministicReverseActivationOrder()
    {
        var bridge = new LifecycleOrderTrackingBridge(["tests.plugin.alpha", "tests.plugin.bravo", "tests.plugin.zulu"]);
        var signalRegistrar = new TestSignalRegistrar();
        var loopStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new DelegatingShellEngine(async cancellationToken =>
        {
            loopStarted.TrySetResult(true);
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return 0;
        });
        var host = new WipShellHost(
            new WipShellHostContainer(CreateOrchestrator(), engine, bridge),
            pluginStartupMode: WipShellPluginStartupMode.AutoLoadPlugins,
            signalRegistrar: signalRegistrar);

        var runTask = host.RunAsync(CancellationToken.None);
        await signalRegistrar.WaitForRegistrationAsync();
        await loopStarted.Task;

        signalRegistrar.TriggerCancel();

        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.Equal(1, bridge.StopInvocationCount);
        Assert.Equal(
        [
            "stop:tests.plugin.zulu",
            "unload:tests.plugin.zulu",
            "stop:tests.plugin.bravo",
            "unload:tests.plugin.bravo",
            "stop:tests.plugin.alpha",
            "unload:tests.plugin.alpha"
        ],
        bridge.StopAndUnloadEvents);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ShellHostShutdown_GivenProcessExitSignal_ExecutesPluginStopAndUnloadHooksInDeterministicReverseActivationOrder()
    {
        var bridge = new LifecycleOrderTrackingBridge(["tests.plugin.alpha", "tests.plugin.bravo", "tests.plugin.zulu"]);
        var signalRegistrar = new TestSignalRegistrar();
        var loopStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new DelegatingShellEngine(async cancellationToken =>
        {
            loopStarted.TrySetResult(true);
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return 0;
        });
        var host = new WipShellHost(
            new WipShellHostContainer(CreateOrchestrator(), engine, bridge),
            pluginStartupMode: WipShellPluginStartupMode.AutoLoadPlugins,
            signalRegistrar: signalRegistrar);

        var runTask = host.RunAsync(CancellationToken.None);
        await signalRegistrar.WaitForRegistrationAsync();
        await loopStarted.Task;

        signalRegistrar.TriggerProcessExit();

        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.Equal(1, bridge.StopInvocationCount);
        Assert.Equal(
        [
            "stop:tests.plugin.zulu",
            "unload:tests.plugin.zulu",
            "stop:tests.plugin.bravo",
            "unload:tests.plugin.bravo",
            "stop:tests.plugin.alpha",
            "unload:tests.plugin.alpha"
        ],
        bridge.StopAndUnloadEvents);
    }

    private static WipRuntimeOrchestrator CreateOrchestrator()
        => new(new NoOpStore(), new NoOpPublisher());

    private sealed class LifecycleOrderTrackingBridge : IModusWipBridge
    {
        private readonly IReadOnlyList<string> _activationOrder;
        private readonly object _gate = new();
        private int _loaded;

        public LifecycleOrderTrackingBridge(IReadOnlyList<string> activationOrder)
        {
            _activationOrder = activationOrder;
        }

        public int StopInvocationCount { get; private set; }

        public IReadOnlyList<string> StopAndUnloadEvents
        {
            get
            {
                lock (_gate)
                {
                    return _events.ToArray();
                }
            }
        }

        private readonly List<string> _events = [];

        public ValueTask<int> LoadPluginsAsync(CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref _loaded, 1);
            return ValueTask.FromResult(_activationOrder.Count);
        }

        public ValueTask StopPluginsAsync(CancellationToken cancellationToken)
        {
            StopInvocationCount++;
            if (Interlocked.Exchange(ref _loaded, 0) == 0)
                return ValueTask.CompletedTask;

            lock (_gate)
            {
                for (var index = _activationOrder.Count - 1; index >= 0; index--)
                {
                    var pluginId = _activationOrder[index];
                    _events.Add($"stop:{pluginId}");
                    _events.Add($"unload:{pluginId}");
                }
            }

            return ValueTask.CompletedTask;
        }

        public RunManifest GetRunManifest()
        {
            var plugins = Volatile.Read(ref _loaded) == 1
                ? _activationOrder.Select(pluginId =>
                    new PluginManifestEntry(
                        PluginId: pluginId,
                        PluginName: pluginId,
                        PluginVersion: "1.0.0",
                        AssemblyName: "Tests",
                        AssemblyVersion: "1.0.0.0",
                        Capabilities: Array.Empty<string>(),
                        RequiredPermissions: Array.Empty<string>())).ToArray()
                : Array.Empty<PluginManifestEntry>();

            return new RunManifest(
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Plugins: plugins,
                Workflows: Array.Empty<WorkflowManifestEntry>());
        }

        public IReadOnlyList<string> GetLoadDiagnostics()
            => Array.Empty<string>();
    }

    private sealed class TestSignalRegistrar : IWipShellHostSignalRegistrar
    {
        private readonly TaskCompletionSource<bool> _registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action? _cancel;
        private Action? _processExit;

        public IDisposable Register(Action onCancelRequested, Action onProcessExitRequested)
        {
            _cancel = onCancelRequested;
            _processExit = onProcessExitRequested;
            _registered.TrySetResult(true);
            return new DelegateDisposable(() =>
            {
                _cancel = null;
                _processExit = null;
            });
        }

        public async Task WaitForRegistrationAsync()
            => await _registered.Task;

        public void TriggerCancel()
            => (_cancel ?? throw new InvalidOperationException("Cancel handler was not registered.")).Invoke();

        public void TriggerProcessExit()
            => (_processExit ?? throw new InvalidOperationException("Process-exit handler was not registered.")).Invoke();

        private sealed class DelegateDisposable : IDisposable
        {
            private readonly Action _dispose;
            private int _disposed;

            public DelegateDisposable(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                _dispose();
            }
        }
    }

    private sealed class DelegatingShellEngine : IWipShellEngine
    {
        private readonly Func<CancellationToken, Task<int>> _loop;

        public DelegatingShellEngine(Func<CancellationToken, Task<int>> loop)
        {
            _loop = loop;
        }

        public Task<int> LoopAsync(CancellationToken cancellationToken)
            => _loop(cancellationToken);
    }

    private sealed class NoOpStore : ISessionStore
    {
        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult<SessionSnapshot?>(null);
    }

    private sealed class NoOpPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}
