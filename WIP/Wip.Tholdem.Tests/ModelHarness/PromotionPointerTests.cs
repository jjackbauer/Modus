using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// WP-5 evidence: a live-refiner proposal the frozen gate accepts moves the champion pointer and
/// stamps it with the persisted evaluation report hash, while an unjustified always-promote control
/// cannot move that pointer at all. Every model call is scripted; no network call is made.
/// </summary>
public sealed class PromotionPointerTests
{
    private const ulong Seed = 20_261_109UL;

    private const string GateValidProposal =
        "{\"raiseThreshold\":0.80,\"callThreshold\":0.40,\"bluffFrequency\":0.10,\"sizingMultiplier\":1.0}";

    private const string UnjustifiedProposal =
        "{\"raiseThreshold\":0.60,\"callThreshold\":0.55,\"bluffFrequency\":0.18,\"sizingMultiplier\":1.15}";

    [Fact]
    public void Promotion_GivenLlmProposedChallenger_ExpectedVerifiedPointerMove()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-live-promotion");
        try
        {
            var championId = PrepareStart(dataDirectory);
            var pointerPath = Path.Combine(dataDirectory, TrainingLoop.ChampionFileName);
            var pointerBefore = File.ReadAllBytes(pointerPath);
            Assert.Equal(
                "{\"strategyId\":\"" + championId + "\",\"iteration\":0}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(pointerBefore));

            var model = new RecordingChatModel();
            model.EnqueueResponse(RecordingChatModel.Response(GateValidProposal));
            var transcripts = new LlmTranscriptStore(dataDirectory);
            var policy = new LlmRefinementPolicy(model, Options(), dataDirectory, transcripts, LiveEnvironment());

            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: Seed,
                    Iterations: 1,
                    TrainingHands: 12,
                    EvaluationSeeds: 24,
                    EvaluationHandsPerMatch: 20,
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true),
                refinementPolicy: policy);

            var iteration = Assert.Single(run.Iterations);
            Assert.Equal(championId, iteration.ChampionId);
            Assert.NotNull(iteration.Report);
            Assert.Equal(24, iteration.Report!.Seeds);
            Assert.True(
                iteration.Verified,
                "The frozen verifier refused the measured challenger at lower bound " + iteration.Report.LowerBoundBbPer100 + ".");
            Assert.True(
                iteration.Decision.Promoted,
                "The frozen gate did not promote the measured challenger at lower bound " + iteration.Report.LowerBoundBbPer100 + ".");
            Assert.Equal("promoted", iteration.Decision.Reason);
            Assert.Equal(TrainingLoop.PromotedOutcome, iteration.Outcome);
            Assert.Equal(iteration.ChallengerId, run.FinalChampionId);
            Assert.Equal(1, model.CallCount);

            var challengerPath = Path.Combine(dataDirectory, "strategies", iteration.ChallengerId + ".json");
            Assert.True(File.Exists(challengerPath), "The challenger artifact was not persisted: " + challengerPath);
            using (var challenger = JsonDocument.Parse(File.ReadAllBytes(challengerPath)))
            {
                var parameters = challenger.RootElement.GetProperty("parameters");
                Assert.Equal(0.80, parameters.GetProperty("raiseThreshold").GetDouble());
                Assert.Equal(0.40, parameters.GetProperty("callThreshold").GetDouble());
                Assert.Equal(0.10, parameters.GetProperty("bluffFrequency").GetDouble());
                Assert.Equal(1.0, parameters.GetProperty("sizingMultiplier").GetDouble());
            }

            var evaluationPath = Path.Combine(
                dataDirectory,
                "evaluations",
                iteration.ChallengerId + "-vs-" + iteration.ChampionId + ".json");
            Assert.True(File.Exists(evaluationPath), "The evaluation report was not persisted: " + evaluationPath);
            var persistedReportHash = StrategyArtifact.ComputeHash(File.ReadAllText(evaluationPath));
            Assert.Equal(iteration.Report.ReportHash, persistedReportHash);

            var pointerAfter = File.ReadAllBytes(pointerPath);
            using (var pointer = JsonDocument.Parse(pointerAfter))
            {
                Assert.Equal(iteration.ChallengerId, pointer.RootElement.GetProperty("strategyId").GetString());
                Assert.Equal(1, pointer.RootElement.GetProperty("iteration").GetInt32());
                Assert.Equal(
                    persistedReportHash,
                    pointer.RootElement.GetProperty("evaluationReportHash").GetString());
            }

            Assert.False(
                pointerBefore.SequenceEqual(pointerAfter),
                "The champion pointer bytes did not change after a verified promotion.");
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Promotion_GivenUnjustifiedAlwaysPromotePolicy_ExpectedNoPointerMove()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-live-unjustified");
        try
        {
            var championId = PrepareStart(dataDirectory);
            var pointerPath = Path.Combine(dataDirectory, TrainingLoop.ChampionFileName);
            var pointerBefore = File.ReadAllBytes(pointerPath);

            var model = new RecordingChatModel();
            model.EnqueueResponse(RecordingChatModel.Response(UnjustifiedProposal));
            var transcripts = new LlmTranscriptStore(dataDirectory);
            var policy = new LlmRefinementPolicy(model, Options(), dataDirectory, transcripts, LiveEnvironment());

            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: Seed,
                    Iterations: 1,
                    TrainingHands: 12,
                    EvaluationSeeds: 24,
                    EvaluationHandsPerMatch: 20,
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true),
                policy: new AlwaysPromotePolicy(),
                refinementPolicy: policy);

            var iteration = Assert.Single(run.Iterations);
            Assert.Equal(championId, iteration.ChampionId);
            Assert.Equal(championId, run.FinalChampionId);
            Assert.NotNull(iteration.Report);
            Assert.True(
                iteration.Report!.LowerBoundBbPer100 <= 5.0,
                "The negative-control proposal cleared the margin at lower bound "
                + iteration.Report.LowerBoundBbPer100
                + ", so the control is not unjustified.");
            Assert.False(iteration.Verified, "The frozen verifier accepted the always-promote control.");
            Assert.False(iteration.Decision.Promoted, "The always-promote control moved the pointer.");
            Assert.Equal(TrainingLoop.VerificationFailedOutcome, iteration.Outcome);
            Assert.Equal(PromotionVerifier.LowerBoundFailureReason, iteration.Decision.Reason);

            var pointerAfter = File.ReadAllBytes(pointerPath);
            Assert.Equal(pointerBefore, pointerAfter);
            using (var pointer = JsonDocument.Parse(pointerAfter))
            {
                Assert.Equal(championId, pointer.RootElement.GetProperty("strategyId").GetString());
                Assert.Equal(0, pointer.RootElement.GetProperty("iteration").GetInt32());
                Assert.False(pointer.RootElement.TryGetProperty("evaluationReportHash", out _));
            }
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
