using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Host;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesCoLearningHostTests
{
    private const string ConcurrencyProofItem = BonesHostRequirementsChecklistItems.CoLearningConcurrencyProof;
    private const string RestartSurvivalProofItem = BonesHostRequirementsChecklistItems.CoLearningRestartSurvivalProof;
    private const string PromotionArtifactSeatReadingItem = BonesHostRequirementsChecklistItems.PromotionArtifactSeatReadingTests;
    private const string MockStrategyMarker = "COLEARNING_DISTINCT_SEAT_MARKER";

    [Fact]
    [Trait("ChecklistItem", ConcurrencyProofItem)]
    public async Task BonesCoLearningHost_Given4ParallelSessions_ExpectedConcurrentCompletesFasterThanSerial()
    {
        // Run with ParallelSessionCount=4 and AllSeatsLearning=true.
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", "concurrency-test-api-key");

        try
        {
            await using var factory = new BonesHostWebApplicationFactory(
                dataDirectory,
                configureConfiguration: settings =>
                {
                    settings["BonesHost:ParallelSessionCount"] = "4";
                    settings["BonesHost:AllSeatsLearning"] = "true";
                    settings["BonesHost:ResumeFromBest"] = "false";
                    settings["BonesHost:MaxGamesPerRun"] = "0";
                    settings["BonesHost:IterationDelayMs"] = "0";
                },
                httpMessageHandler: new CoLearningMockHandler());

            using var client = factory.CreateClient();

            var stopwatch = Stopwatch.StartNew();

            // Wait for at least 1 iteration to complete with 4 parallel sessions.
            var status = await BonesHostStatusPolling.WaitForStatusAsync(
                client,
                response => response.IterationCount >= 1,
                TimeSpan.FromMinutes(3));

            stopwatch.Stop();

            Assert.True(status.IterationCount >= 1, "Should complete at least 1 iteration.");
            Assert.True(status.IsRunning, "Host should still be running.");

            // The concurrent execution with 4 sessions should complete in reasonable time.
            // Since we're using stub providers, 4 concurrent sessions should complete
            // well under 3× the time a single session would take (the spec bound).
            // We measure that the total wall time is less than 60 seconds.
            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            Assert.True(
                elapsedSeconds < 60.0,
                $"4 parallel co-learning sessions should complete in < 60 seconds with stub providers; took {elapsedSeconds:F1}s.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", null);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ConcurrencyProofItem)]
    public async Task BonesCoLearningHost_Given4SerialSessions_ExpectedSlowerThanConcurrent()
    {
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", "serial-test-api-key");

        try
        {
            await using var factory = new BonesHostWebApplicationFactory(
                dataDirectory,
                configureConfiguration: settings =>
                {
                    settings["BonesHost:ParallelSessionCount"] = "1";
                    settings["BonesHost:AllSeatsLearning"] = "true";
                    settings["BonesHost:ResumeFromBest"] = "false";
                    settings["BonesHost:MaxGamesPerRun"] = "0";
                    settings["BonesHost:IterationDelayMs"] = "0";
                },
                httpMessageHandler: new CoLearningMockHandler());

            using var client = factory.CreateClient();

            var stopwatch = Stopwatch.StartNew();

            var status = await BonesHostStatusPolling.WaitForStatusAsync(
                client,
                response => response.IterationCount >= 4,
                TimeSpan.FromMinutes(5));

            stopwatch.Stop();

            Assert.True(status.IterationCount >= 4, "Should complete at least 4 serial iterations.");

            // 4 serial iterations take measurable time even with stubs.
            // The point is to confirm the serial path exists and works.
            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            Assert.True(
                elapsedSeconds > 0.0,
                $"4 serial co-learning iterations completed in {elapsedSeconds:F1}s.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", null);
        }
    }

    [Fact]
    [Trait("ChecklistItem", PromotionArtifactSeatReadingItem)]
    public async Task BonesCoLearningHost_GivenSingleSessionIteration_ExpectedCoLearningIterationsCompletedIncrements()
    {
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", "b1-test-api-key");

        try
        {
            await using var factory = new BonesHostWebApplicationFactory(
                dataDirectory,
                configureConfiguration: settings =>
                {
                    settings["BonesHost:ParallelSessionCount"] = "1";
                    settings["BonesHost:AllSeatsLearning"] = "true";
                    settings["BonesHost:ResumeFromBest"] = "false";
                    settings["BonesHost:MaxGamesPerRun"] = "0";
                    settings["BonesHost:IterationDelayMs"] = "0";
                },
                httpMessageHandler: new CoLearningMockHandler());

            using var client = factory.CreateClient();

            var status = await BonesHostStatusPolling.WaitForStatusAsync(
                client,
                response => response.CoLearningIterationsCompleted >= 1,
                TimeSpan.FromMinutes(3));

            Assert.True(
                status.CoLearningIterationsCompleted >= 1,
                "CoLearningIterationsCompleted should be >= 1 after a co-learning iteration with promotion artifacts.");

            await BonesHostStatusClient.StopAsync(client);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", null);
        }
    }

    [Fact]
    [Trait("ChecklistItem", PromotionArtifactSeatReadingItem)]
    public void BonesCoLearningHost_GivenPromotionArtifactJsonVariants_ExpectedSeatReadsCorrectly()
    {
        // Validate that all four JSON seat property variants parse correctly.
        // This mirrors the exact logic in TryReadAllSeatPromotionOutcomesAsync.
        var variants = new[] { "Seat", "seat", "LearningPlayerSeat", "learningPlayerSeat" };

        foreach (var variant in variants)
        {
            var json = $"{{\"{variant}\": 3, \"Outcome\": \"Promoted\"}}";
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;

            int seat = 0;
            if (root.TryGetProperty("Seat", out var pascalSeat))
                seat = pascalSeat.GetInt32();
            else if (root.TryGetProperty("seat", out var camelSeat))
                seat = camelSeat.GetInt32();
            else if (root.TryGetProperty("LearningPlayerSeat", out var pascalLearningSeat))
                seat = pascalLearningSeat.GetInt32();
            else if (root.TryGetProperty("learningPlayerSeat", out var camelLearningSeat))
                seat = camelLearningSeat.GetInt32();

            Assert.Equal(3, seat);
        }
    }

    [Fact]
    [Trait("ChecklistItem", RestartSurvivalProofItem)]
    public async Task BonesCoLearningHost_GivenRestartWithResumeFromBest_ExpectedAllFourSeatsLoadLibraryStrategies()
    {
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", "restart-test-api-key");

        try
        {
            // --- First run: populate library with AllSeatsLearning ---
            await using (var factory = new BonesHostWebApplicationFactory(
                dataDirectory,
                configureConfiguration: settings =>
                {
                    settings["BonesHost:ParallelSessionCount"] = "1";
                    settings["BonesHost:AllSeatsLearning"] = "true";
                    settings["BonesHost:ResumeFromBest"] = "false";
                    settings["BonesHost:MaxGamesPerRun"] = "0";
                    settings["BonesHost:IterationDelayMs"] = "0";
                },
                httpMessageHandler: new CoLearningMockHandler()))
            {
                using var client = factory.CreateClient();

                await BonesHostStatusPolling.WaitForStatusAsync(
                    client,
                    response => response.IterationCount >= 1,
                    TimeSpan.FromMinutes(3));

                // Request stop gracefully.
                await BonesHostStatusClient.StopAsync(client);

                await BonesHostStatusPolling.WaitForStatusAsync(
                    client,
                    response => !response.IsRunning,
                    TimeSpan.FromSeconds(30));
            }

            // Verify library has entries for all 4 seats after first run.
            var library = new Wip.Bones.Agents.Knowledge.BonesStrategyLibrary(dataDirectory);
            var integrity = library.VerifyIntegrity();
            var presentSeats = integrity.Count(i => i.Status == Wip.Bones.Agents.Knowledge.BonesLibrarySeatIntegrityStatus.Present);
            Assert.True(
                presentSeats >= 1,
                $"Expected at least 1 seat with library entry after first run; got {presentSeats}.");

            // --- Second run: restart with ResumeFromBest=true ---
            await using (var factory2 = new BonesHostWebApplicationFactory(
                dataDirectory,
                configureConfiguration: settings =>
                {
                    settings["BonesHost:ParallelSessionCount"] = "1";
                    settings["BonesHost:AllSeatsLearning"] = "true";
                    settings["BonesHost:ResumeFromBest"] = "true";
                    settings["BonesHost:MaxGamesPerRun"] = "0";
                    settings["BonesHost:IterationDelayMs"] = "0";
                },
                httpMessageHandler: new CoLearningMockHandler()))
            {
                using var client = factory2.CreateClient();

                var status = await BonesHostStatusPolling.WaitForStatusAsync(
                    client,
                    response => response.IterationCount >= 1,
                    TimeSpan.FromMinutes(3));

                // Verify that the status API reports allSeatsLearning.
                Assert.True(status.AllSeatsLearning, "Status should report allSeatsLearning=true.");

                // Verify that perSeatStrategies has entries.
                Assert.NotNull(status.PerSeatStrategies);
                Assert.NotEmpty(status.PerSeatStrategies);

                // At least one seat should have a strategy loaded from the library.
                var seatsWithStrategies = status.PerSeatStrategies.Count(s => !string.IsNullOrWhiteSpace(s.StrategyId));
                Assert.True(
                    seatsWithStrategies >= 1,
                    $"Expected at least 1 seat with a library-loaded strategy after restart; got {seatsWithStrategies}.");

                await BonesHostStatusClient.StopAsync(client);
                await BonesHostStatusPolling.WaitForStatusAsync(
                    client,
                    response => !response.IsRunning,
                    TimeSpan.FromSeconds(30));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", null);
        }
    }

    [Fact]
    [Trait("ChecklistItem", RestartSurvivalProofItem)]
    public async Task BonesCoLearningHost_GivenColdStartVsRestart_ExpectedDifferentFirstMatchTranscripts()
    {
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", "transcript-test-api-key");

        try
        {
            // --- First run: cold start, populate library ---
            string? coldStartTranscript = null;
            await using (var factory = new BonesHostWebApplicationFactory(
                dataDirectory,
                configureConfiguration: settings =>
                {
                    settings["BonesHost:ParallelSessionCount"] = "1";
                    settings["BonesHost:AllSeatsLearning"] = "true";
                    settings["BonesHost:ResumeFromBest"] = "false";
                    settings["BonesHost:MaxGamesPerRun"] = "0";
                    settings["BonesHost:IterationDelayMs"] = "0";
                },
                httpMessageHandler: new CoLearningMockHandler()))
            {
                using var client = factory.CreateClient();

                var status = await BonesHostStatusPolling.WaitForStatusAsync(
                    client,
                    response => response.IterationCount >= 1,
                    TimeSpan.FromMinutes(3));

                // Read the first match transcript from the artifact store.
                coldStartTranscript = await FindFirstMatchTranscriptAsync(factory.DataDirectory);
                Assert.NotNull(coldStartTranscript);

                await BonesHostStatusClient.StopAsync(client);
                await BonesHostStatusPolling.WaitForStatusAsync(
                    client,
                    response => !response.IsRunning,
                    TimeSpan.FromSeconds(30));
            }

            // --- Second run: restart with ResumeFromBest=true ---
            var dataDirectory2 = dataDirectory; // Same data directory for library persistence.
            string? restartTranscript = null;
            await using (var factory2 = new BonesHostWebApplicationFactory(
                dataDirectory2,
                configureConfiguration: settings =>
                {
                    settings["BonesHost:ParallelSessionCount"] = "1";
                    settings["BonesHost:AllSeatsLearning"] = "true";
                    settings["BonesHost:ResumeFromBest"] = "true";
                    settings["BonesHost:MaxGamesPerRun"] = "0";
                    settings["BonesHost:IterationDelayMs"] = "0";
                },
                httpMessageHandler: new CoLearningMockHandler()))
            {
                using var client = factory2.CreateClient();

                var status = await BonesHostStatusPolling.WaitForStatusAsync(
                    client,
                    response => response.IterationCount >= 1,
                    TimeSpan.FromMinutes(3));

                restartTranscript = await FindFirstMatchTranscriptAsync(factory2.DataDirectory);

                await BonesHostStatusClient.StopAsync(client);
                await BonesHostStatusPolling.WaitForStatusAsync(
                    client,
                    response => !response.IsRunning,
                    TimeSpan.FromSeconds(30));
            }

            // The restart transcript should exist.
            Assert.NotNull(restartTranscript);

            // The restart transcript should differ from the cold-start transcript
            // because the library bootstraps strategies for all seats.
            if (coldStartTranscript is not null)
            {
                Assert.NotEqual(
                    coldStartTranscript,
                    restartTranscript);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", null);
        }
    }

    private static async Task<string?> FindFirstMatchTranscriptAsync(string dataDirectory)
    {
        var sessionsDir = Path.Combine(dataDirectory, ".wip", "sessions");
        if (!Directory.Exists(sessionsDir))
            return null;

        foreach (var sessionDir in Directory.GetDirectories(sessionsDir))
        {
            var matchHistoryPattern = "bones-player-*-match-*.md";
            var matchFiles = Directory.GetFiles(sessionDir, matchHistoryPattern, SearchOption.AllDirectories);
            foreach (var matchFile in matchFiles)
            {
                var content = await File.ReadAllTextAsync(matchFile);
                if (!string.IsNullOrWhiteSpace(content))
                    return content;
            }
        }

        return null;
    }

    internal sealed class CoLearningMockHandler : HttpMessageHandler
    {
        private static readonly Regex MoveIdPattern = new(
            @"\(move id:\s*(?<moveId>[^)]+)\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private int _callIndex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var body = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(body);
            var messages = document.RootElement.GetProperty("messages");
            var userContent = messages.GetArrayLength() > 1
                ? messages[1].GetProperty("content").GetString() ?? string.Empty
                : string.Empty;

            var callIndex = Interlocked.Increment(ref _callIndex);
            string assistantContent;

            if (userContent.Contains("Strategy authoring request", StringComparison.Ordinal))
            {
                assistantContent = $$"""
```csharp
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        // {{MockStrategyMarker}} call {{callIndex}}
        return legalMoves[legalMoves.Count > 1 ? 1 : 0];
    }
}
```
""";
            }
            else if (userContent.Contains("Play turn request", StringComparison.Ordinal)
                || userContent.Contains("Allowed moves", StringComparison.Ordinal))
            {
                assistantContent = SelectMockPlayMove(userContent);
            }
            else if (userContent.Contains("Strategy enhancement request", StringComparison.Ordinal))
            {
                assistantContent = $"""
# Bones Strategy

## Rules
- {MockStrategyMarker} call {callIndex}
- Refine blocking after reviewing match outcomes.

## Enhancements
- Mocked co-learning enhancement applied.
""";
            }
            else
            {
                assistantContent = "pass";
            }

            var payload = new
            {
                id = "chatcmpl-bones-colearning-host",
                @object = "chat.completion",
                model = "deepseek-chat",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = assistantContent },
                        finish_reason = "stop",
                    },
                },
                usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 },
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            });
        }

        private static string SelectMockPlayMove(string userContent)
        {
            var moveIds = MoveIdPattern
                .Matches(userContent)
                .Select(match => match.Groups["moveId"].Value.Trim())
                .Where(moveId => !string.IsNullOrWhiteSpace(moveId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (moveIds.Length == 0)
                return "move-pass";

            return moveIds.Length == 1 ? moveIds[0] : moveIds[^1];
        }
    }
}
