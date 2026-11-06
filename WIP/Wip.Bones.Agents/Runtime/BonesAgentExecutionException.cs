using Wip.Abstractions.Agents;

namespace Wip.Bones.Agents.Runtime;

/// <summary>
/// Domain exception raised when the shared runtime agent loop stops with a status other than
/// <see cref="AgentExecutionStatus.Completed"/>. It derives from <see cref="InvalidOperationException"/>
/// so existing domain call sites keep their failure contract while the stopping status and the
/// executed steps stay observable.
/// </summary>
public sealed class BonesAgentExecutionException : InvalidOperationException
{
    public BonesAgentExecutionException(
        AgentExecutionStatus status,
        IReadOnlyList<AgentStep> steps,
        string? error)
        : base(BuildMessage(status, error))
    {
        ArgumentNullException.ThrowIfNull(steps);

        Status = status;
        Steps = steps;
    }

    public AgentExecutionStatus Status { get; }

    public IReadOnlyList<AgentStep> Steps { get; }

    private static string BuildMessage(AgentExecutionStatus status, string? error)
    {
        if (status == AgentExecutionStatus.Failed)
        {
            return error ?? "agent execution failed";
        }

        return $"agent execution stopped with status {status}: {error}";
    }
}