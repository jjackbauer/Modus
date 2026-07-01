using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Delegation;

/// <summary>
/// Request sent to a sub-agent when the main strategy heuristic is uncertain.
/// Contains the game state, candidate moves, and reasoning context.
/// </summary>
public sealed record BonesDelegationRequest
{
    public BonesDelegationRequest(
        BonesRoundState gameState,
        IReadOnlyList<BonesMove> candidateMoves,
        string context)
    {
        ArgumentNullException.ThrowIfNull(gameState);
        ArgumentNullException.ThrowIfNull(candidateMoves);
        ArgumentNullException.ThrowIfNull(context);

        if (candidateMoves.Count == 0)
            throw new ArgumentException("At least one candidate move is required.", nameof(candidateMoves));

        GameState = gameState;
        CandidateMoves = candidateMoves;
        Context = context;
    }

    /// <summary>The current game state including board, hands, and event log.</summary>
    public BonesRoundState GameState { get; }

    /// <summary>The candidate moves the sub-agent should choose from.</summary>
    public IReadOnlyList<BonesMove> CandidateMoves { get; }

    /// <summary>
    /// Additional context explaining why delegation was triggered
    /// (e.g., "multiple candidates with similar heuristic scores").
    /// </summary>
    public string Context { get; }
}
