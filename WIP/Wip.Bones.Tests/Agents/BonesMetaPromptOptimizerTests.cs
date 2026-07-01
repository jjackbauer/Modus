using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesMetaPromptOptimizerTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.T3_2_MetaPromptOptimization;

    private const string SampleSystemPrompt = "You are refining Block Dominoes tactics for seat 1 by revising a C# strategy script.\n"
        + "Output exactly one compilable C# source file: a public sealed class implementing IBonesPlayerSlot.\n"
        + "Review the prior script and match outcomes, then produce an enhanced script.\n"
        + "Preserve incumbent ChooseMove logic that correlates with wins; revise heuristics where outcomes were losses or score differential was negative.\n"
        + "Do not output markdown rules, commentary outside the code fence, or multiple classes.\n";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void OptimizeSystemPrompt_GivenRepeatedWinRateDecline_SuggestsWinRateFocus()
    {
        // Arrange: 3 consecutive iterations with win rate declining but score improving
        var recentOutcomes = new List<BonesEnhancementIterationOutcome>
        {
            new(-0.05, 5, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 1),
            new(-0.10, 8, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 2),
            new(-0.15, 12, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 3),
        };

        var optimizer = new BonesMetaPromptOptimizer();

        // Act
        var optimized = optimizer.OptimizeSystemPrompt(SampleSystemPrompt, recentOutcomes);

        // Assert: prompt was modified (appended guidance)
        Assert.NotEqual(SampleSystemPrompt, optimized);
        Assert.Contains(SampleSystemPrompt, optimized, StringComparison.Ordinal); // original is preserved
        Assert.Contains("endgame", optimized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("win probability", optimized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void OptimizeSystemPrompt_GivenStablePerformance_PreservesCurrentPrompt()
    {
        // Arrange: metrics are stable — no clear decline or improvement trend
        var recentOutcomes = new List<BonesEnhancementIterationOutcome>
        {
            new(0.01, 2, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 1),
            new(-0.02, -1, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 2),
            new(0.03, 3, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 3),
        };

        var optimizer = new BonesMetaPromptOptimizer();

        // Act
        var optimized = optimizer.OptimizeSystemPrompt(SampleSystemPrompt, recentOutcomes);

        // Assert: prompt is unchanged
        Assert.Equal(SampleSystemPrompt, optimized);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void OptimizeSystemPrompt_GivenMixedWinRateAndScore_SuggestsWinRateFocus()
    {
        // Arrange: win rate declining over last 3 iterations but score improving
        var recentOutcomes = new List<BonesEnhancementIterationOutcome>
        {
            new(+0.05, -3, BonesPromotionDecisionOutcome.Promoted, IterationNumber: 1),
            new(-0.05, 5, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 2),
            new(-0.10, 10, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 3),
            new(-0.20, 15, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 4),
        };

        var optimizer = new BonesMetaPromptOptimizer();

        // Act
        var optimized = optimizer.OptimizeSystemPrompt(SampleSystemPrompt, recentOutcomes);

        // Assert: last 3 have declining win rate and improving score, so guidance appended
        Assert.NotEqual(SampleSystemPrompt, optimized);
        Assert.Contains(SampleSystemPrompt, optimized, StringComparison.Ordinal);
        Assert.Contains("endgame", optimized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void OptimizeSystemPrompt_GivenFewerThanWindowSize_PreservesCurrentPrompt()
    {
        // Arrange: only 2 outcomes — window requires 3
        var recentOutcomes = new List<BonesEnhancementIterationOutcome>
        {
            new(-0.05, 5, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 1),
            new(-0.10, 8, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 2),
        };

        var optimizer = new BonesMetaPromptOptimizer(windowSize: 3);

        // Act
        var optimized = optimizer.OptimizeSystemPrompt(SampleSystemPrompt, recentOutcomes);

        // Assert: insufficient data — prompt preserved
        Assert.Equal(SampleSystemPrompt, optimized);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void OptimizeSystemPrompt_GivenScoreDeclineWithWinRateStable_PreservesCurrentPrompt()
    {
        // Arrange: win rate is stable/improving (not declining), score declining
        var recentOutcomes = new List<BonesEnhancementIterationOutcome>
        {
            new(+0.01, -5, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 1),
            new(+0.02, -8, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 2),
            new(+0.01, -10, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 3),
        };

        var optimizer = new BonesMetaPromptOptimizer();

        // Act
        var optimized = optimizer.OptimizeSystemPrompt(SampleSystemPrompt, recentOutcomes);

        // Assert: no win-rate-decline pattern — prompt unchanged
        Assert.Equal(SampleSystemPrompt, optimized);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void OptimizeSystemPrompt_GivenEmptyOutcomes_PreservesCurrentPrompt()
    {
        var recentOutcomes = Array.Empty<BonesEnhancementIterationOutcome>();

        var optimizer = new BonesMetaPromptOptimizer();

        var optimized = optimizer.OptimizeSystemPrompt(SampleSystemPrompt, recentOutcomes);

        Assert.Equal(SampleSystemPrompt, optimized);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void OptimizeSystemPrompt_GivenScoreNotImproving_PreservesCurrentPrompt()
    {
        // Arrange: win rate declining but score is also declining — only triggers when both conditions met
        var recentOutcomes = new List<BonesEnhancementIterationOutcome>
        {
            new(-0.05, -2, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 1),
            new(-0.10, -5, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 2),
            new(-0.15, -8, BonesPromotionDecisionOutcome.Rejected, IterationNumber: 3),
        };

        var optimizer = new BonesMetaPromptOptimizer();

        // Act
        var optimized = optimizer.OptimizeSystemPrompt(SampleSystemPrompt, recentOutcomes);

        // Assert: score not improving — no guidance (both conditions must be true)
        Assert.Equal(SampleSystemPrompt, optimized);
    }
}
