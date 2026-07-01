using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Knowledge;

public sealed class BonesStrategyLineageStoreTests
{
    private const string ChecklistItem = "T2.2: Strategy lineage store";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task RecordVersion_GivenNewStrategyVersion_AppendsToLineage()
    {
        // Arrange
        await using var fixture = await LineageStoreFixture.CreateAsync();
        var playerId = new BonesPlayerId(1);

        var entry = new BonesStrategyLineageEntry(
            new BonesStrategyId("initial-seat-1-v1"),
            new BonesStrategyId("initial-seat-1-v2"),
            playerId,
            BonesPromotionDecisionOutcome.Promoted,
            "Added endgame blocking heuristic; improved tile evaluation scoring",
            DateTimeOffset.UtcNow);

        // Act
        await fixture.Store.RecordVersion(entry, CancellationToken.None);

        // Assert — load lineage and verify entry is retrievable
        var lineage = await fixture.Store.LoadLineage(playerId, CancellationToken.None);

        Assert.NotEmpty(lineage);
        var loaded = Assert.Single(lineage);
        Assert.Equal(entry.PredecessorStrategyId, loaded.PredecessorStrategyId);
        Assert.Equal(entry.SuccessorStrategyId, loaded.SuccessorStrategyId);
        Assert.Equal(entry.PlayerId, loaded.PlayerId);
        Assert.Equal(entry.PromotionOutcome, loaded.PromotionOutcome);
        Assert.Equal(entry.DiffSummary, loaded.DiffSummary);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LoadLineage_GivenMultipleVersions_ReturnsVersionChainInOrder()
    {
        // Arrange
        await using var fixture = await LineageStoreFixture.CreateAsync();
        var playerId = new BonesPlayerId(2);

        var entry1 = new BonesStrategyLineageEntry(
            new BonesStrategyId("initial-seat-2-v1"),
            new BonesStrategyId("initial-seat-2-v2"),
            playerId,
            BonesPromotionDecisionOutcome.Rejected,
            "Simplistic first-attempt heuristic; no endgame awareness",
            DateTimeOffset.UtcNow.AddMinutes(-30));

        var entry2 = new BonesStrategyLineageEntry(
            new BonesStrategyId("initial-seat-2-v2"),
            new BonesStrategyId("initial-seat-2-v3"),
            playerId,
            BonesPromotionDecisionOutcome.Promoted,
            "Added opponent hand tracking and blocking; improved win rate",
            DateTimeOffset.UtcNow.AddMinutes(-10));

        var entry3 = new BonesStrategyLineageEntry(
            new BonesStrategyId("initial-seat-2-v3"),
            new BonesStrategyId("initial-seat-2-v4"),
            playerId,
            BonesPromotionDecisionOutcome.Promoted,
            "Refined endgame: prioritize closing when ahead; early double strategy",
            DateTimeOffset.UtcNow);

        // Act
        await fixture.Store.RecordVersion(entry1, CancellationToken.None);
        await fixture.Store.RecordVersion(entry2, CancellationToken.None);
        await fixture.Store.RecordVersion(entry3, CancellationToken.None);

        var lineage = await fixture.Store.LoadLineage(playerId, CancellationToken.None);

        // Assert — entries returned in version order with correct fields
        Assert.Equal(3, lineage.Count);

        // First entry
        Assert.Equal("initial-seat-2-v1", lineage[0].PredecessorStrategyId.Value);
        Assert.Equal("initial-seat-2-v2", lineage[0].SuccessorStrategyId.Value);
        Assert.Equal(BonesPromotionDecisionOutcome.Rejected, lineage[0].PromotionOutcome);
        Assert.Contains("endgame", lineage[0].DiffSummary, StringComparison.Ordinal);

        // Second entry
        Assert.Equal("initial-seat-2-v2", lineage[1].PredecessorStrategyId.Value);
        Assert.Equal("initial-seat-2-v3", lineage[1].SuccessorStrategyId.Value);
        Assert.Equal(BonesPromotionDecisionOutcome.Promoted, lineage[1].PromotionOutcome);
        Assert.Contains("blocking", lineage[1].DiffSummary, StringComparison.Ordinal);

        // Third entry
        Assert.Equal("initial-seat-2-v3", lineage[2].PredecessorStrategyId.Value);
        Assert.Equal("initial-seat-2-v4", lineage[2].SuccessorStrategyId.Value);
        Assert.Equal(BonesPromotionDecisionOutcome.Promoted, lineage[2].PromotionOutcome);
        Assert.Contains("endgame", lineage[2].DiffSummary, StringComparison.Ordinal);

        // Timestamps should be in chronological order
        Assert.True(lineage[0].RecordedAtUtc <= lineage[1].RecordedAtUtc);
        Assert.True(lineage[1].RecordedAtUtc <= lineage[2].RecordedAtUtc);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void LoadLineage_GivenEnhanceContextInjection_LineageAppearsInPrompt()
    {
        // Arrange
        var playerId = new BonesPlayerId(1);

        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("initial-seat-1-v2"),
            playerId,
            "using Wip.Bones.Domain;\nusing Wip.Bones.Engine;\n\npublic sealed class SeatStrategy : IBonesPlayerSlot\n{\n    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)\n    {\n        return legalMoves[0];\n    }\n}");

        var matchHistories = new[]
        {
            new BonesMatchHistoryTranscript(
                new BonesGameId("test-game"),
                playerId,
                "# Bones Match History\nMatch winner seat: 1\nFinal score for seat 1: 10\nFinal score for seat 3: 6"),
        };

        var lineageEntries = new[]
        {
            new BonesStrategyLineageEntry(
                new BonesStrategyId("initial-seat-1-v1"),
                new BonesStrategyId("initial-seat-1-v2"),
                playerId,
                BonesPromotionDecisionOutcome.Promoted,
                "Improved tile evaluation from simple-first to highest-double preference",
                DateTimeOffset.UtcNow),
        };

        // Act
        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            ["bones-player-1-match-test-game"],
            opponentStrategies: null,
            strategyLineage: lineageEntries);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        // Assert — lineage section appears in prompt
        Assert.Contains(BonesEnhancePromptBuilder.StrategyLineageHeader, userPrompt, StringComparison.Ordinal);
        Assert.Contains("initial-seat-1-v1", userPrompt, StringComparison.Ordinal);
        Assert.Contains("initial-seat-1-v2", userPrompt, StringComparison.Ordinal);
        Assert.Contains("Promoted", userPrompt, StringComparison.Ordinal);
        Assert.Contains("tile evaluation", userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LoadLineage_GivenNoEntries_ReturnsEmptyList()
    {
        // Arrange
        await using var fixture = await LineageStoreFixture.CreateAsync();
        var playerId = new BonesPlayerId(3);

        // Act
        var lineage = await fixture.Store.LoadLineage(playerId, CancellationToken.None);

        // Assert
        Assert.NotNull(lineage);
        Assert.Empty(lineage);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LoadLineage_GivenCrossPlayerAccess_ThrowsIsolationError()
    {
        // Arrange
        await using var fixture = await LineageStoreFixture.CreateAsync();
        var ownerId = new BonesPlayerId(1);
        var requestingId = new BonesPlayerId(2);

        // Create store scoped to requesting player
        var scopedStore = fixture.Store.ForPlayer(requestingId);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await scopedStore.LoadLineage(ownerId, CancellationToken.None));

        Assert.Contains("isolation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class LineageStoreFixture : IAsyncDisposable
    {
        private readonly string _repositoryPath;

        private LineageStoreFixture(string repositoryPath, SessionId sessionId, BonesStrategyLineageStore store)
        {
            _repositoryPath = repositoryPath;
            SessionId = sessionId;
            Store = store;
        }

        public SessionId SessionId { get; }

        public BonesStrategyLineageStore Store { get; }

        public static async Task<LineageStoreFixture> CreateAsync()
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-lineage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var sessionId = new SessionId($"bones-session-{Guid.NewGuid():N}");
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var store = new BonesStrategyLineageStore(artifactStore, sessionId);

            await Task.CompletedTask;
            return new LineageStoreFixture(repositoryPath, sessionId, store);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_repositoryPath))
                Directory.Delete(_repositoryPath, recursive: true);

            return ValueTask.CompletedTask;
        }
    }
}
