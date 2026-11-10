using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// The learning layer stays data-only: no process execution, no network, no source control calls and
/// no dynamic compilation on any path a strategy can reach.
/// </summary>
public sealed class LearningSourceSafetyTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "Process",
        "HttpClient",
        "git ",
        "Assembly.Load",
        "Compile",
    ];

    [Fact]
    public void LearningSources_GivenSafetyScan_ExpectedNoExecutionNetworkOrCodeGeneration()
    {
        var learningDirectory = Path.Combine(
            AgentTestSupport.RepositoryRoot(), "WIP", "Wip.Tholdem.Agents", "Learning");
        Assert.True(Directory.Exists(learningDirectory), $"Missing learning directory: {learningDirectory}");

        var sources = Directory
            .EnumerateFiles(learningDirectory, "*.cs", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.True(sources.Length >= 10, $"Expected at least ten learning sources, found {sources.Length}.");
        Assert.Contains(sources, static path => Path.GetFileName(path) == "StrategyArtifact.cs");
        Assert.Contains(sources, static path => Path.GetFileName(path) == "TrainingLoop.cs");

        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            foreach (var token in ForbiddenTokens)
            {
                Assert.False(
                    text.Contains(token, StringComparison.Ordinal),
                    $"{Path.GetFileName(source)} contains the forbidden token '{token}'.");
            }

            // Strategies stay data: nothing in the learning layer evaluates model-authored text.
            Assert.DoesNotContain("CSharpScript", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CodeDom", text, StringComparison.Ordinal);
        }

        // The proposer, evaluator and loop must be present, and the artifact payload must be JSON data.
        var artifactSource = File.ReadAllText(Path.Combine(learningDirectory, "StrategyArtifact.cs"));
        Assert.Contains("promptText", artifactSource, StringComparison.Ordinal);
        Assert.Contains("parameters", artifactSource, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Diagnostics", artifactSource, StringComparison.Ordinal);
    }
}
