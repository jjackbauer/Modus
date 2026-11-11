using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>The preflop situation the acting seat faces, derived from the public action history.</summary>
public enum PreflopSituation
{
    NotPreflop,
    Unopened,
    FacingLimp,
    FacingOpen,
    FacingThreeBet,
}

/// <summary>
/// The classified preflop situation with the number of raises/all-ins and calls observed so far
/// and whether the acting seat is defending a blind against one raise.
/// </summary>
public sealed record PreflopContext(
    PreflopSituation Situation,
    int Raises,
    int Calls,
    bool IsBlindDefense);

/// <summary>
/// Classifies the preflop situation from the public action history only: two or more raises/all-ins
/// is a three-bet, exactly one is an open, calls without a raise are a limp and otherwise the pot
/// is unopened. Outside preflop there is no preflop situation, so the counts are zero and the
/// blind-defense flag is false.
/// </summary>
public static class PreflopSituationClassifier
{
    /// <summary>Classifies the observation's preflop situation.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="observation"/> is null.</exception>
    public static PreflopContext Classify(PlayerObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (observation.Street != Street.Preflop)
        {
            return new PreflopContext(PreflopSituation.NotPreflop, 0, 0, false);
        }

        var raises = 0;
        var calls = 0;
        foreach (var action in observation.ActionHistory)
        {
            switch (action.Kind)
            {
                case ActionKind.Raise:
                case ActionKind.AllIn:
                    raises++;
                    break;
                case ActionKind.Call:
                    calls++;
                    break;
            }
        }

        var situation = raises >= 2
            ? PreflopSituation.FacingThreeBet
            : raises == 1
                ? PreflopSituation.FacingOpen
                : calls >= 1
                    ? PreflopSituation.FacingLimp
                    : PreflopSituation.Unopened;

        // The engine posts the small blind on the button, so both the button and the big blind
        // count as blind defenders when they face exactly one open.
        var isBlindDefense = situation == PreflopSituation.FacingOpen
            && (observation.Seat == observation.ButtonSeat
                || (observation.SeatCount > 0
                    && observation.Seat == (observation.ButtonSeat + 1) % observation.SeatCount));

        return new PreflopContext(situation, raises, calls, isBlindDefense);
    }
}
