using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Tests;
using Xunit;
using Xunit.Sdk;

namespace Wip.Tholdem.Tests.Training;

internal static class TrainingTestSupport
{
    public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<JsonElement> WaitForJobAsync(
        HttpClient client,
        string jobId,
        Func<JsonElement, bool> predicate,
        int timeoutMilliseconds = 120000,
        string prefix = "/api/training")
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        JsonElement last = default;
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            using var response = await client.GetAsync(prefix + "/" + Uri.EscapeDataString(jobId));
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new XunitException($"GET {prefix}/{jobId} returned {(int)response.StatusCode}.");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            last = document.RootElement.Clone();
            if (predicate(last))
            {
                return last;
            }

            await Task.Delay(25);
        }

        throw new XunitException($"Timed out waiting for {jobId}. Last: {last}");
    }

    public static Task<JsonElement> WaitForTerminalAsync(HttpClient client, string jobId) =>
        WaitForJobAsync(
            client,
            jobId,
            static job => job.GetProperty("status").GetString() is "complete" or "failed" or "cancelled");

    public static string WriteStrategyArtifact(string dataDirectory, StrategyGenome genome)
    {
        var strategies = Path.Combine(dataDirectory, "strategies");
        Directory.CreateDirectory(strategies);
        var artifact = StrategyArtifact.Create(genome, 0, null);
        File.WriteAllText(
            Path.Combine(strategies, artifact.Id + ".json"),
            artifact.ToCanonicalJson(),
            Utf8);
        return artifact.Id;
    }

    public static SortedDictionary<string, string> HashDataDirectory(string dataDirectory)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory
                     .EnumerateFiles(dataDirectory, "*.json", SearchOption.AllDirectories)
                     .Where(static path => !path.Contains("training-seeds", StringComparison.Ordinal))
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(dataDirectory, path).Replace('\\', '/');
            hashes[relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        }

        return hashes;
    }

    public static object JobBody(
        ulong seed,
        int iterations,
        int trainingHands,
        int evaluationSeeds,
        int evaluationHands,
        double margin,
        int minSeeds,
        string? strategyId = null) => new
    {
        seed,
        iterations,
        trainingHands,
        evaluationSeeds,
        evaluationHands,
        marginBbPer100 = margin,
        minSeeds,
        strategyId,
        allowWeakSeed = false,
    };

    public static object SlowJobBody(ulong seed) => JobBody(
        seed,
        iterations: 50,
        trainingHands: 2000,
        evaluationSeeds: 2,
        evaluationHands: 1,
        margin: 5,
        minSeeds: 24);
}

/// <summary>
/// The training API contract: bounds, deterministic identity, one-at-a-time admission, live progress,
/// cancellation without promotion, artifact landing, seed reuse refusal and the server-verified
/// promote. Every assertion names an observable response or a file.
/// </summary>
public sealed class TrainingJobApiTests
{
    [Fact]
    public async Task Post_GivenValidOptions_Expected202DeterministicJobIdAndCompletion()
    {
        await ViewerTestSupport.WithServerAsync("training-api-complete", async server =>
        {
            var body = TrainingTestSupport.JobBody(
                seed: 1_099_511_627_801UL,
                iterations: 2,
                trainingHands: 20,
                evaluationSeeds: 4,
                evaluationHands: 3,
                margin: 0.0,
                minSeeds: 2);
            var accepted = await ViewerTestSupport.PostJsonAsync(server.Client, "/api/training", body, HttpStatusCode.Accepted);
            var jobId = accepted.GetProperty("jobId").GetString()!;
            var optionsHash = accepted.GetProperty("optionsHash").GetString()!;
            Assert.Equal("train-" + optionsHash[..16], jobId);
            Assert.False(string.IsNullOrWhiteSpace(accepted.GetProperty("acceptedAtUtc").GetString()));

            var completed = await TrainingTestSupport.WaitForTerminalAsync(server.Client, jobId);
            Assert.Equal("complete", completed.GetProperty("status").GetString());
            Assert.Equal(2, completed.GetProperty("totalIterations").GetInt32());
            Assert.Equal(2, completed.GetProperty("iteration").GetInt32());
            var entries = completed.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Equal(2, entries.Length);
            Assert.True(completed.GetProperty("lastReport").TryGetProperty("lowerBoundBbPer100", out _));
            Assert.False(string.IsNullOrWhiteSpace(completed.GetProperty("lastReport").GetProperty("decision").GetString()));

            var lastEntry = entries[^1];
            var challengerId = lastEntry.GetProperty("challengerId").GetString()!;
            var championId = lastEntry.GetProperty("championId").GetString()!;
            Assert.True(File.Exists(Path.Combine(server.DataDirectory, "strategies", challengerId + ".json")));
            Assert.True(File.Exists(
                Path.Combine(server.DataDirectory, "evaluations", challengerId + "-vs-" + championId + ".json")));
            Assert.True(File.Exists(Path.Combine(server.DataDirectory, "ledger.json")));
            Assert.True(TrainingLedger.Verify(server.DataDirectory).Verified);
        });
    }

