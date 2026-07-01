using System.Text.Json;
using Wip.Abstractions.Identifiers;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Bones.Tests;
using Xunit;

namespace Wip.Bones.Tests.Knowledge;

public sealed class BonesStrategyBootstrapperTests
{
    private const string BootstrapperItem = BonesRequirementsChecklistItems.StrategyBootstrapper;
    private const string LibraryRankingItem = BonesRequirementsChecklistItems.StrategyLibraryRanking;

    [Fact]
    [Trait("ChecklistItem", BootstrapperItem)]
    public async Task BonesStrategyBootstrapper_GivenResumeFromBestAndLibraryEntries_ExpectedCopiesActiveStrategiesIntoSession()
    {
        await using var fixture = await BonesStrategyBootstrapperTestFixture.CreateAsync();
        var playerId = new BonesPlayerId(1);
        var strategyId = new BonesStrategyId("library-seat-1-v2");
        var artifact = new BonesStrategyArtifact(
            strategyId,
            playerId,
            BonesStrategyKind.Script,
            "public sealed class SeatStrategy : IBonesPlayerSlot { }",
            BonesPromotionStatus.Active);

        fixture.Library.UpsertBestStrategy(
            playerId,
            artifact,
            new BonesStrategyEffectivenessRecord(strategyId, playerId, 3, 2, 1, 8));

        var result = await fixture.Bootstrapper.BootstrapAsync(
            fixture.ArtifactStore,
            fixture.SessionId,
            fixture.RepositoryPath,
            resumeFromBest: true);

        Assert.Contains(playerId, result.BootstrappedFromLibrarySeats);
        var active = await fixture.KnowledgeStore.LoadActiveStrategy(playerId);
        Assert.Equal(strategyId, active.StrategyId);
        Assert.Equal(BonesStrategyKind.Script, active.Kind);
    }

    [Fact]
    [Trait("ChecklistItem", BootstrapperItem)]
    public async Task BonesStrategyBootstrapper_GivenResumeFromBestFalse_ExpectedUsesHardcodedDefaultSeeds()
    {
        await using var fixture = await BonesStrategyBootstrapperTestFixture.CreateAsync();
        var playerId = new BonesPlayerId(2);
        var strategyId = new BonesStrategyId("library-seat-2-v2");
        fixture.Library.UpsertBestStrategy(
            playerId,
            new BonesStrategyArtifact(
                strategyId,
                playerId,
                BonesStrategyKind.Script,
                "library",
                BonesPromotionStatus.Active),
            new BonesStrategyEffectivenessRecord(strategyId, playerId, 2, 1, 1, 4));

        var result = await fixture.Bootstrapper.BootstrapAsync(
            fixture.ArtifactStore,
            fixture.SessionId,
            fixture.RepositoryPath,
            resumeFromBest: false);

        Assert.Empty(result.BootstrappedFromLibrarySeats);
        Assert.Contains(playerId, result.DefaultSeededSeats);

        var active = await fixture.KnowledgeStore.LoadActiveStrategy(playerId);
        Assert.Equal(new BonesStrategyId("initial-seat-2-v1"), active.StrategyId);
        Assert.Equal(BonesStrategyKind.Markdown, active.Kind);
    }

    [Fact]
    [Trait("ChecklistItem", BootstrapperItem)]
    public async Task BonesStrategyBootstrapper_GivenMissingLibraryEntryForOneSeat_ExpectedPonderOrDefaultSeedOnlyForThatSeat()
    {
        await using var fixture = await BonesStrategyBootstrapperTestFixture.CreateAsync();
        var librarySeat = new BonesPlayerId(3);
        var missingSeat = new BonesPlayerId(4);
        var strategyId = new BonesStrategyId("library-seat-3-v1");

        fixture.Library.UpsertBestStrategy(
            librarySeat,
            new BonesStrategyArtifact(
                strategyId,
                librarySeat,
                BonesStrategyKind.Script,
                "library-seat-3",
                BonesPromotionStatus.Active),
            new BonesStrategyEffectivenessRecord(strategyId, librarySeat, 2, 2, 0, 6));

        var result = await fixture.Bootstrapper.BootstrapAsync(
            fixture.ArtifactStore,
            fixture.SessionId,
            fixture.RepositoryPath,
            resumeFromBest: true);

        Assert.Contains(librarySeat, result.BootstrappedFromLibrarySeats);
        Assert.Contains(missingSeat, result.DefaultSeededSeats);
        Assert.Equal(strategyId, (await fixture.KnowledgeStore.LoadActiveStrategy(librarySeat)).StrategyId);
        Assert.Equal(
            new BonesStrategyId("initial-seat-4-v1"),
            (await fixture.KnowledgeStore.LoadActiveStrategy(missingSeat)).StrategyId);
    }

