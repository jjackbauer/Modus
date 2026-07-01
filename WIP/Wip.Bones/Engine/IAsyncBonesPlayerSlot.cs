using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Engine;

public interface IAsyncBonesPlayerSlot
{
    ValueTask<BonesMove> ChooseMoveAsync(
        BonesRoundState state,
        IReadOnlyList<BonesMove> legalMoves,
        CancellationToken cancellationToken);
}
