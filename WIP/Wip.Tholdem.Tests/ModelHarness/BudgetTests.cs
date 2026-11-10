using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// M-09 wall-clock branch: an already-expired wall-clock budget is a stable refusal recorded as one
/// budget-exceeded transcript with zero model calls. Local only: the model is the recording double
/// and the environment lookup is fake, so no network and no real key is reached.
/// </summary>
public sealed class BudgetTests
{
    [Fact]
    public void LlmRefinementPolicy_GivenExpiredWallClock_ExpectedBudgetExceededWithoutCall()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-llm-wall-clock");
        try
        {
            var model = new RecordingChatModel();
            var transcripts = new LlmTranscriptStore(dataDirectory);
            var options = new LlmRefinerOptions(
                ModelId: "deepseek-chat",
                MaxTokens: 256,
                Temperature: 0.2,
                Timeout: TimeSpan.FromSeconds(5),
                MaxRepairRetries: 1,
                MaxCallsPerRun: 8,
                MaxWallClock: TimeSpan.FromTicks(1));
            var policy = new LlmRefinementPolicy(
                model,
                options,
                dataDirectory,
                transcripts,
                LiveEnvironment());

            var exception = Assert.Throws<LlmRefinementRefusalException>(
                () => policy.Propose(TestContext(iteration: 1)));

            Assert.Equal("budget-exceeded", exception.Code);
            Assert.Equal(0, model.CallCount);
            var stored = Assert.Single(transcripts.ReadAll());
            Assert.Equal(1, stored.Iteration);
            Assert.Equal("llm-1", stored.CorrelationId);
            Assert.Equal(LlmTranscriptOutcomes.BudgetExceeded, stored.Outcome);
            Assert.Contains("MaxWallClock", stored.Error);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static Func<string, string?> LiveEnvironment() => name => name switch
    {
        "THOLDEM_LIVE_MODEL" => "1",
        "DEEPSEEK_API_KEY" => "fake-test-key",
        _ => null,
    };

    private static RefinementContext TestContext(int iteration) => new(
        new StrategyGenome(),
        new SessionAnalysis(120, 0.48, -3.5, 1.7, 0.21, 0.0),
        SessionSignatures.Empty,
        iteration);
}
