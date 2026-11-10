using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Training;

/// <summary>
/// The promote contract: a UI confirmation is never authorization; the server re-reads the persisted
/// report, recomputes it from persisted artifacts and seeds, re-runs the frozen verifier, and refuses
/// a tampered report or a mismatched job.
/// </summary>
public sealed class TrainingPromoteApiTests
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static async Task<(string JobId, JsonElement Snapshot)> RunPromotingJobAsync(ViewerTestServer server)
    {
        var strategyId = TrainingTestSupport.WriteStrategyArtifact(
            server.DataDirectory,
            new StrategyGenome(0.7, 0.7, 0.12, 1.0));
        var body = TrainingTestSupport.JobBody(
            seed: 1_099_511_627_790UL,
            iterations: 1,
            trainingHands: 120,
            evaluationSeeds: 50,
            evaluationHands: 10,
            margin: 0.0,
            minSeeds: 2,
            strategyId: strategyId);
        var accepted = await ViewerTestSupport.PostJsonAsync(server.Client, "/api/training", body, HttpStatusCode.Accepted);
        var jobId = accepted.GetProperty("jobId").GetString()!;
        var snapshot = await TrainingTestSupport.WaitForTerminalAsync(server.Client, jobId);
        Assert.True(
            snapshot.GetProperty("canPromote").GetBoolean(),
            "The known promoting fixture must produce a promotable verdict. Snapshot: " + snapshot);
        return (jobId, snapshot);
    }

    private static string ReportPath(ViewerTestServer server, JsonElement snapshot)
    {
        var entry = snapshot.GetProperty("entries").EnumerateArray().Last();
        var challengerId = entry.GetProperty("challengerId").GetString()!;
        var championId = entry.GetProperty("championId").GetString()!;
        return Path.Combine(server.DataDirectory, "evaluations", challengerId + "-vs-" + championId + ".json");
    }

    [Fact]
    public async Task Promote_GivenNoConfirm_Expected409ConfirmationRequired()
    {
        await ViewerTestSupport.WithServerAsync("training-promote-no-confirm", async server =>
        {
            var (jobId, _) = await RunPromotingJobAsync(server);
            var refused = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                $"/api/training/{jobId}/promote",
                new { },
                HttpStatusCode.Conflict);
            Assert.Equal("confirmation-required", refused.GetProperty("error").GetString());
        });
    }

    [Fact]
    public async Task Promote_GivenVerifiedJob_ExpectedServerWritesChampionAndLedgerStaysVerified()
    {
        await ViewerTestSupport.WithServerAsync("training-promote-success", async server =>
        {
            var (jobId, snapshot) = await RunPromotingJobAsync(server);
            var challengerId = snapshot.GetProperty("entries").EnumerateArray().Last()
                .GetProperty("challengerId").GetString()!;

            // Remove the loop's own pointer so a successful endpoint call must write it.
            var pointerPath = Path.Combine(server.DataDirectory, "active-champion.json");
            File.Delete(pointerPath);

            var promoted = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                $"/api/training/{jobId}/promote",
                new { confirm = true },
                HttpStatusCode.OK);
            Assert.Equal("promoted", promoted.GetProperty("status").GetString());
            Assert.Equal(challengerId, promoted.GetProperty("championId").GetString());
            Assert.True(promoted.GetProperty("verified").GetBoolean());

            using var pointer = JsonDocument.Parse(File.ReadAllBytes(pointerPath));
            Assert.Equal(challengerId, pointer.RootElement.GetProperty("strategyId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(pointer.RootElement.GetProperty("evaluationReportHash").GetString()));
            Assert.True(TrainingLedger.Verify(server.DataDirectory).Verified);

            var again = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                $"/api/training/{jobId}/promote",
                new { confirm = true },
                HttpStatusCode.OK);
            Assert.Equal("already-promoted", again.GetProperty("status").GetString());
        });
    }

    [Fact]
    public async Task Promote_GivenTamperedReport_Expected409ServerReverification()
    {
        await ViewerTestSupport.WithServerAsync("training-promote-tamper", async server =>
        {
            var (jobId, snapshot) = await RunPromotingJobAsync(server);
            var reportPath = ReportPath(server, snapshot);
            var text = File.ReadAllText(reportPath);
            Assert.True(EvaluationReportJson.TryParse(text, out var parsed, out _));
            var forged = parsed! with
            {
                MeanBbPer100 = 101.0,
                BbPer100Ci = new Wip.Tholdem.Agents.Simulation.ConfidenceInterval(99.0, 103.0),
                LowerBoundBbPer100 = 99.0,
            };
            File.WriteAllText(reportPath, forged.ToCanonicalJson(), Utf8);

            var refused = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                $"/api/training/{jobId}/promote",
                new { confirm = true },
                HttpStatusCode.Conflict);
            Assert.Equal("report-hash-mismatch", refused.GetProperty("error").GetString());
        });
    }

    [Fact]
    public async Task Promote_GivenReportForAnotherChallenger_Expected409JobMismatch()
    {
        await ViewerTestSupport.WithServerAsync("training-promote-mismatch", async server =>
        {
            var (jobId, snapshot) = await RunPromotingJobAsync(server);
            var reportPath = ReportPath(server, snapshot);
            Assert.True(EvaluationReportJson.TryParse(File.ReadAllText(reportPath), out var parsed, out _));
            var forged = parsed! with { ChallengerId = "tholdem-strategy-v9-deadbeef" };
            File.WriteAllText(reportPath, forged.ToCanonicalJson(), Utf8);

            var refused = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                $"/api/training/{jobId}/promote",
                new { confirm = true },
                HttpStatusCode.Conflict);
            Assert.Equal("report-job-mismatch", refused.GetProperty("error").GetString());
        });
    }

    [Fact]
    public async Task Promote_GivenUnverifiedJob_Expected409NotPromotable()
    {
        await ViewerTestSupport.WithServerAsync("training-promote-unverified", async server =>
        {
            var accepted = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                "/api/training",
                TrainingTestSupport.JobBody(1_099_511_627_812UL, 1, 8, 2, 1, 0, 2),
                HttpStatusCode.Accepted);
            var jobId = accepted.GetProperty("jobId").GetString()!;
            var snapshot = await TrainingTestSupport.WaitForTerminalAsync(server.Client, jobId);
            Assert.False(snapshot.GetProperty("canPromote").GetBoolean());

            var refused = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                $"/api/training/{jobId}/promote",
                new { confirm = true },
                HttpStatusCode.Conflict);
            Assert.Equal("report-not-promotable", refused.GetProperty("error").GetString());
        });
    }
}
