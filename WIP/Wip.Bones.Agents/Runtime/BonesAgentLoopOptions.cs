using Wip.Abstractions.Agents;

namespace Wip.Bones.Agents.Runtime;

/// <summary>
/// Bones-side budget options for the shared runtime agent loop. The Ponder agent maps these onto
/// <see cref="AgentLoopOptions"/> for every model turn so retry/step/tool-failure budgets stay
/// explicit at the domain boundary.
/// </summary>
public sealed class BonesAgentLoopOptions
{
    public int MaxSteps { get; set; } = 3;

    public int? MaxTotalTokens { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    public int MaxConsecutiveToolFailures { get; set; } = 2;

    public AgentLoopOptions ToAgentLoopOptions()
        => new()
        {
            MaxSteps = MaxSteps,
            MaxTotalTokens = MaxTotalTokens,
            Timeout = Timeout,
            MaxConsecutiveToolFailures = MaxConsecutiveToolFailures,
        };
}
