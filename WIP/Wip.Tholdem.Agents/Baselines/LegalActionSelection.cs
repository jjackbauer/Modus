using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Baselines;

/// <summary>
/// Legal-action helpers shared by the baselines. A kind is only returned when the observation lists
/// it, and every bet or raise is clamped into [MinRaiseTo, MaxRaiseTo].
/// </summary>
internal static class LegalActionSelection
{
    public static bool Has(PlayerObservation observation, ActionKind kind)
    {
        for (var index = 0; index < observation.LegalActionKinds.Count; index++)
        {
            if (observation.LegalActionKinds[index] == kind)
            {
                return true;
            }
        }

        return false;
    }

    public static PokerAction BetOrRaise(PlayerObservation observation, long target)
    {
        var amount = Math.Clamp(target, observation.MinRaiseTo, observation.MaxRaiseTo);
        return Has(observation, ActionKind.Bet)
            ? PokerAction.BetTo(amount)
            : PokerAction.RaiseTo(amount);
    }

    public static PokerAction FirstLegal(PlayerObservation observation)
    {
        if (Has(observation, ActionKind.Check))
        {
            return PokerAction.Check();
        }

        if (Has(observation, ActionKind.Call))
        {
            return PokerAction.Call();
        }

        if (Has(observation, ActionKind.Fold))
        {
            return PokerAction.Fold();
        }

        if (Has(observation, ActionKind.Bet))
        {
            return PokerAction.BetTo(observation.MinRaiseTo);
        }

        if (Has(observation, ActionKind.Raise))
        {
            return PokerAction.RaiseTo(observation.MinRaiseTo);
        }

        if (Has(observation, ActionKind.AllIn))
        {
            return PokerAction.AllIn();
        }

        throw new InvalidOperationException("An observation without legal actions cannot be answered.");
    }
}