    [Fact]
    public async Task Post_GivenEveryOutOfBoundsField_Expected400WithThatField()
    {
        await ViewerTestSupport.WithServerAsync("training-api-bounds", async server =>
        {
            var cases = new (string Field, object Value)[]
            {
                ("iterations", 0),
                ("iterations", 51),
                ("trainingHands", 0),
                ("trainingHands", 2001),
                ("evaluationSeeds", 1),
                ("evaluationSeeds", 501),
                ("evaluationHands", 0),
                ("evaluationHands", 51),
                ("marginBbPer100", -0.1),
                ("marginBbPer100", 100.1),
                ("minSeeds", 1),
                ("minSeeds", 1001),
            };

            foreach (var (field, value) in cases)
            {
                var body = new Dictionary<string, object?>
                {
                    ["seed"] = 1_099_511_627_802UL,
                    ["iterations"] = 2,
                    ["trainingHands"] = 20,
                    ["evaluationSeeds"] = 4,
                    ["evaluationHands"] = 3,
                    ["marginBbPer100"] = 0.0,
                    ["minSeeds"] = 2,
                    ["allowWeakSeed"] = false,
                };
                body[field] = value;
                using var response = await server.Client.PostAsJsonAsync("/api/training", body);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(field, document.RootElement.GetProperty("field").GetString());
                Assert.Equal("invalid-option", document.RootElement.GetProperty("error").GetString());
            }
        });
    }

