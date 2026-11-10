using System.Globalization;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Skills;
using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// Offline replay of the recorded live LLM arm: every persisted transcript is parsed again with no
/// model, and each replayed genome is compared with the challenger artifact the live run persisted.
/// The comparison mirrors the scratch host's CandidateMatchesProposal (numerics plus skills).
/// </summary>
public sealed class LiveReplayTests
{
    private const string OperatorRun =
        "dotnet run --project .tmp/goal-llm-harness/arms/Arms.csproj -c Release -- --arm llm "
        + "--seed 1099511927776 --iterations 10 --training-hands 2000 --eval-seeds 50 --eval-hands 20 "
        + "--data-dir .tmp/goal-llm-harness/arms/arm-llm "
        + "--report docs/goal-llm-harness/round-3/llm-arm-report.json --secret-file .secrets/deepseek.env";

    [Fact]
    public void Transcript_GivenRecordedLiveArmTranscripts_ExpectedReplayMatchesPersistedChallengers()
    {
        var root = AgentTestSupport.RepositoryRoot();
        var armDirectory = Path.Combine(root, ".tmp", "goal-llm-harness", "arms", "arm-llm");
        var transcriptPath = Path.Combine(armDirectory, LlmTranscriptStore.FileName);
        var reportPath = Path.Combine(root, "docs", "goal-llm-harness", "round-3", "llm-arm-report.json");

        Assert.True(
            File.Exists(transcriptPath),
            "The recorded live arm transcripts are missing at '" + transcriptPath
            + "'. Run the operator command: " + OperatorRun);
        Assert.True(
            File.Exists(reportPath),
            "The recorded live arm report is missing at '" + reportPath
            + "'. Run the operator command: " + OperatorRun);

        var records = new LlmTranscriptStore(armDirectory).ReadAll();
        Assert.Equal(10, records.Count);
        foreach (var record in records)
        {
            Assert.Equal(LlmTranscriptOutcomes.Ok, record.Outcome);
            Assert.Equal("deepseek-flash", record.ServedModel);
            Assert.False(
                string.IsNullOrWhiteSpace(record.Response),
                "Transcript '" + record.CorrelationId + "' has an empty response.");
            Assert.True(
                record.Usage.PromptTokens > 0,
                "Transcript '" + record.CorrelationId + "' reports promptTokens="
                + record.Usage.PromptTokens.ToString(CultureInfo.InvariantCulture) + ".");
            Assert.True(
                record.LatencyMilliseconds >= 0,
                "Transcript '" + record.CorrelationId + "' reports latencyMilliseconds="
                + record.LatencyMilliseconds.ToString(CultureInfo.InvariantCulture) + ".");
        }

        using var reportDocument = JsonDocument.Parse(File.ReadAllBytes(reportPath));
        var iterations = reportDocument.RootElement.GetProperty("iterations");
        Assert.Equal(10, iterations.GetArrayLength());
        var replayedIterations = new HashSet<int>();
        foreach (var iterationElement in iterations.EnumerateArray())
        {
            var iteration = iterationElement.GetProperty("iteration").GetInt32();
            Assert.InRange(iteration, 1, 10);
            Assert.True(
                replayedIterations.Add(iteration),
                "The report repeats iteration " + iteration.ToString(CultureInfo.InvariantCulture) + ".");

            var correlationId = "llm-" + iteration.ToString(CultureInfo.InvariantCulture);
            var record = Assert.Single(
                records,
                transcript => string.Equals(transcript.CorrelationId, correlationId, StringComparison.Ordinal));
            var challengerId = iterationElement.GetProperty("challengerId").GetString()!;
            Assert.False(
                string.IsNullOrWhiteSpace(challengerId),
                "Iteration " + iteration.ToString(CultureInfo.InvariantCulture) + " has no challengerId.");
            var artifactPath = Path.Combine(armDirectory, "strategies", challengerId + ".json");
            Assert.True(
                File.Exists(artifactPath),
                "The persisted challenger artifact is missing at '" + artifactPath + "' for iteration "
                + iteration.ToString(CultureInfo.InvariantCulture)
                + ". Run the operator command: " + OperatorRun);

            using var artifactDocument = JsonDocument.Parse(File.ReadAllBytes(artifactPath));
            var replayed = new LlmTranscriptReplay().Propose(record);
            AssertCandidateMatchesProposal(
                artifactDocument.RootElement,
                replayed.Genome,
                challengerId,
                iteration);
        }

        Assert.Equal(10, replayedIterations.Count);
    }

