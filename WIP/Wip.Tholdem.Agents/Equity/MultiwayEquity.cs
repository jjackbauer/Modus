using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Randomness;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Hands;

namespace Wip.Tholdem.Agents.Equity;

/// <summary>
/// Seeded Monte Carlo equity against one to five opponents, either uniformly random or conditioned
/// on weighted preflop opponent ranges, with a uniformly random completion of the board. Cards are
/// drawn without replacement from the remaining deck, every player is evaluated on the complete
/// board, and the pot is split equally among the players tied for best. The calculation uses no
/// clock, culture, GUID or ambient randomness, so the same inputs always produce the same estimate.
/// </summary>
public static class MultiwayEquity
{
    /// <summary>Stable identifier of the sampling model recorded in every estimate.</summary>
    public const string Method = "monte-carlo-uniform-1-to-5-v2";

    /// <summary>Stable identifier of the range-conditioned sampling model.</summary>
    public const string RangeMethod = "monte-carlo-range-1-to-5-v2";

    /// <summary>
    /// Estimates win probability, tie probability and expected pot share for the supplied hole
    /// cards and board against <paramref name="opponents"/> uniformly random opponent hands.
    /// </summary>
    public static EquityEstimate Estimate(
        IReadOnlyList<Card> ownHoleCards,
        IReadOnlyList<Card> board,
        int opponents,
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

        if (opponents is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opponents),
                opponents,
                "The opponent count must be between 1 and 5.");
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
        var draws = (2 * opponents) + boardCardsNeeded;
        var deck = new Card[remaining.Length];

        var heroHand = new Card[7];
        heroHand[0] = ownHoleCards[0];
        heroHand[1] = ownHoleCards[1];
        for (var index = 0; index < board.Count; index++)
        {
            heroHand[2 + index] = board[index];
        }

        var opponentHands = new Card[opponents][];
        for (var opponent = 0; opponent < opponents; opponent++)
        {
            var hand = new Card[7];
            for (var index = 0; index < board.Count; index++)
            {
                hand[2 + index] = board[index];
            }

            opponentHands[opponent] = hand;
        }

        var random = new Xorshift64Star(seed);
        long wins = 0;
        long ties = 0;
        long losses = 0;
        double shareSum = 0.0;
        double shareSquareSum = 0.0;

        for (var sample = 0; sample < samples; sample++)
        {
            Array.Copy(remaining, deck, remaining.Length);
            for (var draw = 0; draw < draws; draw++)
            {
                var swap = draw + (int)(random.Next() % (ulong)(deck.Length - draw));
                (deck[draw], deck[swap]) = (deck[swap], deck[draw]);
            }

            for (var opponent = 0; opponent < opponents; opponent++)
            {
                opponentHands[opponent][0] = deck[2 * opponent];
                opponentHands[opponent][1] = deck[(2 * opponent) + 1];
            }

            for (var index = 0; index < boardCardsNeeded; index++)
            {
                var card = deck[(2 * opponents) + index];
                heroHand[2 + board.Count + index] = card;
                for (var opponent = 0; opponent < opponents; opponent++)
                {
                    opponentHands[opponent][2 + board.Count + index] = card;
                }
            }

            var heroRank = HandEvaluator.Evaluate(heroHand);
            var bestRank = heroRank;
            var tiedForBest = 1;
            for (var opponent = 0; opponent < opponents; opponent++)
            {
                var opponentRank = HandEvaluator.Evaluate(opponentHands[opponent]);
                var comparison = opponentRank.CompareTo(bestRank);
                if (comparison > 0)
                {
                    bestRank = opponentRank;
                    tiedForBest = 1;
                }
                else if (comparison == 0)
                {
                    tiedForBest++;
                }
            }

            var heroIsBest = heroRank.CompareTo(bestRank) == 0;
            double share;
            if (!heroIsBest)
            {
                losses++;
                share = 0.0;
            }
            else if (tiedForBest == 1)
            {
                wins++;
                share = 1.0;
            }
            else
            {
                ties++;
                share = 1.0 / tiedForBest;
            }

            shareSum += share;
            shareSquareSum += share * share;
        }

        var winProbability = wins / (double)samples;
        var tieProbability = ties / (double)samples;
        var expectedPotShare = shareSum / samples;
        var standardDeviation = samples > 1
            ? Math.Sqrt(Math.Max(
                0.0,
                (shareSquareSum - (samples * expectedPotShare * expectedPotShare)) / (samples - 1)))
            : 0.0;
        var standardError = standardDeviation / Math.Sqrt(samples);
        var low = Math.Clamp(expectedPotShare - (1.96 * standardError), 0.0, 1.0);
        var high = Math.Clamp(expectedPotShare + (1.96 * standardError), 0.0, 1.0);

