using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.History;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// D5 diagnostics repair: fold-to-raise must reflect the outstanding aggressive action on the
/// street rather than only the immediately preceding action, and a sampled hand with zero failures
/// must be distinguishable from a signal that was never observed.
/// </summary>
public sealed class DiagnosticsRepairTests
{
    [Fact]
    public void Extract_GivenCallBetweenRaiseAndFold_CountsFoldToRaise()
    {
        var hand = FoldAfterRaiseCallerHand();

        var signals = FailureSignatureExtractor.Extract([hand]).Signals;

        var foldToRaise = Signal(signals, FailureSignalNames.FoldToRaise);
        Assert.Equal(1, foldToRaise.Count);
        Assert.Equal(1, foldToRaise.Sample);
        Assert.Equal(1.0, foldToRaise.Rate, 12);
        Assert.True(foldToRaise.Observed);
    }

    [Fact]
    public void Extract_GivenNoHands_AllSignalsUnobserved()
    {
        var signals = FailureSignatureExtractor.Extract(Array.Empty<HandRecord>()).Signals;

        Assert.Equal(6, signals.Count);
        foreach (var signal in signals)
        {
            Assert.Equal(0, signal.Count);
            Assert.Equal(0, signal.Sample);
            Assert.Equal(0.0, signal.Rate);
            Assert.False(signal.Observed);
        }
    }

    [Fact]
    public void Extract_GivenCheckedHand_SampledZeroFailuresAreObserved()
    {
        var hand = CheckedDownHand();

        var signals = FailureSignatureExtractor.Extract([hand]).Signals;

        var aggression = Signal(signals, FailureSignalNames.AggressionByStreet);
        Assert.True(aggression.Observed);
        Assert.Equal(0, aggression.Count);
        Assert.Equal(3, aggression.Sample);

        var foldToRaise = Signal(signals, FailureSignalNames.FoldToRaise);
        Assert.False(foldToRaise.Observed);
        Assert.Equal(0, foldToRaise.Count);
        Assert.Equal(0, foldToRaise.Sample);
    }

    private static FailureSignal Signal(IReadOnlyList<FailureSignal> signals, string name) =>
        signals.Single(signal => signal.Name == name);

    /// <summary>
    /// Seat 1 raises, seat 0 calls (ending the outstanding aggression), seat 1 raises again, seat 2
    /// calls between the raise and the fold, and seat 0 folds. The fold is still facing the
    /// outstanding raise even though the immediately preceding action was a call.
    /// </summary>
    private static HandRecord FoldAfterRaiseCallerHand() =>
        new HandRecord(
            HandNumber: 1,
            Seed: 1UL,
            SmallBlind: 5,
            BigBlind: 10,
            StartingStack: 1000,
            ButtonSeat: 2,
            HoleCardsSeat0: TestCards.Parse("As Kh"),
            HoleCardsSeat1: TestCards.Parse("2c 3d"),
            Board: TestCards.Parse(string.Empty),
            Actions:
            [
                new HandActionRecord(0, 1, "Raise", 30L, 40L),
                new HandActionRecord(0, 0, "Call", 20L, 60L),
                new HandActionRecord(0, 1, "Raise", 70L, 130L),
                new HandActionRecord(0, 2, "Call", 70L, 200L),
                new HandActionRecord(0, 0, "Fold", 0L, 200L),
            ],
            Showdown: false,
            Winners: [2],
            PotAwarded: 200L,
            FinalStacks: [980L, 930L, 1090L])
        {
            SeatCount = 3,
            StartingStacks = [1000L, 1000L, 1000L],
            HoleCards =
            [
                TestCards.Parse("As Kh"),
                TestCards.Parse("2c 3d"),
                TestCards.Parse("7s 8s"),
            ],
        };

    /// <summary>A hand checked through every postflop street: seat 0 acts three times without aggression.</summary>
    private static HandRecord CheckedDownHand() =>
        new HandRecord(
            HandNumber: 1,
            Seed: 2UL,
            SmallBlind: 5,
            BigBlind: 10,
            StartingStack: 1000,
            ButtonSeat: 1,
            HoleCardsSeat0: TestCards.Parse("As Kh"),
            HoleCardsSeat1: TestCards.Parse("2c 3d"),
            Board: TestCards.Parse("4s 5h 9d Jc Qd"),
            Actions:
            [
                new HandActionRecord(0, 1, "Call", 5L, 20L),
                new HandActionRecord(0, 0, "Check", 0L, 20L),
                new HandActionRecord(1, 1, "Check", 0L, 20L),
                new HandActionRecord(1, 0, "Check", 0L, 20L),
                new HandActionRecord(2, 1, "Check", 0L, 20L),
                new HandActionRecord(2, 0, "Check", 0L, 20L),
                new HandActionRecord(3, 1, "Check", 0L, 20L),
                new HandActionRecord(3, 0, "Check", 0L, 20L),
            ],
            Showdown: true,
            Winners: [0],
            PotAwarded: 20L,
            FinalStacks: [1020L, 980L]);
}