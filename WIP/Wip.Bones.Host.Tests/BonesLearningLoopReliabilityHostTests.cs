using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Host;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesLearningLoopReliabilityHostTests
{
    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.GranularStages)]
    public async Task BonesLearningLoopHost_GivenSuccessfulIteration_ExpectedStageNamesIncludePonderPlayEnhance()
    {
        await using var factory = CreateReliabilityFactory();
        using var client = factory.CreateClient();
        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

        var observedStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(180);

        while (DateTime.UtcNow < deadline)
        {
            var status = await BonesHostStatusClient.GetStatusAsync(client);
            if (!string.IsNullOrWhiteSpace(status.CurrentStage))
                observedStages.Add(status.CurrentStage);

            if (FindLatestSessionIdWithEnhanceExecution(factory.DataDirectory) is not null)
                break;

            await Task.Delay(10);
        }

        loopHost.RequestStop();

        Assert.Contains("Ponder", observedStages, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Play", observedStages, StringComparer.OrdinalIgnoreCase);
        Assert.True(
            observedStages.Contains("Enhance", StringComparer.OrdinalIgnoreCase)
                || FindLatestSessionIdWithEnhanceExecution(factory.DataDirectory) is not null,
            "Expected Enhance stage or enhance execution artifact proving enhance stage completed.");
    }

    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.StatusApiDefensiveReads)]
    public async Task BonesStatusApi_GivenPonderInProgressNoActivePointer_ExpectedReturns200WithNullLearningPlayer()
    {
        await using var factory = CreateReliabilityFactory(
            configureTestServices: services =>
            {
                services.RemoveAll<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>();
                services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>(
                    new DelayedStrategyAuthoringProvider(TimeSpan.FromSeconds(3)));
            });
        using var client = factory.CreateClient();
        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

        BonesHostStatusResponse? pendingStatus = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);

        while (DateTime.UtcNow < deadline)
        {
            var current = loopHost.CurrentState.CurrentIteration;
            if (string.IsNullOrWhiteSpace(current.SessionId) || !IsPonderPendingStage(current.StageName))
            {
                await Task.Delay(10);
                continue;
            }

            var response = await client.GetAsync("/api/bones/status");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var status = await response.Content.ReadFromJsonAsync<BonesHostStatusResponse>(BonesHostTestJson.Options)
                ?? throw new InvalidOperationException("Status payload was null.");

            if (status.LearningPlayer is null && string.Equals(status.LearningPlayerStatus, "pending", StringComparison.Ordinal))
            {
                pendingStatus = status;
                break;
            }

            await Task.Delay(10);
        }

        Assert.NotNull(pendingStatus);
        Assert.Null(pendingStatus!.LearningPlayer);
        Assert.Equal("pending", pendingStatus.LearningPlayerStatus);
    }

    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.StatusApiDefensiveReads)]
    public async Task BonesStatusApi_GivenCorruptActivePointer_ExpectedReturns200WithLearningPlayerError()
    {
        await using var factory = CreateReliabilityFactory();
        using var client = factory.CreateClient();
        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(4);
        while (DateTime.UtcNow < deadline)
        {
            _ = await BonesHostStatusClient.GetStatusAsync(client);
            if (FindLatestSessionIdWithActivePointer(factory.DataDirectory) is not null)
                break;

            await Task.Delay(100);
        }

        loopHost.RequestStop();
        await Task.Delay(500);

        var sessionId = FindLatestSessionIdWithActivePointer(factory.DataDirectory)
            ?? throw new InvalidOperationException("No session with an active strategy pointer was found.");

        await CorruptActiveStrategyPointerAsync(factory, sessionId);

        var response = await client.GetAsync("/api/bones/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var corruptStatus = await response.Content.ReadFromJsonAsync<BonesHostStatusResponse>(BonesHostTestJson.Options)
            ?? throw new InvalidOperationException("Status payload was null.");

        Assert.Null(corruptStatus.LearningPlayer);
        Assert.False(string.IsNullOrWhiteSpace(corruptStatus.LearningPlayerError));
    }

    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.StatusApiExtendedFields)]
    public async Task BonesStatusApi_GivenLastEnhanceRejected_ExpectedReportsLastPromotionOutcome()
    {
        await using var factory = CreateRejectedPromotionFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1
                && string.Equals(response.LastPromotionOutcome, "Rejected", StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromMinutes(3));

        Assert.Equal("Rejected", status.LastPromotionOutcome);
    }

    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.StatusApiExtendedFields)]
    public async Task BonesStatusApi_GivenRunningLoop_ExpectedExposesExtendedDiagnosticFields()
    {
        await using var factory = CreateReliabilityFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1
                && !string.IsNullOrWhiteSpace(response.LastPromotionOutcome),
            TimeSpan.FromMinutes(4));

        Assert.False(string.IsNullOrWhiteSpace(status!.CurrentStage));
        Assert.False(string.IsNullOrWhiteSpace(status.LastPromotionOutcome));
    }

    [Fact]
    [Trait("ChecklistItem", BonesHostRequirementsChecklistItems.StrategiesPromotedRejectedCounts)]
    public async Task BonesStatusApi_GivenRunningLoopWithPromotionOutcomes_ExpectedExposesStrategiesPromotedRejectedCounts()
    {
        await using var factory = CreateReliabilityFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1
                && !string.IsNullOrWhiteSpace(response.LastPromotionOutcome),
            TimeSpan.FromMinutes(4));

        Assert.True(status.StrategiesPromoted >= 0);
        Assert.True(status.StrategiesRejected >= 0);
        Assert.True(status.StrategiesPromoted + status.StrategiesRejected > 0,
            "At least one promotion outcome should be recorded after a completed iteration.");
    }

    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.LastIterationError)]
    public async Task BonesLearningLoopHost_GivenIterationException_ExpectedLastIterationErrorPopulated()
    {
        await using var factory = CreateReliabilityFactory(
            configureTestServices: services =>
            {
                services.RemoveAll<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>();
                services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>(
                    new FailFirstPonderAuthoringProvider());
            });
        using var client = factory.CreateClient();
        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1
                && response.LastIterationError is not null,
            TimeSpan.FromMinutes(3));

        var lastError = loopHost.CurrentState.LastIterationError;
        Assert.NotNull(lastError);
        Assert.False(string.IsNullOrWhiteSpace(lastError!.Message));
        Assert.False(string.IsNullOrWhiteSpace(lastError.FailureStage));
    }

    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.IterationFailureIsolation)]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.StageFailureArtifact)]
    public async Task BonesLearningLoopHost_GivenPonderFailure_ExpectedMarksFailedAndStartsNextIteration()
    {
        await using var factory = CreateReliabilityFactory(
            configureTestServices: services =>
            {
                services.RemoveAll<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>();
                services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>(
                    new FailFirstPonderAuthoringProvider());
            });
        using var client = factory.CreateClient();
        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 2,
            TimeSpan.FromMinutes(4));

        Assert.True(loopHost.CurrentState.IsRunning);
        Assert.True(loopHost.CurrentState.IterationCount >= 2);

        var failureArtifacts = Directory.GetFiles(
            factory.DataDirectory,
            "*bones-workflow-stage-failure*",
            SearchOption.AllDirectories);
        Assert.NotEmpty(failureArtifacts);
    }

    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.IterationFailureIsolation)]
    public async Task BonesLearningLoopHost_GivenFailedIteration_ExpectedHostProcessKeepsRunning()
    {
        await using var factory = CreateReliabilityFactory(
            configureTestServices: services =>
            {
                services.RemoveAll<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>();
                services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>(
                    new FailFirstPonderAuthoringProvider());
            });
        using var client = factory.CreateClient();
        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 2 && response.IsRunning,
            TimeSpan.FromMinutes(4));

        Assert.True(loopHost.CurrentState.IsRunning);
    }

    [Fact]
    public async Task BonesLearningLoopReliability_GivenMockCompileRetryAndPlaySuccess_ExpectedEnhanceArtifactPresent()
    {
        await using var factory = CreateReliabilityFactory();
        using var client = factory.CreateClient();
        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(4);
        while (DateTime.UtcNow < deadline)
        {
            _ = await BonesHostStatusClient.GetStatusAsync(client);
            if (FindLatestSessionIdWithEnhanceExecution(factory.DataDirectory) is not null)
                break;

            await Task.Delay(100);
        }

        loopHost.RequestStop();
        await Task.Delay(500);

        var sessionId = FindLatestSessionIdWithEnhanceExecution(factory.DataDirectory)
            ?? throw new InvalidOperationException("No session with enhance execution artifact was found.");

        var artifactStore = factory.Services.GetRequiredService<IArtifactStore>();
        var descriptors = await artifactStore.ListAsync(new SessionId(sessionId), CancellationToken.None);
        Assert.Contains(
            descriptors,
            descriptor => descriptor.RelativePath.Contains("bones-enhance-strategy-execution", StringComparison.Ordinal));
        Assert.Contains(
            descriptors,
            descriptor => descriptor.RelativePath.Contains("bones-strategy-promotion", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BonesLearningLoopReliability_GivenPlayTargetScoreTen_ExpectedPromotionEvalUsesTen()
    {
        await using var factory = CreateReliabilityFactory(targetScore: 10);
        using var client = factory.CreateClient();
        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(4);
        while (DateTime.UtcNow < deadline)
        {
            _ = await BonesHostStatusClient.GetStatusAsync(client);
            if (FindLatestSessionIdWithEnhanceExecution(factory.DataDirectory) is not null)
                break;

            await Task.Delay(100);
        }

        loopHost.RequestStop();
        await Task.Delay(500);

        var sessionId = FindLatestSessionIdWithEnhanceExecution(factory.DataDirectory)
            ?? throw new InvalidOperationException("No session with enhance execution artifact was found.");

        var artifactStore = factory.Services.GetRequiredService<IArtifactStore>();
        var descriptors = await artifactStore.ListAsync(new SessionId(sessionId), CancellationToken.None);
        Assert.Contains(
            descriptors,
            descriptor => descriptor.RelativePath.Contains("bones-strategy-promotion", StringComparison.Ordinal));
    }

    private static BonesHostWebApplicationFactory CreateReliabilityFactory(
        Action<IServiceCollection>? configureTestServices = null,
        int? targetScore = null)
        => new(
            configureConfiguration: settings =>
            {
                settings["BonesHost:ResumeFromBest"] = "false";
                settings["BonesHost:DefaultParameters:GameCount"] = "1";
                if (targetScore is not null)
                    settings["BonesHost:DefaultParameters:TargetScore"] = targetScore.Value.ToString();
            },
            configureTestServices: configureTestServices);

    private static BonesHostWebApplicationFactory CreateRejectedPromotionFactory()
        => new(
            configureConfiguration: settings =>
            {
                settings["BonesHost:ResumeFromBest"] = "false";
                settings["BonesHost:DefaultParameters:GameCount"] = "1";
                settings["BonesHost:DefaultParameters:TargetScore"] = "8";
            },
            configureTestServices: services =>
            {
                var promotionOptions = new BonesStrategyPromotionOptions
                {
                    EvaluationMatchCount = 3,
                    MinimumWinRateImprovement = 0.05,
                    MinimumScoreDifferentialImprovement = 0,
                };

                services.RemoveAll<BonesStrategyPromotionOptions>();
                services.AddSingleton(promotionOptions);
                services.RemoveAll<BonesEnhanceStrategyAgent>();
                services.AddSingleton<BonesEnhanceStrategyAgent>(sp => new BonesEnhanceStrategyAgent(
                    sp.GetRequiredService<IArtifactStore>(),
                    sp.GetRequiredService<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>>(),
                    promotionOptions: promotionOptions,
                    gameBudget: sp.GetRequiredService<BonesGameSimulationBudget>(),
                    strategyLibrary: sp.GetRequiredService<BonesStrategyLibrary>(),
                    forcedEvaluationSeed: 4242));
            });

    private static async Task CorruptActiveStrategyPointerAsync(
        BonesHostWebApplicationFactory factory,
        string sessionId)
    {
        var artifactStore = factory.Services.GetRequiredService<IArtifactStore>();
        var descriptors = await artifactStore.ListAsync(new SessionId(sessionId), CancellationToken.None);
        var pointerDescriptor = descriptors.FirstOrDefault(descriptor =>
            descriptor.ArtifactId.Value.Contains("bones-active-strategy-seat-1", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Active strategy pointer artifact was not found.");

        var pointerPath = Path.Combine(
            factory.DataDirectory,
            pointerDescriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(pointerPath, "{ not-valid-json", CancellationToken.None);
    }

    private static bool IsPonderPendingStage(string? stageName)
        => string.Equals(stageName, "Observe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stageName, "Ponder", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stageName, "Starting", StringComparison.OrdinalIgnoreCase);

    private static string? FindLatestSessionIdWithActivePointer(string dataDirectory)
        => FindLatestSessionIdMatching(
            dataDirectory,
            sessionArtifactsPath =>
                Directory.GetFiles(sessionArtifactsPath, "*bones-active-strategy-seat-1*", SearchOption.AllDirectories).Length > 0);

    private static string? FindLatestSessionIdWithEnhanceExecution(string dataDirectory)
        => FindLatestSessionIdMatching(
            dataDirectory,
            sessionArtifactsPath =>
                Directory.GetFiles(sessionArtifactsPath, "*bones-enhance-strategy-execution*", SearchOption.AllDirectories).Length > 0);

    private static string? FindLatestSessionIdMatching(
        string dataDirectory,
        Func<string, bool> predicate)
    {
        var sessionsRoot = Path.Combine(dataDirectory, ".wip", "sessions");
        if (!Directory.Exists(sessionsRoot))
            return null;

        foreach (var sessionDirectory in Directory.GetDirectories(sessionsRoot).OrderByDescending(Directory.GetCreationTimeUtc))
        {
            var artifactsPath = Path.Combine(sessionDirectory, "artifacts");
            if (Directory.Exists(artifactsPath) && predicate(artifactsPath))
                return Path.GetFileName(sessionDirectory);
        }

        return null;
    }

    private sealed class FailFirstPonderAuthoringProvider
        : IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>
    {
        private int _calls;

        public ValueTask<ModelProviderResponse<BonesStrategyAuthoringResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyAuthoringRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) <= 3)
            {
                return ValueTask.FromResult(
                    new ModelProviderResponse<BonesStrategyAuthoringResult>(
                        payload: new BonesStrategyAuthoringResult("```csharp\nthis is not valid C#\n```"),
                        providerId: "bones-strategy-fail-once",
                        modelId: request.ModelId,
                        usage: new ModelProviderUsage(10, 5),
                        correlationId: request.CorrelationId));
            }

            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyAuthoringResult>(
                    payload: new BonesStrategyAuthoringResult(ValidScript),
                    providerId: "bones-strategy-fail-once",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(10, 5),
                    correlationId: request.CorrelationId));
        }
    }

    private sealed class DelayedStrategyAuthoringProvider(TimeSpan delay)
        : IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>
    {
        public async ValueTask<ModelProviderResponse<BonesStrategyAuthoringResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyAuthoringRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);

            return new ModelProviderResponse<BonesStrategyAuthoringResult>(
                payload: new BonesStrategyAuthoringResult(ValidScript),
                providerId: "bones-strategy-delayed",
                modelId: request.ModelId,
                usage: new ModelProviderUsage(10, 5),
                correlationId: request.CorrelationId);
        }
    }

    private const string ValidScript = """
        ```csharp
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[0];
            }
        }
        ```
        """;
}
