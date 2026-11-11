using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// Contract for the signature-guarded step proposer: a measured fold-to-raise signal with a high
/// direction selects the call-threshold edit, but the step stays at the deterministic 0.02 instead
/// of the diagnosis policy's 0.08 jump. Every other input, and an edit that is behaviourally inert,
/// delegates to the deterministic proposer so the guard remains a conservative overlay.
/// </summary>
public sealed class SignatureGuardedProposerTests
{
    [Fact]
    public void Propose_GivenFoldToRaiseHigh_LowersCallByDeterministicStep()
    {
        var incumbent = new StrategyGenome();
        var analysis = LosingAnalysis();
        var signatures = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 10, 10, 1.0, FailureSignalDirections.High),
        ]);

        var proposed = new SignatureGuardedProposer(signatures)
            .Propose(incumbent, analysis, iteration: 1);

        Assert.Equal(0.43, proposed.CallThreshold, 12);
        Assert.Equal(
            incumbent.CallThreshold - SignatureGuardedProposer.Step,
            proposed.CallThreshold,
            12);
        Assert.Equal(incumbent.RaiseThreshold, proposed.RaiseThreshold, 12);
        Assert.Equal(incumbent.BluffFrequency, proposed.BluffFrequency, 12);
        Assert.Equal(incumbent.SizingMultiplier, proposed.SizingMultiplier, 12);
        Assert.Equal(incumbent.PromptText, proposed.PromptText);
        Assert.False(proposed.BehaviorEquals(incumbent));
    }

    [Fact]
    public void Propose_GivenEmptySignatures_EqualsDeterministicProposer()
    {
        var incumbent = new StrategyGenome();
        var analysis = LosingAnalysis();
        var expected = new DeterministicStrategyProposer().Propose(incumbent, analysis, iteration: 1);

        var proposed = new SignatureGuardedProposer(SessionSignatures.Empty)
            .Propose(incumbent, analysis, iteration: 1);

        Assert.False(expected.BehaviorEquals(incumbent));
        Assert.Equal(expected, proposed);
    }

    [Fact]
    public void Propose_GivenBelowSampleFloor_EqualsDeterministicProposer()
    {
        var incumbent = new StrategyGenome();
        var analysis = LosingAnalysis();
        var signatures = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 9, 9, 1.0, FailureSignalDirections.High),
        ]);
        var expected = new DeterministicStrategyProposer().Propose(incumbent, analysis, iteration: 1);

        var proposed = new SignatureGuardedProposer(signatures).Propose(incumbent, analysis, iteration: 1);

        Assert.Equal(expected, proposed);

        // Nine samples is one short of the ten-sample floor, so the guard must take the
        // deterministic raise-threshold move instead of the signature call-threshold edit.
        Assert.Equal(incumbent.CallThreshold, proposed.CallThreshold, 12);
        Assert.Equal(
            incumbent.RaiseThreshold - DeterministicStrategyProposer.Step,
            proposed.RaiseThreshold,
            12);
        Assert.False(proposed.BehaviorEquals(incumbent));
    }

    [Fact]
    public void Propose_GivenSameInputs_IsDeterministic()
    {
        var incumbent = new StrategyGenome();
        var analysis = LosingAnalysis();
        var signatures = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 10, 10, 1.0, FailureSignalDirections.High),
            new FailureSignal(FailureSignalNames.MissedValue, 10, 10, 1.0, FailureSignalDirections.High),
        ]);

        var proposer = new SignatureGuardedProposer(signatures);
        var first = proposer.Propose(incumbent, analysis, iteration: 1);
        var second = proposer.Propose(incumbent, analysis, iteration: 1);
        var third = new SignatureGuardedProposer(signatures).Propose(incumbent, analysis, iteration: 1);

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(incumbent.CallThreshold - SignatureGuardedProposer.Step, first.CallThreshold, 12);
        Assert.Equal(incumbent.RaiseThreshold, first.RaiseThreshold, 12);
    }

    private static SessionAnalysis LosingAnalysis() =>
        new(
            Hands: 120,
            WinRate: 0.42,
            BbPer100: -3.5,
            AggressionFactor: 1.2,
            ShowdownRate: 0.4,
            Vpip: 0.0);
}
