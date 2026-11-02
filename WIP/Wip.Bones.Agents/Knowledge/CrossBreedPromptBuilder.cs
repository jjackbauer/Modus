using Wip.Bones.Domain;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// Builds a cross-breeding prompt that combines two parent strategies,
/// providing the LLM with both source scripts and performance context.
/// </summary>
public static class CrossBreedPromptBuilder
{
    public static string Build(StrategyEntry parentA, StrategyEntry parentB)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# Cross-Breed Enhancement Prompt");
        sb.AppendLine();
        sb.AppendLine("Combine the best heuristics from two parent strategies into a merged offspring.");
        sb.AppendLine();

        // Parent A
        var winRateA = parentA.MatchesPlayed > 0
            ? (double)parentA.Wins / parentA.MatchesPlayed * 100.0
            : 0.0;
        sb.AppendLine("## Parent A");
        sb.AppendLine($"StrategyId: {parentA.Id.Value}");
        sb.AppendLine($"Win Rate: {winRateA:F0}%");
        sb.AppendLine($"ELO: {parentA.Rating:F0}");
        sb.AppendLine($"Score Differential: {parentA.CumulativeScoreDifferential}");
        sb.AppendLine();
        sb.AppendLine("### Source:");
        sb.AppendLine("```csharp");
        sb.AppendLine(parentA.Source);
        sb.AppendLine("```");
        sb.AppendLine();

        // Parent B
        var winRateB = parentB.MatchesPlayed > 0
            ? (double)parentB.Wins / parentB.MatchesPlayed * 100.0
            : 0.0;
        sb.AppendLine("## Parent B");
        sb.AppendLine($"StrategyId: {parentB.Id.Value}");
        sb.AppendLine($"Win Rate: {winRateB:F0}%");
        sb.AppendLine($"ELO: {parentB.Rating:F0}");
        sb.AppendLine($"Score Differential: {parentB.CumulativeScoreDifferential}");
        sb.AppendLine();
        sb.AppendLine("### Source:");
        sb.AppendLine("```csharp");
        sb.AppendLine(parentB.Source);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Instructions");
        sb.AppendLine("Produce a merged strategy that combines the best aspects of both parents.");

        return sb.ToString();
    }
}
