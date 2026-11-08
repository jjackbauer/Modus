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

    /// <summary>
    /// When false (the default), the compile tool is advertised only on retry turns — the first
    /// model turn has no tools, so a live model cannot spend a round-trip validating a script that
    /// has not failed yet. Set true to advertise tools from the first turn (used by tests that
    /// exercise the tool path directly).
    /// </summary>
    public bool AdvertiseToolsOnFirstTurn { get; set; }

    public AgentLoopOptions ToAgentLoopOptions()
        => new()
        {
            MaxSteps = MaxSteps,
            MaxTotalTokens = MaxTotalTokens,
            Timeout = Timeout,
            MaxConsecutiveToolFailures = MaxConsecutiveToolFailures,
        };
}
