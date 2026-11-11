using Wip.Tholdem.Agents.Equity;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Hands;
using Xunit;

namespace Wip.Tholdem.Tests.Equity;

public sealed class MultiwayEquityTests
{
    private const long ExhaustiveTwoOpponentDeals = 148995;
    private const long ExhaustivePairingShares = 3 * ExhaustiveTwoOpponentDeals;

    [Fact]
    public void Estimate_GivenRiverBoardTwoOpponents_MatchesExhaustivePotShare()
    {
        var own = Parse("As Ah");
        var board = Parse("Kd 7c 2s 9h 3d");
        const int samples = 20000;

        var estimate = MultiwayEquity.Estimate(own, board, 2, 12345UL, samples);
        var exhaustive = ExhaustiveRiverPotShare(own, board);

        Assert.Equal(samples, estimate.Samples);
        Assert.Equal(2, estimate.OpponentCount);
        Assert.Equal(MultiwayEquity.Method, estimate.Method);
        Assert.Equal(samples, estimate.Wins + estimate.Ties + estimate.Losses);
        Assert.Equal(ExhaustiveTwoOpponentDeals, ExhaustiveDealCount(own, board));
        Assert.InRange(exhaustive, 0.0, 1.0);
        Assert.InRange(estimate.ExpectedPotShare, exhaustive - 0.02, exhaustive + 0.02);
        Assert.Equal(estimate.Wins / (double)samples, estimate.WinProbability);
        Assert.Equal(estimate.Ties / (double)samples, estimate.TieProbability);
        Assert.InRange(estimate.StandardError, 0.0, 0.5);
        Assert.InRange(
            estimate.ExpectedPotShare,
            estimate.ExpectedPotShareInterval.Low,
            estimate.ExpectedPotShareInterval.High);
    }

    [Fact]
    public void Estimate_GivenBoardRoyalFlush_AllTieAndSplitEqually()
    {
        var estimate = MultiwayEquity.Estimate(
            Parse("2c 3d"),
            Parse("As Ks Qs Js Ts"),
            3,
            99UL,
            500);

        Assert.Equal(0.0, estimate.WinProbability);
        Assert.Equal(1.0, estimate.TieProbability);
        Assert.Equal(0.25, estimate.ExpectedPotShare);
        Assert.Equal(estimate.Samples, estimate.Ties);
        Assert.Equal(0, estimate.Wins);
        Assert.Equal(0, estimate.Losses);
        Assert.Equal(0.0, estimate.StandardError);
        Assert.Equal(0.25, estimate.ExpectedPotShareInterval.Low);
        Assert.Equal(0.25, estimate.ExpectedPotShareInterval.High);
    }

