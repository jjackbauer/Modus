using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Equity;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// Equity-aware policy: counts the live opponents from the public seats, estimates multiway equity
/// with a seeded Monte Carlo sample and decides from the <see cref="EquityPolicy"/> edges above the
/// opponent-count baseline 1/(liveOpponents+1), sizing bets and raises as a fraction of the pot.
/// Facing a bet the whole confidence interval must clear the side-pot-aware call break-even from
/// <see cref="CallEvObservation"/>; a preflop raise adds <see cref="EquityPolicy.PreflopRaiseMargin"/>
/// (doubled facing a three-bet) and an uncertain interval is folded conservatively. With no bet the
/// policy value-bets a strong expected pot share or bluffs at the seeded frequency. Every returned
/// action is checked against the legal action set. The legacy <see cref="StrategyAgent"/> is
/// deliberately untouched and remains the v1 control.
/// </summary>
public sealed class EquityPolicyAgent : IPokerAgent
{
    /// <summary>Stream identifier of the Monte Carlo equity sample.</summary>
    public const ulong EquityStream = 3UL;

    /// <summary>Stream identifier of the bluff frequency draw.</summary>
    public const ulong BluffStream = 4UL;

    /// <summary>The smallest sample count the agent will use.</summary>
    public const int MinimumSamples = 50;

    /// <summary>The largest sample count the agent will use.</summary>
    public const int MaximumSamples = 5000;

    /// <summary>The smallest pot fraction the agent will use; zero is outside the sizer's domain.</summary>
    public const double MinimumValueBetPotFraction = 0.01;

    /// <summary>The largest pot fraction the agent will use.</summary>
    public const double MaximumValueBetPotFraction = 2.0;

    private const int MaximumOpponents = 5;

    private const ulong UnitModulus = 1_000_000UL;

    public EquityPolicyAgent(EquityPolicy? policy = null, int? EquitySamples = null, string? name = null)
    {
        var source = policy ?? new EquityPolicy();
        Policy = source with
        {
            EquitySamples = Math.Clamp(
                EquitySamples ?? source.EquitySamples,
                MinimumSamples,
                MaximumSamples),
            ValueEdge = Math.Clamp(source.ValueEdge, 0.0, 1.0),
            RaiseEdge = Math.Clamp(source.RaiseEdge, 0.0, 1.0),
            BluffFrequency = Math.Clamp(source.BluffFrequency, 0.0, 1.0),
            ValueBetPotFraction = Math.Clamp(
                source.ValueBetPotFraction,
                MinimumValueBetPotFraction,
                MaximumValueBetPotFraction),
            PreflopRaiseMargin = Math.Clamp(source.PreflopRaiseMargin, 0.0, 1.0),
        };
        Name = string.IsNullOrWhiteSpace(name) ? "equity-policy" : name;
    }

    public string Name { get; }

    /// <summary>The clamped policy this agent decides from; the supplied record is not mutated.</summary>
    public EquityPolicy Policy { get; }

