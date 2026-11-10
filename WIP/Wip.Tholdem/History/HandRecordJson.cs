using System.Text.Json;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;

namespace Wip.Tholdem.History;

/// <summary>
/// Reads the canonical JSON form written by <see cref="HandRecord.ToCanonicalJson"/>. Whitespace is
/// irrelevant; property names and value types are not. Every malformed input throws
/// <see cref="FormatException"/> with a stable reason token and the failing field.
/// </summary>
public static class HandRecordJson
{
    private static readonly string[] KnownActionKinds = ["Fold", "Check", "Call", "Bet", "Raise", "AllIn"];

    public static HandRecord Parse(string json)
    {
        if (json is null)
        {
            throw new FormatException("hand-json-null: the hand record text is null.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException(
                "hand-json-invalid: the hand record is not valid JSON: " + exception.Message,
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("hand-json-object: the hand record must be a JSON object.");
            }

            if (root.TryGetProperty("seats", out _))
            {
                return ParseV2(root);
            }

            var handNumber = RequiredInt32(root, "handNumber");
            if (handNumber < 1)
            {
                throw new FormatException("hand-field-range: field 'handNumber' must be positive.");
            }

            var seed = RequiredUInt64(root, "seed");
            var smallBlind = RequiredInt64(root, "smallBlind");
            var bigBlind = RequiredInt64(root, "bigBlind");
            var startingStack = RequiredInt64(root, "startingStack");
            if (smallBlind <= 0)
            {
                throw new FormatException("hand-field-range: field 'smallBlind' must be positive.");
            }

            if (bigBlind <= 0)
            {
                throw new FormatException("hand-field-range: field 'bigBlind' must be positive.");
            }

            if (startingStack <= 0)
            {
                throw new FormatException("hand-field-range: field 'startingStack' must be positive.");
            }

            if (smallBlind > bigBlind)
            {
                throw new FormatException("hand-field-range: field 'smallBlind' cannot exceed 'bigBlind'.");
            }

            if (startingStack < bigBlind)
            {
                throw new FormatException("hand-field-range: field 'startingStack' must cover 'bigBlind'.");
            }

            var buttonSeat = RequiredInt32(root, "buttonSeat");
            if (buttonSeat is not (0 or 1))
            {
                throw new FormatException("hand-field-range: field 'buttonSeat' must be 0 or 1.");
            }

            var holeCardsSeat0 = RequiredCards(root, "holeCardsSeat0");
            var holeCardsSeat1 = RequiredCards(root, "holeCardsSeat1");
            if (holeCardsSeat0.Count != 2 || holeCardsSeat1.Count != 2)
            {
                throw new FormatException(
                    "hand-field-range: fields 'holeCardsSeat0' and 'holeCardsSeat1' must each hold two cards.");
            }

            var board = RequiredCards(root, "board");
            if (board.Count > 5)
            {
                throw new FormatException("hand-field-range: field 'board' cannot hold more than five cards.");
            }

            var actions = RequiredActions(root, "actions");
            var showdown = RequiredBoolean(root, "showdown");
            var winners = RequiredWinners(root, "winners");
            var potAwarded = RequiredInt64(root, "potAwarded");
            if (potAwarded < 0)
            {
                throw new FormatException("hand-field-range: field 'potAwarded' must not be negative.");
            }

            var finalStacks = RequiredFinalStacks(root, "finalStacks");

            return new HandRecord(
                handNumber,
                seed,
                smallBlind,
                bigBlind,
                startingStack,
                buttonSeat,
                holeCardsSeat0,
                holeCardsSeat1,
                board,
                actions,
                showdown,
                winners,
                potAwarded,
                finalStacks);
        }
    }

