using System.Globalization;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Runtime;

/// <summary>
/// Thread-safe, session-scoped store of completed hands for two to six seats. Callers hand in a
/// <see cref="HandRecord"/>; every value this store hands back is projected through the engine's own
/// per-seat <see cref="PlayerObservation"/>, so a view can never contain the seed, the undealt card
/// order or another seat's hole cards.
/// </summary>
public sealed class SessionHandStore
{
    public const int DefaultSeats = 2;

    private readonly object _gate = new();
    private readonly List<HandRecord> _records = [];
    private readonly int _configuredSeats;

    /// <summary>Creates a store for a two-seat session, matching the pre-multiway default.</summary>
    public SessionHandStore()
        : this(DefaultSeats)
    {
    }

    /// <summary>
    /// Creates a store whose table has <paramref name="seats"/> seats. The count is only consulted
    /// while no hand has been recorded (a recorded v2 hand carries its own seat count), so tools can
    /// name every opponent before the first hand completes.
    /// </summary>
    public SessionHandStore(int seats)
    {
        if (seats is < HoldemHandEngine.MinimumSeats or > HoldemHandEngine.MaximumSeats)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seats),
                seats,
                $"A table must have between {HoldemHandEngine.MinimumSeats} and "
                + $"{HoldemHandEngine.MaximumSeats} seats.");
        }

        _configuredSeats = seats;
    }

    /// <summary>
    /// The number of seats at this table: the configured count for an empty store, otherwise the
    /// largest seat count among the recorded hands.
    /// </summary>
    public int SeatCount
    {
        get
        {
            lock (_gate)
            {
                var count = _configuredSeats;
                foreach (var record in _records)
                {
                    count = Math.Max(count, record.SeatCount);
                }

                return count;
            }
        }
    }

    /// <summary>Records a completed hand for this session.</summary>
    public void Record(HandRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            _records.Add(record);
        }
    }

    /// <summary>
    /// Builds the newest <paramref name="limit"/> hand views for <paramref name="seat"/>, oldest first
    /// and newest last. A non-positive limit yields an empty list.
    /// </summary>
    public IReadOnlyList<SessionHandView> BuildFor(int seat, int limit)
    {
        ValidateSeat(seat);

        if (limit <= 0)
        {
            return Array.Empty<SessionHandView>();
        }

        HandRecord[] records;
        lock (_gate)
        {
            records = _records.ToArray();
        }

        var take = Math.Min(limit, records.Length);
        var views = new SessionHandView[take];
        for (var index = 0; index < take; index++)
        {
            views[index] = ToView(records[records.Length - take + index], seat);
        }

        return views;
    }

    /// <summary>Aggregates the session statistics for one seat.</summary>
    public SeatStatistics BuildStatisticsFor(int seat)
    {
        ValidateSeat(seat);

        var records = Snapshot();
        return ToStatistics(Count(records, seat));
    }

    /// <summary>
    /// Aggregates the session statistics over several seats. Hands are summed and every rate is
    /// recomputed from the summed counters, so it is hand-weighted rather than an average of rates
    /// (the aggression factor keeps the aggressive-actions / max(1, calls) formula).
    /// </summary>
    public SeatStatistics BuildStatisticsFor(IReadOnlyList<int> seats)
    {
        ArgumentNullException.ThrowIfNull(seats);
        for (var index = 0; index < seats.Count; index++)
        {
            ValidateSeat(seats[index]);
        }

        var records = Snapshot();
        var total = default(SeatCounters);
        for (var index = 0; index < seats.Count; index++)
        {
            total = total.Add(Count(records, seats[index]));
        }

        return ToStatistics(total);
    }

    private HandRecord[] Snapshot()
    {
        lock (_gate)
        {
            return _records.ToArray();
        }
    }

    private static SeatCounters Count(HandRecord[] records, int seat)
    {
        if (records.Length == 0)
        {
            return default;
        }

        long vpipHands = 0;
        long pfrHands = 0;
        long aggressiveActions = 0;
        long calls = 0;
        long showdowns = 0;

        foreach (var record in records)
        {
            var sawPreflopCall = false;
            var sawPreflopRaise = false;

            foreach (var action in record.Actions)
            {
                if (action.Seat != seat)
                {
                    continue;
                }

                var preflop = action.Street == (int)Street.Preflop;
                switch (action.Kind)
                {
                    case "Call":
                        calls++;
                        sawPreflopCall = sawPreflopCall || preflop;
                        break;
                    case "Bet":
                        aggressiveActions++;
                        sawPreflopCall = sawPreflopCall || preflop;
                        break;
                    case "Raise":
                        aggressiveActions++;
                        sawPreflopRaise = sawPreflopRaise || preflop;
                        break;
                    case "AllIn":
                        aggressiveActions++;
                        sawPreflopRaise = sawPreflopRaise || preflop;
                        break;
                }
            }

            if (sawPreflopCall || sawPreflopRaise)
            {
                vpipHands++;
            }

            if (sawPreflopRaise)
            {
                pfrHands++;
            }

            if (record.Showdown && !Folded(record, seat))
            {
                showdowns++;
            }
        }

        return new SeatCounters(
            records.Length,
            vpipHands,
            pfrHands,
            aggressiveActions,
            calls,
            showdowns);
    }

    private static bool Folded(HandRecord record, int seat)
    {
        foreach (var action in record.Actions)
        {
            if (action.Seat == seat && string.Equals(action.Kind, "Fold", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static SeatStatistics ToStatistics(SeatCounters counters)
    {
        if (counters.Hands == 0)
        {
            return new SeatStatistics(0, 0d, 0d, 0d, 0d);
        }

        return new SeatStatistics(
            (int)counters.Hands,
            counters.VpipHands / (double)counters.Hands,
            counters.PfrHands / (double)counters.Hands,
            counters.AggressiveActions / (double)Math.Max(1L, counters.Calls),
            counters.Showdowns / (double)counters.Hands);
    }

    private static SessionHandView ToView(HandRecord record, int seat)
    {
        if (seat >= record.SeatCount || seat >= record.StartingStacks.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seat),
                seat,
                $"Seat {seat} is not present in a {record.SeatCount}-seat hand record.");
        }

        // Replaying the record through the public engine is the only supported way to recover a
        // historical hand, and the observation is the engine's own single-seat projection.
        var state = HoldemHandEngine.Replay(record);
        var projection = PlayerObservation.ForSeat(state, seat);

        var actions = new string[record.Actions.Count];
        for (var index = 0; index < actions.Length; index++)
        {
            actions[index] = Describe(record.Actions[index]);
        }

        return new SessionHandView(
            record.HandNumber,
            projection.Board.Select(static card => card.ToString()).ToArray(),
            projection.OwnHoleCards.Select(static card => card.ToString()).ToArray(),
            actions,
            record.Winners.ToArray(),
            record.Showdown,
            record.PotAwarded,
            record.FinalStacks[seat] - record.StartingStacks[seat]);
    }

    private static string Describe(HandActionRecord action)
        => StreetName(action.Street)
            + " seat"
            + action.Seat.ToString(CultureInfo.InvariantCulture)
            + " "
            + action.Kind
            + " "
            + action.Amount.ToString(CultureInfo.InvariantCulture);

    private static string StreetName(int street) => street switch
    {
        (int)Street.Preflop => "Preflop",
        (int)Street.Flop => "Flop",
        (int)Street.Turn => "Turn",
        (int)Street.River => "River",
        (int)Street.Showdown => "Showdown",
        (int)Street.Complete => "Complete",
        _ => "Unknown",
    };

    private static void ValidateSeat(int seat)
    {
        if (seat < 0 || seat >= HoldemHandEngine.MaximumSeats)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seat),
                seat,
                $"A seat must be between 0 and {HoldemHandEngine.MaximumSeats - 1}.");
        }
    }

    private readonly record struct SeatCounters(
        long Hands,
        long VpipHands,
        long PfrHands,
        long AggressiveActions,
        long Calls,
        long Showdowns)
    {
        public SeatCounters Add(in SeatCounters other) => new(
            Hands + other.Hands,
            VpipHands + other.VpipHands,
            PfrHands + other.PfrHands,
            AggressiveActions + other.AggressiveActions,
            Calls + other.Calls,
            Showdowns + other.Showdowns);
    }
}
