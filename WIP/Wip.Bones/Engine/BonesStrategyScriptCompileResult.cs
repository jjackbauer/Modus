namespace Wip.Bones.Engine;

public sealed class BonesStrategyScriptCompileResult
{
    private BonesStrategyScriptCompileResult(bool succeeded, BonesCompiledStrategy? compiled, string? failureReason)
    {
        Succeeded = succeeded;
        Compiled = compiled;
        FailureReason = failureReason;
    }

    public bool Succeeded { get; }

    public BonesCompiledStrategy? Compiled { get; }

    public string? FailureReason { get; }

    public static BonesStrategyScriptCompileResult Success(BonesCompiledStrategy compiled) =>
        new(succeeded: true, compiled, failureReason: null);

    public static BonesStrategyScriptCompileResult Failure(string failureReason) =>
        new(succeeded: false, compiled: null, failureReason);
}
