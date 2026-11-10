using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// WP-5 support: the real LlmRefinementPolicy drives every iteration of TrainingLoop.Run, and the
/// frozen statistical gate re-derives each persisted verdict from the evaluation file. Both tests
/// script the IChatModel; no network call is made.
/// </summary>
public sealed class TrainingLoopLlmPolicyTests
{
    private const ulong Seed = 20_261_109UL;

    [Fact]
    public void TrainingLoop_GivenLlmPolicy_ExpectedModelCalledOncePerIteration()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-loop-llm-calls");
        try
        {
            var proposals = new[]
            {
                new
                {
                    Text = "{\"raiseThreshold\":0.66,\"callThreshold\":0.39,\"bluffFrequency\":0.18,\"sizingMultiplier\":1.4}",
                    Genome = new StrategyGenome(0.66, 0.39, 0.18, 1.4),
                },
                new
                {
                    Text = "{\"raiseThreshold\":0.58,\"callThreshold\":0.31,\"bluffFrequency\":0.24,\"sizingMultiplier\":1.7}",
                    Genome = new StrategyGenome(0.58, 0.31, 0.24, 1.7),
                },
            };

            var model = new RecordingChatModel();
            foreach (var proposal in proposals)
            {
                model.EnqueueResponse(RecordingChatModel.Response(proposal.Text));
            }

            var transcripts = new LlmTranscriptStore(dataDirectory);
            var policy = new LlmRefinementPolicy(
                model,
                Options(),
                dataDirectory,
                transcripts,
                LiveEnvironment());

            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: Seed,
                    Iterations: 2,
                    TrainingHands: 12,
                    EvaluationSeeds: 2,
                    EvaluationHandsPerMatch: 2,
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true),
                refinementPolicy: policy);

            Assert.Equal(2, model.CallCount);
            var stored = transcripts.ReadAll();
            Assert.Equal(2, stored.Count);
            Assert.Equal(new[] { "llm-1", "llm-2" }, stored.Select(static item => item.CorrelationId).ToArray());
            Assert.Equal(2, run.Iterations.Count);

            for (var index = 0; index < run.Iterations.Count; index++)
            {
                var iteration = run.Iterations[index];
                var artifactPath = Path.Combine(
                    dataDirectory,
                    "strategies",
                    iteration.ChallengerId + ".json");
                Assert.True(File.Exists(artifactPath), "The challenger artifact was not persisted: " + artifactPath);

                using var document = JsonDocument.Parse(File.ReadAllBytes(artifactPath));
                var parameters = document.RootElement.GetProperty("parameters");
                Assert.Equal(
                    proposals[index].Genome.RaiseThreshold,
                    parameters.GetProperty("raiseThreshold").GetDouble());
                Assert.Equal(
                    proposals[index].Genome.CallThreshold,
                    parameters.GetProperty("callThreshold").GetDouble());
                Assert.Equal(
                    proposals[index].Genome.BluffFrequency,
                    parameters.GetProperty("bluffFrequency").GetDouble());
                Assert.Equal(
                    proposals[index].Genome.SizingMultiplier,
                    parameters.GetProperty("sizingMultiplier").GetDouble());
            }
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void TrainingLoop_GivenLlmProposal_ExpectedFrozenGateVerdict()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-loop-llm-gate");
        try
        {
            var model = new RecordingChatModel();
            model.EnqueueResponse(RecordingChatModel.Response(
                "{\"raiseThreshold\":0.64,\"callThreshold\":0.36,\"bluffFrequency\":0.2,\"sizingMultiplier\":1.5}"));
            model.EnqueueResponse(RecordingChatModel.Response(
                "{\"raiseThreshold\":0.55,\"callThreshold\":0.3,\"bluffFrequency\":0.26,\"sizingMultiplier\":1.8}"));

            var transcripts = new LlmTranscriptStore(dataDirectory);
            var policy = new LlmRefinementPolicy(
                model,
                Options(),
                dataDirectory,
                transcripts,
                LiveEnvironment());

            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: Seed,
                    Iterations: 2,
                    TrainingHands: 12,
                    EvaluationSeeds: 2,
                    EvaluationHandsPerMatch: 2,
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true),
                refinementPolicy: policy);

            var criteria = new EvaluationCriteria();
            Assert.Equal(5.0, criteria.MarginBbPer100);
            Assert.Equal(24, criteria.MinSeeds);

            foreach (var iteration in run.Iterations)
            {
                Assert.NotNull(iteration.Report);
                var evaluationPath = Path.Combine(
                    dataDirectory,
                    "evaluations",
                    iteration.ChallengerId + "-vs-" + iteration.ChampionId + ".json");
                Assert.True(File.Exists(evaluationPath), "The evaluation report was not persisted: " + evaluationPath);

                var fileText = File.ReadAllText(evaluationPath);
                Assert.Equal(iteration.Report!.ReportHash, StrategyArtifact.ComputeHash(fileText));
                Assert.True(
                    EvaluationReportJson.TryParse(fileText, out var persisted, out var parseReason),
                    parseReason);

                var decision = new StatisticalPromotionPolicy().Decide(persisted!, criteria);
                var verified = PromotionVerifier.Verify(decision, persisted!, criteria, out _);

                Assert.Equal(iteration.Decision.Promoted, decision.Promoted);
                Assert.Equal(iteration.Decision.Reason, decision.Reason);
                Assert.Equal(iteration.Verified, verified);
                Assert.Equal(5.0, decision.MarginBbPer100);
                Assert.Equal(24, criteria.MinSeeds);
            }
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static LlmRefinerOptions Options() =>
        new(
            ModelId: "deepseek-chat",
            MaxTokens: 256,
            Temperature: 0.2,
            Timeout: TimeSpan.FromSeconds(5),
            MaxRepairRetries: 1,
            MaxCallsPerRun: 8);

    private static Func<string, string?> LiveEnvironment() => name => name switch
    {
        "THOLDEM_LIVE_MODEL" => "1",
        "DEEPSEEK_API_KEY" => "fake-test-key",
        _ => null,
    };
}
