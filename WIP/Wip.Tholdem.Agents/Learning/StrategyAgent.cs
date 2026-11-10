using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Learning.Skills;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// A deterministic heads-up agent driven entirely by a <see cref="StrategyGenome"/>. It mirrors the
/// tight-aggressive baseline's decision order, but the raise/call thresholds, the bluff frequency
/// and the bet sizing all come from the genome. The same observation and turn context always yield
/// the same action.
/// </summary>
public sealed class StrategyAgent : IPokerAgent
{
    public StrategyAgent(StrategyGenome? genome = null, string? name = null)
    {
        Genome = (genome ?? new StrategyGenome()).Clamp();
        Name = string.IsNullOrWhiteSpace(name) ? "strategy" : name;
    }

    public string Name { get; }

    public StrategyGenome Genome { get; }

    public ValueTask<PokerAction> ActAsync(
        PlayerObservation observation,
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();

        if (Genome.Skills is not null)
        {
            return ValueTask.FromResult(DecideWithSkills(observation, context, Genome.Skills));
        }

        var strength = HandStrength.Estimate(observation.OwnHoleCards, observation.Board);
        var canBet = LegalActionSelection.Has(observation, ActionKind.Bet);
        var canRaise = LegalActionSelection.Has(observation, ActionKind.Raise);

        if (observation.AmountToCall <= 0)
        {
            if (canBet || canRaise)
            {
                var bluff = Unit(observation, context) < Genome.BluffFrequency;
                if (strength >= Genome.RaiseThreshold || bluff)
                {
                    return ValueTask.FromResult(
                        LegalActionSelection.BetOrRaise(observation, SizingTarget(observation)));
                }
            }

            if (LegalActionSelection.Has(observation, ActionKind.Check))
            {
                return ValueTask.FromResult(PokerAction.Check());
            }
        }
        else
        {
            if ((canRaise || canBet) && strength >= Genome.RaiseThreshold)
            {
                return ValueTask.FromResult(
                    LegalActionSelection.BetOrRaise(observation, SizingTarget(observation)));
            }

            var potOdds = observation.AmountToCall / (double)(observation.Pot + observation.AmountToCall);
            if (LegalActionSelection.Has(observation, ActionKind.Call)
                && strength >= Genome.CallThreshold
                && strength >= potOdds)
            {
                return ValueTask.FromResult(PokerAction.Call());
            }

            if (LegalActionSelection.Has(observation, ActionKind.Fold))
            {
                return ValueTask.FromResult(PokerAction.Fold());
            }

            if (LegalActionSelection.Has(observation, ActionKind.Check))
            {
                return ValueTask.FromResult(PokerAction.Check());
            }
        }

        return ValueTask.FromResult(LegalActionSelection.FirstLegal(observation));
    }

    /// <summary>
    /// The fixed deterministic skill pipeline: the preflop skill governs preflop streets, the postflop
    /// skill every later street, the sizing skill every bet or raise target, and the bluff skill only
    /// the free (no amount to call) branch. Every returned action is validated for legality.
    /// </summary>
    private PokerAction DecideWithSkills(
        PlayerObservation observation,
        AgentTurnContext context,
        StrategySkillSet skills)
    {
        var strength = HandStrength.Estimate(observation.OwnHoleCards, observation.Board);
        var canBet = LegalActionSelection.Has(observation, ActionKind.Bet);
        var canRaise = LegalActionSelection.Has(observation, ActionKind.Raise);
        var preflop = observation.Street == Street.Preflop;
        var raiseThreshold = preflop ? skills.Preflop.RaiseThreshold : skills.Postflop.RaiseThreshold;
        var callThreshold = preflop ? skills.Preflop.CallThreshold : skills.Postflop.CallThreshold;

        if (observation.AmountToCall <= 0)
        {
            if (canBet || canRaise)
            {
                var bluff = Unit(observation, context) < skills.Bluff.Frequency;
                if (strength >= raiseThreshold || bluff)
                {
                    return EnsureLegalSkillAction(
                        observation,
                        LegalActionSelection.BetOrRaise(
                            observation,
                            SizingTarget(observation, skills.Sizing.Multiplier)));
                }
            }

            if (LegalActionSelection.Has(observation, ActionKind.Check))
            {
                return EnsureLegalSkillAction(observation, PokerAction.Check());
            }
        }
        else
        {
            if ((canRaise || canBet) && strength >= raiseThreshold)
            {
                return EnsureLegalSkillAction(
                    observation,
                    LegalActionSelection.BetOrRaise(
                        observation,
                        SizingTarget(observation, skills.Sizing.Multiplier)));
            }

            var potOdds = observation.AmountToCall / (double)(observation.Pot + observation.AmountToCall);
            if (LegalActionSelection.Has(observation, ActionKind.Call)
                && strength >= callThreshold
                && strength >= potOdds)
            {
                return EnsureLegalSkillAction(observation, PokerAction.Call());
            }

            if (LegalActionSelection.Has(observation, ActionKind.Fold))
            {
                return EnsureLegalSkillAction(observation, PokerAction.Fold());
            }

            if (LegalActionSelection.Has(observation, ActionKind.Check))
            {
                return EnsureLegalSkillAction(observation, PokerAction.Check());
            }
        }

        return LegalActionSelection.FirstLegal(observation);
    }

    /// <summary>
    /// Ensures a skill-composed action is legal for the observation: the kind must be legal and a
    /// bet or raise must stay inside [MinRaiseTo, MaxRaiseTo]. Anything else falls back to the first
    /// legal action.
    /// </summary>
    public static PokerAction EnsureLegalSkillAction(PlayerObservation observation, PokerAction action)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!LegalActionSelection.Has(observation, action.Kind))
        {
            return LegalActionSelection.FirstLegal(observation);
        }

        if (action.Kind is ActionKind.Bet or ActionKind.Raise
            && (action.Amount < observation.MinRaiseTo || action.Amount > observation.MaxRaiseTo))
        {
            return LegalActionSelection.FirstLegal(observation);
        }

        return action;
    }

    /// <summary>
    /// The genome's sizing multiplier scales the minimum legal bet/raise and is clamped into
    /// [MinRaiseTo, MaxRaiseTo]; <c>LegalActionSelection</c> applies the same clamp again.
    /// </summary>
    private long SizingTarget(PlayerObservation observation)
    {
        var scaled = observation.MinRaiseTo * Genome.SizingMultiplier;
        var rounded = (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, observation.MinRaiseTo, observation.MaxRaiseTo);
    }

    /// <summary>
    /// The skill sizing multiplier applies the same scaling and clamp as the legacy path, so a skill
    /// set derived from the genome reproduces the exact same target.
    /// </summary>
    private static long SizingTarget(PlayerObservation observation, double multiplier)
    {
        var scaled = observation.MinRaiseTo * multiplier;
        var rounded = (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, observation.MinRaiseTo, observation.MaxRaiseTo);
    }

    private static double Unit(PlayerObservation observation, AgentTurnContext context) =>
        (SeedDerivation.DeriveAgentSeed(
            observation.ToCanonicalJson(),
            context.HandNumber,
            context.Seat,
            context.ActionIndex,
            2UL) % 1_000_000UL) / 1_000_000.0;
}
