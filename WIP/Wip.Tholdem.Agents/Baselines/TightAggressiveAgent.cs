using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Baselines;

/// <summary>
/// Tight-aggressive baseline: raises strong holdings, calls only with enough strength and pot odds,
/// and bluffs at a seeded frequency when no call is required.
/// </summary>
public sealed class TightAggressiveAgent : IPokerAgent
{
    public TightAggressiveAgent(TightAggressiveProfile? profile = null, string? name = null)
    {
        Profile = profile ?? new TightAggressiveProfile();
        Name = string.IsNullOrWhiteSpace(name) ? "tight-aggressive" : name;
    }

    public string Name { get; }

    public TightAggressiveProfile Profile { get; }

    public ValueTask<PokerAction> ActAsync(
        PlayerObservation observation,
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();

        var strength = HandStrength.Estimate(observation.OwnHoleCards, observation.Board);
        var canBet = LegalActionSelection.Has(observation, ActionKind.Bet);
        var canRaise = LegalActionSelection.Has(observation, ActionKind.Raise);

        if (observation.AmountToCall <= 0)
        {
            if (canBet || canRaise)
            {
                var bluff = Unit(observation, context) < Profile.BluffFrequency;
                if (strength >= Profile.RaiseThreshold || bluff)
                {
                    return ValueTask.FromResult(
                        LegalActionSelection.BetOrRaise(observation, RaiseTarget(observation)));
                }
            }

            if (LegalActionSelection.Has(observation, ActionKind.Check))
            {
                return ValueTask.FromResult(PokerAction.Check());
            }
        }
        else
        {
            if ((canRaise || canBet) && strength >= Profile.RaiseThreshold)
            {
                return ValueTask.FromResult(
                    LegalActionSelection.BetOrRaise(observation, RaiseTarget(observation)));
            }

            var potOdds = observation.AmountToCall / (double)(observation.Pot + observation.AmountToCall);
            if (LegalActionSelection.Has(observation, ActionKind.Call)
                && strength >= Profile.CallThreshold
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

    private static long RaiseTarget(PlayerObservation observation) =>
        observation.OwnStreetCommitment + observation.AmountToCall + observation.Pot;

    private static double Unit(PlayerObservation observation, AgentTurnContext context) =>
        (SeedDerivation.DeriveAgentSeed(
            observation.ToCanonicalJson(),
            context.HandNumber,
            context.Seat,
            context.ActionIndex,
            2UL) % 1_000_000UL) / 1_000_000.0;
}