    [Fact]
    public void Estimate_GivenSameSeed_IsDeterministic()
    {
        var first = MultiwayEquity.Estimate(Parse("As Ah"), Parse("Kd 7c 2s"), 3, 42UL, 5000);
        var second = MultiwayEquity.Estimate(Parse("As Ah"), Parse("Kd 7c 2s"), 3, 42UL, 5000);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Estimate_GivenFiveOpponents_ReturnsCountsAndValidInterval()
    {
        const int samples = 10000;
        var estimate = MultiwayEquity.Estimate(
            Parse("As Ah"),
            Parse("Kd 7c 2s 9h 3d"),
            5,
            7UL,
            samples);

        Assert.Equal(5, estimate.OpponentCount);
        Assert.Equal(samples, estimate.Samples);
        Assert.Equal(samples, estimate.Wins + estimate.Ties + estimate.Losses);
        Assert.InRange(estimate.WinProbability, 0.0, 1.0);
        Assert.InRange(estimate.TieProbability, 0.0, 1.0);
        Assert.InRange(estimate.ExpectedPotShare, 0.0, 1.0);
        Assert.InRange(estimate.StandardError, 0.0, 0.5);
        Assert.InRange(
            estimate.ExpectedPotShare,
            estimate.ExpectedPotShareInterval.Low,
            estimate.ExpectedPotShareInterval.High);
        Assert.InRange(estimate.ExpectedPotShareInterval.Low, 0.0, 1.0);
        Assert.InRange(estimate.ExpectedPotShareInterval.High, 0.0, 1.0);
    }

    [Fact]
    public void Estimate_GivenInvalidInputs_Throws()
    {
        var holes = Parse("As Ah");
        var board = Parse("Kd 7c 2s");

        Assert.Throws<ArgumentNullException>(() => MultiwayEquity.Estimate(null!, board, 1, 1UL, 10));
        Assert.Throws<ArgumentNullException>(() => MultiwayEquity.Estimate(holes, null!, 1, 1UL, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => MultiwayEquity.Estimate(holes, board, 0, 1UL, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => MultiwayEquity.Estimate(holes, board, 6, 1UL, 10));
        Assert.Throws<ArgumentException>(() => MultiwayEquity.Estimate(Parse("As As"), board, 1, 1UL, 10));
        Assert.Throws<ArgumentException>(() => MultiwayEquity.Estimate(holes, Parse("As 7c 2s"), 1, 1UL, 10));
        Assert.Throws<ArgumentException>(() => MultiwayEquity.Estimate(holes, Parse("Kd"), 1, 1UL, 10));
        Assert.Throws<ArgumentException>(() => MultiwayEquity.Estimate(holes, Parse("Kd 7c"), 1, 1UL, 10));
        Assert.Throws<ArgumentException>(() =>
            MultiwayEquity.Estimate(holes, Parse("Kd 7c 2s 9h 3d 4c"), 1, 1UL, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => MultiwayEquity.Estimate(holes, board, 1, 1UL, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MultiwayEquity.Estimate(holes, board, 1, 1UL, -1));
    }

    private static double ExhaustiveRiverPotShare(Card[] own, Card[] board)
    {
        Assert.Equal(5, board.Length);
        var remaining = Remaining(own, board);
        var heroRank = HandEvaluator.Evaluate(own.Concat(board).ToArray());
        var pairRanks = new HandRank[remaining.Count, remaining.Count];
        for (var first = 0; first < remaining.Count; first++)
        {
            for (var second = first + 1; second < remaining.Count; second++)
            {
                pairRanks[first, second] = HandEvaluator.Evaluate(new[]
                {
                    remaining[first],
                    remaining[second],
                    board[0],
                    board[1],
                    board[2],
                    board[3],
                    board[4],
                });
            }
        }

        long deals = 0;
        long shareEvaluations = 0;
        double shareSum = 0.0;
        for (var i = 0; i < remaining.Count; i++)
        {
            for (var j = i + 1; j < remaining.Count; j++)
            {
                for (var k = j + 1; k < remaining.Count; k++)
                {
                    for (var l = k + 1; l < remaining.Count; l++)
                    {
                        shareSum += TiedShare(heroRank, pairRanks[i, j], pairRanks[k, l]);
                        shareSum += TiedShare(heroRank, pairRanks[i, k], pairRanks[j, l]);
                        shareSum += TiedShare(heroRank, pairRanks[i, l], pairRanks[j, k]);
                        shareEvaluations += 3;
                        deals++;
                    }
                }
            }
        }

        Assert.Equal(ExhaustiveTwoOpponentDeals, deals);
        Assert.Equal(ExhaustivePairingShares, shareEvaluations);
        return shareSum / shareEvaluations;
    }

    private static double TiedShare(HandRank heroRank, HandRank firstOpponent, HandRank secondOpponent)
    {
        var best = heroRank;
        if (firstOpponent.CompareTo(best) > 0)
        {
            best = firstOpponent;
        }

        if (secondOpponent.CompareTo(best) > 0)
        {
            best = secondOpponent;
        }

        if (heroRank.CompareTo(best) != 0)
        {
            return 0.0;
        }

        var tied = 1;
        if (firstOpponent.CompareTo(best) == 0)
        {
            tied++;
        }

        if (secondOpponent.CompareTo(best) == 0)
        {
            tied++;
        }

        return 1.0 / tied;
    }

    private static long ExhaustiveDealCount(Card[] own, Card[] board)
    {
        var remaining = Remaining(own, board).Count;
        return (long)remaining * (remaining - 1) * (remaining - 2) * (remaining - 3) / 24;
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

    private static Card[] Parse(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Card.Parse).ToArray();
}
