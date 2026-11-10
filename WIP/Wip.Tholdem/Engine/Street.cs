namespace Wip.Tholdem.Engine;

/// <summary>
/// The betting street a hand is currently on. <see cref="Showdown"/> is the evaluation phase and
/// <see cref="Complete"/> is the terminal state after the pot has been awarded.
/// </summary>
public enum Street
{
    Preflop = 0,
    Flop = 1,
    Turn = 2,
    River = 3,
    Showdown = 4,
    Complete = 5,
}
