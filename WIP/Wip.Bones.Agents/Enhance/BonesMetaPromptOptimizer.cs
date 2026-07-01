namespace Wip.Bones.Agents.Enhance;

/// <summary>
/// Analyzes enhancement iteration outcomes and optimizes the system prompt used by the
/// enhancement LLM. Detects patterns like repeated win-rate decline with score improvement
/// and appends targeted guidance to the system prompt.
/// </summary>
public sealed class BonesMetaPromptOptimizer
{
    private const int DefaultWindowSize = 3;

    private readonly int _windowSize;

    public BonesMetaPromptOptimizer(int windowSize = DefaultWindowSize)
    {
        if (windowSize < 1)
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be at least 1.");

        _windowSize = windowSize;
    }

    /// <summary>
    /// Analyzes recent enhancement outcomes and returns an optimized system prompt.
    /// When a pattern is detected (e.g., repeated win rate decline with score improvement),
    /// appends focused guidance. When stable, returns the prompt unchanged.
    /// </summary>
    public string OptimizeSystemPrompt(
        string currentPrompt,
        IReadOnlyList<BonesEnhancementIterationOutcome> recentOutcomes)
    {
        ArgumentNullException.ThrowIfNull(currentPrompt);
        ArgumentNullException.ThrowIfNull(recentOutcomes);

        if (recentOutcomes.Count < _windowSize)
            return currentPrompt;

        // Take the most recent window
        var window = recentOutcomes
            .OrderBy(o => o.IterationNumber)
            .TakeLast(_windowSize)
            .ToList();

        if (DetectWinRateDeclineWithScoreImprovement(window))
        {
            return AppendWinRateFocusGuidance(currentPrompt);
        }

        // Stable — no change
        return currentPrompt;
    }

    /// <summary>
    /// Detects the pattern: win rate declined across all consecutive pairs in the window
    /// and score improved across all consecutive pairs in the window.
    /// </summary>
    private static bool DetectWinRateDeclineWithScoreImprovement(
        IReadOnlyList<BonesEnhancementIterationOutcome> window)
    {
        if (window.Count < 2)
            return false;

        var winRateDeclining = true;
        var scoreImproving = true;

        for (var i = 1; i < window.Count; i++)
        {
            if (window[i].WinRateDelta >= window[i - 1].WinRateDelta)
                winRateDeclining = false;

            if (window[i].ScoreDelta <= window[i - 1].ScoreDelta)
                scoreImproving = false;
        }

        return winRateDeclining && scoreImproving;
    }

    private static string AppendWinRateFocusGuidance(string currentPrompt)
    {
        const string guidance = """

            ## Meta-optimization guidance

            Recent enhancement iterations show a pattern of declining win rate while score differential has been improving.
            This suggests the enhancement process is prioritizing score optimization at the expense of match victories.

            Adjust your enhancement strategy:
            - Prioritize endgame win probability over marginal score improvements.
            - Favor heuristics that close out matches and block opponent finishing moves.
            - A strategy that loses by fewer points is still a loss — prioritize winning.

            """;

        return currentPrompt + guidance;
    }
}
