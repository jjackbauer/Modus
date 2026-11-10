using Wip.Tholdem.Agents.Learning.Skills;

namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// Turns the strongest usable failure signature into one bounded parameter edit. When no signal has
/// enough samples it delegates to the legacy session-driven proposer, so an empty context is
/// byte-identical to the pre-refinement proposal path.
/// </summary>
/// <remarks>
/// The selection is deterministic: the highest <see cref="FailureSignal.Rate"/> among signals whose
/// <see cref="FailureSignal.Sample"/> reaches <see cref="MinimumSample"/> and whose rate reaches
/// <see cref="MinimumSignalRate"/>, with an ordinal signal-name tie-break. The floor is inclusive, so
/// the default 0 keeps a rate-0 signal eligible and the pre-floor selection unchanged. When no signal
/// passes, the legacy session-driven proposer produces the challenger. The edit always carries a
/// fixed-vocabulary rationale code and never free text.
/// </remarks>
public sealed class SignatureDrivenRefinementPolicy : IRefinementPolicy
{
    /// <summary>The primary parameter step for a signature edit.</summary>
    public const double Step = 0.08;

    /// <summary>The smaller step used for showdown and stack-off outcomes.</summary>
    public const double OutcomeStep = 0.06;

    /// <summary>A signal is usable only when it was observed in at least this many samples.</summary>
    public const int MinimumSample = 10;

    private readonly IStrategyProposer _legacyProposer;

    private readonly bool _enableSkillEdits;

    private readonly RefinementStrategy _strategy;

    private readonly double _minimumSignalRate;

    public SignatureDrivenRefinementPolicy(
        IStrategyProposer? legacyProposer = null,
        bool enableSkillEdits = false,
        RefinementStrategy strategy = RefinementStrategy.SignatureDriven,
        double minimumSignalRate = 0.0)
    {
        _legacyProposer = legacyProposer ?? new DeterministicStrategyProposer();
        _enableSkillEdits = enableSkillEdits;
        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown refinement strategy.");
        }

