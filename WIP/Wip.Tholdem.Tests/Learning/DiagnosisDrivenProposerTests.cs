using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// Contract for the diagnosis-driven proposer adapter: measured failure signatures select the
/// challenger through the shipped refinement policy, while an empty or below-floor signature set
/// stays byte-identical to the deterministic proposer. Every fact fixes the complete input, so a
/// passing run is repeatable without a clock, GUID, culture or ambient random source.
/// </summary>
public sealed class DiagnosisDrivenProposerTests
{
    [Fact]
    public void Propose_GivenFoldToRaiseSignal_LowersCallThresholdByStep()
    {
        var incumbent = new StrategyGenome();
        var proposer = new DiagnosisDrivenProposer(
            new SessionSignatures(
            [
                new FailureSignal(FailureSignalNames.FoldToRaise, 10, 10, 1.0, FailureSignalDirections.High),
            ]));

        var proposed = proposer.Propose(incumbent, LosingAnalysis(), iteration: 1);

        Assert.Equal(0.37, proposed.CallThreshold, 12);
        Assert.Equal(
            incumbent.CallThreshold - SignatureDrivenRefinementPolicy.Step,
            proposed.CallThreshold,
            12);
        Assert.Equal(incumbent.RaiseThreshold, proposed.RaiseThreshold, 12);
        Assert.Equal(incumbent.BluffFrequency, proposed.BluffFrequency, 12);
        Assert.Equal(incumbent.SizingMultiplier, proposed.SizingMultiplier, 12);
        Assert.Equal(incumbent.PromptText, proposed.PromptText);
        Assert.False(proposed.BehaviorEquals(incumbent));
    }

    [Fact]
    public void Propose_GivenNoSignatures_EqualsDeterministicProposer()
    {
        var incumbent = new StrategyGenome();
        var analysis = WinningAnalysis();
        var expected = new DeterministicStrategyProposer().Propose(incumbent, analysis, iteration: 1);

        var proposed = new DiagnosisDrivenProposer(SessionSignatures.Empty)
            .Propose(incumbent, analysis, iteration: 1);

        Assert.False(expected.BehaviorEquals(incumbent));
        Assert.Equal(expected, proposed);
    }

    [Fact]
    public void Propose_GivenBelowSampleFloor_FallsBackToDeterministic()
    {
        var incumbent = new StrategyGenome();
        var analysis = LosingAnalysis();
        var signatures = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 3, 3, 1.0, FailureSignalDirections.High),
        ]);
        var expected = new DeterministicStrategyProposer().Propose(incumbent, analysis, iteration: 1);

        var proposed = new DiagnosisDrivenProposer(signatures).Propose(incumbent, analysis, iteration: 1);

        Assert.Equal(expected, proposed);

        // The 3/3 fold-to-raise signal is below the policy's ten-sample floor, so the adapter must
        // take the deterministic raise-threshold move instead of the signature call-threshold edit.
        Assert.Equal(incumbent.CallThreshold, proposed.CallThreshold, 12);
        Assert.Equal(incumbent.RaiseThreshold - DeterministicStrategyProposer.Step, proposed.RaiseThreshold, 12);
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

        var proposer = new DiagnosisDrivenProposer(signatures);
        var first = proposer.Propose(incumbent, analysis, iteration: 1);
        var second = proposer.Propose(incumbent, analysis, iteration: 1);
        var third = new DiagnosisDrivenProposer(signatures).Propose(incumbent, analysis, iteration: 1);

        Assert.Equal(first, second);
        Assert.Equal(first, third);

        // The equal rates are tie-broken by ordinal name, so both proposers select fold-to-raise.
        Assert.Equal(incumbent.CallThreshold - SignatureDrivenRefinementPolicy.Step, first.CallThreshold, 12);
    }

    private static SessionAnalysis LosingAnalysis() =>
        new(
            Hands: 120,
            WinRate: 0.42,
            BbPer100: -3.5,
            AggressionFactor: 1.2,
            ShowdownRate: 0.4,
            Vpip: 0.0);

    private static SessionAnalysis WinningAnalysis() =>
        new(
            Hands: 120,
            WinRate: 0.58,
            BbPer100: 14.5,
            AggressionFactor: 1.8,
            ShowdownRate: 0.3,
            Vpip: 0.0);
}
