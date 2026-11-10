using System.Globalization;
using System.Text;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;

namespace Wip.Tholdem.History;

/// <summary>
/// The public projection of one seat: stack, current-street commitment and the two public status
/// flags. It never carries hole cards.
/// </summary>
public sealed record SeatPublicState(
    int Seat,
    long Stack,
    long StreetCommitment,
    bool Folded,
    bool AllIn);

/// <summary>
/// The hidden-information-safe projection of a hand for one seat. It contains no seed, no deck
/// order and no other seat's hole cards, and it deliberately does not reference <see cref="HandRecord"/>.
/// </summary>
/// <remarks>
/// The canonical JSON of a two-seat observation is byte-identical to the pre-multiway form: the
/// <c>seatCount</c>, <c>seat</c> and <c>seats</c> members are only emitted for three or more seats,
/// which keeps every recorded agent-seed fixture stable.
/// </remarks>
public sealed record PlayerObservation
{
    internal PlayerObservation()
    {
        Board = Array.Empty<Card>();
        OwnHoleCards = Array.Empty<Card>();
        LegalActionKinds = Array.Empty<ActionKind>();
        Winners = Array.Empty<int>();
        Seats = Array.Empty<SeatPublicState>();
    }

    public int HandNumber { get; internal init; }

    public Street Street { get; internal init; }

    public int ButtonSeat { get; internal init; }

    public int ActingSeat { get; internal init; }

    public int SeatCount { get; internal init; }

    public int Seat { get; internal init; }

    public IReadOnlyList<Card> Board { get; internal init; }

    public IReadOnlyList<Card> OwnHoleCards { get; internal init; }

    public long OwnStack { get; internal init; }

    public long OpponentStack { get; internal init; }

    public long Pot { get; internal init; }

    public long OwnStreetCommitment { get; internal init; }

    public long OpponentStreetCommitment { get; internal init; }

    public long AmountToCall { get; internal init; }

    public long MinRaiseTo { get; internal init; }

    public long MaxRaiseTo { get; internal init; }

    public IReadOnlyList<ActionKind> LegalActionKinds { get; internal init; }

    public bool IsComplete { get; internal init; }

    public IReadOnlyList<int> Winners { get; internal init; }

    public IReadOnlyList<SeatPublicState> Seats { get; internal init; }

