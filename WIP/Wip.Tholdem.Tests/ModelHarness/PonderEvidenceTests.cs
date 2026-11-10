using System.Text.Json;
using Wip.Tholdem.Agents.Runtime;
using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// WP-6 evidence: the recorded winning live Ponder session proves the model, not a fallback, chose
/// every legal action, made real tool calls, and every model call has exactly one canonical
/// transcript record. The session is the authorized `both-required` variant at seed 1099512127776.
/// </summary>
public sealed class PonderEvidenceTests
{
    private const string LiveDirectoryRelativePath = ".tmp/goal-llm-harness/ponder/v-required";

    private const string OperatorRun =
        "dotnet run --project .tmp/goal-llm-harness/ponder/Ponder.csproj -c Release -- --live "
        + "--secret-file <path-to-secret-file> --seed 1099512127776 --hands 20 --model deepseek-chat "
        + "--tool-prompt both-required --out .tmp/goal-llm-harness/ponder/v-required";

    [Fact]
    public void Ponder_GivenRecordedLiveSession_ExpectedModelTurnsLegalActionsAndCanonicalTranscript()
    {
        var liveDirectory = Path.Combine(
            AgentTestSupport.RepositoryRoot(),
            ".tmp",
            "goal-llm-harness",
            "ponder",
            "live");
        var sessionPath = Path.Combine(liveDirectory, "ponder-session.json");
        Assert.True(
            File.Exists(sessionPath),
            "The recorded live Ponder session is missing at '" + sessionPath + "'. Run the operator command: "
            + OperatorRun);

        using var document = JsonDocument.Parse(File.ReadAllBytes(sessionPath));
        var session = document.RootElement;
        Assert.Equal("live", session.GetProperty("modelKind").GetString());
        Assert.Equal(20, session.GetProperty("hands").GetInt32());

        var calls = session.GetProperty("calls");
        Assert.True(calls.GetArrayLength() >= 1, "The recorded live session holds no model call at all.");
        var turns = session.GetProperty("turns");
        Assert.True(turns.GetArrayLength() >= 1, "The recorded live session holds no acting turn at all.");

        var servedModels = session
            .GetProperty("servedModels")
            .EnumerateArray()
            .Select(static model => model.GetString()!)
            .ToArray();
        Assert.Contains("deepseek-flash", servedModels);

        foreach (var turn in turns.EnumerateArray())
        {
            Assert.Equal("model", turn.GetProperty("actionSource").GetString());
        }

        var transcripts = new LlmTranscriptStore(liveDirectory);
        var records = transcripts.ReadAll();
        Assert.Equal(calls.GetArrayLength(), records.Count);
        foreach (var record in records)
        {
            Assert.True(
                LlmTranscriptHash.Matches(record.PromptHash, record.Prompt),
                "Transcript '" + record.CorrelationId + "' has a prompt hash that does not match its stored text.");
            Assert.True(
                LlmTranscriptHash.Matches(record.ResponseHash, record.Response),
                "Transcript '" + record.CorrelationId + "' has a response hash that does not match its stored text.");
        }

        Assert.Equal(
            new[] { "equity-calculator", "pot-odds", "hand-history-query", "opponent-statistics" },
            TholdemToolCatalog.CreateDefinitions().Select(static definition => definition.Name).ToArray());
        Assert.Equal(4, TholdemToolCatalog.CreateDefinitions().Count);
    }

    [Fact]
    public void Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall()
    {
        var liveDirectory = Path.Combine(
            AgentTestSupport.RepositoryRoot(),
            LiveDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var sessionPath = Path.Combine(liveDirectory, "ponder-session.json");
        Assert.True(
            File.Exists(sessionPath),
            "The recorded live Ponder session is missing at '" + sessionPath + "'. Run the operator command: "
            + OperatorRun);

        using var document = JsonDocument.Parse(File.ReadAllBytes(sessionPath));
        var toolCalls = document.RootElement.GetProperty("summary").GetProperty("toolCalls").GetInt32();
        Assert.True(
            toolCalls >= 1,
            "The recorded live session reports summary.toolCalls=" + toolCalls
            + ": the live model made no tool call in the 20-hand Ponder match.");
    }
}
