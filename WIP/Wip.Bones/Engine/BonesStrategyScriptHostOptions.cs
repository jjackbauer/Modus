namespace Wip.Bones.Engine;

public sealed class BonesStrategyScriptHostOptions
{
    public const int DefaultMaxCompiledScripts = 256;

    public TimeSpan CompilationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public int MaxCompiledScripts { get; init; } = DefaultMaxCompiledScripts;
}