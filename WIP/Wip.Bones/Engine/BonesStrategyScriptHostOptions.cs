namespace Wip.Bones.Engine;

public sealed class BonesStrategyScriptHostOptions
{
    public const int DefaultMaxCompiledScripts = 256;

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