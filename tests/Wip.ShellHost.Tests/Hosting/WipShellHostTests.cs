using Wip.ShellHost.Hosting;
using Xunit;
using Wip.Abstractions.Sessions;
using Wip.Modus.Hosting;
using Wip.Runtime.Runtime;
using Wip.Shell.Interactive;

namespace Wip.ShellHost.Tests.Hosting;

public sealed class WipShellHostTests
{
    [Fact]
    public async Task RunAsync_GivenDefaultStartup_ExpectedPromptReadyWithoutPluginLoadInvocation()
    {
        var bridge = new TrackingBridge();
        var loopInvocationCount = 0;
        var engine = new DelegatingShellEngine(_ =>
        {
            loopInvocationCount++;
            return Task.FromResult(0);
        });
        var host = new WipShellHost(new WipShellHostContainer(CreateOrchestrator(), engine, bridge));

        var exitCode = await host.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, loopInvocationCount);
        Assert.Equal(0, bridge.LoadCount);
        Assert.Equal(1, bridge.StopCount);
    }

    [Fact]
    public async Task RunAsync_GivenOptInStartupAutoLoadEnabled_ExpectedLoadPluginsInvokedBeforePrompt()
    {
        var bridge = new TrackingBridge();
        var observedLoadCountDuringLoop = -1;
        var engine = new DelegatingShellEngine(_ =>
        {
            observedLoadCountDuringLoop = bridge.LoadCount;
            return Task.FromResult(0);
        });
        var host = new WipShellHost(
            new WipShellHostContainer(CreateOrchestrator(), engine, bridge),
            pluginStartupMode: WipShellPluginStartupMode.AutoLoadPlugins);

        var exitCode = await host.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, observedLoadCountDuringLoop);
        Assert.Equal(1, bridge.LoadCount);
        Assert.Equal(1, bridge.StopCount);
    }

    [Fact]
    public async Task RunAsync_GivenAutoLoadAcrossMultipleInvocations_ExpectedLoadPluginsInvokedOncePerHostContainerLifetime()
    {
        var bridge = new TrackingBridge();
        var engine = new DelegatingShellEngine(_ => Task.FromResult(0));
        var host = new WipShellHost(
            new WipShellHostContainer(CreateOrchestrator(), engine, bridge),
            pluginStartupMode: WipShellPluginStartupMode.AutoLoadPlugins);

        var firstExit = await host.RunAsync(CancellationToken.None);
        var secondExit = await host.RunAsync(CancellationToken.None);

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        Assert.Equal(1, bridge.LoadCount);
        Assert.Equal(2, bridge.StopCount);
    }

    [Fact]
    public async Task RunAsync_GivenMultipleInvocations_ExpectedNoImplicitPluginLoadAcrossHostContainerLifetime()
    {
        var bridge = new TrackingBridge();
        var engine = new DelegatingShellEngine(_ => Task.FromResult(0));
        var host = new WipShellHost(new WipShellHostContainer(CreateOrchestrator(), engine, bridge));

        var firstExit = await host.RunAsync(CancellationToken.None);
        var secondExit = await host.RunAsync(CancellationToken.None);

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        Assert.Equal(0, bridge.LoadCount);
        Assert.Equal(2, bridge.StopCount);
    }

    [Fact]
    public async Task RunAsync_GivenCancellation_ExpectedStopPluginsInvokedExactlyOncePerRun()
    {
        var bridge = new TrackingBridge();
        var engine = new DelegatingShellEngine(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        });
        var host = new WipShellHost(new WipShellHostContainer(CreateOrchestrator(), engine, bridge));

        var exitCode = await host.RunAsync(new CancellationToken(canceled: true));

        Assert.Equal(0, exitCode);
        Assert.Equal(0, bridge.LoadCount);
        Assert.Equal(1, bridge.StopCount);
    }

    [Fact]
    public async Task RunAsync_GivenExplicitLoadThenCancellation_ExpectedStopPluginsInvokedExactlyOnce()
    {
        var bridge = new TrackingBridge();
        var orchestrator = CreateOrchestrator();
        using var input = new DelayedSequenceTextReader([
            (TimeSpan.Zero, "plugins load"),
            (TimeSpan.FromSeconds(5), null)
        ]);
        using var output = new StringWriter();
        var engine = new WipShellEngine(new WipShellCommandLoop(orchestrator, input, output, bridge));
        var host = new WipShellHost(new WipShellHostContainer(orchestrator, engine, bridge));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var exitCode = await host.RunAsync(cancellation.Token);
        var rendered = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, bridge.LoadCount);
        Assert.Equal(1, bridge.StopCount);
        Assert.Contains("Plugins loaded: 1.", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_GivenConcurrentInvocations_ExpectedSingleActiveRunPerHostContainerLifetime()
    {
        var bridge = new TrackingBridge();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new DelegatingShellEngine(async _ =>
        {
            started.TrySetResult();
            await release.Task;
            return 0;
        });
        var host = new WipShellHost(new WipShellHostContainer(CreateOrchestrator(), engine, bridge));

        var firstRun = host.RunAsync(CancellationToken.None);
        await started.Task;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(CancellationToken.None));
        Assert.Contains("Concurrent RunAsync invocations are not supported", error.Message, StringComparison.Ordinal);

        release.TrySetResult();
        var firstExit = await firstRun;

        Assert.Equal(0, firstExit);
        Assert.Equal(0, bridge.LoadCount);
        Assert.Equal(1, bridge.StopCount);
    }

    private sealed class TrackingBridge : IModusWipBridge
    {
        public int LoadCount { get; private set; }

        public int StopCount { get; private set; }

        public ValueTask<int> LoadPluginsAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return ValueTask.FromResult(LoadCount);
        }

        public ValueTask StopPluginsAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        public RunManifest GetRunManifest()
            => new(DateTimeOffset.UtcNow, Array.Empty<PluginManifestEntry>(), Array.Empty<WorkflowManifestEntry>());

        public IReadOnlyList<string> GetLoadDiagnostics()
            => Array.Empty<string>();
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

    private static WipRuntimeOrchestrator CreateOrchestrator()
        => new(new NoOpStore(), new NoOpPublisher());

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
}
