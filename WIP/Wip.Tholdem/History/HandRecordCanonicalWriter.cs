using System.Globalization;
using System.Text;
using Wip.Tholdem.Cards;

namespace Wip.Tholdem.History;

/// <summary>
/// Writes the canonical, whitespace-free, culture-free JSON form of a <see cref="HandRecord"/>.
/// The property order is fixed and the hash is never part of the document.
/// </summary>
public static class HandRecordCanonicalWriter
{
    public static string Write(HandRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.SeatCount == 2 && AllStartingStacksEqual(record)
            ? WriteV1(record)
            : WriteV2(record);
    }

    private static bool AllStartingStacksEqual(HandRecord record)
    {
        if (record.StartingStacks.Count != record.SeatCount || record.StartingStacks.Count == 0)
        {
            return false;
        }

        var first = record.StartingStacks[0];
        for (var seat = 1; seat < record.StartingStacks.Count; seat++)
        {
            if (record.StartingStacks[seat] != first)
            {
                return false;
            }
        }

        return true;
    }

    private static string WriteV1(HandRecord record)
    {
        var builder = new StringBuilder(512);
        builder.Append('{');
        AppendProperty(builder, "handNumber", record.HandNumber);
        builder.Append(',');
        AppendProperty(builder, "seed", record.Seed);
        builder.Append(',');
        AppendProperty(builder, "smallBlind", record.SmallBlind);
        builder.Append(',');
        AppendProperty(builder, "bigBlind", record.BigBlind);
        builder.Append(',');
        AppendProperty(builder, "startingStack", record.StartingStack);
        builder.Append(',');
        AppendProperty(builder, "buttonSeat", record.ButtonSeat);
        builder.Append(',');
        AppendCardsProperty(builder, "holeCardsSeat0", record.HoleCardsSeat0);
        builder.Append(',');
        AppendCardsProperty(builder, "holeCardsSeat1", record.HoleCardsSeat1);
        builder.Append(',');
        AppendCardsProperty(builder, "board", record.Board);
        builder.Append(',');
        builder.Append("\"actions\":[");
        for (var index = 0; index < record.Actions.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var action = record.Actions[index];
            builder.Append('{');
            AppendProperty(builder, "street", action.Street);
            builder.Append(',');
            AppendProperty(builder, "seat", action.Seat);
            builder.Append(',');
            AppendStringProperty(builder, "kind", action.Kind);
            builder.Append(',');
            AppendProperty(builder, "amount", action.Amount);
            builder.Append(',');
            AppendProperty(builder, "potAfter", action.PotAfter);
            builder.Append('}');
        }

        builder.Append(']');
        builder.Append(',');
        AppendProperty(builder, "showdown", record.Showdown);
        builder.Append(',');
        AppendIntsProperty(builder, "winners", record.Winners);
        builder.Append(',');
        AppendProperty(builder, "potAwarded", record.PotAwarded);
        builder.Append(',');
        AppendLongsProperty(builder, "finalStacks", record.FinalStacks);
        builder.Append('}');
        return builder.ToString();
    }

    private static string WriteV2(HandRecord record)
    {
        var builder = new StringBuilder(640);
        builder.Append('{');
        AppendStringProperty(builder, "schema", "tholdem.hand.v2");
        builder.Append(',');
        AppendProperty(builder, "handNumber", record.HandNumber);
        builder.Append(',');
        AppendProperty(builder, "seed", record.Seed);
        builder.Append(',');
        AppendProperty(builder, "smallBlind", record.SmallBlind);
        builder.Append(',');
        AppendProperty(builder, "bigBlind", record.BigBlind);
        builder.Append(',');
        AppendProperty(builder, "startingStack", record.StartingStack);
        builder.Append(',');
        AppendLongsProperty(builder, "startingStacks", record.StartingStacks);
        builder.Append(',');
        AppendProperty(builder, "buttonSeat", record.ButtonSeat);
        builder.Append(',');
        AppendProperty(builder, "seats", record.SeatCount);
        builder.Append(',');
        AppendCardsArrayProperty(builder, "holeCards", record.HoleCards);
        builder.Append(',');
        AppendCardsProperty(builder, "board", record.Board);
        builder.Append(',');
        builder.Append("\"actions\":[");
        for (var index = 0; index < record.Actions.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var action = record.Actions[index];
            builder.Append('{');
            AppendProperty(builder, "street", action.Street);
            builder.Append(',');
            AppendProperty(builder, "seat", action.Seat);
            builder.Append(',');
            AppendStringProperty(builder, "kind", action.Kind);
            builder.Append(',');
            AppendProperty(builder, "amount", action.Amount);
            builder.Append(',');
            AppendProperty(builder, "potAfter", action.PotAfter);
            builder.Append('}');
        }

        builder.Append(']');
        builder.Append(',');
        AppendPotsProperty(builder, "pots", record.Pots);
        builder.Append(',');
        AppendProperty(builder, "showdown", record.Showdown);
        builder.Append(',');
        AppendIntsProperty(builder, "winners", record.Winners);
        builder.Append(',');
        AppendProperty(builder, "potAwarded", record.PotAwarded);
        builder.Append(',');
        AppendLongsProperty(builder, "finalStacks", record.FinalStacks);
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendCardsArrayProperty(
        StringBuilder builder,
        string name,
        IReadOnlyList<IReadOnlyList<Card>> hands)
    {
        AppendString(builder, name);
        builder.Append(':');
        builder.Append('[');
        for (var handIndex = 0; handIndex < hands.Count; handIndex++)
        {
            if (handIndex > 0)
            {
                builder.Append(',');
            }

            builder.Append('[');
            var cards = hands[handIndex];
            for (var cardIndex = 0; cardIndex < cards.Count; cardIndex++)
            {
                if (cardIndex > 0)
                {
                    builder.Append(',');
                }

                AppendString(builder, cards[cardIndex].ToString());
            }

            builder.Append(']');
        }

        builder.Append(']');
    }

    private static void AppendPotsProperty(
        StringBuilder builder,
        string name,
        IReadOnlyList<Wip.Tholdem.Engine.PotAward> pots)
    {
        AppendString(builder, name);
        builder.Append(':');
        builder.Append('[');
        for (var index = 0; index < pots.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append('{');
            AppendProperty(builder, "amount", pots[index].Amount);
            builder.Append(',');
            AppendIntsProperty(builder, "winners", pots[index].Winners);
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

    private static void AppendProperty(StringBuilder builder, string name, ulong value)
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

    private static void AppendStringProperty(StringBuilder builder, string name, string value)
    {
        AppendString(builder, name);
        builder.Append(':');
        AppendString(builder, value);
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

    private static void AppendLongsProperty(StringBuilder builder, string name, IReadOnlyList<long> values)
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
