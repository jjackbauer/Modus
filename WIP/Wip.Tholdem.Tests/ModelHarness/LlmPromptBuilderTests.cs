using System.Text.Json;
using System.Text.RegularExpressions;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Learning.Skills;
using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// M-02: the prompt carries exactly the measured data sections, in a fixed order, and never carries
/// hidden information, a seed, a card, a key, a tool definition or executable text.
/// </summary>
public sealed class LlmPromptBuilderTests
{
    [Fact]
    public void LlmPromptBuilder_GivenFullContext_ExpectedEverySectionAndBounds()
    {
        var skills = new StrategySkillSet(
            new PreflopSkillParameters(0.7, 0.4),
            new PostflopSkillParameters(0.65, 0.35),
            new SizingSkillParameters(1.5),
            new BluffSkillParameters(0.2));
        var context = new LlmPromptContext(
            Iteration: 3,
            Incumbent: new StrategyGenome(0.72, 0.45, 0.12, 1.25, Skills: skills),
            Analysis: new SessionAnalysis(120, 0.48, -3.5, 1.7, 0.21, 0.05),
            Signals: [new FailureSignal("fold-to-raise", 3, 20, 0.15, "low")],
            Memory: [new HarnessMemoryEntry("pattern", "tighten-preflop", 2, 1, 3)],
            Lineage:
            [
                new StrategyLineageEntry(
                    "challenger-abc123",
                    2,
                    "root-def456",
                    1,
                    "promoted",
                    "accepted",
                    12.5,
                    3.2,
                    24),
            ]);

        var userText = LlmPromptBuilder.BuildUserPrompt(context);
        var builtPrompt = LlmPromptBuilder.BuildPrompt(context);

        Assert.Equal(LlmPromptBuilder.SystemPromptText + "\n\n" + userText, builtPrompt);
        Assert.Contains("untrusted data", LlmPromptBuilder.SystemPromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly one JSON object", LlmPromptBuilder.SystemPromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("raiseThreshold", LlmPromptBuilder.SystemPromptText);
        Assert.Contains("callThreshold", LlmPromptBuilder.SystemPromptText);
        Assert.Contains("bluffFrequency", LlmPromptBuilder.SystemPromptText);
        Assert.Contains("sizingMultiplier", LlmPromptBuilder.SystemPromptText);

        using var document = JsonDocument.Parse(userText);
        var root = document.RootElement;
        Assert.Equal(
            new[] { "iteration", "incumbent", "analysis", "signals", "memory", "lineage", "bounds" },
            root.EnumerateObject().Select(static property => property.Name).ToArray());
        Assert.Equal(3, root.GetProperty("iteration").GetInt32());

        var incumbent = root.GetProperty("incumbent");
        Assert.Equal(5, incumbent.EnumerateObject().Count());
        Assert.Equal(0.72, incumbent.GetProperty("raiseThreshold").GetDouble());
        Assert.Equal(0.45, incumbent.GetProperty("callThreshold").GetDouble());
        Assert.Equal(0.12, incumbent.GetProperty("bluffFrequency").GetDouble());
        Assert.Equal(1.25, incumbent.GetProperty("sizingMultiplier").GetDouble());
        var promptSkills = incumbent.GetProperty("skills");
        Assert.Equal(0.7, promptSkills.GetProperty("preflop").GetProperty("raiseThreshold").GetDouble());
        Assert.Equal(0.4, promptSkills.GetProperty("preflop").GetProperty("callThreshold").GetDouble());
        Assert.Equal(0.65, promptSkills.GetProperty("postflop").GetProperty("raiseThreshold").GetDouble());
        Assert.Equal(0.35, promptSkills.GetProperty("postflop").GetProperty("callThreshold").GetDouble());
        Assert.Equal(1.5, promptSkills.GetProperty("sizing").GetProperty("multiplier").GetDouble());
        Assert.Equal(0.2, promptSkills.GetProperty("bluff").GetProperty("frequency").GetDouble());

        var analysis = root.GetProperty("analysis");
        Assert.Equal(8, analysis.EnumerateObject().Count());
        Assert.Equal(120, analysis.GetProperty("hands").GetInt32());
        Assert.Equal(0.48, analysis.GetProperty("winRate").GetDouble());
        Assert.Equal(-3.5, analysis.GetProperty("bbPer100").GetDouble());
        Assert.Equal(1.7, analysis.GetProperty("aggressionFactor").GetDouble());
        Assert.Equal(0.21, analysis.GetProperty("showdownRate").GetDouble());
        Assert.Equal(0.05, analysis.GetProperty("vpip").GetDouble());

        var signal = Assert.Single(root.GetProperty("signals").EnumerateArray());
        Assert.Equal(5, signal.EnumerateObject().Count());
        Assert.Equal("fold-to-raise", signal.GetProperty("name").GetString());
        Assert.Equal(3, signal.GetProperty("count").GetInt32());
        Assert.Equal(20, signal.GetProperty("sample").GetInt32());
        Assert.Equal(0.15, signal.GetProperty("rate").GetDouble());
        Assert.Equal("low", signal.GetProperty("direction").GetString());

        var memory = Assert.Single(root.GetProperty("memory").EnumerateArray());
        Assert.Equal(5, memory.EnumerateObject().Count());
        Assert.Equal("pattern", memory.GetProperty("kind").GetString());
        Assert.Equal("tighten-preflop", memory.GetProperty("key").GetString());
        Assert.Equal(2, memory.GetProperty("count").GetInt32());
        Assert.Equal(1, memory.GetProperty("firstIteration").GetInt32());
        Assert.Equal(3, memory.GetProperty("lastIteration").GetInt32());

        var lineage = Assert.Single(root.GetProperty("lineage").EnumerateArray());
        Assert.Equal(4, lineage.EnumerateObject().Count());
        Assert.Equal("challenger-abc123", lineage.GetProperty("id").GetString());
        Assert.Equal(2, lineage.GetProperty("version").GetInt32());
        Assert.Equal(1, lineage.GetProperty("createdAtIteration").GetInt32());
        Assert.Equal("promoted", lineage.GetProperty("outcome").GetString());

        var bounds = root.GetProperty("bounds");
        Assert.Equal(4, bounds.EnumerateObject().Count());
        Assert.Equal(new[] { 0.0, 1.0 }, bounds.GetProperty("raiseThreshold").EnumerateArray().Select(static value => value.GetDouble()).ToArray());
        Assert.Equal(new[] { 0.0, 1.0 }, bounds.GetProperty("callThreshold").EnumerateArray().Select(static value => value.GetDouble()).ToArray());
        Assert.Equal(new[] { 0.0, 1.0 }, bounds.GetProperty("bluffFrequency").EnumerateArray().Select(static value => value.GetDouble()).ToArray());
        Assert.Equal(new[] { 0.25, 3.0 }, bounds.GetProperty("sizingMultiplier").EnumerateArray().Select(static value => value.GetDouble()).ToArray());

        Assert.DoesNotMatch(new Regex("\\d{13,}"), userText);
        Assert.DoesNotContain("As", userText, StringComparison.Ordinal);
        Assert.DoesNotContain("Kd", userText, StringComparison.Ordinal);
        Assert.DoesNotContain("tools", userText, StringComparison.Ordinal);
    }
}
