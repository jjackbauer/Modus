using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// WP-5 evidence: a transport failure on the single refinement call is a stable refusal that records
/// one http-5xx transcript, writes no evaluation report and leaves the champion pointer untouched.
/// The failure is scripted; no network call is made.
/// </summary>
public sealed class ModelFailureTests
{
    private const ulong Seed = 20_261_109UL;

    private const string ProviderFailure = "The provider returned HTTP 503.";

    [Fact]
    public void TrainingLoop_GivenModelFailure_ExpectedNoPromotionAndRecordedError()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-model-failure");
        try
        {
            var championId = PrepareStart(dataDirectory);
            var pointerPath = Path.Combine(dataDirectory, TrainingLoop.ChampionFileName);
            var pointerBefore = File.ReadAllBytes(pointerPath);

            var model = new RecordingChatModel();
            model.EnqueueFailure(new HttpRequestException(ProviderFailure));
            var transcripts = new LlmTranscriptStore(dataDirectory);
            var policy = new LlmRefinementPolicy(model, Options(), dataDirectory, transcripts, LiveEnvironment());

            var exception = Assert.Throws<LlmRefinementRefusalException>(
                () => TrainingLoop.Run(
                    new TrainingOptions(
                        Seed: Seed,
                        Iterations: 1,
                        TrainingHands: 12,
                        EvaluationSeeds: 2,
                        EvaluationHandsPerMatch: 2,
                        DataDirectory: dataDirectory,
                        HarnessRefinement: true),
                    refinementPolicy: policy));

            Assert.Equal("model-failure", exception.Code);
            Assert.Equal(1, model.CallCount);

            var pointerAfter = File.ReadAllBytes(pointerPath);
            Assert.Equal(pointerBefore, pointerAfter);
            using (var pointer = JsonDocument.Parse(pointerAfter))
            {
                Assert.Equal(championId, pointer.RootElement.GetProperty("strategyId").GetString());
                Assert.Equal(0, pointer.RootElement.GetProperty("iteration").GetInt32());
                Assert.False(pointer.RootElement.TryGetProperty("evaluationReportHash", out _));
            }

            var evaluationDirectory = Path.Combine(dataDirectory, "evaluations");
            var evaluationFiles = Directory.EnumerateFiles(evaluationDirectory, "*.json").ToArray();
            Assert.Empty(evaluationFiles);
            Assert.DoesNotContain(
                evaluationFiles,
                path => Path.GetFileName(path).EndsWith("-vs-" + championId + ".json", StringComparison.Ordinal));

            var stored = Assert.Single(transcripts.ReadAll());
            Assert.Equal("llm-1", stored.CorrelationId);
            Assert.Equal(1, stored.Iteration);
            Assert.Equal(LlmTranscriptOutcomes.Http5xx, stored.Outcome);
            Assert.NotNull(stored.Error);
            Assert.Contains("503", stored.Error!, StringComparison.Ordinal);
            Assert.Equal(ProviderFailure, stored.Error);
            Assert.Equal(string.Empty, stored.Response);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    /// <summary>Prepares the 0.70/0.70/0.12/1.0 start exactly as the arm runner's --prepare-start does.</summary>
    private static string PrepareStart(string dataDirectory)
    {
        var artifact = StrategyArtifact.Create(new StrategyGenome(0.70, 0.70, 0.12, 1.0), 0, null);
        var strategyDirectory = Path.Combine(dataDirectory, "strategies");
        Directory.CreateDirectory(strategyDirectory);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(
            Path.Combine(strategyDirectory, artifact.Id + ".json"),
            artifact.ToCanonicalJson(),
            utf8);
        File.WriteAllText(
            Path.Combine(dataDirectory, TrainingLoop.ChampionFileName),
            "{\"strategyId\":\"" + artifact.Id + "\",\"iteration\":0}",
            utf8);
        return artifact.Id;
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
