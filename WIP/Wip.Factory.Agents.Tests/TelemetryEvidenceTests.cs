using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Telemetry;
using Wip.Factory.Telemetry;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class TelemetryEvidenceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wip-factory-telemetry-tests", Guid.NewGuid().ToString("N"));

    private static readonly WorkItemId Item = new("W-1042");
    private static readonly SessionId Session = new("session-1");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static AgentSessionTrace Trace(string summary, string? failure = null) =>
        new(
            Session,
            Item,
            new AgentDefinitionId("factory.review-analyst"),
            new AgentProfileVersion(1),
            new ModelId("deepseek-chat"),
            InputTokens: 120,
            OutputTokens: 40,
            Outcome: "Completed",
            FailureCategory: failure,
            ToolNames: ["tool.repo-read"],
            Summary: summary,
            RecordedAtUtc: DateTimeOffset.UtcNow);

    [Fact]
    public async Task TraceStore_GivenCompletedSession_PersistsSanitizedTraceWithoutSecrets()
    {
        var store = new LocalAgentSessionTraceStore(Path.Combine(_root, "traces"));

        const string secret = "sk-live-abcdef0123456789";
        await store.AppendAsync(Trace($"model replied using api key {secret} and then finished"));

        var traces = await store.QueryAsync(Item);
        var trace = Assert.Single(traces);

        // The secret must not survive in memory or on disk, and the trace must still be useful.
        Assert.DoesNotContain(secret, trace.Summary, StringComparison.Ordinal);
        Assert.Contains("REDACTED", trace.Summary, StringComparison.Ordinal);
        Assert.Equal(120, trace.InputTokens);
        Assert.Equal(40, trace.OutputTokens);
        Assert.Equal("deepseek-chat", trace.ModelId.Value);

        var persisted = await File.ReadAllTextAsync(Path.Combine(_root, "traces", "session-traces.jsonl"));
        Assert.DoesNotContain(secret, persisted, StringComparison.Ordinal);

        // A reloaded store reads the sanitized record, not the original text.
        var reopened = new LocalAgentSessionTraceStore(Path.Combine(_root, "traces"));
        var reloaded = Assert.Single(await reopened.QueryAsync(Item));
        Assert.DoesNotContain(secret, reloaded.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Attribution_GivenReleasedWork_UsesObservationWithoutMutatingEarlierEvidence()
    {
        var traces = new LocalAgentSessionTraceStore(Path.Combine(_root, "traces"));
        await traces.AppendAsync(Trace("first attempt", failure: "MaxStepsReached"));
        await traces.AppendAsync(Trace("second attempt", failure: "MaxStepsReached"));
        await traces.AppendAsync(Trace("third attempt"));

        var clusters = await traces.FailureClustersAsync();
        var cluster = Assert.Single(clusters);
        Assert.Equal("MaxStepsReached", cluster.Category);
        Assert.Equal(2, cluster.Occurrences);

        var attribution = new LocalOutcomeAttributionStore(Path.Combine(_root, "outcomes"));

        var first = new DownstreamOutcome(
            new OutcomeId("outcome-1"), Item, Session, "change-failure-rate", 0.02, "medium: single release", DateTimeOffset.UtcNow);
        await attribution.LinkAsync(first);

        // Re-linking the same observation id must not rewrite the earlier evidence.
        await attribution.LinkAsync(first with { Value = 0.97, AttributionUncertainty = "rewritten" });

        var links = await attribution.LinksForAsync(Item);
        var link = Assert.Single(links);
        Assert.Equal(0.02, link.Value);
        Assert.Equal("medium: single release", link.AttributionUncertainty);

        // A second, distinct observation is appended without disturbing the first.
        await attribution.LinkAsync(new DownstreamOutcome(
            new OutcomeId("outcome-2"), Item, Session, "mttr-minutes", 12.0, "high: direct telemetry", DateTimeOffset.UtcNow));

        Assert.Equal(2, (await attribution.LinksForAsync(Item)).Count);

        // An observation without stated uncertainty is refused, so certainty is never implied.
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await attribution.LinkAsync(new DownstreamOutcome(
                new OutcomeId("outcome-3"), Item, Session, "x", 1.0, "   ", DateTimeOffset.UtcNow)));
    }
}