    private static void AssertCandidateMatchesProposal(
        JsonElement artifact,
        StrategyGenome genome,
        string challengerId,
        int iteration)
    {
        var context = "challenger '" + challengerId + "' at iteration "
            + iteration.ToString(CultureInfo.InvariantCulture);
        var parameters = artifact.GetProperty("parameters");
        Assert.True(
            genome.RaiseThreshold == parameters.GetProperty("raiseThreshold").GetDouble(),
            "raiseThreshold differs from the persisted artifact for " + context + ".");
        Assert.True(
            genome.CallThreshold == parameters.GetProperty("callThreshold").GetDouble(),
            "callThreshold differs from the persisted artifact for " + context + ".");
        Assert.True(
            genome.BluffFrequency == parameters.GetProperty("bluffFrequency").GetDouble(),
            "bluffFrequency differs from the persisted artifact for " + context + ".");
        Assert.True(
            genome.SizingMultiplier == parameters.GetProperty("sizingMultiplier").GetDouble(),
            "sizingMultiplier differs from the persisted artifact for " + context + ".");
        AssertSkillsMatch(artifact, genome.Skills, context);
    }

    private static void AssertSkillsMatch(
        JsonElement artifact,
        StrategySkillSet? proposalSkills,
        string context)
    {
        var hasArtifactSkills = artifact.TryGetProperty("skills", out var skills)
            && skills.ValueKind == JsonValueKind.Object;
        if (proposalSkills is null)
        {
            Assert.False(
                hasArtifactSkills,
                "The replayed proposal has no skills but the persisted artifact for " + context + " does.");
            return;
        }

        Assert.True(
            hasArtifactSkills,
            "The replayed proposal has skills but the persisted artifact for " + context + " has none.");
        var preflop = skills.GetProperty("preflop");
        var postflop = skills.GetProperty("postflop");
        var sizing = skills.GetProperty("sizing");
        var bluff = skills.GetProperty("bluff");
        Assert.True(
            proposalSkills.Preflop.RaiseThreshold == preflop.GetProperty("raiseThreshold").GetDouble(),
            "skills.preflop.raiseThreshold differs from the persisted artifact for " + context + ".");
        Assert.True(
            proposalSkills.Preflop.CallThreshold == preflop.GetProperty("callThreshold").GetDouble(),
            "skills.preflop.callThreshold differs from the persisted artifact for " + context + ".");
        Assert.True(
            proposalSkills.Postflop.RaiseThreshold == postflop.GetProperty("raiseThreshold").GetDouble(),
            "skills.postflop.raiseThreshold differs from the persisted artifact for " + context + ".");
        Assert.True(
            proposalSkills.Postflop.CallThreshold == postflop.GetProperty("callThreshold").GetDouble(),
            "skills.postflop.callThreshold differs from the persisted artifact for " + context + ".");
        Assert.True(
            proposalSkills.Sizing.Multiplier == sizing.GetProperty("multiplier").GetDouble(),
            "skills.sizing.multiplier differs from the persisted artifact for " + context + ".");
        Assert.True(
            proposalSkills.Bluff.Frequency == bluff.GetProperty("frequency").GetDouble(),
            "skills.bluff.frequency differs from the persisted artifact for " + context + ".");
    }
}
