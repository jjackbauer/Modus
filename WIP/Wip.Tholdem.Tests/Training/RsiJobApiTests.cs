using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Training;

/// <summary>
/// The mini-RSI HTTP surface: bounded defaults, a completed job that leaves the strategy champion
/// untouched, a refusal to promote an ineligible run, and an explicit pointer only on promote.
/// </summary>
public sealed class RsiJobApiTests
{
    private static object SmallJob(ulong seed) => new
    {
        seed,
        metaIterations = 1,
        innerIterations = 1,
        trainingHands = 4,
        evaluationSeeds = 2,
        evaluationHands = 1,
        marginBbPer100 = 0.0,
        minSeeds = 2,
        metaSeeds = 2,
        metaHands = 1,
        freshSeeds = 2,
        freshHands = 1,
        requiredHitRateImprovement = 0.1,
        maxInnerIterations = 20,
        maxEvaluationsPerEpisode = 10,
        maxWallClockSeconds = 60,
        allowWeakSeed = false,
    };

    [Fact]
    public async Task Post_GivenSmallMetaRun_ExpectedCompletionAndNoStrategyChampionChange()
    {
        await ViewerTestSupport.WithServerAsync("rsi-api-complete", async server =>
        {
            var accepted = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                "/api/rsi",
                SmallJob(1_099_511_627_960UL),
                HttpStatusCode.Accepted);
            var jobId = accepted.GetProperty("jobId").GetString()!;
            Assert.StartsWith("rsi-", jobId, StringComparison.Ordinal);

            var completed = await TrainingTestSupport.WaitForJobAsync(
                server.Client,
                jobId,
                static job => job.GetProperty("status").GetString() is "complete" or "failed" or "cancelled",
                timeoutMilliseconds: 120000,
                prefix: "/api/rsi");
            Assert.Equal("complete", completed.GetProperty("status").GetString());
            Assert.Equal(1, completed.GetProperty("totalIterations").GetInt32());
            var entries = completed.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Single(entries);
            Assert.False(string.IsNullOrWhiteSpace(entries[0].GetProperty("outcome").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(completed.GetProperty("frozenManifestHash").GetString()));
            Assert.True(MetaStore.VerifyLedger(server.DataDirectory).Verified);

            // Mini-RSI must never touch the strategy champion pointer.
            Assert.False(File.Exists(Path.Combine(server.DataDirectory, "active-champion.json")));
            Assert.True(File.Exists(Path.Combine(server.DataDirectory, "meta", MetaStore.ActiveProposerFileName)));

            var refused = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                $"/api/rsi/{jobId}/promote",
                new { confirm = true },
                HttpStatusCode.Conflict);
            Assert.Equal("no-eligible-proposer", refused.GetProperty("error").GetString());
        });
    }

    [Fact]
    public async Task Get_GivenUnknownRsiJob_Expected404()
    {
        await ViewerTestSupport.WithServerAsync("rsi-api-unknown", async server =>
        {
            using var response = await server.Client.GetAsync("/api/rsi/rsi-does-not-exist");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        });
    }

    [Fact]
    public async Task Post_GivenMetaBoundsViolation_Expected400()
    {
        await ViewerTestSupport.WithServerAsync("rsi-api-bounds", async server =>
        {
            var body = new
            {
                seed = 1_099_511_627_961UL,
                metaIterations = 9,
                innerIterations = 1,
                trainingHands = 4,
                evaluationSeeds = 2,
                evaluationHands = 1,
                marginBbPer100 = 0.0,
                minSeeds = 2,
                metaSeeds = 2,
            };
            using var response = await server.Client.PostAsJsonAsync("/api/rsi", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("metaIterations", document.RootElement.GetProperty("field").GetString());
        });
    }
}
