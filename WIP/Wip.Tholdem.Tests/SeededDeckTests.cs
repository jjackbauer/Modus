using Wip.Tholdem.Cards;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class SeededDeckTests
{
    [Fact]
    public void Create_GivenSeed42_ExpectedCompleteDeckWithAllDistinctCards()
    {
        var deck = SeededDeck.Create(42);

        Assert.Equal(52, deck.Count);
        Assert.Equal(52, deck.Select(card => card.ToString()).Distinct().Count());
        Assert.Equal<IEnumerable<string>>(
            UnshuffledDeck().OrderBy(DeckIndex).Select(card => card.ToString()),
            deck.OrderBy(DeckIndex).Select(card => card.ToString()));
    }

    [Fact]
    public void Create_GivenSeed42_ExpectedPinnedFirstFiveCards()
    {
        var deck = SeededDeck.Create(42);

        Assert.Equal("7d", deck[0].ToString());
        Assert.Equal("Qc", deck[1].ToString());
        Assert.Equal("9c", deck[2].ToString());
        Assert.Equal("4s", deck[3].ToString());
        Assert.Equal("8c", deck[4].ToString());
    }

    [Fact]
    public void Create_GivenSeed7_ExpectedCompleteDeckWithAllDistinctCards()
    {
        var deck = SeededDeck.Create(7);

        Assert.Equal(52, deck.Count);
        Assert.Equal(52, deck.Select(card => card.ToString()).Distinct().Count());
        Assert.Equal<IEnumerable<string>>(
            UnshuffledDeck().OrderBy(DeckIndex).Select(card => card.ToString()),
            deck.OrderBy(DeckIndex).Select(card => card.ToString()));
    }

    [Fact]
    public void Create_GivenSameSeedTwice_ExpectedIdenticalSequence()
    {
        var first = SeededDeck.Create(42).Select(card => card.ToString()).ToArray();
        var second = SeededDeck.Create(42).Select(card => card.ToString()).ToArray();

        Assert.Equal(string.Join(" ", first), string.Join(" ", second));
    }

    [Fact]
    public void Create_GivenDifferentSeeds_ExpectedDifferentSequences()
    {
        var seed42 = SeededDeck.Create(42).Select(card => card.ToString()).ToArray();
        var seed7 = SeededDeck.Create(7).Select(card => card.ToString()).ToArray();

        Assert.NotEqual(string.Join(" ", seed42), string.Join(" ", seed7));
    }

    [Fact]
    public void Create_GivenSeedZero_ExpectedDeterministicSequence()
    {
        var first = SeededDeck.Create(0).Select(card => card.ToString()).ToArray();
        var second = SeededDeck.Create(0).Select(card => card.ToString()).ToArray();

        Assert.Equal(52, first.Length);
        Assert.Equal(string.Join(" ", first), string.Join(" ", second));
    }

    private static Card[] UnshuffledDeck()
    {
        var cards = new Card[SeededDeck.Size];
        for (var suit = 0; suit < 4; suit++)
        {
            for (var rank = 2; rank <= 14; rank++)
            {
                cards[(suit * 13) + (rank - 2)] = new Card((Rank)rank, (Suit)suit);
            }
        }

        return cards;
    }

    private static int DeckIndex(Card card) => ((int)card.Suit * 13) + ((int)card.Rank - 2);
}
