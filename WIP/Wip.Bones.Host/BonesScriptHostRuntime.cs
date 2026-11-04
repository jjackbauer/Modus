using Microsoft.Extensions.Logging;
using Wip.Bones.Engine;
using Wip.Sandbox.WorkerClient;

namespace Wip.Bones.Host;

/// <summary>
/// Owns the script-host execution mode for the Bones host.
/// <para>
/// InProcess (default, safe under conditional Gate G2): model-authored strategies compile and
/// load in the host process; the runtime logs a warning at startup and trusts the operator.
/// Worker (opt-in, trusted-only): starts the sandbox worker (or accepts a supplied client),
/// completes the handshake before returning, and owns the process lifetime so the container
/// disposes it on shutdown.
/// </para>
/// </summary>
public sealed class BonesScriptHostRuntime : IDisposable, IAsyncDisposable
{
    public const string LoggerCategory = "Wip.Bones.Host.BonesScriptHostRuntime";

    private readonly SandboxWorkerClient? _workerClient;
    private readonly bool _ownsWorkerClient;

    private BonesScriptHostRuntime(
        BonesStrategyScriptHost host,
        BonesScriptHostMode mode,
        SandboxWorkerClient? workerClient,
        bool ownsWorkerClient)
    {
        Host = host;
        Mode = mode;
        _workerClient = workerClient;
        _ownsWorkerClient = ownsWorkerClient;
    }

    public BonesStrategyScriptHost Host { get; }

    public BonesScriptHostMode Mode { get; }

    public SandboxWorkerClient? WorkerClient => _workerClient;

    public static BonesScriptHostRuntime Create(
        BonesStrategyScriptHostOptions options,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var logger = loggerFactory?.CreateLogger(LoggerCategory);

        if (options.Mode == BonesScriptHostMode.Worker)
        {
            // Validate before any process is started so a misconfiguration cannot leak a worker.
            if (!options.TrustedOnly)
            {
                throw new InvalidOperationException(
                    "Bones ScriptHost mode is Worker but TrustedOnly is false. Worker mode is trusted-only; " +
                    "set BonesHost:ScriptHost:TrustedOnly=true explicitly and run only trusted strategies.");
            }

            if (options.WorkerClient is { } suppliedClient)
            {
                logger?.LogInformation(
                    "Script execution mode is Worker: strategies compile and run in the supplied sandbox worker client.");
                return new BonesScriptHostRuntime(
                    new BonesStrategyScriptHost(options),
                    options.Mode,
                    suppliedClient,
                    ownsWorkerClient: false);
            }

            var workerPath = ResolveWorkerPath(options);
            if (!File.Exists(workerPath))
            {
                throw new InvalidOperationException(
                    $"Bones ScriptHost mode is Worker but the sandbox worker was not found at '{workerPath}'. " +
                    "Build WIP/Wip.Sandbox.Worker in the host configuration or set BonesHost:ScriptHost:WorkerPath.");
            }

            var client = SandboxWorkerClient.Start(workerPath, new SandboxWorkerOptions());
            try
            {
                client.HandshakeAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                SafeDispose(client);
                throw new InvalidOperationException(
                    $"Bones ScriptHost mode is Worker but the sandbox worker at '{workerPath}' did not complete the handshake: {exception.Message}",
                    exception);
            }

            TryLog(
                () => logger?.LogInformation(
                    "Script execution mode is Worker: strategies compile and run in the sandbox worker at {WorkerPath}.",
                    workerPath),
                $"Script execution mode is Worker: strategies compile and run in the sandbox worker at {workerPath}.");

            return new BonesScriptHostRuntime(
                new BonesStrategyScriptHost(options.WithWorkerClient(client)),
                options.Mode,
                client,
                ownsWorkerClient: true);
        }

        TryLog(
            () => logger?.LogWarning(
                "Script execution mode is InProcess: model-authored strategies compile and load in this host process. " +
                "This is trusted-only under conditional Gate G2; keep the default until the sandbox gate is fully " +
                "approved, then set BonesHost:ScriptHost:Mode=Worker."),
            "WARNING: Script execution mode is InProcess: model-authored strategies compile and load in this host " +
            "process. Trusted-only under conditional Gate G2; set BonesHost:ScriptHost:Mode=Worker after the " +
            "sandbox gate is fully approved.");

        return new BonesScriptHostRuntime(
            new BonesStrategyScriptHost(options),
            options.Mode,
            options.WorkerClient,
            ownsWorkerClient: false);
    }

    /// <summary>
    /// Startup logging is best-effort: a misconfigured sink (for example a Windows Event Log source the
    /// process may not write to) must never stop the host. The notice still reaches the console.
    /// </summary>
    private static void TryLog(Action log, string fallbackMessage)
    {
        try
        {
            log();
        }
        catch (AggregateException)
        {
            Console.Out.WriteLine(fallbackMessage);
        }
    }

    public static string ResolveWorkerPath(BonesStrategyScriptHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.WorkerPath))
        {
            return Path.Combine(AppContext.BaseDirectory, "Wip.Sandbox.Worker.dll");
        }

        return Path.IsPathRooted(options.WorkerPath)
            ? options.WorkerPath
            : Path.Combine(AppContext.BaseDirectory, options.WorkerPath);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        if (_ownsWorkerClient && _workerClient is not null)
        {
            return _workerClient.DisposeAsync();
        }

        return ValueTask.CompletedTask;
    }

    private static void SafeDispose(SandboxWorkerClient client)
    {
        try
        {
            client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort cleanup of a worker that never completed the handshake.
        }
    }
}