        _strategy = strategy;
        if (!double.IsFinite(minimumSignalRate) || minimumSignalRate < 0.0 || minimumSignalRate > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSignalRate),
                minimumSignalRate,
                "The minimum signal rate must be finite and in [0, 1].");
        }

        _minimumSignalRate = minimumSignalRate;
    }

    /// <summary>
    /// The inclusive positive-evidence floor a signal must reach to be usable. The default 0 keeps a
    /// rate-0 signal eligible, so the pre-floor selection is unchanged.
    /// </summary>
    public double MinimumSignalRate => _minimumSignalRate;

    public RefinementProposal Propose(RefinementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_strategy == RefinementStrategy.SessionDriven)
        {
            // The legacy path, reached through the same refinement boundary so the loop has one
            // proposal shape whether refinement is on or off.
            return Legacy(context);
        }

        var signal = StrongestUsable(context.Signatures);
        if (signal is null)
        {
            return Legacy(context);
        }

        if (_enableSkillEdits
            && string.Equals(signal.Name, FailureSignalNames.AggressionByStreet, StringComparison.Ordinal))
        {
            var skillEdit = ProposeSkillEdit(context, signal);
            if (skillEdit is not null)
            {
                return skillEdit;
            }
        }

        var (mapped, rationale, step) = Map(context.Incumbent, signal);
        if (mapped is null)
        {
            return Legacy(context);
        }

        var candidate = mapped.Clamp();
        if (candidate.BehaviorEquals(context.Incumbent))
        {
            // The implicated parameter was already at its boundary: move a threshold by the same
            // step so a non-inert challenger always reaches the guard.
            candidate = FlipThreshold(context.Incumbent, step);
            rationale = RefinementRationaleCodes.ClampedFlip;
        }

        return new RefinementProposal(candidate, rationale);
    }

    /// <summary>
    /// The R-10 opt-in path: the aggression-by-street signal edits the bluff skill of a default skill
    /// set derived from the incumbent (or of the incumbent's existing set). It returns null at the
    /// domain boundary so the measured numeric path still produces the challenger.
    /// </summary>
    private static RefinementProposal? ProposeSkillEdit(RefinementContext context, FailureSignal signal)
    {
        double delta;
        string rationale;
        if (string.Equals(signal.Direction, FailureSignalDirections.Low, StringComparison.Ordinal))
        {
            delta = Step;
            rationale = RefinementRationaleCodes.AggressionLow;
        }
        else if (string.Equals(signal.Direction, FailureSignalDirections.High, StringComparison.Ordinal))
        {
            delta = -Step;
            rationale = RefinementRationaleCodes.AggressionHigh;
        }
        else
        {
            return null;
        }

        var incumbent = context.Incumbent;
        var baseSkills = incumbent.Skills ?? StrategySkillSet.FromGenome(incumbent);
        var edited = baseSkills with
        {
            Bluff = new BluffSkillParameters(baseSkills.Bluff.Frequency + delta),
        };
        var clamped = edited.Clamp();
        if (clamped.Bluff.Frequency == baseSkills.Bluff.Frequency)
        {
            return null;
        }

        var candidate = (incumbent with { Skills = clamped }).Clamp();
        return new RefinementProposal(candidate, rationale);
    }

    private FailureSignal? StrongestUsable(SessionSignatures signatures)
    {
        if (signatures is null || signatures.Signals.Count == 0)
        {
            return null;
        }

        return signatures.Signals
            .Where(signal => signal.Sample >= MinimumSample && signal.Rate >= _minimumSignalRate)
            .OrderByDescending(static signal => signal.Rate)
            .ThenBy(static signal => signal.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static (StrategyGenome? Genome, string Rationale, double Step) Map(
        StrategyGenome incumbent,
        FailureSignal signal) =>
        signal.Name switch
        {
            FailureSignalNames.FoldToRaise => (
                Nudge(incumbent, GenomeParameter.CallThreshold, -Step),
                RefinementRationaleCodes.FoldToRaise,
                Step),
            FailureSignalNames.CallWithWeakHand => (
                Nudge(incumbent, GenomeParameter.CallThreshold, Step),
                RefinementRationaleCodes.CallWithWeakHand,
                Step),
            FailureSignalNames.MissedValue => (
                Nudge(incumbent, GenomeParameter.RaiseThreshold, -Step),
                RefinementRationaleCodes.MissedValue,
                Step),
            FailureSignalNames.AggressionByStreet => MapAggression(incumbent, signal.Direction),
            FailureSignalNames.ShowdownLoss => (
                Nudge(incumbent, GenomeParameter.CallThreshold, OutcomeStep),
                RefinementRationaleCodes.ShowdownLoss,
                OutcomeStep),
            FailureSignalNames.StackOffLoss => (
                Nudge(incumbent, GenomeParameter.RaiseThreshold, OutcomeStep),
                RefinementRationaleCodes.StackOffLoss,
                OutcomeStep),
            _ => (null, RefinementRationaleCodes.LegacySessionDriven, Step),
        };

    private static (StrategyGenome? Genome, string Rationale, double Step) MapAggression(
        StrategyGenome incumbent,
        string direction)
    {
        if (string.Equals(direction, FailureSignalDirections.Low, StringComparison.Ordinal))
        {
            return (
                Nudge(incumbent, GenomeParameter.BluffFrequency, Step),
                RefinementRationaleCodes.AggressionLow,
                Step);
        }

        if (string.Equals(direction, FailureSignalDirections.High, StringComparison.Ordinal))
        {
            return (
                Nudge(incumbent, GenomeParameter.BluffFrequency, -Step),
                RefinementRationaleCodes.AggressionHigh,
                Step);
        }

        return (null, RefinementRationaleCodes.LegacySessionDriven, Step);
    }

    private RefinementProposal Legacy(RefinementContext context) =>
        new(
            _legacyProposer.Propose(context.Incumbent, context.Analysis, context.Iteration),
            RefinementRationaleCodes.LegacySessionDriven);

    private static StrategyGenome Nudge(StrategyGenome genome, GenomeParameter parameter, double delta) =>
        (parameter switch
        {
            GenomeParameter.RaiseThreshold => genome with { RaiseThreshold = genome.RaiseThreshold + delta },
            GenomeParameter.CallThreshold => genome with { CallThreshold = genome.CallThreshold + delta },
            GenomeParameter.BluffFrequency => genome with { BluffFrequency = genome.BluffFrequency + delta },
            _ => genome with { SizingMultiplier = genome.SizingMultiplier + delta },
        }).Clamp();

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