    private static HandRecord ParseV2(JsonElement root)
    {
        if (!root.TryGetProperty("schema", out var schema)
            || schema.ValueKind != JsonValueKind.String
            || !string.Equals(schema.GetString(), "tholdem.hand.v2", StringComparison.Ordinal))
        {
            throw new FormatException("hand-schema: a v2 hand record must declare schema 'tholdem.hand.v2'.");
        }

        var seats = RequiredInt32(root, "seats");
        if (seats is < 2 or > 6)
        {
            throw new FormatException("hand-field-range: field 'seats' must be between 2 and 6.");
        }

        var handNumber = RequiredInt32(root, "handNumber");
        if (handNumber < 1)
        {
            throw new FormatException("hand-field-range: field 'handNumber' must be positive.");
        }

        var seed = RequiredUInt64(root, "seed");
        var smallBlind = RequiredInt64(root, "smallBlind");
        var bigBlind = RequiredInt64(root, "bigBlind");
        var startingStack = RequiredInt64(root, "startingStack");
        if (smallBlind <= 0)
        {
            throw new FormatException("hand-field-range: field 'smallBlind' must be positive.");
        }

        if (bigBlind <= 0)
        {
            throw new FormatException("hand-field-range: field 'bigBlind' must be positive.");
        }

        if (startingStack <= 0)
        {
            throw new FormatException("hand-field-range: field 'startingStack' must be positive.");
        }

        if (smallBlind > bigBlind)
        {
            throw new FormatException("hand-field-range: field 'smallBlind' cannot exceed 'bigBlind'.");
        }

        if (startingStack < bigBlind)
        {
            throw new FormatException("hand-field-range: field 'startingStack' must cover 'bigBlind'.");
        }

        var startingStacks = RequiredStartingStacks(root, "startingStacks", seats, bigBlind);

        var buttonSeat = RequiredInt32(root, "buttonSeat");
        if (buttonSeat < 0 || buttonSeat >= seats)
        {
            throw new FormatException("hand-field-range: field 'buttonSeat' must be a seat between 0 and 'seats' - 1.");
        }

        var holeCards = RequiredHandCards(root, "holeCards", seats);
        var board = RequiredCards(root, "board");
        if (board.Count > 5)
        {
            throw new FormatException("hand-field-range: field 'board' cannot hold more than five cards.");
        }

        var actions = RequiredActionsForSeats(root, "actions", seats);
        var pots = RequiredPots(root, "pots", seats);
        var showdown = RequiredBoolean(root, "showdown");
        var winners = RequiredWinnersForSeats(root, "winners", seats);
        var potAwarded = RequiredInt64(root, "potAwarded");
        if (potAwarded < 0)
        {
            throw new FormatException("hand-field-range: field 'potAwarded' must not be negative.");
        }

        var finalStacks = RequiredFinalStacksForSeats(root, "finalStacks", seats);

        return new HandRecord(
            handNumber,
            seed,
            smallBlind,
            bigBlind,
            startingStack,
            buttonSeat,
            holeCards[0],
            holeCards[1],
            board,
            actions,
            showdown,
            winners,
            potAwarded,
            finalStacks)
        {
            SeatCount = seats,
            StartingStacks = startingStacks,
            HoleCards = holeCards,
            Pots = pots,
        };
    }