    public static PlayerObservation ForSeat(TableState state, int seat)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (seat < 0 || seat >= state.SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seat),
                seat,
                $"A seat must be between 0 and {state.SeatCount - 1}.");
        }

        var legal = HoldemHandEngine.GetLegalActions(state);
        var opponentStack = 0L;
        var opponentCommitment = 0L;
        var publicSeats = new SeatPublicState[state.SeatCount];
        for (var candidate = 0; candidate < state.SeatCount; candidate++)
        {
            publicSeats[candidate] = new SeatPublicState(
                candidate,
                state.Stacks[candidate],
                state.StreetCommitted[candidate],
                state.Folded[candidate],
                state.AllIn[candidate]);

            if (candidate != seat)
            {
                opponentStack += state.Stacks[candidate];
                opponentCommitment += state.StreetCommitted[candidate];
            }
        }

        return new PlayerObservation
        {
            HandNumber = state.HandNumber,
            Street = state.Street,
            ButtonSeat = state.ButtonSeat,
            ActingSeat = state.ActingSeat,
            SeatCount = state.SeatCount,
            Seat = seat,
            Board = state.Board.ToArray(),
            OwnHoleCards = state.HoleCards[seat].ToArray(),
            OwnStack = state.Stacks[seat],
            OpponentStack = opponentStack,
            Pot = state.Pot,
            OwnStreetCommitment = state.StreetCommitted[seat],
            OpponentStreetCommitment = opponentCommitment,
            AmountToCall = legal.CallAmount,
            MinRaiseTo = legal.MinRaiseTo,
            MaxRaiseTo = legal.MaxRaiseTo,
            LegalActionKinds = legal.Actions.Select(action => action.Kind).ToArray(),
            IsComplete = state.IsComplete,
            Winners = state.Winners.ToArray(),
            Seats = publicSeats,
        };
    }

    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(SeatCount == 2 ? 320 : 480);
        builder.Append('{');
        AppendProperty(builder, "handNumber", HandNumber);
        builder.Append(',');
        AppendProperty(builder, "street", (int)Street);
        builder.Append(',');
        AppendProperty(builder, "buttonSeat", ButtonSeat);
        builder.Append(',');
        AppendProperty(builder, "actingSeat", ActingSeat);
        builder.Append(',');
        AppendCardsProperty(builder, "board", Board);
        builder.Append(',');
        AppendCardsProperty(builder, "ownHoleCards", OwnHoleCards);
        builder.Append(',');
        AppendProperty(builder, "ownStack", OwnStack);
        builder.Append(',');
        AppendProperty(builder, "opponentStack", OpponentStack);
        builder.Append(',');
        AppendProperty(builder, "pot", Pot);
        builder.Append(',');
        AppendProperty(builder, "ownStreetCommitment", OwnStreetCommitment);
        builder.Append(',');
        AppendProperty(builder, "opponentStreetCommitment", OpponentStreetCommitment);
        builder.Append(',');
        AppendProperty(builder, "amountToCall", AmountToCall);
        builder.Append(',');
        AppendProperty(builder, "minRaiseTo", MinRaiseTo);
        builder.Append(',');
        AppendProperty(builder, "maxRaiseTo", MaxRaiseTo);
        builder.Append(',');
        builder.Append("\"legalActionKinds\":[");
        for (var index = 0; index < LegalActionKinds.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendString(builder, LegalActionKinds[index].ToString());
        }

        builder.Append(']');
        builder.Append(',');
        AppendProperty(builder, "isComplete", IsComplete);
        builder.Append(',');
        AppendIntsProperty(builder, "winners", Winners);

        if (SeatCount != 2)
        {
            builder.Append(',');
            AppendProperty(builder, "seatCount", SeatCount);
            builder.Append(',');
            AppendProperty(builder, "seat", Seat);
            builder.Append(',');
            AppendSeatsProperty(builder, "seats", Seats);
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendSeatsProperty(
        StringBuilder builder,
        string name,
        IReadOnlyList<SeatPublicState> seats)
    {
        AppendString(builder, name);
        builder.Append(':');
        builder.Append('[');
        for (var index = 0; index < seats.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append('{');
            AppendProperty(builder, "seat", seats[index].Seat);
            builder.Append(',');
            AppendProperty(builder, "stack", seats[index].Stack);
            builder.Append(',');
            AppendProperty(builder, "streetCommitment", seats[index].StreetCommitment);
            builder.Append(',');
            AppendProperty(builder, "folded", seats[index].Folded);
            builder.Append(',');
            AppendProperty(builder, "allIn", seats[index].AllIn);
            builder.Append('}');
        }

        builder.Append(']');
    }

    private static void AppendProperty(StringBuilder builder, string name, long value)
    {
        AppendString(builder, name);
        builder.Append(':');
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendProperty(StringBuilder builder, string name, int value)
    {
        AppendString(builder, name);
        builder.Append(':');
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendProperty(StringBuilder builder, string name, bool value)
    {
        AppendString(builder, name);
        builder.Append(':');
        builder.Append(value ? "true" : "false");
    }

    private static void AppendCardsProperty(StringBuilder builder, string name, IReadOnlyList<Card> cards)
    {
        AppendString(builder, name);
        builder.Append(':');
        builder.Append('[');
        for (var index = 0; index < cards.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendString(builder, cards[index].ToString());
        }

        builder.Append(']');
    }

    private static void AppendIntsProperty(StringBuilder builder, string name, IReadOnlyList<int> values)
    {
        AppendString(builder, name);
        builder.Append(':');
        builder.Append('[');
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(values[index].ToString(CultureInfo.InvariantCulture));
        }

        builder.Append(']');
    }

    private static void AppendString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < ' ')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