    [Fact]
    public async Task Post_GivenWeakSeedWithoutOptInOrOverTheCap_Expected400()
    {
        await ViewerTestSupport.WithServerAsync("training-api-weak-seed", async server =>
        {
            var weak = new
            {
                seed = 42UL,
                iterations = 1,
                trainingHands = 4,
                evaluationSeeds = 2,
                evaluationHands = 1,
                marginBbPer100 = 0,
                minSeeds = 2,
                allowWeakSeed = false,
            };
            var refused = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                "/api/training",
                weak,
                HttpStatusCode.BadRequest);
            Assert.Equal("weak-seed", refused.GetProperty("error").GetString());
            Assert.Equal("seed", refused.GetProperty("field").GetString());

            var overCap = new
            {
                seed = 42UL,
                iterations = 6,
                trainingHands = 4,
                evaluationSeeds = 2,
                evaluationHands = 1,
                marginBbPer100 = 0,
                minSeeds = 2,
                allowWeakSeed = true,
            };
            var capped = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                "/api/training",
                overCap,
                HttpStatusCode.BadRequest);
            Assert.Equal("weak-seed-iteration-cap", capped.GetProperty("error").GetString());
            Assert.Equal("iterations", capped.GetProperty("field").GetString());
        });
    }

    [Fact]
    public async Task Post_GivenUnknownStrategyId_Expected400UnknownStrategy()
    {
        await ViewerTestSupport.WithServerAsync("training-api-strategy-id", async server =>
        {
            var body = TrainingTestSupport.JobBody(
                1_099_511_627_803UL,
                1,
                4,
                2,
                1,
                0,
                2,
                strategyId: "tholdem-strategy-v1-deadbeef");
            var refused = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                "/api/training",
                body,
                HttpStatusCode.BadRequest);
            Assert.Equal("unknown-strategy", refused.GetProperty("error").GetString());
            Assert.Equal("strategyId", refused.GetProperty("field").GetString());
        });
    }

    [Fact]
    public async Task Post_GivenSameOptions_ExpectedIdempotentSameJobId()
    {
        await ViewerTestSupport.WithServerAsync("training-api-idempotent", async server =>
        {
            var body = TrainingTestSupport.SlowJobBody(1_099_511_627_804UL);
            var first = await ViewerTestSupport.PostJsonAsync(server.Client, "/api/training", body, HttpStatusCode.Accepted);
            var jobId = first.GetProperty("jobId").GetString()!;

            var second = await ViewerTestSupport.PostJsonAsync(server.Client, "/api/training", body, HttpStatusCode.Accepted);
            Assert.Equal(jobId, second.GetProperty("jobId").GetString());

            using var cancel = await server.Client.DeleteAsync("/api/training/" + Uri.EscapeDataString(jobId));
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
            await TrainingTestSupport.WaitForJobAsync(
                server.Client,
                jobId,
                static job => job.GetProperty("status").GetString() == "cancelled");
        });
    }

    [Fact]
    public async Task Post_GivenSecondJobWhileOneRuns_Expected409()
    {
        await ViewerTestSupport.WithServerAsync("training-api-one-at-a-time", async server =>
        {
            var body = TrainingTestSupport.SlowJobBody(1_099_511_627_805UL);
            var first = await ViewerTestSupport.PostJsonAsync(server.Client, "/api/training", body, HttpStatusCode.Accepted);
            var jobId = first.GetProperty("jobId").GetString()!;

            var conflicting = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                "/api/training",
                TrainingTestSupport.JobBody(1_099_511_627_806UL, 1, 4, 2, 1, 0, 2),
                HttpStatusCode.Conflict);
            Assert.Equal("job-already-running", conflicting.GetProperty("error").GetString());

            using var cancel = await server.Client.DeleteAsync("/api/training/" + Uri.EscapeDataString(jobId));
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
            await TrainingTestSupport.WaitForJobAsync(
                server.Client,
                jobId,
                static job => job.GetProperty("status").GetString() == "cancelled");
        });
    }

    [Fact]
    public async Task Get_GivenAcceptedJob_ExpectedProgressVisibleWithin500Milliseconds()
    {
        await ViewerTestSupport.WithServerAsync("training-api-progress-latency", async server =>
        {
            var body = TrainingTestSupport.SlowJobBody(1_099_511_627_807UL);
            var accepted = await ViewerTestSupport.PostJsonAsync(server.Client, "/api/training", body, HttpStatusCode.Accepted);
            var jobId = accepted.GetProperty("jobId").GetString()!;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var response = await server.Client.GetAsync("/api/training/" + Uri.EscapeDataString(jobId));
            var elapsed = stopwatch.ElapsedMilliseconds;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(elapsed < 500, $"Progress took {elapsed} ms.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Contains(
                document.RootElement.GetProperty("status").GetString(),
                new[] { "queued", "running" });
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("phase").GetString()));

            using var cancel = await server.Client.DeleteAsync("/api/training/" + Uri.EscapeDataString(jobId));
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
            await TrainingTestSupport.WaitForJobAsync(
                server.Client,
                jobId,
                static job => job.GetProperty("status").GetString() == "cancelled");
        });
    }

    [Fact]
    public async Task Delete_GivenRunningJob_ExpectedCancelledWithoutChampionChange()
    {
        await ViewerTestSupport.WithServerAsync("training-api-cancel", async server =>
        {
            // Establish an active champion first, then prove cancellation cannot move it.
            var quick = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                "/api/training",
                TrainingTestSupport.JobBody(1_099_511_627_808UL, 1, 4, 2, 1, 5, 24),
                HttpStatusCode.Accepted);
            await TrainingTestSupport.WaitForTerminalAsync(server.Client, quick.GetProperty("jobId").GetString()!);
            var pointerPath = Path.Combine(server.DataDirectory, "active-champion.json");
            var before = File.ReadAllText(pointerPath);

            var slow = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                "/api/training",
                TrainingTestSupport.SlowJobBody(1_099_511_627_809UL),
                HttpStatusCode.Accepted);
            var slowId = slow.GetProperty("jobId").GetString()!;
            await TrainingTestSupport.WaitForJobAsync(
                server.Client,
                slowId,
                static job => job.GetProperty("status").GetString() == "running");

            using var cancel = await server.Client.DeleteAsync("/api/training/" + Uri.EscapeDataString(slowId));
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
            var cancelled = await TrainingTestSupport.WaitForJobAsync(
                server.Client,
                slowId,
                static job => job.GetProperty("status").GetString() == "cancelled");
            Assert.Equal("cancelled", cancelled.GetProperty("status").GetString());
            Assert.Equal(before, File.ReadAllText(pointerPath));
        });
    }

    [Fact]
    public async Task Post_GivenSeedReuseWithDifferentOptions_Expected409()
    {
        await ViewerTestSupport.WithServerAsync("training-api-seed-reuse", async server =>
        {
            const ulong seed = 1_099_511_627_810UL;
            var first = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                "/api/training",
                TrainingTestSupport.JobBody(seed, 1, 4, 2, 1, 0, 2),
                HttpStatusCode.Accepted);
            await TrainingTestSupport.WaitForTerminalAsync(server.Client, first.GetProperty("jobId").GetString()!);

            var reused = await ViewerTestSupport.PostJsonAsync(
                server.Client,
                "/api/training",
                TrainingTestSupport.JobBody(seed, 2, 4, 2, 1, 0, 2),
                HttpStatusCode.Conflict);
            Assert.Equal("seed-reuse", reused.GetProperty("error").GetString());
        });
    }

    [Fact]
    public async Task Post_GivenSameOptionsInTwoDirectories_ExpectedIdenticalArtifactHashes()
    {
        object body = TrainingTestSupport.JobBody(
            seed: 1_099_511_627_811UL,
            iterations: 2,
            trainingHands: 20,
            evaluationSeeds: 4,
            evaluationHands: 3,
            margin: 0.0,
            minSeeds: 2);

        SortedDictionary<string, string>? firstHashes = null;
        await ViewerTestSupport.WithServerAsync("training-api-determinism-a", async server =>
        {
            var accepted = await ViewerTestSupport.PostJsonAsync(server.Client, "/api/training", body, HttpStatusCode.Accepted);
            await TrainingTestSupport.WaitForTerminalAsync(server.Client, accepted.GetProperty("jobId").GetString()!);
            firstHashes = TrainingTestSupport.HashDataDirectory(server.DataDirectory);
        });

        SortedDictionary<string, string>? secondHashes = null;
        await ViewerTestSupport.WithServerAsync("training-api-determinism-b", async server =>
        {
            var accepted = await ViewerTestSupport.PostJsonAsync(server.Client, "/api/training", body, HttpStatusCode.Accepted);
            await TrainingTestSupport.WaitForTerminalAsync(server.Client, accepted.GetProperty("jobId").GetString()!);
            secondHashes = TrainingTestSupport.HashDataDirectory(server.DataDirectory);
        });

        Assert.NotNull(firstHashes);
        Assert.NotNull(secondHashes);
        Assert.Equal(firstHashes!.Keys, secondHashes!.Keys);
        foreach (var key in firstHashes.Keys)
        {
            Assert.Equal(firstHashes[key], secondHashes[key]);
        }
    }
}
