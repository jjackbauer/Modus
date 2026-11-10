using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// WP-5 boundary: the model harness lives in its own project and the learning layer still has no
/// chat-model, HTTP, vendor or harness reference; the pre-existing learning source-safety scan keeps
/// its forbidden-token list.
/// </summary>
public sealed class LearningIsolationTests
{
    private static readonly string[] LearningForbiddenTokens =
    [
        "IChatModel",
        "HttpClient",
        "DeepSeek",
        "ModelHarness",
    ];

    [Fact]
    public void LearningSources_GivenModelHarnessFolder_ExpectedNoDirectHttpClientInLearning()
    {
        var repositoryRoot = AgentTestSupport.RepositoryRoot();
        var modelHarnessDirectory = Path.Combine(repositoryRoot, "WIP", "Wip.Tholdem.ModelHarness");
        Assert.True(Directory.Exists(modelHarnessDirectory), "Missing model harness project: " + modelHarnessDirectory);
        Assert.True(
            File.Exists(Path.Combine(modelHarnessDirectory, "LlmRefinementPolicy.cs")),
            "Missing LlmRefinementPolicy.cs in " + modelHarnessDirectory);
        Assert.True(
            File.Exists(Path.Combine(modelHarnessDirectory, "LiveModelConfiguration.cs")),
            "Missing LiveModelConfiguration.cs in " + modelHarnessDirectory);

        var learningDirectory = Path.Combine(repositoryRoot, "WIP", "Wip.Tholdem.Agents", "Learning");
        var sources = Directory
            .EnumerateFiles(learningDirectory, "*.cs", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.True(sources.Length >= 10, "Expected at least ten learning sources, found " + sources.Length + ".");

        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            foreach (var token in LearningForbiddenTokens)
            {
                Assert.False(
                    text.Contains(token, StringComparison.Ordinal),
                    Path.GetFileName(source) + " contains the forbidden token '" + token + "'.");
            }
        }

        var safetyScanPath = Path.Combine(
            repositoryRoot,
            "WIP",
            "Wip.Tholdem.Tests",
            "Learning",
            "LearningSourceSafetyTests.cs");
        Assert.True(File.Exists(safetyScanPath), "The learning source-safety scan is missing: " + safetyScanPath);
        var safetyScan = File.ReadAllText(safetyScanPath);
        Assert.Contains("ForbiddenTokens", safetyScan, StringComparison.Ordinal);
        foreach (var token in new[] { "\"Process\"", "\"HttpClient\"", "\"Assembly.Load\"", "\"Compile\"" })
        {
            Assert.Contains(token, safetyScan, StringComparison.Ordinal);
        }
    }
}
