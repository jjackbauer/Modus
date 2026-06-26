namespace Wip.ShellHost.Hosting;

public interface IWipShellHostSignalRegistrar
{
    IDisposable Register(Action onCancelRequested, Action onProcessExitRequested);
}

public sealed class DefaultWipShellHostSignalRegistrar : IWipShellHostSignalRegistrar
{
    public IDisposable Register(Action onCancelRequested, Action onProcessExitRequested)
    {
        ArgumentNullException.ThrowIfNull(onCancelRequested);
        ArgumentNullException.ThrowIfNull(onProcessExitRequested);

        ConsoleCancelEventHandler cancelHandler = (_, args) =>
        {
            args.Cancel = true;
            onCancelRequested();
        };

        EventHandler processExitHandler = (_, _) => onProcessExitRequested();

        Console.CancelKeyPress += cancelHandler;
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        return new SignalRegistration(cancelHandler, processExitHandler);
    }

    private sealed class SignalRegistration : IDisposable
    {
        private readonly ConsoleCancelEventHandler _cancelHandler;
        private readonly EventHandler _processExitHandler;
        private int _disposed;

        public SignalRegistration(ConsoleCancelEventHandler cancelHandler, EventHandler processExitHandler)
        {
            _cancelHandler = cancelHandler;
            _processExitHandler = processExitHandler;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Console.CancelKeyPress -= _cancelHandler;
            AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
        }
    }
}

public sealed class WipShellHost : IAsyncDisposable
{
    private readonly WipShellHostContainer _container;
    private readonly WipShellPluginStartupMode _pluginStartupMode;
    private readonly IWipShellHostSignalRegistrar _signalRegistrar;
    private int _runInProgress;
    private int _pluginStopInvoked;

    public WipShellHost(
        WipShellHostContainer container,
        WipShellPluginStartupMode pluginStartupMode = WipShellPluginStartupMode.ExplicitCommandOnly,
        IWipShellHostSignalRegistrar? signalRegistrar = null)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _pluginStartupMode = pluginStartupMode;
        _signalRegistrar = signalRegistrar ?? new DefaultWipShellHostSignalRegistrar();
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _runInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("Concurrent RunAsync invocations are not supported for the same host container lifetime.");
        }

        Volatile.Write(ref _pluginStopInvoked, 0);

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var signalRegistration = _signalRegistrar.Register(
            onCancelRequested: () => RequestShutdown(shutdown),
            onProcessExitRequested: () =>
            {
                RequestShutdown(shutdown);
                StopPluginsSynchronouslyForProcessExit();
            });

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
                await StopPluginsOnceAsync(CancellationToken.None);
            }
            finally
            {
                Volatile.Write(ref _runInProgress, 0);
            }
        }
    }

    private static void RequestShutdown(CancellationTokenSource shutdown)
    {
        try
        {
            shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void StopPluginsSynchronouslyForProcessExit()
    {
        try
        {
            StopPluginsOnceAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort shutdown only for process-exit path.
        }
    }

    private async ValueTask StopPluginsOnceAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _pluginStopInvoked, 1) != 0)
            return;

        await _container.Bridge.StopPluginsAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
        => _container.DisposeAsync();
}
