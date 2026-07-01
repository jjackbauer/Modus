using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Knowledge;

public sealed class BonesLongContextMemoryStoreTests
{
    [Fact]
    [Trait("ChecklistItem", "T3.3")]
    public async Task StorePattern_GivenDiscoveredPattern_PersistsAcrossSessions()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-memory-test-{Guid.NewGuid():N}");
        try
        {
            var storeA = new BonesLongContextMemoryStore(new WipArtifactStoreLocal(dataDir), dataDir);
            var playerId = new BonesPlayerId(1);
            var pattern = new BonesDiscoveredPattern(
                "Seat 1 favors early corner play", 0.85, DateTimeOffset.UtcNow);
            await storeA.StorePatternAsync(playerId, pattern, CancellationToken.None);

            var storeB = new BonesLongContextMemoryStore(new WipArtifactStoreLocal(dataDir), dataDir);
            var loaded = await storeB.LoadPatternsAsync(playerId, CancellationToken.None);

            Assert.Single(loaded);
            Assert.Equal(pattern.PatternDescription, loaded[0].PatternDescription);
            Assert.Equal(pattern.Confidence, loaded[0].Confidence);
        }
        finally { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); }
    }

    [Fact]
    [Trait("ChecklistItem", "T3.3")]
    public async Task LoadContext_GivenEnhanceRequest_IncludesHistoricalPatternsInPrompt()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-memory-test-{Guid.NewGuid():N}");
        try
        {
            var store = new BonesLongContextMemoryStore(new WipArtifactStoreLocal(dataDir), dataDir);
            var playerId = new BonesPlayerId(1);

            await store.StorePatternAsync(playerId, new BonesDiscoveredPattern(
                "Pattern A: early doubles", 0.9, DateTimeOffset.UtcNow), CancellationToken.None);
            await store.StorePatternAsync(playerId, new BonesDiscoveredPattern(
                "Pattern B: late blocking", 0.7, DateTimeOffset.UtcNow), CancellationToken.None);
            await store.RecordFailedApproachAsync(playerId,
                new BonesFailedApproach("All-pass strategy", 0, false), CancellationToken.None);

            var loadedPatterns = await store.LoadPatternsAsync(playerId, CancellationToken.None);
            var loadedApproaches = await store.LoadFailedApproachesAsync(playerId, CancellationToken.None);

            var context = store.BuildEnhancementContext(playerId, loadedPatterns, loadedApproaches);
            Assert.Contains("Pattern A", context);
            Assert.Contains("Pattern B", context);
            Assert.Contains("All-pass strategy", context);
        }
        finally { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); }
    }

    [Fact]
    [Trait("ChecklistItem", "T3.3")]
    public async Task RecordFailedApproach_GivenRepeatedRejection_MarksAsDeadEnd()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-memory-test-{Guid.NewGuid():N}");
        try
        {
            var store = new BonesLongContextMemoryStore(new WipArtifactStoreLocal(dataDir), dataDir);
            var playerId = new BonesPlayerId(1);

            for (var i = 0; i < 3; i++)
            {
                await store.RecordFailedApproachAsync(playerId,
                    new BonesFailedApproach("Greedy high-pip strategy", 0, false), CancellationToken.None);
            }

            var approaches = await store.LoadFailedApproachesAsync(playerId, CancellationToken.None);
            Assert.Single(approaches);
            Assert.True(approaches[0].IsMarkedDeadEnd);
            Assert.True(approaches[0].RejectionCount >= 3);
        }
        finally { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); }
    }

    [Fact]
    [Trait("ChecklistItem", "T3.3")]
    public async Task RecordFailedApproach_GivenTwoRejections_DoesNotMarkAsDeadEnd()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-memory-test-{Guid.NewGuid():N}");
        try
        {
            var store = new BonesLongContextMemoryStore(new WipArtifactStoreLocal(dataDir), dataDir);
            var playerId = new BonesPlayerId(1);

            await store.RecordFailedApproachAsync(playerId,
                new BonesFailedApproach("Balanced approach", 0, false), CancellationToken.None);
            await store.RecordFailedApproachAsync(playerId,
                new BonesFailedApproach("Balanced approach", 0, false), CancellationToken.None);

            var approaches = await store.LoadFailedApproachesAsync(playerId, CancellationToken.None);
            Assert.Single(approaches);
            Assert.False(approaches[0].IsMarkedDeadEnd);
        }
        finally { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); }
    }

    [Fact]
    [Trait("ChecklistItem", "T3.3")]
    public async Task BuildEnhancementContext_GivenNoData_ReturnsEmptyString()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-memory-test-{Guid.NewGuid():N}");
        try
        {
            var store = new BonesLongContextMemoryStore(new WipArtifactStoreLocal(dataDir), dataDir);
            var playerId = new BonesPlayerId(1);
            var context = store.BuildEnhancementContext(playerId,
                Array.Empty<BonesDiscoveredPattern>(),
                Array.Empty<BonesFailedApproach>());
            Assert.DoesNotContain("Pattern", context);
        }
        finally { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); }
    }

    [Fact]
    [Trait("ChecklistItem", "T3.3")]
    public async Task LoadPatterns_GivenNoPatterns_ReturnsEmptyList()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-memory-test-{Guid.NewGuid():N}");
        try
        {
            var store = new BonesLongContextMemoryStore(new WipArtifactStoreLocal(dataDir), dataDir);
            var playerId = new BonesPlayerId(1);
            Assert.Empty(await store.LoadPatternsAsync(playerId, CancellationToken.None));
        }
        finally { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); }
    }
}
