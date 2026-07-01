using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Skills;

/// <summary>
/// A composable heuristic that evaluates a game state and optionally produces a move.
/// Skills are executed in priority order by <see cref="BonesSkillCompositor"/>.
/// Return null when the skill cannot determine a move; the compositor will delegate
/// to the next skill.
/// </summary>
public interface ISkill
{
    /// <summary>
    /// Evaluate the game state and return a move if this skill can make a decision.
    /// Returns null when the skill defers to the next skill in the chain.
    /// </summary>
    Task<BonesMove?> EvaluateAsync(
        BonesRoundState state,
        IReadOnlyList<BonesMove> legalMoves,
        CancellationToken cancellationToken);
}