    private static IReadOnlyList<IReadOnlyList<Card>> RequiredHandCards(
        JsonElement root,
        string name,
        int seats)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != seats)
        {
            throw new FormatException(
                $"hand-field-range: field '{name}' must be an array of exactly {seats} hole-card arrays.");
        }

        var hands = new IReadOnlyList<Card>[seats];
        var handIndex = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2)
            {
                throw new FormatException(
                    $"hand-field-range: {name}[{handIndex}] must hold exactly two cards.");
            }

            var cards = new Card[2];
            var cardIndex = 0;
            foreach (var card in item.EnumerateArray())
            {
                if (card.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException(
                        $"hand-card-invalid: {name}[{handIndex}][{cardIndex}] must be a card string.");
                }

                var text = card.GetString()!;
                try
                {
                    cards[cardIndex] = Card.Parse(text);
                }
                catch (ArgumentException exception)
                {
                    throw new FormatException(
                        $"hand-card-invalid: {name}[{handIndex}][{cardIndex}] contains invalid card text "
                        + $"'{text}': {exception.Message}",
                        exception);
                }

                cardIndex++;
            }

            hands[handIndex] = cards;
            handIndex++;
        }

        return hands;
    }

    private static IReadOnlyList<HandActionRecord> RequiredActionsForSeats(
        JsonElement root,
        string name,
        int seats)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"hand-field-type: field '{name}' must be an array of action objects.");
        }

        var actions = new HandActionRecord[value.GetArrayLength()];
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"hand-action-invalid: {name}[{index}] must be an object.");
            }

            var street = ActionInt32(item, name, index, "street");
            if (street is < 0 or > 5)
            {
                throw new FormatException(
                    $"hand-action-invalid: {name}[{index}].street must be between 0 and 5.");
            }

            var seat = ActionInt32(item, name, index, "seat");
            if (seat < 0 || seat >= seats)
            {
                throw new FormatException(
                    $"hand-action-invalid: {name}[{index}].seat must be a seat between 0 and {seats - 1}.");
            }

            var kind = ActionString(item, name, index, "kind");
            if (Array.IndexOf(KnownActionKinds, kind) < 0)
            {
                throw new FormatException(
                    $"hand-action-invalid: {name}[{index}].kind '{kind}' is not a defined action kind.");
            }

            var amount = ActionInt64(item, name, index, "amount");
            if (kind is not ("Bet" or "Raise") && amount != 0)
            {
                throw new FormatException(
                    $"hand-action-invalid: {name}[{index}].amount must be zero for kind '{kind}'.");
            }

            if (amount < 0)
            {
                throw new FormatException($"hand-action-invalid: {name}[{index}].amount must not be negative.");
            }

            var potAfter = ActionInt64(item, name, index, "potAfter");
            if (potAfter < 0)
            {
                throw new FormatException($"hand-action-invalid: {name}[{index}].potAfter must not be negative.");
            }

            actions[index] = new HandActionRecord(street, seat, kind, amount, potAfter);
            index++;
        }

        return actions;
    }

    private static IReadOnlyList<PotAward> RequiredPots(JsonElement root, string name, int seats)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"hand-field-type: field '{name}' must be an array of pot layers.");
        }

        var pots = new PotAward[value.GetArrayLength()];
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"hand-pot-invalid: {name}[{index}] must be an object.");
            }

            var amount = ActionInt64(item, name, index, "amount");
            if (amount < 0)
            {
                throw new FormatException($"hand-pot-invalid: {name}[{index}].amount must not be negative.");
            }

            var winners = RequiredWinnersForSeats(item, name + "[" + index + "].winners", seats);
            pots[index] = new PotAward(amount, winners);
            index++;
        }

        return pots;
    }

    private static IReadOnlyList<int> RequiredWinnersForSeats(
        JsonElement item,
        string name,
        int seats)
    {
        if (!item.TryGetProperty("winners", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"hand-field-type: field '{name}' must be an array of seat numbers.");
        }

        var winners = new int[value.GetArrayLength()];
        var index = 0;
        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Number || !entry.TryGetInt32(out var seat))
            {
                throw new FormatException($"hand-field-type: {name}[{index}] must be an integer seat number.");
            }

            if (seat < 0 || seat >= seats)
            {
                throw new FormatException($"hand-field-range: {name}[{index}] must be between 0 and {seats - 1}.");
            }

            winners[index] = seat;
            index++;
        }

        return winners;
    }

    private static IReadOnlyList<long> RequiredStartingStacks(
        JsonElement root,
        string name,
        int seats,
        long bigBlind)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != seats)
        {
            throw new FormatException(
                $"hand-field-range: field '{name}' must hold exactly {seats} starting-stack sizes.");
        }

        var stacks = new long[seats];
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out var stack))
            {
                throw new FormatException($"hand-field-type: {name}[{index}] must be a 64-bit integer.");
            }

            if (stack < bigBlind)
            {
                throw new FormatException(
                    $"hand-field-range: {name}[{index}] must cover the big blind {bigBlind}.");
            }

            stacks[index] = stack;
            index++;
        }

        return stacks;
    }

    private static IReadOnlyList<long> RequiredFinalStacksForSeats(JsonElement root, string name, int seats)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != seats)
        {
            throw new FormatException(
                $"hand-field-range: field '{name}' must hold exactly {seats} stack sizes.");
        }

        var stacks = new long[seats];
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out var stack))
            {
                throw new FormatException($"hand-field-type: {name}[{index}] must be a 64-bit integer.");
            }

            if (stack < 0)
            {
                throw new FormatException($"hand-field-range: {name}[{index}] must not be negative.");
            }

            stacks[index] = stack;
            index++;
        }

        return stacks;
    }

    private static JsonElement Required(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            throw new FormatException($"hand-field-missing: required field '{name}' is missing.");
        }

        return value;
    }

    private static int RequiredInt32(JsonElement root, string name)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new FormatException($"hand-field-type: field '{name}' must be a 32-bit integer.");
        }

        return result;
    }

    private static long RequiredInt64(JsonElement root, string name)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw new FormatException($"hand-field-type: field '{name}' must be a 64-bit integer.");
        }

        return result;
    }

    private static ulong RequiredUInt64(JsonElement root, string name)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out var result))
        {
            throw new FormatException($"hand-field-type: field '{name}' must be an unsigned 64-bit integer.");
        }

        return result;
    }

    private static bool RequiredBoolean(JsonElement root, string name)
    {
        var value = Required(root, name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new FormatException($"hand-field-type: field '{name}' must be a boolean.");
        }

        return value.GetBoolean();
    }

    private static IReadOnlyList<Card> RequiredCards(JsonElement root, string name)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"hand-field-type: field '{name}' must be an array of card strings.");
        }

        var cards = new Card[value.GetArrayLength()];
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new FormatException($"hand-card-invalid: {name}[{index}] must be a card string.");
            }

            var text = item.GetString()!;
            try
            {
                cards[index] = Card.Parse(text);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException(
                    $"hand-card-invalid: {name}[{index}] contains invalid card text '{text}': "
                    + exception.Message,
                    exception);
            }

            index++;
        }

        return cards;
    }

    private static IReadOnlyList<HandActionRecord> RequiredActions(JsonElement root, string name)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"hand-field-type: field '{name}' must be an array of action objects.");
        }

        var actions = new HandActionRecord[value.GetArrayLength()];
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"hand-action-invalid: {name}[{index}] must be an object.");
            }

            var street = ActionInt32(item, name, index, "street");
            if (street is < 0 or > 5)
            {
                throw new FormatException(
                    $"hand-action-invalid: {name}[{index}].street must be between 0 and 5.");
            }

            var seat = ActionInt32(item, name, index, "seat");
            if (seat is not (0 or 1))
            {
                throw new FormatException($"hand-action-invalid: {name}[{index}].seat must be 0 or 1.");
            }

            var kind = ActionString(item, name, index, "kind");
            if (Array.IndexOf(KnownActionKinds, kind) < 0)
            {
                throw new FormatException(
                    $"hand-action-invalid: {name}[{index}].kind '{kind}' is not a defined action kind.");
            }

            var amount = ActionInt64(item, name, index, "amount");
            if (kind is not ("Bet" or "Raise") && amount != 0)
            {
                throw new FormatException(
                    $"hand-action-invalid: {name}[{index}].amount must be zero for kind '{kind}'.");
            }

            if (amount < 0)
            {
                throw new FormatException($"hand-action-invalid: {name}[{index}].amount must not be negative.");
            }

            var potAfter = ActionInt64(item, name, index, "potAfter");
            if (potAfter < 0)
            {
                throw new FormatException($"hand-action-invalid: {name}[{index}].potAfter must not be negative.");
            }

            actions[index] = new HandActionRecord(street, seat, kind, amount, potAfter);
            index++;
        }

        return actions;
    }

    private static IReadOnlyList<int> RequiredWinners(JsonElement root, string name)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"hand-field-type: field '{name}' must be an array of seat numbers.");
        }

        var winners = new int[value.GetArrayLength()];
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var seat))
            {
                throw new FormatException($"hand-field-type: {name}[{index}] must be an integer seat number.");
            }

            if (seat is not (0 or 1))
            {
                throw new FormatException($"hand-field-range: {name}[{index}] must be 0 or 1.");
            }

            winners[index] = seat;
            index++;
        }

        return winners;
    }

    private static IReadOnlyList<long> RequiredFinalStacks(JsonElement root, string name)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"hand-field-type: field '{name}' must be an array of two stack sizes.");
        }

        var stacks = new long[value.GetArrayLength()];
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out var stack))
            {
                throw new FormatException($"hand-field-type: {name}[{index}] must be a 64-bit integer.");
            }

            if (stack < 0)
            {
                throw new FormatException($"hand-field-range: {name}[{index}] must not be negative.");
            }

            stacks[index] = stack;
            index++;
        }

        if (stacks.Length != 2)
        {
            throw new FormatException($"hand-field-range: field '{name}' must hold exactly two stack sizes.");
        }

        return stacks;
    }

    private static int ActionInt32(JsonElement action, string name, int index, string field)
    {
        if (!action.TryGetProperty(field, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result))
        {
            throw new FormatException($"hand-action-invalid: {name}[{index}].{field} must be an integer.");
        }

        return result;
    }

    private static long ActionInt64(JsonElement action, string name, int index, string field)
    {
        if (!action.TryGetProperty(field, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result))
        {
            throw new FormatException($"hand-action-invalid: {name}[{index}].{field} must be a 64-bit integer.");
        }

        return result;
    }

    private static string ActionString(JsonElement action, string name, int index, string field)
    {
        if (!action.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"hand-action-invalid: {name}[{index}].{field} must be a string.");
        }

        return value.GetString()!;
    }
}
