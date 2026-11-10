using Wip.Tholdem.Cards;
using Wip.Tholdem.Hands;

namespace Wip.Tholdem.Agents.Baselines;

/// <summary>
/// A deterministic strength estimate in [0, 1]. Preflop uses a chart-like rank score; postflop uses
/// the evaluated hand category plus its top kicker rank.
/// </summary>
internal static class HandStrength
{
    public static double Estimate(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
    {
        if (hole.Count != 2)
        {
            throw new ArgumentException(
                $"Exactly two hole cards are required; received {hole.Count}.",
                nameof(hole));
        }

        return board.Count >= 3 ? Postflop(hole, board) : Preflop(hole[0], hole[1]);
    }

    private static double Preflop(Card first, Card second)
    {
        var high = Math.Max((int)first.Rank, (int)second.Rank);
        var low = Math.Min((int)first.Rank, (int)second.Rank);
        double score;
        if (high == low)
        {
            score = 0.46 + (0.49 * ((high - 2) / 12.0));
        }
        else
        {
            score = 0.28 + (0.42 * ((high + low - 4) / 24.0));
            if (first.Suit == second.Suit)
            {
                score += 0.06;
            }

            var gap = high - low;
            if (gap == 1)
            {
                score += 0.04;
            }
            else if (gap == 2)
            {
                score += 0.02;
            }
            else if (gap == 4)
            {
                score -= 0.03;
            }
            else if (gap >= 5)
            {
                score -= 0.05;
            }
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    private static double Postflop(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
    {
        var cards = new Card[hole.Count + board.Count];
        for (var index = 0; index < hole.Count; index++)
        {
            cards[index] = hole[index];
        }

        for (var index = 0; index < board.Count; index++)
        {
            cards[hole.Count + index] = board[index];
        }

        var rank = HandEvaluator.Evaluate(cards);
        var baseline = rank.Category switch
        {
            HandCategory.HighCard => 0.10,
            HandCategory.OnePair => 0.30,
            HandCategory.TwoPair => 0.46,
            HandCategory.ThreeOfAKind => 0.60,
            HandCategory.Straight => 0.72,
            HandCategory.Flush => 0.80,
            HandCategory.FullHouse => 0.86,
            HandCategory.FourOfAKind => 0.93,
            HandCategory.StraightFlush => 0.98,
            _ => 0.10,
        };
        var top = rank.Tiebreakers.Count > 0 ? rank.Tiebreakers[0] : 2;
        var kicker = (top - 2) / 12.0 * 0.06;
        return Math.Clamp(baseline + kicker, 0.0, 1.0);
    }
}