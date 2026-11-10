using Wip.Tholdem.Agents.Randomness;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Hands;

namespace Wip.Tholdem.Agents.Equity;

/// <summary>
/// Seeded Monte Carlo equity of two hole cards against one uniformly random opponent hand and a
/// uniformly random completion of the board. The remaining deck is fixed in canonical card order
/// and partially shuffled per sample by <see cref="Xorshift64Star"/>.
/// </summary>
public static class MonteCarloEquity
{
    public static EquityResult Estimate(
        IReadOnlyList<Card> ownHoleCards,
        IReadOnlyList<Card> board,
        ulong seed,
        int samples)
    {
        ArgumentNullException.ThrowIfNull(ownHoleCards);
        ArgumentNullException.ThrowIfNull(board);

        if (ownHoleCards.Count != 2)
        {
            throw new ArgumentException(
                $"Exactly two hole cards are required; received {ownHoleCards.Count}.",
                nameof(ownHoleCards));
        }

        if (board.Count is not (0 or 3 or 4 or 5))
        {
            throw new ArgumentException(
                $"A board must contain 0, 3, 4 or 5 cards; received {board.Count}.",
                nameof(board));
        }

        if (samples <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samples),
                samples,
                "A Monte Carlo estimate requires at least one sample.");
        }

        var dead = new HashSet<Card>();
        foreach (var card in ownHoleCards)
        {
            if (!dead.Add(card))
            {
                throw new ArgumentException(
                    $"Duplicate card '{card}' in the hole cards.",
                    nameof(ownHoleCards));
            }
        }

        foreach (var card in board)
        {
            if (!dead.Add(card))
            {
                throw new ArgumentException(
                    $"Duplicate card '{card}' between the hole cards and the board.",
                    nameof(board));
            }
        }

        var remaining = new Card[52 - dead.Count];
        var position = 0;
        for (var suit = 0; suit < 4; suit++)
        {
            for (var rank = 2; rank <= 14; rank++)
            {
                var card = new Card((Rank)rank, (Suit)suit);
                if (dead.Contains(card))
                {
                    continue;
                }

                remaining[position] = card;
                position++;
            }
        }

        var boardCardsNeeded = 5 - board.Count;
        var draws = 2 + boardCardsNeeded;
        var deck = new Card[remaining.Length];
        var ownHand = new Card[7];
        var opponentHand = new Card[7];
        ownHand[0] = ownHoleCards[0];
        ownHand[1] = ownHoleCards[1];
        for (var index = 0; index < board.Count; index++)
        {
            ownHand[2 + index] = board[index];
            opponentHand[2 + index] = board[index];
        }

        var random = new Xorshift64Star(seed);
        long wins = 0;
        long ties = 0;
        long losses = 0;

        for (var sample = 0; sample < samples; sample++)
        {
            Array.Copy(remaining, deck, remaining.Length);
            for (var draw = 0; draw < draws; draw++)
            {
                var swap = draw + (int)(random.Next() % (ulong)(deck.Length - draw));
                (deck[draw], deck[swap]) = (deck[swap], deck[draw]);
            }

            opponentHand[0] = deck[0];
            opponentHand[1] = deck[1];
            for (var index = 0; index < boardCardsNeeded; index++)
            {
                var card = deck[2 + index];
                ownHand[2 + board.Count + index] = card;
                opponentHand[2 + board.Count + index] = card;
            }

            var comparison = HandEvaluator.Evaluate(ownHand).CompareTo(HandEvaluator.Evaluate(opponentHand));
            if (comparison > 0)
            {
                wins++;
            }
            else if (comparison < 0)
            {
                losses++;
            }
            else
            {
                ties++;
            }
        }

        return new EquityResult(
            (wins + (ties / 2.0)) / samples,
            samples,
            (int)wins,
            (int)ties,
            (int)losses);
    }
}