    public ValueTask<PokerAction> ActAsync(
        PlayerObservation observation,
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();

        var liveOpponents = CountLiveOpponents(observation);
        if (liveOpponents is < 1 or > MaximumOpponents)
        {
            // No equity model applies: no live opponent to sample against, or more opponents than
            // the sampler supports. Answer the first legal action instead of failing the turn.
            return ValueTask.FromResult(FirstLegal(observation));
        }

        // Expected pot share is a 1/(opponents+1) quantity, so both equity edges are measured from
        // that opponent-count-relative baseline rather than from a fixed absolute threshold.
        var baseline = 1.0 / (liveOpponents + 1);
        var canonical = observation.ToCanonicalJson();
        var equitySeed = SeedDerivation.DeriveAgentSeed(
            canonical,
            context.HandNumber,
            context.Seat,
            context.ActionIndex,
            EquityStream);
        var equity = MultiwayEquity.Estimate(
            observation.OwnHoleCards,
            observation.Board,
            liveOpponents,
            equitySeed,
            Policy.EquitySamples);

        if (observation.AmountToCall > 0)
        {
            // The layered calculator supplies the effective call (capped by the largest live
            // commitment) and the pot the caller can actually contest, so a short all-in cannot
            // make the agent call more than it can win.
            var callEv = CallEvObservation.Compute(observation, equity.ExpectedPotShare);
            var preflop = PreflopSituationClassifier.Classify(observation);
            var margin = preflop.Raises == 0
                ? 0.0
                : Policy.PreflopRaiseMargin * (preflop.Raises >= 2 ? 2.0 : 1.0);
            var threshold = Math.Clamp(callEv.BreakEvenEquity + margin, 0.0, 1.0);
            var decision = EquityThresholdPolicy.DecideCall(equity, threshold);
            var raiseThreshold = baseline + Policy.RaiseEdge + margin;
            var canRaise = LegalActionSelection.Has(observation, ActionKind.Bet)
                || LegalActionSelection.Has(observation, ActionKind.Raise);

            if (decision.Decision == EquityDecision.Call
                && decision.LowerBound > threshold
                && equity.ExpectedPotShare >= raiseThreshold
                && decision.LowerBound >= raiseThreshold
                && canRaise)
            {
                return ValueTask.FromResult(
                    EnsureLegal(
                        observation,
                        LegalActionSelection.BetOrRaise(observation, SizingTarget(observation))));
            }

            if (decision.Decision == EquityDecision.Call
                && decision.LowerBound > threshold
                && LegalActionSelection.Has(observation, ActionKind.Call))
            {
                return ValueTask.FromResult(EnsureLegal(observation, PokerAction.Call()));
            }

            if (decision.Decision is EquityDecision.Fold or EquityDecision.Uncertain
                && LegalActionSelection.Has(observation, ActionKind.Fold))
            {
                return ValueTask.FromResult(EnsureLegal(observation, PokerAction.Fold()));
            }

            if (LegalActionSelection.Has(observation, ActionKind.Call))
            {
                return ValueTask.FromResult(EnsureLegal(observation, PokerAction.Call()));
            }

            return ValueTask.FromResult(FirstLegal(observation));
        }

        var canBetOrRaise = LegalActionSelection.Has(observation, ActionKind.Bet)
            || LegalActionSelection.Has(observation, ActionKind.Raise);
        if (canBetOrRaise)
        {
            var bluff = Unit(canonical, context, BluffStream) < Policy.BluffFrequency;
            if (equity.ExpectedPotShare >= baseline + Policy.ValueEdge || bluff)
            {
                return ValueTask.FromResult(
                    EnsureLegal(
                        observation,
                        LegalActionSelection.BetOrRaise(observation, SizingTarget(observation))));
            }
        }

        if (LegalActionSelection.Has(observation, ActionKind.Check))
        {
            return ValueTask.FromResult(EnsureLegal(observation, PokerAction.Check()));
        }

        return ValueTask.FromResult(FirstLegal(observation));
    }

    /// <summary>
    /// Counts the public seats that are not the acting seat and have not folded. An all-in seat is
    /// still live for showdown equity, so only the folded flag excludes it.
    /// </summary>
    private static int CountLiveOpponents(PlayerObservation observation)
    {
        var live = 0;
        for (var index = 0; index < observation.Seats.Count; index++)
        {
            var seat = observation.Seats[index];
            if (seat.Seat != observation.Seat && !seat.Folded)
            {
                live++;
            }
        }

        return live;
    }

    private long SizingTarget(PlayerObservation observation) =>
        PotRelativeSizing.Target(
            observation.Pot,
            observation.AmountToCall,
            observation.OwnStreetCommitment,
            Policy.ValueBetPotFraction,
            observation.MinRaiseTo,
            observation.MaxRaiseTo);

    private static PokerAction FirstLegal(PlayerObservation observation) =>
        EnsureLegal(observation, LegalActionSelection.FirstLegal(observation));

    /// <summary>
    /// A bet or raise kind must be listed and its amount must stay inside [MinRaiseTo, MaxRaiseTo];
    /// anything else falls back to the first legal action.
    /// </summary>
    private static PokerAction EnsureLegal(PlayerObservation observation, PokerAction action)
    {
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
    /// A seeded unit in [0, 1) derived from the canonical observation and the public turn
    /// coordinates under the supplied stream. The millisecond-scale modulus matches the baseline
    /// agents' unit derivation and uses no ambient randomness.
    /// </summary>
    private static double Unit(
        string canonicalObservation,
        AgentTurnContext context,
        ulong stream) =>
        (SeedDerivation.DeriveAgentSeed(
            canonicalObservation,
            context.HandNumber,
            context.Seat,
            context.ActionIndex,
            stream) % UnitModulus) / (double)UnitModulus;
}