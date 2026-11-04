using Wip.Sandbox.WorkerClient;

namespace Wip.Bones.Engine;

public enum BonesScriptHostMode
{
    InProcess = 0,
    Worker = 1
}

public sealed class BonesStrategyScriptHostOptions
{
    public const int DefaultMaxCompiledScripts = 256;

    /// <summary>
    /// Execution mode. <see cref="BonesScriptHostMode.InProcess"/> is the safe default while
    /// Gate G2 is conditional (trusted-only): model-authored code would otherwise load in the
    /// host process. <see cref="BonesScriptHostMode.Worker"/> is trusted-only, opt-in, and fails
    /// closed unless a started worker client (or a worker path the host can launch) is supplied.
    /// </summary>
    public BonesScriptHostMode Mode { get; init; } = BonesScriptHostMode.InProcess;

    /// <summary>
    /// Required for Worker mode. No untrusted model-authored code may set this.
    /// </summary>
    public bool TrustedOnly { get; init; } = true;

    public SandboxWorkerClient? WorkerClient { get; init; }

    /// <summary>
    /// Worker executable path used by the host when <see cref="Mode"/> is Worker and no
    /// <see cref="WorkerClient"/> is supplied. Relative paths resolve against the host base
    /// directory; empty means <c>Wip.Sandbox.Worker.dll</c> next to the host.
    /// </summary>
    public string? WorkerPath { get; init; }

    /// <summary>
    /// Budget for compiling one strategy script. The first Roslyn compile in a process can take
    /// several seconds on a cold/slow disk, so this matches the host appsettings and the sandbox
    /// worker compile timeout (30 s) instead of a tight 10 s.
    /// </summary>
    public TimeSpan CompilationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// When true, <see cref="BonesStrategyScriptHost.ExecuteChooseMoveDirect"/> runs each
    /// model-authored move on a dedicated background thread bounded by
    /// <see cref="ExecutionTimeout"/>. Defaults to false so the fast direct and benchmark
    /// paths keep calling the player slot inline.
    /// </summary>
    public bool EnableDirectExecutionTimeout { get; init; }

    public int MaxCompiledScripts { get; init; } = DefaultMaxCompiledScripts;

    public BonesStrategyScriptHostOptions WithWorkerClient(SandboxWorkerClient workerClient)
    {
        ArgumentNullException.ThrowIfNull(workerClient);

        return new BonesStrategyScriptHostOptions
        {
            Mode = Mode,
            TrustedOnly = TrustedOnly,
            WorkerClient = workerClient,
            WorkerPath = WorkerPath,
            CompilationTimeout = CompilationTimeout,
            ExecutionTimeout = ExecutionTimeout,
            EnableDirectExecutionTimeout = EnableDirectExecutionTimeout,
            MaxCompiledScripts = MaxCompiledScripts,
        };
    }
}