using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// WP-5 evidence: a cancellation raised inside the promotion policy aborts the iteration after the
/// evaluation report exists in memory but before anything is persisted, so the pointer, the
/// evaluations directory and the ledger all stay exactly as they were.
/// </summary>
public sealed class CancellationTests
{
    private const ulong Seed = 20_261_109UL;

    [Fact]
    public void TrainingLoop_GivenCancellation_ExpectedNoPointerChangeAndNoPersistedPromotion()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-cancellation");
        try
        {
            var championId = PrepareStart(dataDirectory);
            var pointerPath = Path.Combine(dataDirectory, TrainingLoop.ChampionFileName);
            var pointerBefore = File.ReadAllBytes(pointerPath);

            var model = new RecordingChatModel();
            model.EnqueueResponse(RecordingChatModel.Response(
                "{\"raiseThreshold\":0.66,\"callThreshold\":0.39,\"bluffFrequency\":0.18,\"sizingMultiplier\":1.4}"));
            var transcripts = new LlmTranscriptStore(dataDirectory);
            var llmPolicy = new LlmRefinementPolicy(model, Options(), dataDirectory, transcripts, LiveEnvironment());

            using var cancellation = new CancellationTokenSource();
            var cancelling = new CancellingPromotionPolicy(cancellation);

            Assert.Throws<OperationCanceledException>(
                () => TrainingLoop.Run(
                    new TrainingOptions(
                        Seed: Seed,
                        Iterations: 1,
                        TrainingHands: 12,
                        EvaluationSeeds: 2,
                        EvaluationHandsPerMatch: 2,
                        DataDirectory: dataDirectory,
                        HarnessRefinement: true),
                    policy: cancelling,
                    cancellationToken: cancellation.Token,
                    refinementPolicy: llmPolicy));

            Assert.Equal(1, cancelling.DecideCount);
            Assert.True(cancellation.IsCancellationRequested, "The policy did not cancel inside Decide.");
            Assert.Equal(1, model.CallCount);

            var pointerAfter = File.ReadAllBytes(pointerPath);
            Assert.Equal(pointerBefore, pointerAfter);
            using (var pointer = JsonDocument.Parse(pointerAfter))
            {
                Assert.Equal(championId, pointer.RootElement.GetProperty("strategyId").GetString());
                Assert.Equal(0, pointer.RootElement.GetProperty("iteration").GetInt32());
                Assert.False(pointer.RootElement.TryGetProperty("evaluationReportHash", out _));
            }

            var evaluationFiles = Directory
                .EnumerateFiles(Path.Combine(dataDirectory, "evaluations"), "*.json")
                .ToArray();
            Assert.Empty(evaluationFiles);
            Assert.DoesNotContain(
                evaluationFiles,
                path => Path.GetFileName(path).EndsWith("-vs-" + championId + ".json", StringComparison.Ordinal));

            // The ledger must name no row for the cancelled iteration: absent, or present with zero entries.
            var ledgerPath = Path.Combine(dataDirectory, TrainingLoop.LedgerFileName);
            Assert.Equal(0, LedgerEntryCount(ledgerPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static int LedgerEntryCount(string ledgerPath)
    {
        if (!File.Exists(ledgerPath))
        {
            return 0;
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(ledgerPath));
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.GetArrayLength()
            : -1;
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

    /// <summary>Wraps the real gate, cancels a token source inside <see cref="Decide"/>, then returns its decision.</summary>
    private sealed class CancellingPromotionPolicy : IPromotionPolicy
    {
        private readonly CancellationTokenSource _cancellation;

        private readonly StatisticalPromotionPolicy _inner = new();

        public CancellingPromotionPolicy(CancellationTokenSource cancellation) => _cancellation = cancellation;

        public int DecideCount { get; private set; }

        public PromotionDecision Decide(EvaluationReport report, EvaluationCriteria criteria)
        {
            DecideCount++;
            var decision = _inner.Decide(report, criteria);
            _cancellation.Cancel();
            return decision;
        }
    }
}
