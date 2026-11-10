namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// How the opt-in refinement path chooses the challenger. This is proposer-policy data: it is
/// evolved by the meta-loop like every other <c>ProposerGenome</c> field and never changes the
/// frozen promotion gate. <see cref="SessionDriven"/> is the default so a genome that does not
/// mention the field keeps the pre-refinement proposal path exactly.
/// </summary>
public enum RefinementStrategy
{
    /// <summary>Delegate to the supplied legacy session-driven proposer, without a signature edit.</summary>
    SessionDriven = 0,

    /// <summary>Turn the strongest usable failure signature into one bounded parameter edit.</summary>
    SignatureDriven = 1,
}