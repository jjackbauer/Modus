using Wip.Tholdem.Agents.Equity;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Baselines;

/// <summary>
/// Pot-odds baseline: calls only when seeded Monte Carlo equity clears the pot odds plus a margin,
/// folds otherwise, and bets a strong equity when there is nothing to call.
/// </summary>
public sealed class PotOddsEquityAgent : IPokerAgent
{
    public const int DefaultSamples = 2000;

    private const double CallMargin = 0.05;

    private const double ValueBetThreshold = 0.60;

    private const ulong EquityStream = 0x50UL;

    private readonly int _samples;

    public PotOddsEquityAgent(int samples = DefaultSamples, string? name = null)
    {
        if (samples <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samples),
                samples,
                "At least one Monte Carlo sample is required.");
        }

        _samples = samples;
        Name = string.IsNullOrWhiteSpace(name) ? "pot-odds-equity" : name;
    }

    public string Name { get; }

    public int Samples => _samples;

    public ValueTask<PokerAction> ActAsync(
        PlayerObservation observation,
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();

        var equitySeed = SeedDerivation.DeriveAgentSeed(
            observation.ToCanonicalJson(),
            context.HandNumber,
            context.Seat,
            context.ActionIndex,
            EquityStream);
        var equity = MonteCarloEquity.Estimate(
            observation.OwnHoleCards,
            observation.Board,
            equitySeed,
            _samples).Equity;

        if (observation.AmountToCall > 0)
        {
            var potOdds = observation.AmountToCall / (double)(observation.Pot + observation.AmountToCall);
            if (equity >= potOdds + CallMargin)
            {
                if (LegalActionSelection.Has(observation, ActionKind.Call))
                {
                    return ValueTask.FromResult(PokerAction.Call());
                }

                if (LegalActionSelection.Has(observation, ActionKind.AllIn))
                {
                    return ValueTask.FromResult(PokerAction.AllIn());
                }
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
        else
        {
            if (equity >= ValueBetThreshold
                && (LegalActionSelection.Has(observation, ActionKind.Bet)
                    || LegalActionSelection.Has(observation, ActionKind.Raise)))
            {
                return ValueTask.FromResult(
                    LegalActionSelection.BetOrRaise(observation, observation.Pot));
            }

            if (LegalActionSelection.Has(observation, ActionKind.Check))
            {
                return ValueTask.FromResult(PokerAction.Check());
            }
        }

        return ValueTask.FromResult(LegalActionSelection.FirstLegal(observation));
    }
}