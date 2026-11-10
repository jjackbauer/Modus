namespace Wip.Tholdem.Agents.Learning;

/// <summary>Produces a challenger genome from the incumbent and the measured session.</summary>
public interface IStrategyProposer
{
    StrategyGenome Propose(StrategyGenome incumbent, SessionAnalysis analysis, int iteration);
}

/// <summary>
/// A data-only proposer: it moves exactly one genome parameter by the policy's step, choosing the
/// parameter and the direction from the proposer genome (and, for the shipped session-driven policy,
/// from the measured session and the iteration parity), then clamps the result into the genome
/// domain. It generates no code; the prompt template is copied as text after literal placeholder
/// substitution. When the primary move is absorbed by clamping the proposer moves a threshold by the
/// same step so a non-inert challenger always reaches the gate.
/// </summary>
public sealed class DeterministicStrategyProposer : IStrategyProposer
{
    /// <summary>The shipped step; kept as a constant so the pre-meta call sites stay explicit.</summary>
    public const double Step = 0.02;

    public DeterministicStrategyProposer(ProposerGenome? genome = null)
    {
        Genome = (genome ?? ProposerGenome.Default).Clamp();
    }

    /// <summary>The proposer policy this instance applies.</summary>
    public ProposerGenome Genome { get; }

    public StrategyGenome Propose(StrategyGenome incumbent, SessionAnalysis analysis, int iteration)
    {
        ArgumentNullException.ThrowIfNull(incumbent);
        ArgumentNullException.ThrowIfNull(analysis);

        var evenIteration = (iteration & 1) == 0;
        var profitable = analysis.BbPer100 >= 0.0;
        var aggressive = analysis.AggressionFactor >= 1.0;
        var step = Genome.MutationStep;

        StrategyGenome candidate = Genome.ParameterSelectionPolicy switch
        {
            ParameterSelectionPolicy.Cyclic => ProposeCyclic(incumbent, iteration, profitable, aggressive, step),
            ParameterSelectionPolicy.AggressiveFirst => ProposeAggressive(
                incumbent,
                evenIteration,
                aggressive,
                step),
            _ => ProposeSessionDriven(incumbent, evenIteration, profitable, aggressive, step),
        };

        candidate = candidate with
        {
            PromptText = PromptTemplateRenderer.Render(
                Genome.PromptTemplate,
                candidate,
                step,
                iteration),
        };

        return candidate.BehaviorEquals(incumbent) ? FlipThreshold(incumbent, step) : candidate;
    }

    private static StrategyGenome ProposeSessionDriven(
        StrategyGenome incumbent,
        bool evenIteration,
        bool profitable,
        bool aggressive,
        double step)
    {
        if (!profitable)
        {
            // A losing session keeps the same behaviour but trades selectivity for price.
            return evenIteration
                ? Nudge(incumbent, GenomeParameter.CallThreshold, -step)
                : Nudge(incumbent, GenomeParameter.RaiseThreshold, -step);
        }

        // A winning session presses the edge: more value sizing or more bluffs, depending on how
        // aggressive the measured session already was.
        return evenIteration
            ? Nudge(incumbent, GenomeParameter.BluffFrequency, aggressive ? step : -step)
            : Nudge(incumbent, GenomeParameter.SizingMultiplier, aggressive ? step : -step);
    }

    /// <summary>
    /// A fixed four-parameter cycle. Threshold direction follows the session (tighten when the
    /// measured session wins, loosen when it loses); sizing and bluff direction follows aggression.
    /// </summary>
    private static StrategyGenome ProposeCyclic(
        StrategyGenome incumbent,
        int iteration,
        bool profitable,
        bool aggressive,
        double step)
    {
        var parameter = (GenomeParameter)(((iteration - 1) % 4 + 4) % 4);
        var direction = parameter is GenomeParameter.RaiseThreshold or GenomeParameter.CallThreshold
            ? profitable ? step : -step
            : aggressive ? step : -step;
        return Nudge(incumbent, parameter, direction);
    }

    private static StrategyGenome ProposeAggressive(
        StrategyGenome incumbent,
        bool evenIteration,
        bool aggressive,
        double step)
    {
        var parameter = evenIteration ? GenomeParameter.BluffFrequency : GenomeParameter.SizingMultiplier;
        return Nudge(incumbent, parameter, aggressive ? step : -step);
    }

    private static StrategyGenome Nudge(StrategyGenome genome, GenomeParameter parameter, double delta) =>
        (parameter switch
        {
            GenomeParameter.RaiseThreshold => genome with { RaiseThreshold = genome.RaiseThreshold + delta },
            GenomeParameter.CallThreshold => genome with { CallThreshold = genome.CallThreshold + delta },
            GenomeParameter.BluffFrequency => genome with { BluffFrequency = genome.BluffFrequency + delta },
            _ => genome with { SizingMultiplier = genome.SizingMultiplier + delta },
        }).Clamp();

    /// <summary>
    /// Moves one threshold by the policy step in the direction that stays inside [0, 1]. The caller
    /// only reaches this path when the primary move was behaviourally absorbed by clamping, so at
    /// least one threshold has a free direction in practice; the call threshold is the fallback.
    /// </summary>
    private static StrategyGenome FlipThreshold(StrategyGenome genome, double step)
    {
        var raise = genome.RaiseThreshold <= 1.0 - step
            ? genome.RaiseThreshold + step
            : genome.RaiseThreshold - step;
        var raised = (genome with { RaiseThreshold = raise }).Clamp();
        if (!raised.BehaviorEquals(genome))
        {
            return raised;
        }

        var call = genome.CallThreshold <= 1.0 - step
            ? genome.CallThreshold + step
            : genome.CallThreshold - step;
        return (genome with { CallThreshold = call }).Clamp();
    }

    private enum GenomeParameter
    {
        RaiseThreshold,
        CallThreshold,
        BluffFrequency,
        SizingMultiplier,
    }
}
