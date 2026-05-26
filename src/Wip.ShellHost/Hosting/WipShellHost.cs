namespace Wip.ShellHost.Hosting;

public sealed class WipShellHost : IAsyncDisposable
{
    private readonly WipShellHostContainer _container;
    private readonly WipShellPluginStartupMode _pluginStartupMode;
    private int _runInProgress;

    public WipShellHost(
        WipShellHostContainer container,
        WipShellPluginStartupMode pluginStartupMode = WipShellPluginStartupMode.ExplicitCommandOnly)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _pluginStartupMode = pluginStartupMode;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _runInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("Concurrent RunAsync invocations are not supported for the same host container lifetime.");
        }

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        ConsoleCancelEventHandler cancelHandler = (_, args) =>
        {
            args.Cancel = true;
            shutdown.Cancel();
        };

        EventHandler processExitHandler = (_, _) =>
        {
            try
            {
                shutdown.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        };

        Console.CancelKeyPress += cancelHandler;
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        try
        {
            if (_container.Bridge is Wip.Modus.Hosting.IModusWipDebugChannel debugChannel)
            {
                debugChannel.BeginHostRun(
                    runCorrelationId: Guid.NewGuid().ToString("N"),
                    runStartedAtUtc: DateTimeOffset.UtcNow);
            }

            if (_pluginStartupMode == WipShellPluginStartupMode.AutoLoadPlugins
                && _container.PluginLifetimeGate.TryReserveLoad())
            {
                await _container.Bridge.LoadPluginsAsync(shutdown.Token);
            }

            return await _container.ShellEngine.LoopAsync(shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            try
            {
                Console.CancelKeyPress -= cancelHandler;
                AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
                await _container.Bridge.StopPluginsAsync(CancellationToken.None);
            }
            finally
            {
                Volatile.Write(ref _runInProgress, 0);
            }
        }
    }

    public ValueTask DisposeAsync()
        => _container.DisposeAsync();
}
