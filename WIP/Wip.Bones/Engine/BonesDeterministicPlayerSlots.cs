using Wip.Bones.Domain;

namespace Wip.Bones.Engine;

public sealed class BonesFirstLegalMovePlayerSlot : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(legalMoves);

        if (legalMoves.Count == 0)
            throw new InvalidOperationException("No legal moves are available for the active player.");

        return legalMoves[0];
    }
}