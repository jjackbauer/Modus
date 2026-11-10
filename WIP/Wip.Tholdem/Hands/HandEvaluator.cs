using Wip.Tholdem.Cards;

namespace Wip.Tholdem.Hands;

/// <summary>
/// Evaluates five, six or seven cards and returns the best five-card hand as a
/// <see cref="HandRank"/>. The evaluator is deterministic: combinations are visited in a fixed
/// lexicographic order and only strictly stronger candidates replace the current best.
/// </summary>
public static class HandEvaluator
{
    public static HandRank Evaluate(IReadOnlyList<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        if (cards.Count is < 5 or > 7)
        {
            throw new ArgumentException(
                $"A hand must contain 5, 6 or 7 cards; received {cards.Count}.",
                nameof(cards));
        }

        for (var i = 0; i < cards.Count; i++)
        {
            for (var j = i + 1; j < cards.Count; j++)
            {
                if (cards[i] == cards[j])
                {
                    throw new ArgumentException($"Duplicate card '{cards[i]}' in the hand.", nameof(cards));
                }
            }
        }

        if (cards.Count == 5)
        {
            return EvaluateFive(cards[0], cards[1], cards[2], cards[3], cards[4]);
        }

        var best = default(HandRank);
        var hasBest = false;
        var count = cards.Count;
        for (var i0 = 0; i0 <= count - 5; i0++)
        {
            for (var i1 = i0 + 1; i1 <= count - 4; i1++)
            {
                for (var i2 = i1 + 1; i2 <= count - 3; i2++)
                {
                    for (var i3 = i2 + 1; i3 <= count - 2; i3++)
                    {
                        for (var i4 = i3 + 1; i4 < count; i4++)
                        {
                            var candidate = EvaluateFive(
                                cards[i0],
                                cards[i1],
                                cards[i2],
                                cards[i3],
                                cards[i4]);

                            if (!hasBest || candidate.CompareTo(best) > 0)
                            {
                                best = candidate;
                                hasBest = true;
                            }
                        }
                    }
                }
            }
        }

        return best;
    }

    private static HandRank EvaluateFive(Card card0, Card card1, Card card2, Card card3, Card card4)
    {
        Span<Card> hand = stackalloc Card[5];
        hand[0] = card0;
        hand[1] = card1;
        hand[2] = card2;
        hand[3] = card3;
        hand[4] = card4;

        Span<int> counts = stackalloc int[15];
        foreach (var card in hand)
        {
            counts[(int)card.Rank]++;
        }

        var isFlush = card0.Suit == card1.Suit
            && card0.Suit == card2.Suit
            && card0.Suit == card3.Suit
            && card0.Suit == card4.Suit;

        Span<int> groupRanks = stackalloc int[5];
        Span<int> groupCounts = stackalloc int[5];
        var groupCount = 0;
        for (var rank = 14; rank >= 2; rank--)
        {
            var count = counts[rank];
            if (count == 0)
            {
                continue;
            }

            var index = groupCount;
            while (index > 0
                && (groupCounts[index - 1] < count
                    || (groupCounts[index - 1] == count && groupRanks[index - 1] < rank)))
            {
                groupCounts[index] = groupCounts[index - 1];
                groupRanks[index] = groupRanks[index - 1];
                index--;
            }

            groupCounts[index] = count;
            groupRanks[index] = rank;
            groupCount++;
        }

        var straightHigh = 0;
        for (var high = 14; high >= 6; high--)
        {
            if (counts[high] > 0
                && counts[high - 1] > 0
                && counts[high - 2] > 0
                && counts[high - 3] > 0
                && counts[high - 4] > 0)
            {
                straightHigh = high;
                break;
            }
        }

        if (straightHigh == 0
            && counts[14] > 0
            && counts[5] > 0
            && counts[4] > 0
            && counts[3] > 0
            && counts[2] > 0)
        {
            straightHigh = 5;
        }

        if (isFlush && straightHigh > 0)
        {
            return new HandRank(HandCategory.StraightFlush, new[] { straightHigh });
        }

        if (groupCounts[0] == 4)
        {
            return new HandRank(HandCategory.FourOfAKind, new[] { groupRanks[0], groupRanks[1] });
        }

        if (groupCounts[0] == 3 && groupCounts[1] >= 2)
        {
            return new HandRank(HandCategory.FullHouse, new[] { groupRanks[0], groupRanks[1] });
        }

        if (isFlush)
        {
            return new HandRank(
                HandCategory.Flush,
                new[] { groupRanks[0], groupRanks[1], groupRanks[2], groupRanks[3], groupRanks[4] });
        }

        if (straightHigh > 0)
        {
            return new HandRank(HandCategory.Straight, new[] { straightHigh });
        }

        if (groupCounts[0] == 3)
        {
            return new HandRank(
                HandCategory.ThreeOfAKind,
                new[] { groupRanks[0], groupRanks[1], groupRanks[2] });
        }

        if (groupCounts[0] == 2 && groupCounts[1] == 2)
        {
            return new HandRank(
                HandCategory.TwoPair,
                new[] { groupRanks[0], groupRanks[1], groupRanks[2] });
        }

        if (groupCounts[0] == 2)
        {
            return new HandRank(
                HandCategory.OnePair,
                new[] { groupRanks[0], groupRanks[1], groupRanks[2], groupRanks[3] });
        }

        return new HandRank(
            HandCategory.HighCard,
            new[] { groupRanks[0], groupRanks[1], groupRanks[2], groupRanks[3], groupRanks[4] });
    }
}
