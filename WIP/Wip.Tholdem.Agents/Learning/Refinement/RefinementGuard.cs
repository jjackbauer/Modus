using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Learning.Skills;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// Validates a refinement proposal's skill edits before the evaluator is spent: the genome must be
/// fully clamped, at most one skill parameter may change, and a skill edit that changes no decision
/// on the fixed probe session is refused. A proposal without a skill edit is passed through
/// unchanged to the next guard.
/// </summary>
public sealed class RefinementGuard : IProposalGuard
{
    /// <summary>A skill value is outside its documented domain.</summary>
    public const string OutOfDomainCode = "skill-out-of-domain";

    /// <summary>The proposal changes more than one skill parameter.</summary>
    public const string MultipleSkillEditsCode = "skill-multiple-edits";

    /// <summary>The skill edit changes no decision on the fixed probe session.</summary>
    public const string InertSkillCode = "skill-inert";

    /// <summary>The fixed probe session seed used to detect a behaviourally inert skill edit.</summary>
    public const ulong ProbeSeed = 20_261_114UL;

    /// <summary>The fixed probe session length used to detect a behaviourally inert skill edit.</summary>
    public const int ProbeHands = 400;

    private readonly IProposalGuard? _next;

    public RefinementGuard(IProposalGuard? next = null)
    {
        _next = next;
    }

    public bool IsAcceptable(StrategyGenome incumbent, StrategyGenome candidate, out string reason)
    {
        ArgumentNullException.ThrowIfNull(incumbent);
        ArgumentNullException.ThrowIfNull(candidate);

        var incumbentSkills = EffectiveSkills(incumbent);
        var candidateSkills = EffectiveSkills(candidate);

        // A proposal without any skill set on either side is a numeric proposal: unchanged.
        if (incumbent.Skills is null && candidate.Skills is null)
        {
            return Delegate(incumbent, candidate, out reason);
        }

        var skillShapeChanged = (incumbent.Skills is null) != (candidate.Skills is null);
        if (!skillShapeChanged && incumbentSkills.BehaviorEquals(candidateSkills))
        {
            // The skill values are identical: the edit belongs to the numeric surface.
            return Delegate(incumbent, candidate, out reason);
        }

        if (!IsFullyClamped(candidate))
        {
            reason = OutOfDomainCode;
            return false;
        }

        if (incumbentSkills.BehavioralChangeCount(candidateSkills) > 1)
        {
            reason = MultipleSkillEditsCode;
            return false;
        }

        if (!IsBehaviorallyActive(incumbent, candidate))
        {
            reason = InertSkillCode;
            return false;
        }

        return Delegate(incumbent, candidate, out reason);
    }

    private static StrategySkillSet EffectiveSkills(StrategyGenome genome) =>
        genome.Skills ?? StrategySkillSet.FromGenome(genome);

    private static bool IsFullyClamped(StrategyGenome genome) =>
        InRange(genome.RaiseThreshold, 0.0, 1.0)
        && InRange(genome.CallThreshold, 0.0, 1.0)
        && InRange(genome.BluffFrequency, 0.0, 1.0)
        && InRange(
            genome.SizingMultiplier,
            StrategyGenome.MinimumSizingMultiplier,
            StrategyGenome.MaximumSizingMultiplier)
        && (genome.Skills is null || genome.Skills.IsWithinDomain());

    private static bool InRange(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;

    private static bool IsBehaviorallyActive(StrategyGenome incumbent, StrategyGenome candidate) =>
        !string.Equals(
            ProbeManifestHash(incumbent),
            ProbeManifestHash(candidate),
            StringComparison.Ordinal);

    private static string ProbeManifestHash(StrategyGenome genome) =>
        MatchSimulator.Run(
            new StrategyAgent(genome, "probe"),
            new TightAggressiveAgent(name: "probe-baseline"),
            new SimulationOptions(ProbeSeed, ProbeHands)).ManifestHash;

    private bool Delegate(StrategyGenome incumbent, StrategyGenome candidate, out string reason)
    {
        if (_next is null)
        {
            reason = TrainingLoop.ProposalAcceptedReason;
            return true;
        }

        return _next.IsAcceptable(incumbent, candidate, out reason);
    }
}