    [Fact]
    [Trait("ChecklistItem", BootstrapperItem)]
    public async Task BootstrapAsync_GivenResumeFromBestAndNoLibraryEntryForSeat1_SeedsDefaultStrategy()
    {
        await using var fixture = await BonesStrategyBootstrapperTestFixture.CreateAsync();
        var seat1 = new BonesPlayerId(1);

        var result = await fixture.Bootstrapper.BootstrapAsync(
            fixture.ArtifactStore,
            fixture.SessionId,
            fixture.RepositoryPath,
            resumeFromBest: true);

        Assert.Contains(seat1, result.DefaultSeededSeats);
        Assert.DoesNotContain(seat1, result.BootstrappedFromLibrarySeats);
        var active = await fixture.KnowledgeStore.LoadActiveStrategy(seat1);
        Assert.NotNull(active);
        Assert.Equal(new BonesStrategyId("initial-seat-1-v1"), active.StrategyId);
    }

    [Fact]
    [Trait("ChecklistItem", BootstrapperItem)]
    public async Task BootstrapAsync_GivenResumeFromBestAndLibraryEntryForSeat1_BootstrapsFromLibrary()
    {
        await using var fixture = await BonesStrategyBootstrapperTestFixture.CreateAsync();
        var seat1 = new BonesPlayerId(1);
        var strategyId = new BonesStrategyId("library-seat-1-v3");
        var artifact = new BonesStrategyArtifact(
            strategyId,
            seat1,
            BonesStrategyKind.Script,
            "public sealed class SeatStrategy : IBonesPlayerSlot { }",
            BonesPromotionStatus.Active);

        fixture.Library.UpsertBestStrategy(
            seat1,
            artifact,
            new BonesStrategyEffectivenessRecord(strategyId, seat1, 5, 3, 2, 12));

        var result = await fixture.Bootstrapper.BootstrapAsync(
            fixture.ArtifactStore,
            fixture.SessionId,
            fixture.RepositoryPath,
            resumeFromBest: true);

        Assert.Contains(seat1, result.BootstrappedFromLibrarySeats);
        Assert.DoesNotContain(seat1, result.DefaultSeededSeats);
        var active = await fixture.KnowledgeStore.LoadActiveStrategy(seat1);
        Assert.Equal(strategyId, active.StrategyId);
        Assert.Equal(BonesStrategyKind.Script, active.Kind);
    }

    [Fact]
    [Trait("ChecklistItem", LibraryRankingItem)]
    [Trait("ChecklistItem", BootstrapperItem)]
    public async Task BonesStrategyBootstrapper_GivenCorruptEntry_ExpectedSkipsSeatAndFallsBackToDefaultSeed()
    {
        await using var fixture = await BonesStrategyBootstrapperTestFixture.CreateAsync();
        var corruptSeat = new BonesPlayerId(2);
        var corruptPath = Path.Combine(
            fixture.DataDirectory,
            ".bones",
            "strategy-library",
            $"seat-{corruptSeat.Seat}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(corruptPath)!);
        await File.WriteAllTextAsync(corruptPath, "{ not-valid-json");

        var result = await fixture.Bootstrapper.BootstrapAsync(
            fixture.ArtifactStore,
            fixture.SessionId,
            fixture.RepositoryPath,
            resumeFromBest: true);

        Assert.DoesNotContain(corruptSeat, result.BootstrappedFromLibrarySeats);
        Assert.Contains(corruptSeat, result.DefaultSeededSeats);
        Assert.Equal(
            new BonesStrategyId("initial-seat-2-v1"),
            (await fixture.KnowledgeStore.LoadActiveStrategy(corruptSeat)).StrategyId);
    }

    private sealed class BonesStrategyBootstrapperTestFixture : IAsyncDisposable
    {
        public string DataDirectory { get; }
        public string RepositoryPath { get; }
        public SessionId SessionId { get; }
        public WipArtifactStoreLocal ArtifactStore { get; }
        public BonesStrategyLibrary Library { get; }
        public BonesStrategyBootstrapper Bootstrapper { get; }
        public BonesPlayerKnowledgeStore KnowledgeStore { get; }

        private BonesStrategyBootstrapperTestFixture(
            string dataDirectory,
            string repositoryPath,
            SessionId sessionId,
            WipArtifactStoreLocal artifactStore,
            BonesStrategyLibrary library,
            BonesStrategyBootstrapper bootstrapper,
            BonesPlayerKnowledgeStore knowledgeStore)
        {
            DataDirectory = dataDirectory;
            RepositoryPath = repositoryPath;
            SessionId = sessionId;
            ArtifactStore = artifactStore;
            Library = library;
            Bootstrapper = bootstrapper;
            KnowledgeStore = knowledgeStore;
        }

        public static Task<BonesStrategyBootstrapperTestFixture> CreateAsync()
        {
            var dataDirectory = Path.Combine(Path.GetTempPath(), $"bones-bootstrap-{Guid.NewGuid():N}");
            var repositoryPath = Path.Combine(dataDirectory, "repository");
            Directory.CreateDirectory(repositoryPath);

            var sessionId = new SessionId(Guid.NewGuid().ToString("N"));
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var library = new BonesStrategyLibrary(dataDirectory);
            var bootstrapper = new BonesStrategyBootstrapper(library);
            var knowledgeStore = new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, sessionId);

            return Task.FromResult(new BonesStrategyBootstrapperTestFixture(
                dataDirectory,
                repositoryPath,
                sessionId,
                artifactStore,
                library,
                bootstrapper,
                knowledgeStore));
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(DataDirectory))
                Directory.Delete(DataDirectory, recursive: true);

            return ValueTask.CompletedTask;
        }
    }
}
