using System.Reflection;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class PlayerObservationTests
{
    [Fact]
    public void ForSeat_GivenHiddenInformation_ExpectedNoSeedDeckOrOpponentHoleCards()
    {
        var state = EngineTestSupport.CheckDown(42);

        for (var seat = 0; seat < 2; seat++)
        {
            var observation = PlayerObservation.ForSeat(state, seat);
            var json = observation.ToCanonicalJson();
            var ownHole = seat == 0 ? state.HoleCardsSeat0 : state.HoleCardsSeat1;
            var opponentHole = seat == 0 ? state.HoleCardsSeat1 : state.HoleCardsSeat0;

            Assert.Equal(ownHole.ToArray(), observation.OwnHoleCards.ToArray());
            Assert.DoesNotContain("seed", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("42", json);

            foreach (var card in opponentHole)
            {
                Assert.DoesNotContain($"\"{card}\"", json);
            }

            var allowed = new HashSet<Card>(state.Board);
            foreach (var card in ownHole)
            {
                allowed.Add(card);
            }

            for (var suit = 0; suit <= 3; suit++)
            {
                for (var rank = 2; rank <= 14; rank++)
                {
                    var card = new Card((Rank)rank, (Suit)suit);
                    if (!allowed.Contains(card))
                    {
                        Assert.DoesNotContain($"\"{card}\"", json);
                    }
                }
            }

            foreach (var card in ownHole)
            {
                Assert.Contains($"\"{card}\"", json);
            }
        }
    }

    [Fact]
    public void ForSeat_GivenPublicSurface_ExpectedDocumentedPropertiesAndNoForbiddenNames()
    {
        var properties = typeof(PlayerObservation)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var names = properties.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[]
            {
                "ActingSeat",
                "ActionHistory",
                "AmountToCall",
                "Board",
                "ButtonSeat",
                "HandNumber",
                "IsComplete",
                "LegalActionKinds",
                "MaxRaiseTo",
                "MinRaiseTo",
                "OpponentStack",
                "OpponentStreetCommitment",
                "OwnHoleCards",
                "OwnStack",
                "OwnStreetCommitment",
                "OwnTotalCommitted",
                "Pot",
                "Seat",
                "SeatCount",
                "Seats",
                "Street",
                "Winners",
            },
            names);

        foreach (var property in properties)
        {
            Assert.DoesNotContain("Seed", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Deck", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OpponentHole", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(typeof(HandRecord), property.PropertyType);
        }
    }

    [Fact]
    public void ForSeat_GivenActingSeat_ExpectedLegalActionKindsMatchEngine()
    {
        var states = new[]
        {
            HeadsUpHandEngine.Start(42),
            HeadsUpHandEngine.Apply(HeadsUpHandEngine.Start(42), PokerAction.Call()),
            EngineTestSupport.LimpAndFlop(),
            EngineTestSupport.PlayAllInHand(42),
        };

        foreach (var state in states)
        {
            var seat = state.IsComplete ? 0 : state.ActingSeat;
            var observation = PlayerObservation.ForSeat(state, seat);
            var legal = HeadsUpHandEngine.GetLegalActions(state);

            Assert.Equal(legal.Actions.Select(a => a.Kind).ToArray(), observation.LegalActionKinds.ToArray());
            Assert.Equal(legal.CallAmount, observation.AmountToCall);
            Assert.Equal(legal.MinRaiseTo, observation.MinRaiseTo);
            Assert.Equal(legal.MaxRaiseTo, observation.MaxRaiseTo);
            Assert.Equal(state.HandNumber, observation.HandNumber);
            Assert.Equal(state.Street, observation.Street);
            Assert.Equal(state.ButtonSeat, observation.ButtonSeat);
            Assert.Equal(state.ActingSeat, observation.ActingSeat);
            Assert.Equal(state.Board.ToArray(), observation.Board.ToArray());
            Assert.Equal(state.Stacks[seat], observation.OwnStack);
            Assert.Equal(state.Stacks[1 - seat], observation.OpponentStack);
            Assert.Equal(state.Pot, observation.Pot);
            Assert.Equal(state.StreetCommitted[seat], observation.OwnStreetCommitment);
            Assert.Equal(state.StreetCommitted[1 - seat], observation.OpponentStreetCommitment);
            Assert.Equal(state.IsComplete, observation.IsComplete);
            Assert.Equal(state.Winners.ToArray(), observation.Winners.ToArray());
        }
    }
}