        return new EquityEstimate(
            winProbability,
            tieProbability,
            expectedPotShare,
            samples,
            opponents,
            Method,
            (int)wins,
            (int)ties,
            (int)losses,
            standardError,
            new ConfidenceInterval(low, high))
        {
            RangeModelId = "uniform",
        };
    }

    /// <summary>
    /// Estimates win probability, tie probability and expected pot share for the supplied hole
    /// cards and board against one to five weighted preflop opponent ranges. Every range supplies
    /// one combo per sample; combos that collide with the hero, the board or an already sampled
    /// opponent are excluded, the pick is weighted by combo weight, and the board is completed
    /// without replacement. Ties split the pot equally. A range with no available combo raises
    /// <see cref="InvalidOperationException"/> with the stable token <c>range-exhausted</c>.
    /// </summary>
    public static EquityEstimate Estimate(
        IReadOnlyList<Card> ownHoleCards,
        IReadOnlyList<Card> board,
        IReadOnlyList<OpponentRange> opponentRanges,
        ulong seed,
        int samples)
    {
        ArgumentNullException.ThrowIfNull(ownHoleCards);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(opponentRanges);

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

        if (opponentRanges.Count is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opponentRanges),
                opponentRanges.Count,
                "The opponent count must be between 1 and 5.");
        }

        if (samples <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samples),
                samples,
                "A Monte Carlo estimate requires at least one sample.");
        }

        var opponents = opponentRanges.Count;
        for (var opponent = 0; opponent < opponents; opponent++)
        {
            if (opponentRanges[opponent] is null)
            {
                throw new ArgumentException(
                    $"The opponent range at index {opponent} is null.",
                    nameof(opponentRanges));
            }
        }

        var dead = new bool[52];
        foreach (var card in ownHoleCards)
        {
            var index = OpponentRange.CardIndex(card);
            if (dead[index])
            {
                throw new ArgumentException(
                    $"Duplicate card '{card}' in the hole cards.",
                    nameof(ownHoleCards));
            }

            dead[index] = true;
        }

        foreach (var card in board)
        {
            var index = OpponentRange.CardIndex(card);
            if (dead[index])
            {
                throw new ArgumentException(
                    $"Duplicate card '{card}' between the hole cards and the board.",
                    nameof(board));
            }

            dead[index] = true;
        }

        var remaining = new Card[52 - ownHoleCards.Count - board.Count];
        var position = 0;
        for (var suit = 0; suit < 4; suit++)
        {
            for (var rank = 2; rank <= 14; rank++)
            {
                var card = new Card((Rank)rank, (Suit)suit);
                if (!dead[OpponentRange.CardIndex(card)])
                {
                    remaining[position] = card;
                    position++;
                }
            }
        }

        var boardCardsNeeded = 5 - board.Count;
        var heroHand = new Card[7];
        heroHand[0] = ownHoleCards[0];
        heroHand[1] = ownHoleCards[1];
        for (var index = 0; index < board.Count; index++)
        {
            heroHand[2 + index] = board[index];
        }

        var opponentHands = new Card[opponents][];
        for (var opponent = 0; opponent < opponents; opponent++)
        {
            var hand = new Card[7];
            for (var index = 0; index < board.Count; index++)
            {
                hand[2 + index] = board[index];
            }

            opponentHands[opponent] = hand;
        }

        var sampleDead = new bool[52];
        var pool = new Card[remaining.Length];
        var random = new Xorshift64Star(seed);
        long wins = 0;
        long ties = 0;
        long losses = 0;
        double shareSum = 0.0;
        double shareSquareSum = 0.0;

        for (var sample = 0; sample < samples; sample++)
        {
            Array.Copy(dead, sampleDead, dead.Length);

            for (var opponent = 0; opponent < opponents; opponent++)
            {
                var combos = opponentRanges[opponent].Combos;
                var totalWeight = 0.0;
                for (var combo = 0; combo < combos.Count; combo++)
                {
                    var entry = combos[combo];
                    if (!sampleDead[OpponentRange.CardIndex(entry.First)]
                        && !sampleDead[OpponentRange.CardIndex(entry.Second)])
                    {
                        totalWeight += entry.Weight;
                    }
                }

                if (!(totalWeight > 0.0))
                {
                    throw new InvalidOperationException("range-exhausted");
                }

                var threshold = NextUnit(ref random) * totalWeight;
                var cumulative = 0.0;
                var chosen = -1;
                for (var combo = 0; combo < combos.Count; combo++)
                {
                    var entry = combos[combo];
                    if (sampleDead[OpponentRange.CardIndex(entry.First)]
                        || sampleDead[OpponentRange.CardIndex(entry.Second)])
                    {
                        continue;
                    }

                    cumulative += entry.Weight;
                    if (threshold < cumulative)
                    {
                        chosen = combo;
                        break;
                    }
                }

                if (chosen < 0)
                {
                    // Floating-point rounding at the tail: take the last eligible combo.
                    for (var combo = combos.Count - 1; combo >= 0; combo--)
                    {
                        var entry = combos[combo];
                        if (!sampleDead[OpponentRange.CardIndex(entry.First)]
                            && !sampleDead[OpponentRange.CardIndex(entry.Second)])
                        {
                            chosen = combo;
                            break;
                        }
                    }
                }

                if (chosen < 0)
                {
                    throw new InvalidOperationException("range-exhausted");
                }

                var picked = combos[chosen];
                sampleDead[OpponentRange.CardIndex(picked.First)] = true;
                sampleDead[OpponentRange.CardIndex(picked.Second)] = true;
                opponentHands[opponent][0] = picked.First;
                opponentHands[opponent][1] = picked.Second;
            }

            var poolCount = 0;
            for (var card = 0; card < remaining.Length; card++)
            {
                if (!sampleDead[OpponentRange.CardIndex(remaining[card])])
                {
                    pool[poolCount] = remaining[card];
                    poolCount++;
                }
            }

            for (var draw = 0; draw < boardCardsNeeded; draw++)
            {
                var swap = draw + (int)(random.Next() % (ulong)(poolCount - draw));
                (pool[draw], pool[swap]) = (pool[swap], pool[draw]);
            }

            for (var index = 0; index < boardCardsNeeded; index++)
            {
                var card = pool[index];
                heroHand[2 + board.Count + index] = card;
                for (var opponent = 0; opponent < opponents; opponent++)
                {
                    opponentHands[opponent][2 + board.Count + index] = card;
                }
            }

            var heroRank = HandEvaluator.Evaluate(heroHand);
            var bestRank = heroRank;
            var tiedForBest = 1;
            for (var opponent = 0; opponent < opponents; opponent++)
            {
                var opponentRank = HandEvaluator.Evaluate(opponentHands[opponent]);
                var comparison = opponentRank.CompareTo(bestRank);
                if (comparison > 0)
                {
                    bestRank = opponentRank;
                    tiedForBest = 1;
                }
                else if (comparison == 0)
                {
                    tiedForBest++;
                }
            }

            var heroIsBest = heroRank.CompareTo(bestRank) == 0;
            double share;
            if (!heroIsBest)
            {
                losses++;
                share = 0.0;
            }
            else if (tiedForBest == 1)
            {
                wins++;
                share = 1.0;
            }
            else
            {
                ties++;
                share = 1.0 / tiedForBest;
            }

            shareSum += share;
            shareSquareSum += share * share;
        }

        var winProbability = wins / (double)samples;
        var tieProbability = ties / (double)samples;
        var expectedPotShare = shareSum / samples;
        var standardDeviation = samples > 1
            ? Math.Sqrt(Math.Max(
                0.0,
                (shareSquareSum - (samples * expectedPotShare * expectedPotShare)) / (samples - 1)))
            : 0.0;
        var standardError = standardDeviation / Math.Sqrt(samples);
        var low = Math.Clamp(expectedPotShare - (1.96 * standardError), 0.0, 1.0);
        var high = Math.Clamp(expectedPotShare + (1.96 * standardError), 0.0, 1.0);

        var rangeModelId = "range:" + StrategyArtifact.ComputeHash(
            string.Join("\n", opponentRanges.Select(static range => range.RangeHash)))[..16];

        return new EquityEstimate(
            winProbability,
            tieProbability,
            expectedPotShare,
            samples,
            opponents,
            RangeMethod,
            (int)wins,
            (int)ties,
            (int)losses,
            standardError,
            new ConfidenceInterval(low, high))
        {
            RangeModelId = rangeModelId,
        };
    }

    private static double NextUnit(ref Xorshift64Star random) =>
        (random.Next() >> 11) * (1.0 / 9007199254740992.0);
}
