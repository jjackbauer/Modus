using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Engine;

public interface IBonesPlayerSlot
{
    BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves);
}