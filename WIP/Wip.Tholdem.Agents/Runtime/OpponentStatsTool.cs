using Wip.Abstractions.Capabilities;
using Wip.Tholdem.Engine;

namespace Wip.Tholdem.Agents.Runtime;

public sealed record OpponentStatsRequest();

/// <summary>
/// The aggregate statistics of every opponent of the requesting seat. <see cref="Seat"/> is the
/// single opponent seat for a two-seat table and <c>-1</c> when several seats were aggregated;
/// <see cref="Opponents"/> always lists the aggregated seats in ascending order.
/// </summary>
public sealed record OpponentStatsResult(int Seat, SeatStatistics Statistics)
{
    public IReadOnlyList<int> Opponents { get; init; } = Array.Empty<int>();
}

/// <summary>
/// Reads the aggregate session statistics of the opposing seats. The seat is fixed when the tool is
/// constructed and the request has no seat argument, so the model cannot choose whose statistics it
/// receives. At a multiway table every seat other than <c>seat</c> is aggregated: hands are summed and
/// the rates are hand-weighted.
/// </summary>
public sealed class OpponentStatsTool : ITool<OpponentStatsRequest, OpponentStatsResult>
{
    private readonly int _seat;
    private readonly SessionHandStore _store;

    public OpponentStatsTool(int seat, SessionHandStore store)
    {
        if (seat < 0 || seat >= HoldemHandEngine.MaximumSeats)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seat),
                seat,
                $"A seat must be between 0 and {HoldemHandEngine.MaximumSeats - 1}.");
        }

        _seat = seat;
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public ValueTask<OpponentStatsResult> ExecuteAsync(
        OpponentStatsRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var seatCount = _store.SeatCount;
        var opponents = new List<int>(Math.Max(0, seatCount - 1));
        for (var candidate = 0; candidate < seatCount; candidate++)
        {
            if (candidate != _seat)
            {
                opponents.Add(candidate);
            }
        }

        var primarySeat = opponents.Count == 1 ? opponents[0] : -1;
        return new ValueTask<OpponentStatsResult>(
            new OpponentStatsResult(primarySeat, _store.BuildStatisticsFor(opponents))
            {
                Opponents = opponents.ToArray(),
            });
    }
}
