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
    /// Execution mode. InProcess is the safe default while WP-1.3 is not wired;
    /// Worker is trusted-only and fails closed until the worker executor exists.
    /// </summary>
    public BonesScriptHostMode Mode { get; init; } = BonesScriptHostMode.InProcess;

    /// <summary>
    /// Required for Worker mode. No untrusted model-authored code may set this.
    /// </summary>
    public bool TrustedOnly { get; init; } = true;

    public SandboxWorkerClient? WorkerClient { get; init; }

    public TimeSpan CompilationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// When true, <see cref="BonesStrategyScriptHost.ExecuteChooseMoveDirect"/> runs each
    /// model-authored move on a dedicated background thread bounded by
    /// <see cref="ExecutionTimeout"/>. Defaults to false so the fast direct and benchmark
    /// paths keep calling the player slot inline.
    /// </summary>
    public bool EnableDirectExecutionTimeout { get; init; }

    public int MaxCompiledScripts { get; init; } = DefaultMaxCompiledScripts;
}