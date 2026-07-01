using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Skills;

/// <summary>
/// Endgame-specific heuristics. When the current player is close to going out
/// (few tiles remaining), this skill prioritizes playing the tile that reduces
/// their hand to the smallest number of tiles, favoring doubles when available
/// (since doubles must be played with matching ends on both sides).
/// </summary>
public sealed class EndgameSkill : ISkill
{
    /// <summary>
    /// Players with fewer than this many tiles are considered "in the endgame".
    /// </summary>
    private const int EndgameTileThreshold = 3;

    public Task<BonesMove?> EvaluateAsync(
        BonesRoundState state,
        IReadOnlyList<BonesMove> legalMoves,
        CancellationToken cancellationToken)
    {
        var hand = state.Hands.GetHand(state.CurrentPlayer);

        // Only activate when the current player has few tiles remaining
        if (hand.Tiles.Length > EndgameTileThreshold)
            return Task.FromResult<BonesMove?>(null);

        // Best move: play a double if available (more restrictive, play it while you can)
        BonesMove? bestMove = null;
        var bestScore = -1;

        foreach (var move in legalMoves)
        {
            if (move.IsPass)
                continue;

            var tile = move.Tile!.Value;
            // Prioritize doubles, then highest pip count
            var score = (tile.IsDouble ? 100 : 0) + tile.TotalPips;

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
        }

        return Task.FromResult(bestMove);
    }
}
