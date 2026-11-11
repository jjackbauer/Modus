using System.Globalization;

namespace Wip.Tholdem.Agents.Simulation;

/// <summary>
/// Thrown when one hand applies more actions than <see cref="SimulationOptions.ActionCapPerHand"/>
/// allows. The hand number is one-based, matching the hand files and <see cref="HandOutcome"/>.
/// </summary>
public sealed class HandActionCapExceededException : InvalidOperationException
{
    public HandActionCapExceededException(int handNumber, int cap)
        : base(
            "Hand " + handNumber.ToString(CultureInfo.InvariantCulture)
            + " exceeded the action cap of " + cap.ToString(CultureInfo.InvariantCulture) + ".")
    {
        HandNumber = handNumber;
        Cap = cap;
    }

    public int HandNumber { get; }

    public int Cap { get; }
}
