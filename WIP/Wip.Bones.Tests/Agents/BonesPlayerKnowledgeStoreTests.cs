using Microsoft.Extensions.Logging;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesPlayerKnowledgeStoreTests
{
    private const string KnowledgeStoreChecklistItem = BonesRequirementsChecklistItems.KnowledgeStore;
    private const string IsolationNegativeGateChecklistItem = BonesRequirementsChecklistItems.IsolationNegativeGate;
    private const string EffectivenessLogWarningChecklistItem = "E2: GetEffectiveness must log a warning when it catches InvalidOperationException and returns an empty record";

    [Fact]
    [Trait("ChecklistItem", KnowledgeStoreChecklistItem)]
    public async Task BonesPlayerKnowledgeStore_GivenSaveStrategy_ExpectedArtifactUnderPlayerScopedPath()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var playerId = new BonesPlayerId(2);
        var strategyId = new BonesStrategyId("opening-lead-v1");
        var document = new BonesStrategyDocument(
            strategyId,
            playerId,
            markdown: "# Strategy\nPrefer highest double on opening lead.");

        var descriptor = await fixture.Store.SaveStrategy(playerId, document, CancellationToken.None);

        Assert.Contains($"bones-player-{playerId.Seat}-strategy-{strategyId.Value}", descriptor.RelativePath, StringComparison.Ordinal);
        Assert.Contains(fixture.SessionId.Value, descriptor.RelativePath, StringComparison.Ordinal);

        var artifactPath = fixture.GetArtifactPath(descriptor);
        var persistedMarkdown = await File.ReadAllTextAsync(artifactPath, CancellationToken.None);
        Assert.Equal(document.Markdown, persistedMarkdown);
    }

    [Fact]
    [Trait("ChecklistItem", KnowledgeStoreChecklistItem)]
    public async Task BonesPlayerKnowledgeStore_GivenFourPlayers_ExpectedLoadStrategyReturnsOnlyOwningPlayerDocument()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var strategies = new Dictionary<BonesPlayerId, BonesStrategyDocument>();
        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            var document = new BonesStrategyDocument(
                new BonesStrategyId($"seat-{seat}-rules"),
                playerId,
                markdown: $"# Seat {seat}\nPlay tile matching seat {seat} heuristics.");

            strategies[playerId] = document;
            await fixture.Store.SaveStrategy(playerId, document, CancellationToken.None);
        }

        foreach (var (playerId, expectedDocument) in strategies)
        {
            var loaded = await fixture.Store.LoadStrategy(playerId, fixture.SessionId, CancellationToken.None);

            Assert.Equal(expectedDocument.StrategyId, loaded.StrategyId);
            Assert.Equal(playerId, loaded.PlayerId);
            Assert.Equal(expectedDocument.Markdown, loaded.Markdown);
        }
    }

    [Fact]
    [Trait("ChecklistItem", IsolationNegativeGateChecklistItem)]
    public async Task BonesPlayerKnowledgeStore_GivenCrossPlayerStrategyRead_ExpectedDeterministicIsolationFailure()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var ownerPlayer = new BonesPlayerId(1);
        var requestingPlayer = new BonesPlayerId(2);
        var strategyId = new BonesStrategyId("seat-1-secret-rules");

        await fixture.Store.SaveStrategy(
            ownerPlayer,
            new BonesStrategyDocument(
                strategyId,
                ownerPlayer,
                markdown: "# Seat 1\nDo not reveal this opening heuristic."),
            CancellationToken.None);

        var scopedStore = fixture.Store.ForPlayer(requestingPlayer);

        var strategyException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scopedStore.LoadStrategy(ownerPlayer, fixture.SessionId, CancellationToken.None));

        Assert.Contains("isolation", strategyException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"seat {requestingPlayer.Seat}", strategyException.Message, StringComparison.Ordinal);
        Assert.Contains($"seat {ownerPlayer.Seat}", strategyException.Message, StringComparison.Ordinal);

        await fixture.Store.SaveObservation(
            ownerPlayer,
            new BonesObservationTranscript(
                new BonesGameId("private-observation"),
                ownerPlayer,
                "# Observation\nSeat 1 private board read."),
            CancellationToken.None);

        var observationException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scopedStore.LoadObservationTranscripts(ownerPlayer, CancellationToken.None));

        Assert.Contains("isolation", observationException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", KnowledgeStoreChecklistItem)]
    public async Task BonesPlayerKnowledgeStore_GivenSaveObservation_ExpectedListedInPlayerHistoryOnly()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var observerOne = new BonesPlayerId(1);
        var observerTwo = new BonesPlayerId(3);
        var gameOne = new BonesGameId("observe-game-1");
        var gameTwo = new BonesGameId("observe-game-2");

        var observationOne = await fixture.Store.SaveObservation(
            observerOne,
            new BonesObservationTranscript(gameOne, observerOne, "# Observation\nSeat 1 saw a blocked round."),
            CancellationToken.None);

        var observationTwo = await fixture.Store.SaveObservation(
            observerTwo,
            new BonesObservationTranscript(gameTwo, observerTwo, "# Observation\nSeat 3 saw dominoes win."),
            CancellationToken.None);

        await fixture.Store.SaveMatchHistory(
            observerOne,
            new BonesMatchHistoryTranscript(
                new BonesGameId("match-game-1"),
                observerOne,
                "# Match\nSeat 1 won with 42 points."),
            CancellationToken.None);

        var playerOneHistory = await fixture.Store.ListHistory(observerOne, CancellationToken.None);
        var playerTwoHistory = await fixture.Store.ListHistory(observerTwo, CancellationToken.None);

        Assert.Collection(
            playerOneHistory,
            descriptor => Assert.Equal(observationOne.ArtifactId, descriptor.ArtifactId),
            descriptor => Assert.Contains("bones-match-1-match-game-1", descriptor.ArtifactId.Value, StringComparison.Ordinal));

        var singleObservation = Assert.Single(playerTwoHistory);
        Assert.Equal(observationTwo.ArtifactId, singleObservation.ArtifactId);
        Assert.DoesNotContain(
            playerTwoHistory,
            descriptor => descriptor.ArtifactId == observationOne.ArtifactId);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.KnowledgeStoreActivePointer)]
    public async Task BonesPlayerKnowledgeStore_GivenNoActivePointer_ExpectedTryLoadActiveStrategyReturnsNull()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();
        var playerId = new BonesPlayerId(1);

        var active = await fixture.Store.TryLoadActiveStrategy(playerId, CancellationToken.None);

        Assert.Null(active);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.KnowledgeStoreActivePointer)]
    public async Task BonesPlayerKnowledgeStore_GivenCandidateAndActive_ExpectedLoadActiveStrategyReturnsIncumbentNotCandidate()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var playerId = new BonesPlayerId(2);
        var incumbentId = new BonesStrategyId("seat-2-v1");
        var candidateId = new BonesStrategyId("seat-2-v2");

        await fixture.Store.SaveStrategyArtifact(
            playerId,
            new BonesStrategyArtifact(incumbentId, playerId, BonesStrategyKind.Script, ActiveStrategyScriptSource, BonesPromotionStatus.Active),
            CancellationToken.None);

        await fixture.Store.SaveCandidateStrategy(
            playerId,
            new BonesStrategyArtifact(candidateId, playerId, BonesStrategyKind.Script, ActiveStrategyScriptSource, BonesPromotionStatus.Candidate),
            CancellationToken.None);

        var active = await fixture.Store.LoadActiveStrategy(playerId, CancellationToken.None);

        Assert.Equal(incumbentId, active.StrategyId);
        Assert.Equal(BonesPromotionStatus.Active, active.PromotionStatus);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.KnowledgeStoreActivePointer)]
    public async Task BonesPlayerKnowledgeStore_GivenPromoteStrategy_ExpectedActivePointerUpdatesToCandidate()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var playerId = new BonesPlayerId(3);
        var incumbentId = new BonesStrategyId("seat-3-v1");
        var candidateId = new BonesStrategyId("seat-3-v2");

        await fixture.Store.SaveStrategyArtifact(
            playerId,
            new BonesStrategyArtifact(incumbentId, playerId, BonesStrategyKind.Script, ActiveStrategyScriptSource, BonesPromotionStatus.Active),
            CancellationToken.None);

        await fixture.Store.SaveCandidateStrategy(
            playerId,
            new BonesStrategyArtifact(candidateId, playerId, BonesStrategyKind.Script, ActiveStrategyScriptSource, BonesPromotionStatus.Candidate),
            CancellationToken.None);

        await fixture.Store.PromoteStrategy(playerId, candidateId, CancellationToken.None);

        var active = await fixture.Store.LoadActiveStrategy(playerId, CancellationToken.None);
        Assert.Equal(candidateId, active.StrategyId);

        var incumbent = await fixture.Store.LoadStrategyArtifact(playerId, incumbentId, CancellationToken.None);
        Assert.Equal(BonesPromotionStatus.Superseded, incumbent.PromotionStatus);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.KnowledgeStoreActivePointer)]
    public async Task BonesPlayerKnowledgeStore_GivenRejectedCandidate_ExpectedActivePointerUnchanged()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var playerId = new BonesPlayerId(4);
        var incumbentId = new BonesStrategyId("seat-4-v1");
        var candidateId = new BonesStrategyId("seat-4-v2");

        await fixture.Store.SaveStrategyArtifact(
            playerId,
            new BonesStrategyArtifact(incumbentId, playerId, BonesStrategyKind.Script, ActiveStrategyScriptSource, BonesPromotionStatus.Active),
            CancellationToken.None);

        await fixture.Store.SaveCandidateStrategy(
            playerId,
            new BonesStrategyArtifact(candidateId, playerId, BonesStrategyKind.Script, ActiveStrategyScriptSource, BonesPromotionStatus.Candidate),
            CancellationToken.None);

        var candidate = await fixture.Store.LoadStrategyArtifact(playerId, candidateId, CancellationToken.None);
        await fixture.Store.SaveStrategyArtifact(
            playerId,
            new BonesStrategyArtifact(candidate.StrategyId, candidate.PlayerId, candidate.Kind, candidate.Source, BonesPromotionStatus.Rejected),
            CancellationToken.None);

        var active = await fixture.Store.LoadActiveStrategy(playerId, CancellationToken.None);
        Assert.Equal(incumbentId, active.StrategyId);
    }

    [Fact]
    [Trait("ChecklistItem", IsolationNegativeGateChecklistItem)]
    public async Task BonesPlayerKnowledgeStore_GivenCrossPlayerPromoteAttempt_ExpectedIsolationGateThrows()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var ownerPlayer = new BonesPlayerId(1);
        var otherPlayer = new BonesPlayerId(2);
        var candidateId = new BonesStrategyId("seat-1-v2");

        await fixture.Store.SaveCandidateStrategy(
            ownerPlayer,
            new BonesStrategyArtifact(candidateId, ownerPlayer, BonesStrategyKind.Script, ActiveStrategyScriptSource, BonesPromotionStatus.Candidate),
            CancellationToken.None);

        var scopedStore = fixture.Store.ForPlayer(otherPlayer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scopedStore.PromoteStrategy(ownerPlayer, candidateId, CancellationToken.None));

        Assert.Contains("isolation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.LoadStrategyActiveAlias)]
    public async Task BonesPlayerKnowledgeStore_GivenNoActivePointer_ExpectedLoadStrategyThrows()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var playerId = new BonesPlayerId(1);

        await fixture.Store.SaveCandidateStrategy(
            playerId,
            new BonesStrategyArtifact(
                new BonesStrategyId("orphan-v1"),
                playerId,
                BonesStrategyKind.Script,
                ActiveStrategyScriptSource,
                BonesPromotionStatus.Candidate),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Store.LoadStrategy(playerId, fixture.SessionId, CancellationToken.None));

        Assert.Contains("active strategy pointer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.KnowledgeStoreActivePointer)]
    public async Task BonesPlayerKnowledgeStore_GivenActivePointer_ExpectedPersistsBonesActiveStrategySeatArtifact()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var playerId = new BonesPlayerId(1);
        var strategyId = new BonesStrategyId("seat-1-v1");

        await fixture.Store.SaveStrategyArtifact(
            playerId,
            new BonesStrategyArtifact(strategyId, playerId, BonesStrategyKind.Script, ActiveStrategyScriptSource, BonesPromotionStatus.Active),
            CancellationToken.None);

        var descriptors = await fixture.ArtifactStore.ListAsync(fixture.SessionId, CancellationToken.None);
        Assert.Contains(descriptors, descriptor => descriptor.ArtifactId.Value == $"bones-active-strategy-seat-{playerId.Seat}");
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.KnowledgeStoreActivePointer)]
    public async Task BonesPlayerKnowledgeStore_GivenNoEffectivenessRecord_ExpectedGetEffectivenessReturnsEmpty()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var effectiveness = await fixture.Store.GetEffectiveness(
            new BonesPlayerId(2),
            new BonesStrategyId("seat-2-v1"),
            CancellationToken.None);

        Assert.Equal(0, effectiveness.MatchesPlayed);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.StrategyEffectivenessRecord)]
    public void BonesStrategyEffectivenessRecord_GivenWinOutcome_ExpectedIncrementsWinsAndMatchesPlayed()
    {
        var strategyId = new BonesStrategyId("seat-1-v1");
        var playerId = new BonesPlayerId(1);
        var record = BonesStrategyEffectivenessRecord.Empty(strategyId, playerId);

        var updated = record.RecordWin(scoreDifferential: 8);

        Assert.Equal(1, updated.MatchesPlayed);
        Assert.Equal(1, updated.Wins);
        Assert.Equal(0, updated.Losses);
        Assert.Equal(8, updated.CumulativeScoreDifferential);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.StrategyEffectivenessRecord)]
    public void BonesStrategyEffectivenessRecord_GivenLossOutcome_ExpectedIncrementsLossesAndScoreDifferential()
    {
        var strategyId = new BonesStrategyId("seat-2-v1");
        var playerId = new BonesPlayerId(2);
        var record = BonesStrategyEffectivenessRecord.Empty(strategyId, playerId);

        var updated = record.RecordLoss(scoreDifferential: -12);

        Assert.Equal(1, updated.MatchesPlayed);
        Assert.Equal(0, updated.Wins);
        Assert.Equal(1, updated.Losses);
        Assert.Equal(-12, updated.CumulativeScoreDifferential);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.StrategyEffectivenessRecord)]
    public async Task BonesPlayerKnowledgeStore_GivenEffectivenessRecord_ExpectedPersistsAndReloadsByStrategyAndPlayer()
    {
        await using var fixture = await BonesKnowledgeStoreFixture.CreateAsync();

        var strategyId = new BonesStrategyId("seat-3-v2");
        var playerId = new BonesPlayerId(3);
        var record = BonesStrategyEffectivenessRecord.Empty(strategyId, playerId)
            .RecordWin(scoreDifferential: 5)
            .RecordLoss(scoreDifferential: -3);

        await fixture.Store.SaveEffectivenessRecord(playerId, record, CancellationToken.None);

        var loaded = await fixture.Store.LoadEffectivenessRecord(playerId, strategyId, CancellationToken.None);

        Assert.Equal(2, loaded.MatchesPlayed);
        Assert.Equal(1, loaded.Wins);
        Assert.Equal(1, loaded.Losses);
        Assert.Equal(2, loaded.CumulativeScoreDifferential);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.StrategyPromotionOptions)]
    public void BonesStrategyPromotionOptions_GivenDefaultConstruction_ExpectedExposesConfigurableThresholds()
    {
        var options = new BonesStrategyPromotionOptions();

        Assert.Equal(20, options.EvaluationMatchCount);
        Assert.Equal(0.05, options.MinimumWinRateImprovement);
        Assert.Equal(1, options.MinimumScoreDifferentialImprovement);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.StrategyPromotionOptions)]
    public void BonesStrategyPromotionOptions_GivenCustomValues_ExpectedRetainsConfiguredThresholds()
    {
        var options = new BonesStrategyPromotionOptions
        {
            EvaluationMatchCount = 12,
            MinimumWinRateImprovement = 0.15,
            MinimumScoreDifferentialImprovement = 4,
        };

        Assert.Equal(12, options.EvaluationMatchCount);
        Assert.Equal(0.15, options.MinimumWinRateImprovement);
        Assert.Equal(4, options.MinimumScoreDifferentialImprovement);
    }

    [Fact]
    [Trait("ChecklistItem", EffectivenessLogWarningChecklistItem)]
    public async Task GetEffectiveness_GivenCorruptEffectivenessFile_LogsWarningAndReturnsEmpty()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var repositoryPath = tempDir.Path;
        var sessionId = new SessionId($"bones-session-{Guid.NewGuid():N}");
        var playerId = new BonesPlayerId(3);
        var strategyId = new BonesStrategyId("seat-3-v2");

        var artifactStore = new WipArtifactStoreLocal(repositoryPath);

        // Create a corrupt effectiveness file (write invalid JSON into the artifact store)
        // so that LoadEffectivenessRecord throws InvalidOperationException on deserialization.
        await artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-effectiveness-{playerId.Seat}-{strategyId.Value}"),
                kind: ArtifactKind.Json,
                fileName: $"bones-player-{playerId.Seat}-effectiveness-{strategyId.Value}",
                content: "{ this is not valid json }",
                producerType: "test",
                producerVersion: "1.0.0",
                producedAtUtc: DateTimeOffset.UtcNow),
            CancellationToken.None);

        var (logCollector, logger) = CreateLogCollector();

        var store = new BonesPlayerKnowledgeStore(
            artifactStore,
            repositoryPath,
            sessionId,
            logger: logger);

        // Act
        var result = await store.GetEffectiveness(playerId, strategyId, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.MatchesPlayed);
        Assert.Equal(0, result.Wins);
        Assert.Equal(0, result.Losses);
        Assert.Equal(0, result.CumulativeScoreDifferential);

        var warnings = logCollector.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Single(warnings);
        Assert.Contains("invalid JSON", warnings[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(strategyId.Value, warnings[0].Message, StringComparison.Ordinal);
        Assert.Contains(playerId.Seat.ToString(), warnings[0].Message, StringComparison.Ordinal);
        Assert.NotNull(warnings[0].Exception);
    }

    // Also verify: when no logger is provided, no exception is thrown (null-conditional guard).
    [Fact]
    [Trait("ChecklistItem", EffectivenessLogWarningChecklistItem)]
    public async Task GetEffectiveness_GivenCorruptFileAndNoLogger_ReturnsEmptyWithoutThrowing()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var repositoryPath = tempDir.Path;
        var sessionId = new SessionId($"bones-session-{Guid.NewGuid():N}");
        var playerId = new BonesPlayerId(2);
        var strategyId = new BonesStrategyId("seat-2-v1");

        var artifactStore = new WipArtifactStoreLocal(repositoryPath);

        // Save corrupt JSON
        await artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-effectiveness-{playerId.Seat}-{strategyId.Value}"),
                kind: ArtifactKind.Json,
                fileName: $"bones-player-{playerId.Seat}-effectiveness-{strategyId.Value}",
                content: "{ not json }",
                producerType: "test",
                producerVersion: "1.0.0",
                producedAtUtc: DateTimeOffset.UtcNow),
            CancellationToken.None);

        var store = new BonesPlayerKnowledgeStore(
            artifactStore,
            repositoryPath,
            sessionId,
            logger: null);

        // Act
        var result = await store.GetEffectiveness(playerId, strategyId, CancellationToken.None);

        // Assert — no exception thrown, returns empty record
        Assert.NotNull(result);
        Assert.Equal(0, result.MatchesPlayed);
    }

    private sealed class LogCollector : ILoggerProvider, ILogger
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }

        void IDisposable.Dispose() { }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private static (LogCollector collector, ILogger<BonesPlayerKnowledgeStore> logger) CreateLogCollector()
    {
        var collector = new LogCollector();
        var factory = LoggerFactory.Create(builder => builder.AddProvider(collector));
        var logger = factory.CreateLogger<BonesPlayerKnowledgeStore>();
        return (collector, logger);
    }

    private const string ActiveStrategyScriptSource =
        """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[0];
            }
        }
        """;

    private sealed class BonesKnowledgeStoreFixture : IAsyncDisposable
    {
        private readonly string _repositoryPath;

        private BonesKnowledgeStoreFixture(string repositoryPath, SessionId sessionId, BonesPlayerKnowledgeStore store)
        {
            _repositoryPath = repositoryPath;
            SessionId = sessionId;
            Store = store;
        }

        public SessionId SessionId { get; }

        public BonesPlayerKnowledgeStore Store { get; }

        public WipArtifactStoreLocal ArtifactStore { get; private set; } = null!;

        public static async Task<BonesKnowledgeStoreFixture> CreateAsync()
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-knowledge-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var sessionId = new SessionId($"bones-session-{Guid.NewGuid():N}");
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var store = new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, sessionId);

            await Task.CompletedTask;
            return new BonesKnowledgeStoreFixture(repositoryPath, sessionId, store)
            {
                ArtifactStore = artifactStore,
            };
        }

        public string GetArtifactPath(Wip.Abstractions.Artifacts.ArtifactDescriptor descriptor)
            => Path.Combine(_repositoryPath, descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_repositoryPath))
                Directory.Delete(_repositoryPath, recursive: true);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"bones-e2-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
