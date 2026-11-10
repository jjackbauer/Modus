using Wip.Tholdem.Agents.Equity;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Hands;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class MonteCarloEquityTests
{
    // Dispatch note: 0.826366 is the exhaustive equity of As Ah against the FIXED hand Ks Kh
    // (1712304 boards). MonteCarloEquity has no opponent-card parameter by contract, so it models
    // one uniformly random opponent; the exhaustive answer for that model is ~0.852 and the seeded
    // sample is pinned below. The delta is listed in docs/goal-tholdem/round-3/implementation.md.
    [Fact]
    public void Estimate_GivenAcesOnEmptyBoard_ExpectedSeededRandomHandEquity()
    {
        var result = MonteCarloEquity.Estimate(Holes("As", "Ah"), Array.Empty<Card>(), 7UL, 20000);

        Assert.Equal(20000, result.Samples);
        Assert.Equal(16971, result.Wins);
        Assert.Equal(103, result.Ties);
        Assert.Equal(2926, result.Losses);
        Assert.Equal(16971 + 103 + 2926, result.Wins + result.Ties + result.Losses);
        Assert.Equal((result.Wins + (result.Ties / 2.0)) / result.Samples, result.Equity);
        Assert.Equal(0.851125, result.Equity, 6);
        Assert.InRange(result.Equity, 0.831125, 0.871125);
    }

    [Fact]
    public void Estimate_GivenSameInputsTwice_ExpectedIdenticalEquityResult()
    {
        var first = MonteCarloEquity.Estimate(Holes("As", "Ah"), Array.Empty<Card>(), 7UL, 20000);
        var second = MonteCarloEquity.Estimate(Holes("As", "Ah"), Array.Empty<Card>(), 7UL, 20000);
        var differentSeed = MonteCarloEquity.Estimate(Holes("As", "Ah"), Array.Empty<Card>(), 8UL, 20000);

        Assert.Equal(first, second);
        Assert.Equal(first.Equity, second.Equity);
        Assert.Equal(first.Wins, second.Wins);
        Assert.Equal(first.Ties, second.Ties);
        Assert.Equal(first.Losses, second.Losses);
        Assert.Equal(first.Samples, second.Samples);
        Assert.Equal(first.Samples, differentSeed.Samples);
    }

    [Fact]
    public void Estimate_GivenTurnBoard_ExpectedWithinTwoPointsOfExhaustiveEnumeration()
    {
        var own = Holes("As", "Ah");
        var board = Board("Kd", "7c", "2s", "9h");
        var exact = ExhaustiveTurnEquity(own, board);
        var result = MonteCarloEquity.Estimate(own, board, 11UL, 20000);
        var repeated = MonteCarloEquity.Estimate(own, board, 11UL, 20000);

        Assert.Equal(20000, result.Samples);
        Assert.Equal(45540, ExhaustiveTurnBoards(own, board));
        Assert.InRange(exact, 0.0, 1.0);
        Assert.InRange(result.Equity, exact - 0.02, exact + 0.02);
        Assert.Equal(result, repeated);
    }

    [Fact]
    public void Estimate_GivenInvalidInputs_ExpectedArgumentExceptions()
    {
        var holes = Holes("As", "Ah");
        var board = Board("Kd", "7c", "2s");

        Assert.Throws<ArgumentNullException>(() => MonteCarloEquity.Estimate(null!, board, 1UL, 10));
        Assert.Throws<ArgumentNullException>(() => MonteCarloEquity.Estimate(holes, null!, 1UL, 10));
        Assert.Throws<ArgumentException>(() => MonteCarloEquity.Estimate(Holes("As"), Array.Empty<Card>(), 1UL, 10));
        Assert.Throws<ArgumentException>(() => MonteCarloEquity.Estimate(Holes("As", "Ah", "Ad"), Array.Empty<Card>(), 1UL, 10));
        Assert.Throws<ArgumentException>(() => MonteCarloEquity.Estimate(holes, Board("Kd"), 1UL, 10));
        Assert.Throws<ArgumentException>(() => MonteCarloEquity.Estimate(holes, Board("Kd", "7c"), 1UL, 10));
        Assert.Throws<ArgumentException>(() => MonteCarloEquity.Estimate(holes, Board("Kd", "7c", "2s", "9h", "3d", "4c"), 1UL, 10));
        Assert.Throws<ArgumentException>(() => MonteCarloEquity.Estimate(Holes("As", "As"), Array.Empty<Card>(), 1UL, 10));
        Assert.Throws<ArgumentException>(() => MonteCarloEquity.Estimate(holes, Board("As", "7c", "2s"), 1UL, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonteCarloEquity.Estimate(holes, board, 1UL, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonteCarloEquity.Estimate(holes, board, 1UL, -5));
    }

    private static int ExhaustiveTurnBoards(Card[] own, Card[] board)
    {
        var remaining = Remaining(own, board);
        return remaining.Count * (remaining.Count - 1) / 2 * (remaining.Count - 2);
    }

    private static double ExhaustiveTurnEquity(Card[] own, Card[] board)
    {
        var remaining = Remaining(own, board);
        long wins = 0, ties = 0, losses = 0, total = 0;
        var fullBoard = new Card[5];
        for (var index = 0; index < board.Length; index++)
        {
            fullBoard[index] = board[index];
        }

        for (var first = 0; first < remaining.Count; first++)
        {
            for (var second = first + 1; second < remaining.Count; second++)
            {
                for (var river = 0; river < remaining.Count; river++)
                {
                    if (river == first || river == second)
                    {
                        continue;
                    }

                    fullBoard[4] = remaining[river];
                    var ownRank = HandEvaluator.Evaluate(own.Concat(fullBoard).ToArray());
                    var opponent = new[] { remaining[first], remaining[second] };
                    var opponentRank = HandEvaluator.Evaluate(opponent.Concat(fullBoard).ToArray());
                    if (ownRank.CompareTo(opponentRank) > 0)
                    {
                        wins++;
                    }
                    else if (ownRank.CompareTo(opponentRank) < 0)
                    {
                        losses++;
                    }
                    else
                    {
                        ties++;
                    }

                    total++;
                }
            }
        }

        Assert.Equal(wins + ties + losses, total);
        return (wins + (ties / 2.0)) / total;
    }

    private static List<Card> Remaining(Card[] own, Card[] board)
    {
        var dead = new HashSet<Card>(own.Concat(board));
        var remaining = new List<Card>(52 - dead.Count);
        for (var suit = 0; suit < 4; suit++)
        {
            for (var rank = 2; rank <= 14; rank++)
            {
                var card = new Card((Rank)rank, (Suit)suit);
                if (!dead.Contains(card))
                {
                    remaining.Add(card);
                }
            }
        }

        return remaining;
    }

    private static Card[] Holes(params string[] cards) => cards.Select(Card.Parse).ToArray();

    private static Card[] Board(params string[] cards) => cards.Select(Card.Parse).ToArray();
}