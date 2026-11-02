using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// Describes a seat-remapping configuration for cross-seat play matches.
/// Maps each authored seat (1-4) to a deployed seat (1-4), allowing strategies
/// to be tested at seats different from where they were authored.
/// </summary>
public sealed class BonesCrossSeatMatchConfig
{
    /// <summary>
    /// Maps authored seat → deployed seat.
    /// Every seat 1-4 must appear exactly once as a key and exactly once as a value.
    /// </summary>
    public IReadOnlyDictionary<BonesPlayerId, BonesPlayerId> SeatRemapping { get; }

    public BonesCrossSeatMatchConfig(IReadOnlyDictionary<BonesPlayerId, BonesPlayerId> seatRemapping)
    {
        ArgumentNullException.ThrowIfNull(seatRemapping);
        ValidateSeatRemapping(seatRemapping);
        SeatRemapping = seatRemapping;
    }

    /// <summary>
    /// Returns the deployed (destination) seat for a given authored (source) seat.
    /// </summary>
    public BonesPlayerId GetDeployedSeat(BonesPlayerId authoredSeat)
    {
        if (!SeatRemapping.TryGetValue(authoredSeat, out var deployedSeat))
        {
            throw new InvalidOperationException(
                $"No seat remapping defined for authored seat {authoredSeat.Seat}.");
        }

        return deployedSeat;
    }

    /// <summary>
    /// Creates an identity remapping (no cross-seat rotation).
    /// Every seat maps to itself.
    /// </summary>
    public static BonesCrossSeatMatchConfig Identity { get; } = CreateIdentity();

    private static BonesCrossSeatMatchConfig CreateIdentity()
    {
        var identity = new Dictionary<BonesPlayerId, BonesPlayerId>();
        for (var seat = 1; seat <= 4; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            identity[playerId] = playerId;
        }

        return new BonesCrossSeatMatchConfig(identity);
    }

    private static void ValidateSeatRemapping(IReadOnlyDictionary<BonesPlayerId, BonesPlayerId> seatRemapping)
    {
        if (seatRemapping.Count != 4)
        {
            throw new ArgumentException(
                $"Seat remapping must contain exactly 4 entries (seats 1-4), got {seatRemapping.Count}.",
                nameof(seatRemapping));
        }

        var expectedKeys = new HashSet<int> { 1, 2, 3, 4 };
        var seenKeys = new HashSet<int>();
        var seenValues = new HashSet<int>();

        foreach (var (authored, deployed) in seatRemapping)
        {
            if (authored.Seat < 1 || authored.Seat > 4)
            {
                throw new ArgumentException(
                    $"Authored seat {authored.Seat} is out of range (1-4).",
                    nameof(seatRemapping));
            }

            if (deployed.Seat < 1 || deployed.Seat > 4)
            {
                throw new ArgumentException(
                    $"Deployed seat {deployed.Seat} is out of range (1-4).",
                    nameof(seatRemapping));
            }

            if (!seenKeys.Add(authored.Seat))
            {
                throw new ArgumentException(
                    $"Duplicate authored seat {authored.Seat} in seat remapping.",
                    nameof(seatRemapping));
            }

            if (!seenValues.Add(deployed.Seat))
            {
                throw new ArgumentException(
                    $"Duplicate deployed seat {deployed.Seat} in seat remapping.",
                    nameof(seatRemapping));
            }
        }

        if (!seenKeys.SetEquals(expectedKeys) || !seenValues.SetEquals(expectedKeys))
        {
            throw new ArgumentException(
                "Seat remapping must include all seats 1-4 as both keys and values.",
                nameof(seatRemapping));
        }
    }
}
