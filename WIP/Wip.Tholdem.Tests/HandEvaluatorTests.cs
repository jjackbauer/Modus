using Wip.Tholdem.Hands;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class HandEvaluatorTests
{
    [Fact]
    public void Evaluate_GivenSevenCards_ExpectedCategory()
    {
        AssertCategory(HandCategory.HighCard, "As Kd Qc Jh 9s 7d 2c");
        AssertCategory(HandCategory.OnePair, "As Ad Qc Jh 9s 7d 2c");
        AssertCategory(HandCategory.TwoPair, "As Ad Qc Qh 9s 7d 2c");
        AssertCategory(HandCategory.ThreeOfAKind, "As Ad Ac Qh 9s 7d 2c");
        AssertCategory(HandCategory.Straight, "9s 8d 7c 6h 5s Kd 2c");
        AssertCategory(HandCategory.Flush, "As Ks Qs Js 9s 7d 2c");
        AssertCategory(HandCategory.FullHouse, "Ks Kd Kc 7h 7s Qd 2c");
        AssertCategory(HandCategory.FourOfAKind, "Ks Kd Kc Kh 7s Qd 2c");
        AssertCategory(HandCategory.StraightFlush, "9h 8h 7h 6h 5h Kd 2c");
    }

    [Fact]
    public void Evaluate_GivenAdjacentCategories_ExpectedStrongerScoreAndReverseComparison()
    {
        AssertStronger("As Ad Qc Jh 9s 7d 2c", "As Kd Qc Jh 9s 7d 2c");
        AssertStronger("As Ad Qc Qh 9s 7d 2c", "As Ad Qc Jh 9s 7d 2c");
        AssertStronger("As Ad Ac Qh 9s 7d 2c", "As Ad Qc Qh 9s 7d 2c");
        AssertStronger("9s 8d 7c 6h 5s Kd 2c", "As Ad Ac Qh 9s 7d 2c");
        AssertStronger("As Ks Qs Js 9s 7d 2c", "9s 8d 7c 6h 5s Kd 2c");
        AssertStronger("Ks Kd Kc 7h 7s Qd 2c", "As Ks Qs Js 9s 7d 2c");
        AssertStronger("Ks Kd Kc Kh 7s Qd 2c", "Ks Kd Kc 7h 7s Qd 2c");
        AssertStronger("9h 8h 7h 6h 5h Kd 2c", "Ks Kd Kc Kh 7s Qd 2c");
    }

    [Fact]
    public void Evaluate_GivenWheel_ExpectedFiveHighStraight()
    {
        var wheel = HandEvaluator.Evaluate(TestCards.Parse("As 2d 3c 4h 5s"));

        Assert.Equal(HandCategory.Straight, wheel.Category);
        Assert.Equal(new[] { 5 }, wheel.Tiebreakers.ToArray());
        Assert.Equal("Straight[5]", wheel.ToString());
    }

    [Fact]
    public void Evaluate_GivenSixHighStraight_ExpectedBeatsWheel()
    {
        var wheel = HandEvaluator.Evaluate(TestCards.Parse("As 2d 3c 4h 5s"));
        var sixHigh = HandEvaluator.Evaluate(TestCards.Parse("2c 3d 4h 5s 6c"));

        Assert.Equal(HandCategory.Straight, sixHigh.Category);
        Assert.Equal(new[] { 6 }, sixHigh.Tiebreakers.ToArray());
        Assert.True(sixHigh.Score > wheel.Score);
        Assert.True(wheel.CompareTo(sixHigh) < 0);
    }

    [Fact]
    public void Evaluate_GivenNineKicker_ExpectedBeatsEightKicker()
    {
        var nineKicker = HandEvaluator.Evaluate(TestCards.Parse("As Kd Qc Jh 9s"));
        var eightKicker = HandEvaluator.Evaluate(TestCards.Parse("As Kd Qc Jh 8s"));

        Assert.Equal(HandCategory.HighCard, nineKicker.Category);
        Assert.Equal(HandCategory.HighCard, eightKicker.Category);
        Assert.Equal(new[] { 14, 13, 12, 11, 9 }, nineKicker.Tiebreakers.ToArray());
        Assert.Equal(new[] { 14, 13, 12, 11, 8 }, eightKicker.Tiebreakers.ToArray());
        Assert.True(nineKicker.Score > eightKicker.Score);
        Assert.True(eightKicker.CompareTo(nineKicker) < 0);
    }

    [Fact]
    public void Evaluate_GivenTwoTrips_ExpectedHigherTripsFullHouse()
    {
        var kings = HandEvaluator.Evaluate(TestCards.Parse("Kh Kd Ks Qh Qd Qs 2c"));
        var queens = HandEvaluator.Evaluate(TestCards.Parse("Qh Qd Qs Jh Jd Js 2c"));

        Assert.Equal(HandCategory.FullHouse, kings.Category);
        Assert.Equal(HandCategory.FullHouse, queens.Category);
        Assert.Equal(new[] { 13, 12 }, kings.Tiebreakers.ToArray());
        Assert.Equal(new[] { 12, 11 }, queens.Tiebreakers.ToArray());
        Assert.Equal("FullHouse[K,K,K,Q,Q]", kings.ToString());
        Assert.True(kings.Score > queens.Score);
    }

    [Fact]
    public void Evaluate_GivenFourOfAKind_ExpectedBestKickerUsed()
    {
        var kingKicker = HandEvaluator.Evaluate(TestCards.Parse("9s 9d 9h 9c Kd 2c 3h"));
        var queenKicker = HandEvaluator.Evaluate(TestCards.Parse("9s 9d 9h 9c Qd 2c 3h"));

        Assert.Equal(HandCategory.FourOfAKind, kingKicker.Category);
        Assert.Equal(HandCategory.FourOfAKind, queenKicker.Category);
        Assert.Equal(new[] { 9, 13 }, kingKicker.Tiebreakers.ToArray());
        Assert.Equal(new[] { 9, 12 }, queenKicker.Tiebreakers.ToArray());
        Assert.True(kingKicker.Score > queenKicker.Score);
    }

    [Fact]
    public void Evaluate_GivenSixSuitedCards_ExpectedBestFiveFlush()
    {
        var flush = HandEvaluator.Evaluate(TestCards.Parse("Ah Kh Qh Jh 9h 8h 2c"));

        Assert.Equal(HandCategory.Flush, flush.Category);
        Assert.Equal(new[] { 14, 13, 12, 11, 9 }, flush.Tiebreakers.ToArray());
    }

    [Fact]
    public void Evaluate_GivenBoardPlays_ExpectedIdenticalBestFive()
    {
        var first = HandEvaluator.Evaluate(TestCards.Parse("As Kd Qc Jh 9s 2c 3d"));
        var second = HandEvaluator.Evaluate(TestCards.Parse("As Kd Qc Jh 9s 4c 5d"));

        Assert.Equal(HandCategory.HighCard, first.Category);
        Assert.Equal(HandCategory.HighCard, second.Category);
        Assert.Equal(new[] { 14, 13, 12, 11, 9 }, first.Tiebreakers.ToArray());
        Assert.Equal(new[] { 14, 13, 12, 11, 9 }, second.Tiebreakers.ToArray());
        Assert.Equal(974009L, first.Score);
        Assert.Equal(974009L, second.Score);
        Assert.Equal(0, first.CompareTo(second));
        Assert.Equal(0, second.CompareTo(first));
        Assert.Equal(first.Score, second.Score);
    }

    [Fact]
    public void Evaluate_GivenSameCardsInDifferentOrder_ExpectedEqualScore()
    {
        var forward = HandEvaluator.Evaluate(TestCards.Parse("As Kd Qc Jh 9s 7d 2c"));
        var reversed = HandEvaluator.Evaluate(TestCards.Parse("2c 7d 9s Jh Qc Kd As"));

        Assert.Equal(HandCategory.HighCard, forward.Category);
        Assert.Equal(HandCategory.HighCard, reversed.Category);
        Assert.Equal(new[] { 14, 13, 12, 11, 9 }, forward.Tiebreakers.ToArray());
        Assert.Equal(new[] { 14, 13, 12, 11, 9 }, reversed.Tiebreakers.ToArray());
        Assert.Equal(974009L, forward.Score);
        Assert.Equal(974009L, reversed.Score);
        Assert.Equal(forward.Score, reversed.Score);
        Assert.Equal(0, forward.CompareTo(reversed));
    }

    [Fact]
    public void Evaluate_GivenSixCards_ExpectedBestFiveSelected()
    {
        var rank = HandEvaluator.Evaluate(TestCards.Parse("As Kd Qc Jh 9s 7d"));

        Assert.Equal(HandCategory.HighCard, rank.Category);
        Assert.Equal(new[] { 14, 13, 12, 11, 9 }, rank.Tiebreakers.ToArray());
    }

    [Fact]
    public void Evaluate_GivenTooFewTooManyOrDuplicateCards_ExpectedArgumentException()
    {
        var invalid = new[]
        {
            "As Kd Qc Jh",
            "As Kd Qc Jh 9s 7d 2c 3h",
            "As Kd Qc Jh As",
        };

        foreach (var cards in invalid)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                _ = HandEvaluator.Evaluate(TestCards.Parse(cards));
            });
        }
    }

    private static void AssertCategory(HandCategory expectedCategory, string cards)
    {
        var rank = HandEvaluator.Evaluate(TestCards.Parse(cards));

        Assert.Equal(expectedCategory, rank.Category);
    }

    private static void AssertStronger(string strongerCards, string weakerCards)
    {
        var stronger = HandEvaluator.Evaluate(TestCards.Parse(strongerCards));
        var weaker = HandEvaluator.Evaluate(TestCards.Parse(weakerCards));

        Assert.True(
            stronger.Score > weaker.Score,
            $"Expected {stronger} ({stronger.Score}) to outrank {weaker} ({weaker.Score}).");
        Assert.True(
            weaker.CompareTo(stronger) < 0,
            $"Expected {weaker} to compare below {stronger}.");
    }
}
