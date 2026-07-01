using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Skills;

/// <summary>
/// Counter-adapts based on opponent patterns by analyzing the event log.
/// When an opponent recently played a tile, this skill recognizes
/// that the opponent likely still holds tiles with matching pips,
/// and avoids leaving open ends matching those pips when possible.
///
/// This is a simplified opponent model — a full implementation would
/// track observed tiles per opponent over multiple rounds.
/// </summary>
public sealed class OpponentModelSkill : ISkill
{
    public Task<BonesMove?> EvaluateAsync(
        BonesRoundState state,
        IReadOnlyList<BonesMove> legalMoves,
        CancellationToken cancellationToken)
    {
        if (state.Board.IsEmpty)
            return Task.FromResult<BonesMove?>(null);

        // Collect pips from the last few events as "observed opponent patterns"
        var opponentPatternPips = new HashSet<int>();
        var currentPlayer = state.CurrentPlayer;

        // Look at recent events by other players to infer their hand composition
        var recentCount = 0;
        for (var i = state.EventLog.Length - 1; i >= 0 && recentCount < 4; i--)
        {
            var evt = state.EventLog[i];
            if (evt.PlayerId == currentPlayer || evt.Tile is null)
                continue;

            // When an opponent plays a tile, the pips on it are likely in their hand
            opponentPatternPips.Add(evt.Tile.Value.LowPip.Value);
            opponentPatternPips.Add(evt.Tile.Value.HighPip.Value);
            recentCount++;
        }

        if (opponentPatternPips.Count == 0)
            return Task.FromResult<BonesMove?>(null);

        var leftEnd = state.Board.LeftEnd!.Value.Pip;
        var rightEnd = state.Board.RightEnd!.Value.Pip;

        // Prefer moves whose exposed pip is NOT in the opponent pattern set
        BonesMove? bestMove = null;
        var bestScore = -1;

        foreach (var move in legalMoves)
        {
            if (move.IsPass)
                continue;

            var tile = move.Tile!.Value;
            var matchingEnd = move.Side == BonesBoardSide.Left ? leftEnd : rightEnd;
            var exposedPip = tile.LowPip == matchingEnd ? tile.HighPip : tile.LowPip;

            var avoidsOpponentPattern = !opponentPatternPips.Contains(exposedPip.Value);
            var score = (avoidsOpponentPattern ? 50 : 0) + tile.TotalPips;

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
        }

        return Task.FromResult(bestMove);
    }
}
