using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Skills;

/// <summary>
/// Identifies opponent scoring opportunities by checking whether any opponent
/// is close to going out (has few tiles remaining). When an opponent is on the
/// verge of winning, this skill attempts to block by playing a tile that
/// does not leave that opponent's pip open, when possible.
/// </summary>
public sealed class BlockingSkill : ISkill
{
    public Task<BonesMove?> EvaluateAsync(
        BonesRoundState state,
        IReadOnlyList<BonesMove> legalMoves,
        CancellationToken cancellationToken)
    {
        // Find the opponent with the fewest tiles (closest to winning)
        var currentPlayer = state.CurrentPlayer;
        var fewestTiles = int.MaxValue;
        BonesPlayerId? threatPlayer = null;

        foreach (var (playerId, hand) in state.Hands.HandsByPlayer)
        {
            if (playerId == currentPlayer)
                continue;

            if (hand.Tiles.Length < fewestTiles)
            {
                fewestTiles = hand.Tiles.Length;
                threatPlayer = playerId;
            }
        }

        // If no clear threat (all have same number of tiles) or threat has >3 tiles, skip
        if (threatPlayer is null || fewestTiles > 3)
            return Task.FromResult<BonesMove?>(null);

        // Build the set of pips the threat player can play
        var threatHand = state.Hands.GetHand(threatPlayer.Value);
        var threatPips = new HashSet<int>();
        foreach (var tile in threatHand.Tiles)
        {
            threatPips.Add(tile.LowPip.Value);
            threatPips.Add(tile.HighPip.Value);
        }

        // Try to find a playable move whose exposed pip is NOT in the threat set
        // Only applies when board is non-empty
        if (state.Board.IsEmpty)
            return Task.FromResult<BonesMove?>(null);

        var leftEnd = state.Board.LeftEnd!.Value.Pip;
        var rightEnd = state.Board.RightEnd!.Value.Pip;

        BonesMove? bestBlockingMove = null;
        var bestBlockingScore = 0; // higher = better block

        foreach (var move in legalMoves)
        {
            if (move.IsPass)
                continue;

            var tile = move.Tile!.Value;
            var matchingEnd = move.Side == BonesBoardSide.Left ? leftEnd : rightEnd;
            var exposedPip = tile.LowPip == matchingEnd ? tile.HighPip : tile.LowPip;

            var blocksThreat = !threatPips.Contains(exposedPip.Value);

            if (blocksThreat && tile.TotalPips > bestBlockingScore)
            {
                bestBlockingScore = tile.TotalPips;
                bestBlockingMove = move;
            }
        }

        return Task.FromResult(bestBlockingMove);
    }
}
