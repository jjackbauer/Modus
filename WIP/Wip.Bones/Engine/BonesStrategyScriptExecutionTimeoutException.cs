namespace Wip.Bones.Engine;

public sealed class BonesStrategyScriptExecutionTimeoutException : Exception
{
    public BonesStrategyScriptExecutionTimeoutException(TimeSpan timeout)
        : base($"Strategy script execution exceeded the configured timeout of {timeout.TotalMilliseconds:0} ms.")
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}