using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class PopulationMatchComposerTests
{
    // ────────────────────────────────────────────
    // P2 — Test 5: Composer assigns 4 unique seats
    // ────────────────────────────────────────────
    [Fact]
    public void PopulationMatchComposer_GivenPoolOfSize16_ComposeMatch_AssignsFourUniqueSeats()
    {
        var pool = CreatePoolOfSize(16);
        var composer = new PopulationMatchComposer();

        var composition = composer.ComposeMatch(pool);

        Assert.Equal(4, composition.Entries.Count);
        Assert.Equal(4, composition.PlayerSlots.Count);

        // Every seat 1-4 must be assigned
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            Assert.True(composition.PlayerSlots.ContainsKey(playerId),
                $"Seat {seat} should be assigned a player slot.");
            Assert.NotNull(composition.PlayerSlots[playerId]);
        }

        // All four drawn strategies are distinct by ID
        var ids = composition.Entries.Select(e => e.Id.Value).ToHashSet();
        Assert.Equal(4, ids.Count);
    }

    // ────────────────────────────────────────────
    // P2 — Test 6: Repeated calls produce different compositions
    // ────────────────────────────────────────────
    [Fact]
    public void PopulationMatchComposer_GivenRepeatedCalls_ProducesDifferentCompositions()
    {
        const int poolSize = 16;
        const int maxAttempts = 100;

        var pool = CreatePoolOfSize(poolSize);
        var composer = new PopulationMatchComposer();

        var foundDifferent = false;
        string? firstIds = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var composition = composer.ComposeMatch(pool);
            var ids = string.Join(",",
                composition.Entries
                    .Select(e => e.Id.Value)
                    .OrderBy(id => id));

            if (firstIds is null)
            {
                firstIds = ids;
                continue;
            }

            if (ids != firstIds)
            {
                foundDifferent = true;
                break;
            }
        }

        Assert.True(foundDifferent,
            $"Expected different compositions within {maxAttempts} attempts; all produced {firstIds}.");
    }

    // ────────────────────────────────────────────
    // P2 — Test 7: Removed strategy not in composed match
    // ────────────────────────────────────────────
    [Fact]
    public void PopulationMatchComposer_GivenStrategyRemoved_DoesNotIncludeRemovedStrategy()
    {
        const int poolSize = 8;
        var pool = CreatePoolOfSize(poolSize);
        var composer = new PopulationMatchComposer();

        // Pick a strategy ID present in the pool and verify it can appear
        var removedId = new BonesStrategyId("s5");
        Assert.True(pool.Pool.ContainsKey(removedId),
            "Strategy s5 should exist before removal.");

        // Remove it — but since StrategyPool doesn't have a Remove method,
        // we simulate by creating a new pool without that entry.
        var filteredPool = new StrategyPool();
        foreach (var (id, entry) in pool.Pool)
        {
            if (id != removedId)
                filteredPool.Add(entry);
        }

        Assert.False(filteredPool.Pool.ContainsKey(removedId),
            "Strategy s5 should be absent after filtered pool creation.");

        // Run several compositions and ensure removed strategy never appears
        for (var i = 0; i < 20; i++)
        {
            var composition = composer.ComposeMatch(filteredPool);

            foreach (var entry in composition.Entries)
            {
                Assert.NotEqual(removedId.Value, entry.Id.Value);
            }
        }
    }

    // ────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────
    private static StrategyPool CreatePoolOfSize(int size)
    {
        var pool = new StrategyPool();
        for (var i = 1; i <= size; i++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"),
                seat: 1,
                BonesStrategyKind.Script,
                $"// source {i}",
                matchesPlayed: i * 2,
                wins: i,
                losses: i / 2,
                cumulativeScoreDifferential: i * 5,
                rating: 1500.0 + i * 10));
        }

        return pool;
    }
}
