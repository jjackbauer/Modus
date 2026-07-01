using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Skills;

/// <summary>
/// Evaluates each legal move based on the tile's total pip value.
/// Prefers higher-value tiles to maximize scoring potential.
/// </summary>
public sealed class TileEvaluationSkill : ISkill
{
    public Task<BonesMove?> EvaluateAsync(
        BonesRoundState state,
        IReadOnlyList<BonesMove> legalMoves,
        CancellationToken cancellationToken)
    {
        // Exclude pass moves — tile evaluation only applies to playable moves
        BonesMove? bestMove = null;
        var bestPips = -1;

        foreach (var move in legalMoves)
        {
            if (move.IsPass)
                continue;

            var pips = move.Tile!.Value.TotalPips;
            if (pips > bestPips)
            {
                bestPips = pips;
                bestMove = move;
            }
        }

        return Task.FromResult(bestMove);
    }
}
