using Wip.Bones.Agents.Budget;
using Xunit;

namespace Wip.Bones.Tests.Budget;

public sealed class BonesAdaptiveEvaluationBudgetTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.AdaptiveEvaluationBudget;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void DetermineMatchCount_GivenCandidateClearlyBetter_ReturnsZeroAfterCheckpoint()
    {
        // After 10 matches with 9/10 candidate wins (0.90), the adaptive budget
        // should signal early termination by returning 0 remaining matches.
        var options = new BonesAdaptiveEvaluationBudgetOptions
        {
            BatchSize = 5,
            FullEvaluationMatchCount = 20,
            EarlyStopCheckpoint = 10,
            ClearWinnerThreshold = 0.90,
            ClearLoserThreshold = 0.10,
        };
        var budget = new BonesAdaptiveEvaluationBudget(options);

        var remaining = budget.DetermineMatchCount(
            matchesCompleted: 10,
            candidateWins: 9);

        Assert.Equal(0, remaining);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void GetState_GivenCandidateClearlyBetter_ReturnsStopEarlyPromote()
    {
        var options = new BonesAdaptiveEvaluationBudgetOptions
        {
            BatchSize = 5,
            FullEvaluationMatchCount = 20,
            EarlyStopCheckpoint = 10,
            ClearWinnerThreshold = 0.90,
            ClearLoserThreshold = 0.10,
        };
        var budget = new BonesAdaptiveEvaluationBudget(options);

        var state = budget.GetState(
            matchesCompleted: 10,
            candidateWins: 9);

        Assert.Equal(BonesAdaptiveDecision.StopEarlyPromote, state.Decision);
        Assert.Equal(10, state.MatchesCompleted);
        Assert.Equal(9, state.CandidateWins);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void DetermineMatchCount_GivenCandidateClearlyWorse_ReturnsZeroAfterCheckpoint()
    {
        // After 10 matches with 1/10 candidate wins (0.10), the adaptive budget
        // should signal early termination.
        var options = new BonesAdaptiveEvaluationBudgetOptions
        {
            BatchSize = 5,
            FullEvaluationMatchCount = 20,
            EarlyStopCheckpoint = 10,
            ClearWinnerThreshold = 0.90,
            ClearLoserThreshold = 0.10,
        };
        var budget = new BonesAdaptiveEvaluationBudget(options);

        var remaining = budget.DetermineMatchCount(
            matchesCompleted: 10,
            candidateWins: 1);

        Assert.Equal(0, remaining);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void GetState_GivenCandidateClearlyWorse_ReturnsStopEarlyReject()
    {
        var options = new BonesAdaptiveEvaluationBudgetOptions
        {
            BatchSize = 5,
            FullEvaluationMatchCount = 20,
            EarlyStopCheckpoint = 10,
            ClearWinnerThreshold = 0.90,
            ClearLoserThreshold = 0.10,
        };
        var budget = new BonesAdaptiveEvaluationBudget(options);

        var state = budget.GetState(
            matchesCompleted: 10,
            candidateWins: 1);

        Assert.Equal(BonesAdaptiveDecision.StopEarlyReject, state.Decision);
        Assert.Equal(10, state.MatchesCompleted);
        Assert.Equal(1, state.CandidateWins);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void DetermineMatchCount_GivenBorderlineResult_ContinuesToFullBudget()
    {
        // At 10 matches with 5/10 wins (0.50), the result is borderline.
        // Budget should continue to the full 20-match schedule.
        var options = new BonesAdaptiveEvaluationBudgetOptions
        {
            BatchSize = 5,
            FullEvaluationMatchCount = 20,
            EarlyStopCheckpoint = 10,
            ClearWinnerThreshold = 0.90,
            ClearLoserThreshold = 0.10,
        };
        var budget = new BonesAdaptiveEvaluationBudget(options);

        var remaining = budget.DetermineMatchCount(
            matchesCompleted: 10,
            candidateWins: 5);

        // Remaining matches = 20 - 10 = 10
        Assert.Equal(10, remaining);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void GetState_GivenBorderlineResult_ReturnsContinue()
    {
        var options = new BonesAdaptiveEvaluationBudgetOptions
        {
            BatchSize = 5,
            FullEvaluationMatchCount = 20,
            EarlyStopCheckpoint = 10,
            ClearWinnerThreshold = 0.90,
            ClearLoserThreshold = 0.10,
        };
        var budget = new BonesAdaptiveEvaluationBudget(options);

        var state = budget.GetState(
            matchesCompleted: 10,
            candidateWins: 5);

        Assert.Equal(BonesAdaptiveDecision.Continue, state.Decision);
    }

    [Theory]
    [InlineData(0, 0, 5)]   // Start: batch size of 5
    [InlineData(5, 5, 5)]   // 5/5 wins — still before checkpoint, keep going
    [InlineData(8, 7, 2)]   // At 8 matches, next batch would overshoot checkpoint (10),
                            // so return 2 to reach exactly checkpoint
    [InlineData(10, 9, 0)]  // 9/10 wins at checkpoint → stop early
    [InlineData(10, 1, 0)]  // 1/10 wins at checkpoint → stop early
    [InlineData(10, 5, 10)] // 5/10 wins at checkpoint → borderline, continue to 20
    [InlineData(15, 8, 5)]  // Past checkpoint, still borderline → continue
    [InlineData(18, 9, 2)]  // Nearing end, borderline → finish remaining
    [InlineData(20, 10, 0)] // At full budget, no more matches
    public void DetermineMatchCount_GivenVariousStates_ReturnsExpectedRemaining(
        int matchesCompleted,
        int candidateWins,
        int expectedRemaining)
    {
        var options = new BonesAdaptiveEvaluationBudgetOptions
        {
            BatchSize = 5,
            FullEvaluationMatchCount = 20,
            EarlyStopCheckpoint = 10,
            ClearWinnerThreshold = 0.90,
            ClearLoserThreshold = 0.10,
        };
        var budget = new BonesAdaptiveEvaluationBudget(options);

        var remaining = budget.DetermineMatchCount(matchesCompleted, candidateWins);

        Assert.Equal(expectedRemaining, remaining);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void DetermineMatchCount_GivenFullBudgetReached_ReturnsZero()
    {
        var budget = new BonesAdaptiveEvaluationBudget();

        var remaining = budget.DetermineMatchCount(
            matchesCompleted: 20,
            candidateWins: 10);

        Assert.Equal(0, remaining);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void Options_ReturnsImmutableCopy()
    {
        var budget = new BonesAdaptiveEvaluationBudget();

        var options = budget.Options;
        options.BatchSize = 999;

        // The internal state must not be affected by mutating the copy.
        var remaining = budget.DetermineMatchCount(0, 0);
        Assert.Equal(5, remaining); // default batch size is 5
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void Constructor_GivenInvalidOptions_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BonesAdaptiveEvaluationBudget(new BonesAdaptiveEvaluationBudgetOptions
            {
                BatchSize = 0,
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BonesAdaptiveEvaluationBudget(new BonesAdaptiveEvaluationBudgetOptions
            {
                FullEvaluationMatchCount = 0,
            }));

        Assert.Throws<ArgumentException>(() =>
            new BonesAdaptiveEvaluationBudget(new BonesAdaptiveEvaluationBudgetOptions
            {
                ClearLoserThreshold = 0.90,
                ClearWinnerThreshold = 0.50,
            }));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void DetermineMatchCount_GivenInvalidInputs_Throws()
    {
        var budget = new BonesAdaptiveEvaluationBudget();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            budget.DetermineMatchCount(-1, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            budget.DetermineMatchCount(5, 6)); // candidateWins > matchesCompleted
    }
}
