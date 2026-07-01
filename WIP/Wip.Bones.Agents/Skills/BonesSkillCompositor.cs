using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Skills;

/// <summary>
/// Composes multiple <see cref="ISkill"/> implementations into a single move-decision pipeline.
/// Skills are executed in priority order. The first skill that returns a non-null move wins.
/// If no skill produces a move, the compositor falls back to the first legal move.
/// </summary>
public sealed class BonesSkillCompositor
{
    private readonly IReadOnlyList<ISkill> _skills;

    /// <summary>
    /// Creates a compositor with the given skills executed in the provided order.
    /// </summary>
    public BonesSkillCompositor(IReadOnlyList<ISkill> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        _skills = skills;
    }

    /// <summary>
    /// The skills configured in this compositor, in execution order.
    /// </summary>
    public IReadOnlyList<ISkill> Skills => _skills;

    /// <summary>
    /// Compose a move by evaluating skills in priority order.
    /// Falls back to the first legal move when no skill produces a move.
    /// </summary>
    public async Task<BonesMove> ComposeMoveAsync(
        BonesRoundState state,
        IReadOnlyList<BonesMove> legalMoves,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(legalMoves);

        if (legalMoves.Count == 0)
            throw new InvalidOperationException("No legal moves are available for the active player.");

        foreach (var skill in _skills)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var move = await skill.EvaluateAsync(state, legalMoves, cancellationToken)
                .ConfigureAwait(false);

            if (move is not null)
                return move;
        }

        // Safety fallback: first legal move
        return legalMoves[0];
    }
}
