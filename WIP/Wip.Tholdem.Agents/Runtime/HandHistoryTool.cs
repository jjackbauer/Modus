using Wip.Abstractions.Capabilities;
using Wip.Tholdem.Engine;

namespace Wip.Tholdem.Agents.Runtime;

/// <summary>
/// The hand-history request. <see cref="Limit"/> is bounded by the tool; the seat is fixed when the
/// tool is constructed, so model-provided arguments can never select another seat.
/// </summary>
public sealed record HandHistoryRequest(int Limit = 10);

public sealed record HandHistoryResult(int Seat, IReadOnlyList<SessionHandView> Hands);

/// <summary>
/// Reads the newest recorded hands of the current session for one fixed seat.
/// </summary>
public sealed class HandHistoryTool : ITool<HandHistoryRequest, HandHistoryResult>
{
    public const int DefaultLimit = 10;

    public const int MaxLimit = 100;

    private readonly int _seat;
    private readonly SessionHandStore _store;

    public HandHistoryTool(int seat, SessionHandStore store)
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

    public ValueTask<HandHistoryResult> ExecuteAsync(
        HandHistoryRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var limit = request.Limit > 0 ? Math.Min(request.Limit, MaxLimit) : DefaultLimit;
        return new ValueTask<HandHistoryResult>(new HandHistoryResult(_seat, _store.BuildFor(_seat, limit)));
    }
}
