using Wip.Tholdem.Cards;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class CardTests
{
    [Fact]
    public void Parse_GivenCanonicalText_ExpectedRoundTrip()
    {
        AssertCard("As", Rank.Ace, Suit.Spades);
        AssertCard("Td", Rank.Ten, Suit.Diamonds);
        AssertCard("2c", Rank.Two, Suit.Clubs);

        static void AssertCard(string text, Rank expectedRank, Suit expectedSuit)
        {
            var card = Card.Parse(text);

            Assert.Equal(expectedRank, card.Rank);
            Assert.Equal(expectedSuit, card.Suit);
            Assert.Equal(text, card.ToString());
        }
    }

    [Fact]
    public void Parse_GivenNull_ExpectedArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            _ = Card.Parse(null!);
        });
    }

    [Fact]
    public void Parse_GivenInvalidText_ExpectedArgumentException()
    {
        var invalid = new[] { "", "A", "Ass", "as", "AS", "1s", "Ax" };

        foreach (var text in invalid)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                _ = Card.Parse(text);
            });
        }
    }

    [Fact]
    public void Constructor_GivenUndefinedRankOrSuit_ExpectedArgumentException()
    {
        var invalid = new[] { (1, 0), (15, 0), (14, 4), (14, -1) };

        foreach (var (rank, suit) in invalid)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                _ = new Card((Rank)rank, (Suit)suit);
            });
        }
    }
}
