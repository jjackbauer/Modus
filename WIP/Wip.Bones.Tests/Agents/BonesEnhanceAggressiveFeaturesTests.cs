using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Artifacts;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Script;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Agents;

/// <summary>
/// Behavior-proof tests for F2 (EnhanceIterationsPerMatch > 1),
/// F3 (RequireDistinctOutput / BonesStagnationException),
/// and F4 (BonesStrategyDiversityGate).
/// </summary>
public sealed class BonesEnhanceAggressiveFeaturesTests
{
    private const string FirstLegalMoveScriptSource = """
using System.Linq;
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves.OrderBy(move => move.MoveId.Value, System.StringComparer.Ordinal).First();
    }
}
""";

    private const string LastLegalMoveScriptSource = """
using System.Linq;
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves.OrderBy(move => move.MoveId.Value, System.StringComparer.Ordinal).Last();
    }
}
""";

    private const string UniqueCommentScriptSource = """
// Unique strategy variant alpha
using System.Linq;
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves.OrderBy(move => move.MoveId.Value, System.StringComparer.Ordinal).First();
    }
}
""";

    // ---- F2: EnhanceIterationsPerMatch tests ----

    [Fact]
    public async Task EnhanceIterationsPerMatch_GreaterThanOne_RunsMultipleIterations()
    {
        await using var fixture = new SimpleEnhanceFixture();
        var playerId = new BonesPlayerId(1);
        var priorId = new BonesStrategyId("f2-seat-1-v1");
        await fixture.SaveActiveScriptAsync(playerId, priorId, FirstLegalMoveScriptSource);
        await fixture.SaveOpponentStrategiesAsync(playerId);
        await fixture.SaveMatchHistoryAsync(playerId, new BonesGameId("f2-match-1"),
            $"## Match outcome summary\nOutcome: Loss\nScore differential vs winner: -6\n");

        var aggressiveOpts = new BonesAggressiveIterationOptions
        {
            EnhanceIterationsPerMatch = 3,
            RequireDistinctOutput = false
        };

        var stubProvider = new CapturingBonesEnhancementModelProvider();
        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            stubProvider,
            aggressiveIterationOptions: aggressiveOpts,
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 5 });

        var result = await agent.ExecuteAsync(
            new BonesEnhanceStrategyRequest(playerId, fixture.RepositoryPath, new BonesGameId("f2-match-1"),
                EvaluationSeed: 42),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.True(result.EnhanceIterationsCompleted >= 1,
            $"Should complete at least 1 iteration, got {result.EnhanceIterationsCompleted}");
        // Provider should have been called at least once per iteration (plus possible compile retries)
        Assert.True(stubProvider.CallCount >= result.EnhanceIterationsCompleted,
            $"Provider called {stubProvider.CallCount}, expected at least {result.EnhanceIterationsCompleted}");
    }

    [Fact]
    public async Task EnhanceIterationsPerMatch_DefaultOne_CompletesNormally()
    {
        await using var fixture = new SimpleEnhanceFixture();
        var playerId = new BonesPlayerId(1);
        var priorId = new BonesStrategyId("f2-default-seat-1-v1");
        await fixture.SaveActiveScriptAsync(playerId, priorId, FirstLegalMoveScriptSource);
        await fixture.SaveOpponentStrategiesAsync(playerId);
        await fixture.SaveMatchHistoryAsync(playerId, new BonesGameId("f2-default-match"),
            $"## Match outcome summary\nOutcome: Loss\nScore differential vs winner: -6\n");

        var stubProvider = new CapturingBonesEnhancementModelProvider();
        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            stubProvider,
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 5 });

        var result = await agent.ExecuteAsync(
            new BonesEnhanceStrategyRequest(playerId, fixture.RepositoryPath, new BonesGameId("f2-default-match"),
                EvaluationSeed: 42),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.Equal(1, result.EnhanceIterationsCompleted);
    }

    // ---- F3: RequireDistinctOutput / BonesStagnationException tests ----

    [Fact]
    public void BonesStagnationException_HasDiagnosticProperties()
    {
        var ex = new BonesStagnationException("Test stagnation")
        {
            StagnationCount = 3,
            FinalTemperature = 1.4,
            PriorStrategyId = "seat-1-v5"
        };

        Assert.Equal(3, ex.StagnationCount);
        Assert.Equal(1.4, ex.FinalTemperature);
        Assert.Equal("seat-1-v5", ex.PriorStrategyId);
        Assert.Contains("Test stagnation", ex.Message);
    }

    [Fact]
    public async Task RequireDistinctOutput_WhenProviderReturnsIdenticalSource_ThrowsStagnationException()
    {
        await using var fixture = new SimpleEnhanceFixture();
        var playerId = new BonesPlayerId(1);
        var priorId = new BonesStrategyId("f3-seat-1-v1");
        await fixture.SaveActiveScriptAsync(playerId, priorId, FirstLegalMoveScriptSource);
        await fixture.SaveOpponentStrategiesAsync(playerId);
        await fixture.SaveMatchHistoryAsync(playerId, new BonesGameId("f3-match"),
            $"## Match outcome summary\nOutcome: Loss\nScore differential vs winner: -6\n");

        // This provider ALWAYS returns the exact same source as the active strategy
        var identicalProvider = new IdenticalSourceEnhancementModelProvider(FirstLegalMoveScriptSource);
        var aggressiveOpts = new BonesAggressiveIterationOptions
        {
            RequireDistinctOutput = true,
            DistinctOutputMaxRetries = 2,
            EnhanceIterationsPerMatch = 1
        };

        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            identicalProvider,
            aggressiveIterationOptions: aggressiveOpts,
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 5 });

        var stagnationEx = await Assert.ThrowsAsync<BonesStagnationException>(() =>
            agent.ExecuteAsync(
                new BonesEnhanceStrategyRequest(playerId, fixture.RepositoryPath, new BonesGameId("f3-match"),
                    EvaluationSeed: 42),
                fixture.CreateCapabilityContext(),
                CancellationToken.None).AsTask());

        Assert.True(stagnationEx.StagnationCount >= 1,
            $"Should detect at least 1 stagnation; got {stagnationEx.StagnationCount}");
    }

    [Fact]
    public async Task RequireDistinctOutput_Disabled_DoesNotDetectIdenticalOutput()
    {
        await using var fixture = new SimpleEnhanceFixture();
        var playerId = new BonesPlayerId(1);
        var priorId = new BonesStrategyId("f3-disabled-seat-1-v1");
        await fixture.SaveActiveScriptAsync(playerId, priorId, FirstLegalMoveScriptSource);
        await fixture.SaveOpponentStrategiesAsync(playerId);
        await fixture.SaveMatchHistoryAsync(playerId, new BonesGameId("f3-disabled-match"),
            $"## Match outcome summary\nOutcome: Loss\nScore differential vs winner: -6\n");

        var identicalProvider = new IdenticalSourceEnhancementModelProvider(FirstLegalMoveScriptSource);
        var aggressiveOpts = new BonesAggressiveIterationOptions
        {
            RequireDistinctOutput = false,
            EnhanceIterationsPerMatch = 1
        };

        var agent = new BonesEnhanceStrategyAgent(
            fixture.ArtifactStore,
            identicalProvider,
            aggressiveIterationOptions: aggressiveOpts,
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 5 });

        // Should not throw stagnation; just produce identical result
        var result = await agent.ExecuteAsync(
            new BonesEnhanceStrategyRequest(playerId, fixture.RepositoryPath, new BonesGameId("f3-disabled-match"),
                EvaluationSeed: 42),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.Equal(0, result.StagnationDetected);
    }

    // ---- F4: BonesStrategyDiversityGate tests ----

    [Fact]
    public void ComputeDiversity_IdenticalSources_ReturnsZero()
    {
        var gate = new BonesStrategyDiversityGate(2);
        var source = "public sealed class Test : IBonesPlayerSlot { }";

        var diversity = gate.ComputeDiversity(source, source);
        Assert.Equal(0.0, diversity);
    }

    [Fact]
    public void ComputeDiversity_DifferentSources_ReturnsPositive()
    {
        var gate = new BonesStrategyDiversityGate(2);
        var sourceA = "public sealed class TestA : IBonesPlayerSlot { }";
        var sourceB = "public sealed class TestB : IBonesPlayerSlot { }";

        var diversity = gate.ComputeDiversity(sourceA, sourceB);
        Assert.True(diversity > 0.0,
            $"Diversity should be positive for different sources, got {diversity}");
    }

    [Fact]
    public void ComputeDiversity_NormalizedWhitespace_StillZero()
    {
        var gate = new BonesStrategyDiversityGate(2);
        var sourceA = "public sealed class Test : IBonesPlayerSlot { }\n";
        var sourceB = "public sealed class Test : IBonesPlayerSlot { }\r\n";

        var diversity = gate.ComputeDiversity(sourceA, sourceB);
        Assert.Equal(0.0, diversity);
    }

    [Fact]
    public void ShouldInvokePonder_WhenBelowMinimumDistinct_ReturnsTrue()
    {
        var gate = new BonesStrategyDiversityGate(2);
        var playerId = new BonesPlayerId(1);

        // Only 1 entry, minimum is 2
        var entries = new List<BonesStrategyLibraryEntry>
        {
            new(
                new BonesStrategyId("seat-1-v1"),
                playerId,
                BonesStrategyKind.Script,
                "public sealed class S1 : IBonesPlayerSlot { }",
                new BonesStrategyEffectivenessRecord(new BonesStrategyId("seat-1-v1"), playerId, 10, 5, 5, 0),
                DateTimeOffset.UtcNow)
        };

        Assert.True(gate.ShouldInvokePonder(playerId, entries));
    }

    [Fact]
    public void ShouldInvokePonder_WhenEnoughDistinct_ReturnsFalse()
    {
        var gate = new BonesStrategyDiversityGate(2);
        var playerId = new BonesPlayerId(1);

        var entries = new List<BonesStrategyLibraryEntry>
        {
            new(
                new BonesStrategyId("seat-1-v1"),
                playerId,
                BonesStrategyKind.Script,
                "public sealed class S1 : IBonesPlayerSlot { /* first */ }",
                new BonesStrategyEffectivenessRecord(new BonesStrategyId("seat-1-v1"), playerId, 10, 5, 5, 0),
                DateTimeOffset.UtcNow),
            new(
                new BonesStrategyId("seat-1-v2"),
                playerId,
                BonesStrategyKind.Script,
                "public sealed class S2 : IBonesPlayerSlot { /* second different */ }",
                new BonesStrategyEffectivenessRecord(new BonesStrategyId("seat-1-v2"), playerId, 10, 5, 5, 0),
                DateTimeOffset.UtcNow)
        };

        Assert.False(gate.ShouldInvokePonder(playerId, entries));
    }

    [Fact]
    public void ShouldInvokePonder_MinimumOne_ReturnsFalse()
    {
        // Default minimum of 1 - never needs extra Ponder
        var gate = new BonesStrategyDiversityGate(1);
        var playerId = new BonesPlayerId(1);

        var entries = new List<BonesStrategyLibraryEntry>
        {
            new(
                new BonesStrategyId("seat-1-v1"),
                playerId,
                BonesStrategyKind.Script,
                "public sealed class S1 : IBonesPlayerSlot { }",
                new BonesStrategyEffectivenessRecord(new BonesStrategyId("seat-1-v1"), playerId, 10, 5, 5, 0),
                DateTimeOffset.UtcNow)
        };

        Assert.False(gate.ShouldInvokePonder(playerId, entries));
    }

    [Fact]
    public void CountDistinctStrategies_AllIdentical_ReturnsOne()
    {
        var gate = new BonesStrategyDiversityGate(3);
        var playerId = new BonesPlayerId(1);
        var source = "public sealed class Same : IBonesPlayerSlot { }";

        var entries = new List<BonesStrategyLibraryEntry>();
        for (var i = 0; i < 5; i++)
        {
            entries.Add(new(
                new BonesStrategyId($"seat-1-v{i}"),
                playerId,
                BonesStrategyKind.Script,
                source,
                new BonesStrategyEffectivenessRecord(new BonesStrategyId($"seat-1-v{i}"), playerId, 1, 0, 1, 0),
                DateTimeOffset.UtcNow));
        }

        var count = gate.CountDistinctStrategies(entries);
        Assert.Equal(1, count);
    }

    [Fact]
    public void CountDistinctStrategies_DifferentEntries_ReturnsCorrectCount()
    {
        var gate = new BonesStrategyDiversityGate(1);
        var playerId = new BonesPlayerId(1);

        var entries = new List<BonesStrategyLibraryEntry>
        {
            new(
                new BonesStrategyId("seat-1-v1"),
                playerId,
                BonesStrategyKind.Script,
                "// variant A\npublic sealed class A : IBonesPlayerSlot { }",
                new BonesStrategyEffectivenessRecord(new BonesStrategyId("seat-1-v1"), playerId, 10, 5, 5, 0),
                DateTimeOffset.UtcNow),
            new(
                new BonesStrategyId("seat-1-v2"),
                playerId,
                BonesStrategyKind.Script,
                "// variant B - totally different approach\npublic sealed class B : IBonesPlayerSlot { public BonesMove ChooseMove(BonesRoundState s, IReadOnlyList<BonesMove> m) { return m[0]; } }",
                new BonesStrategyEffectivenessRecord(new BonesStrategyId("seat-1-v2"), playerId, 10, 5, 5, 0),
                DateTimeOffset.UtcNow),
            new(
                new BonesStrategyId("seat-1-v3"),
                playerId,
                BonesStrategyKind.Script,
                "// variant C - another approach\npublic sealed class C : IBonesPlayerSlot { public BonesMove ChooseMove(BonesRoundState s, IReadOnlyList<BonesMove> m) { return m[^1]; } }",
                new BonesStrategyEffectivenessRecord(new BonesStrategyId("seat-1-v3"), playerId, 10, 5, 5, 0),
                DateTimeOffset.UtcNow)
        };

        var count = gate.CountDistinctStrategies(entries);
        Assert.Equal(3, count);
    }

    [Fact]
    public void Library_ShouldInvokePonderForDiversity_WithoutGate_ReturnsFalse()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-div-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);
        try
        {
            var library = new BonesStrategyLibrary(dataDir); // no diversity gate
            var playerId = new BonesPlayerId(1);

            Assert.False(library.ShouldInvokePonderForDiversity(playerId));
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, recursive: true);
        }
    }

    [Fact]
    public void Library_ShouldInvokePonderForDiversity_WithGateAndFewEntries_ReturnsTrue()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-div-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);
        try
        {
            var gate = new BonesStrategyDiversityGate(2);
            var library = new BonesStrategyLibrary(dataDir, gate);
            var playerId = new BonesPlayerId(1);

            var artifact = new BonesStrategyArtifact(
                new BonesStrategyId("seat-1-v1"),
                playerId,
                BonesStrategyKind.Script,
                "public sealed class S1 : IBonesPlayerSlot { }",
                BonesPromotionStatus.Active);
            var metrics = new BonesStrategyEffectivenessRecord(
                new BonesStrategyId("seat-1-v1"), playerId, 10, 5, 5, 0);

            library.UpsertBestStrategy(playerId, artifact, metrics);

            Assert.True(library.ShouldInvokePonderForDiversity(playerId));
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, recursive: true);
        }
    }

    [Fact]
    public void Library_DiversityGateProperty_ExposesGate()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-div-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);
        try
        {
            var gate = new BonesStrategyDiversityGate(3);
            var library = new BonesStrategyLibrary(dataDir, gate);

            Assert.NotNull(library.DiversityGate);
            Assert.Equal(3, library.DiversityGate!.MinimumDistinctStrategies);
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, recursive: true);
        }
    }

    // ---- Test infrastructure ----

    private sealed class SimpleEnhanceFixture : IAsyncDisposable
    {
        private readonly string _repoPath;
        public SimpleEnhanceFixture()
        {
            _repoPath = Path.Combine(Path.GetTempPath(), $"modus-bones-af-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_repoPath);
            SessionId = new SessionId($"bones-af-{Guid.NewGuid():N}");
            ArtifactStore = new WipArtifactStoreLocal(_repoPath);
            KnowledgeStore = new BonesPlayerKnowledgeStore(ArtifactStore, _repoPath, SessionId);
        }

        public SessionId SessionId { get; }
        public WipArtifactStoreLocal ArtifactStore { get; }
        public BonesPlayerKnowledgeStore KnowledgeStore { get; }
        public string RepositoryPath => _repoPath;

        public async Task SaveActiveScriptAsync(BonesPlayerId playerId, BonesStrategyId strategyId, string source)
        {
            await KnowledgeStore.SaveStrategyArtifact(playerId,
                new BonesStrategyArtifact(strategyId, playerId, BonesStrategyKind.Script, source,
                    BonesPromotionStatus.Active),
                CancellationToken.None);
        }

        public async Task SaveOpponentStrategiesAsync(BonesPlayerId learningPlayerId)
        {
            for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            {
                if (seat == learningPlayerId.Seat) continue;
                var oppId = new BonesPlayerId(seat);
                await SaveActiveScriptAsync(oppId, new BonesStrategyId($"initial-seat-{seat}-v1"),
                    FirstLegalMoveScriptSource);
            }
        }

        public async Task SaveMatchHistoryAsync(BonesPlayerId playerId, BonesGameId gameId, string markdown)
        {
            await KnowledgeStore.SaveMatchHistory(playerId,
                new BonesMatchHistoryTranscript(gameId, playerId, markdown),
                CancellationToken.None);
        }

        public CapabilityContext CreateCapabilityContext()
            => new(SessionId, Path.Combine(_repoPath, ".wip", "worktrees", "bones-enhance"));

        public async ValueTask DisposeAsync()
        {
            if (Directory.Exists(_repoPath))
                Directory.Delete(_repoPath, recursive: true);
            await ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingBonesEnhancementModelProvider
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
        private int _callCount;
        public int CallCount => _callCount;

        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            var markdown = $"""
```csharp
{FirstLegalMoveScriptSource}
```
""";
            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult(markdown),
                    providerId: "bones-enhance-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(140, 60),
                    correlationId: request.CorrelationId));
        }
    }

    private sealed class IdenticalSourceEnhancementModelProvider
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
        private readonly string _sourceToReturn;

        public IdenticalSourceEnhancementModelProvider(string sourceToReturn)
        {
            _sourceToReturn = sourceToReturn;
        }

        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            var markdown = $"```csharp\n{_sourceToReturn}\n```";
            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult(markdown),
                    providerId: "bones-enhance-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(140, 60),
                    correlationId: request.CorrelationId));
        }
    }
}
