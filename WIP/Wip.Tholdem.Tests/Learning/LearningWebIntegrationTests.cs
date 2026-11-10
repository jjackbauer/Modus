using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// The committed web test for the learning artifacts: a trained data directory is served by the real
/// Kestrel host and the frozen strategy/ledger endpoints must expose exactly what the loop wrote.
/// </summary>
public sealed class LearningWebIntegrationTests
{
    [Fact]
    public async Task StrategyAndLedgerEndpoints_GivenTrainedDataDirectory_ExpectedPersistedArtifacts()
    {
        await ViewerTestSupport.WithServerAsync("learning-artifacts", async server =>
        {
            var options = new TrainingOptions(
                Seed: 909090UL,
                Iterations: 2,
                TrainingHands: 12,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 4,
                DataDirectory: server.DataDirectory);
            var run = TrainingLoop.Run(options);

            var strategies = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/strategies");
            Assert.Equal(0, strategies.GetProperty("skipped").GetInt32());
            var entries = strategies.GetProperty("strategies").EnumerateArray().ToArray();
            var expectedIds = run.Iterations
                .Select(static iteration => iteration.ChampionId)
                .Concat(run.Iterations.Select(static iteration => iteration.ChallengerId))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expectedIds, entries.Select(static entry => entry.GetProperty("id").GetString()).ToArray());
            Assert.Equal(expectedIds.Length, entries.Length);

            foreach (var entry in entries)
            {
                var payload = entry.GetProperty("payload");
                Assert.Equal(entry.GetProperty("id").GetString(), payload.GetProperty("id").GetString());
                Assert.True(payload.GetProperty("parameters").TryGetProperty("raiseThreshold", out _));
                Assert.True(payload.GetProperty("parameters").TryGetProperty("sizingMultiplier", out _));
                Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("promptText").GetString()));
            }

            var championDetail = await ViewerTestSupport.GetJsonAsync(
                server.Client,
                "/api/strategies/" + Uri.EscapeDataString(run.FinalChampionId));
            Assert.Equal(run.FinalChampionId, championDetail.GetProperty("id").GetString());
            Assert.Equal(1, championDetail.GetProperty("payload").GetProperty("version").GetInt32());

            var ledger = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/ledger");
            Assert.Equal(0, ledger.GetProperty("skipped").GetInt32());
            var ledgerEntries = ledger.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Equal(run.Iterations.Count, ledgerEntries.Length);
            for (var index = 0; index < ledgerEntries.Length; index++)
            {
                var entry = ledgerEntries[index];
                Assert.Equal(index + 1, entry.GetProperty("iteration").GetInt32());
                Assert.Equal(run.Iterations[index].ChampionId, entry.GetProperty("championId").GetString());
                Assert.Equal(run.Iterations[index].ChallengerId, entry.GetProperty("challengerId").GetString());
                Assert.False(entry.GetProperty("promoted").GetBoolean());
                Assert.Equal(run.Iterations[index].Decision.Reason, entry.GetProperty("reason").GetString());
                Assert.Equal(run.Iterations[index].Report.MeanBbPer100, entry.GetProperty("meanBbPer100").GetDouble(), 9);
                Assert.Equal(run.Iterations[index].Report.LowerBoundBbPer100, entry.GetProperty("lowerBoundBbPer100").GetDouble(), 9);
                Assert.Equal(5.0, entry.GetProperty("marginBbPer100").GetDouble(), 9);
                Assert.Equal(2, entry.GetProperty("seeds").GetInt32());
                Assert.True(entry.GetProperty("verified").GetBoolean());
                Assert.Equal("rejected", entry.GetProperty("outcome").GetString());
            }

            // A fresh run persists the version-1 champion pointer it actually used.
            var freshChampion = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/champion");
            Assert.Equal(run.FinalChampionId, freshChampion.GetProperty("strategyId").GetString());
            Assert.Equal(0, freshChampion.GetProperty("iteration").GetInt32());

            // A promotion moves the champion pointer, and the endpoint passes it through verbatim.
            var promotedOptions = options with
            {
                Seed = 909092UL,
                Iterations = 1,
                Criteria = new EvaluationCriteria(MarginBbPer100: -10_000.0, MinSeeds: 2),
            };
            var promoted = TrainingLoop.Run(promotedOptions);
            Assert.Equal(promoted.Iterations[0].ChallengerId, promoted.FinalChampionId);

            var championAfterPromotion = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/champion");
            Assert.Equal(
                promoted.FinalChampionId,
                championAfterPromotion.GetProperty("strategyId").GetString());
            Assert.Equal(1, championAfterPromotion.GetProperty("iteration").GetInt32());

            // The promoted run appends to the existing ledger instead of overwriting it.
            var ledgerAfterPromotion = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/ledger");
            var promotedEntries = ledgerAfterPromotion.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Equal(run.Iterations.Count + 1, promotedEntries.Length);
            for (var index = 0; index < run.Iterations.Count; index++)
            {
                Assert.Equal(run.Iterations[index].ChampionId, promotedEntries[index].GetProperty("championId").GetString());
                Assert.Equal(run.Iterations[index].ChallengerId, promotedEntries[index].GetProperty("challengerId").GetString());
            }

            var promotedEntry = promotedEntries[^1];
            Assert.True(promotedEntry.GetProperty("promoted").GetBoolean());
            Assert.Equal("promoted", promotedEntry.GetProperty("reason").GetString());
            Assert.Equal(promoted.Iterations[0].ChallengerId, promotedEntry.GetProperty("challengerId").GetString());
        });
    }
}
