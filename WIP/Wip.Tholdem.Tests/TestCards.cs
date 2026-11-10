using Wip.Tholdem.Cards;
using Wip.Tholdem.Hands;
using Xunit;

namespace Wip.Tholdem.Tests;

internal static class TestCards
{
    public static Card[] Parse(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cards = new Card[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            cards[i] = Card.Parse(parts[i]);
        }

        return cards;
    }
}
