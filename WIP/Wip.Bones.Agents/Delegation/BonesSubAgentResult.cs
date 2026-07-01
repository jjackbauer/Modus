using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Delegation;

/// <summary>
/// Result returned by a sub-agent after analyzing a delegation request.
/// </summary>
public sealed record BonesSubAgentResult
{
    public BonesSubAgentResult(
        BonesMove chosenMove,
        string reasoning,
        TimeSpan latency)
    {
        ArgumentNullException.ThrowIfNull(chosenMove);
        ArgumentNullException.ThrowIfNull(reasoning);

        ChosenMove = chosenMove;
        Reasoning = reasoning;
        Latency = latency;
    }

    /// <summary>The move the sub-agent selected from the candidates.</summary>
    public BonesMove ChosenMove { get; }

    /// <summary>Explanation of why this move was chosen.</summary>
    public string Reasoning { get; }

    /// <summary>How long the sub-agent took to decide.</summary>
    public TimeSpan Latency { get; }
}
