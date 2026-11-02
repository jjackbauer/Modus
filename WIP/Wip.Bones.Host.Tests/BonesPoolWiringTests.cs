using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Play;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

/// <summary>
/// Phase 2 wiring tests: I4 (DI injection), I5 (RecordOutcome wiring),
/// and I6 (ELO update observability).
/// </summary>
public sealed class BonesPoolWiringTests
{
    // ════════════════════════════════════════════
    // I4: Inject StrategyPool into BonesPlayMatchTool
    // ════════════════════════════════════════════

    [Fact]
    public void BonesPlayMatchTool_GivenDiResolution_StrategyPoolDependencyIsResolved()
    {
        // Build a minimal DI container simulating the host registration
        var services = new ServiceCollection();
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-wiring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);

        try
        {
            var artifactStore = new WipArtifactStoreLocal(dataDir);
            services.AddSingleton<IArtifactStore>(artifactStore);
            services.AddSingleton(new BonesGameEngine());
            services.AddSingleton<StrategyPool>();
            services.AddSingleton<BonesGameSimulationBudget>(new BonesGameSimulationBudget(1000));
            services.AddSingleton<PopulationMatchComposer>();

            var provider = services.BuildServiceProvider();
            var pool = provider.GetRequiredService<StrategyPool>();
            var engine = provider.GetRequiredService<BonesGameEngine>();
            var budget = provider.GetRequiredService<BonesGameSimulationBudget>();
            var composer = provider.GetRequiredService<PopulationMatchComposer>();
            var artifact = provider.GetRequiredService<IArtifactStore>();

            // Construct BonesPlayMatchTool with the pool injected
            var tool = new BonesPlayMatchTool(
                artifactStore: artifact,
                engine: engine,
                modelProvider: new StubBonesPlayTurnModelProvider(),
                strategyPool: pool,
                matchComposer: composer,
                gameBudget: budget);

            // Verify the tool was created without error (no null ref)
            Assert.NotNull(tool);

            // Verify PopulationMatchComposer resolves
            Assert.NotNull(composer);
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, recursive: true);
        }
    }

    [Fact]
    public void PopulationMatchComposer_GivenDiRegistration_ResolvesFromContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<PopulationMatchComposer>();
        var provider = services.BuildServiceProvider();

        var composer = provider.GetRequiredService<PopulationMatchComposer>();
        Assert.NotNull(composer);
    }

    // ════════════════════════════════════════════
    // I5: Call RecordOutcome after every match
    // ════════════════════════════════════════════

    [Fact]
    public void RecordOutcome_GivenMatchResult_UpdatesAllFourParticipantRatings()
    {
        var pool = new StrategyPool(new Random(42));

        // Add 4 distinct strategies
        var s1 = new StrategyEntry(
            new BonesStrategyId("s1"), 1, BonesStrategyKind.Script,
            "// s1", 0, 0, 0, 0, 1500.0);
        var s2 = new StrategyEntry(
            new BonesStrategyId("s2"), 2, BonesStrategyKind.Script,
            "// s2", 0, 0, 0, 0, 1500.0);
        var s3 = new StrategyEntry(
            new BonesStrategyId("s3"), 3, BonesStrategyKind.Script,
            "// s3", 0, 0, 0, 0, 1500.0);
        var s4 = new StrategyEntry(
            new BonesStrategyId("s4"), 4, BonesStrategyKind.Script,
            "// s4", 0, 0, 0, 0, 1500.0);

        pool.Add(s1);
        pool.Add(s2);
        pool.Add(s3);
        pool.Add(s4);

        // Record pre-match ratings
        var preRating1 = pool.Pool[new BonesStrategyId("s1")].Rating;
        var preRating2 = pool.Pool[new BonesStrategyId("s2")].Rating;
        var preRating3 = pool.Pool[new BonesStrategyId("s3")].Rating;
        var preRating4 = pool.Pool[new BonesStrategyId("s4")].Rating;

        // Create match result where seat 1 wins
        var winner = new BonesPlayerId(1);
        var cumulativeScores = new Dictionary<BonesPlayerId, int>
        {
            [new BonesPlayerId(1)] = 100,
            [new BonesPlayerId(2)] = 50,
            [new BonesPlayerId(3)] = 40,
            [new BonesPlayerId(4)] = 30,
        };

        var matchResult = new BonesMatchResult(
            new BonesGameId("test-game"),
            winner,
            cumulativeScores,
            Array.Empty<BonesMatchRoundRecord>(),
            Array.Empty<BonesEvent>());

        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = new BonesStrategyId("s1"),
            [2] = new BonesStrategyId("s2"),
            [3] = new BonesStrategyId("s3"),
            [4] = new BonesStrategyId("s4"),
        };

        pool.RecordOutcome(matchResult, seatToStrategyId);

        // Verify all 4 participants' ratings changed
        var post1 = pool.Pool[new BonesStrategyId("s1")];
        var post2 = pool.Pool[new BonesStrategyId("s2")];
        var post3 = pool.Pool[new BonesStrategyId("s3")];
        var post4 = pool.Pool[new BonesStrategyId("s4")];

        Assert.NotEqual(preRating1, post1.Rating); // Winner: should gain
        Assert.NotEqual(preRating2, post2.Rating); // Loser: should lose
        Assert.NotEqual(preRating3, post3.Rating);
        Assert.NotEqual(preRating4, post4.Rating);

        Assert.True(post1.Rating > preRating1, "Winner rating should increase");
        Assert.True(post2.Rating < preRating2, "Loser rating should decrease");
    }

    [Fact]
    public void RecordOutcome_GivenWinner_UpdatesWinsAndMatchesPlayed()
    {
        var pool = new StrategyPool(new Random(42));

        var winner = new StrategyEntry(
            new BonesStrategyId("winner"), 1, BonesStrategyKind.Script,
            "// winner", 0, 0, 0, 0, 1500.0);
        var loser = new StrategyEntry(
            new BonesStrategyId("loser"), 2, BonesStrategyKind.Script,
            "// loser", 0, 0, 0, 0, 1500.0);
        var neutral1 = new StrategyEntry(
            new BonesStrategyId("n1"), 3, BonesStrategyKind.Script,
            "// n1", 0, 0, 0, 0, 1500.0);
        var neutral2 = new StrategyEntry(
            new BonesStrategyId("n2"), 4, BonesStrategyKind.Script,
            "// n2", 0, 0, 0, 0, 1500.0);

        pool.Add(winner);
        pool.Add(loser);
        pool.Add(neutral1);
        pool.Add(neutral2);

        var cumulativeScores = new Dictionary<BonesPlayerId, int>
        {
            [new BonesPlayerId(1)] = 100,
            [new BonesPlayerId(2)] = 50,
            [new BonesPlayerId(3)] = 40,
            [new BonesPlayerId(4)] = 30,
        };

        var matchResult = new BonesMatchResult(
            new BonesGameId("test-game"),
            new BonesPlayerId(1), // seat 1 wins
            cumulativeScores,
            Array.Empty<BonesMatchRoundRecord>(),
            Array.Empty<BonesEvent>());

        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = new BonesStrategyId("winner"),
            [2] = new BonesStrategyId("loser"),
            [3] = new BonesStrategyId("n1"),
            [4] = new BonesStrategyId("n2"),
        };

        pool.RecordOutcome(matchResult, seatToStrategyId);

        var winnerEntry = pool.Pool[new BonesStrategyId("winner")];
        var loserEntry = pool.Pool[new BonesStrategyId("loser")];

        Assert.Equal(1, winnerEntry.MatchesPlayed);
        Assert.Equal(1, winnerEntry.Wins);
        Assert.Equal(0, winnerEntry.Losses);

        Assert.Equal(1, loserEntry.MatchesPlayed);
        Assert.Equal(0, loserEntry.Wins);
        Assert.Equal(1, loserEntry.Losses);
    }

    // ════════════════════════════════════════════
    // I6: Verify ELO updates are observable
    // ════════════════════════════════════════════

    [Fact]
    public void PoolEndpoint_GivenRecordOutcomeCalled_EntryMatchesPlayedIncremented()
    {
        var pool = new StrategyPool(new Random(42));

        // Add 4 entries
        for (var seat = 1; seat <= 4; seat++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{seat}"), seat, BonesStrategyKind.Script,
                $"// source {seat}", 0, 0, 0, 0, 1500.0));
        }

        Assert.Equal(0, pool.Pool[new BonesStrategyId("s1")].MatchesPlayed);

        var cumulativeScores = new Dictionary<BonesPlayerId, int>
        {
            [new BonesPlayerId(1)] = 100,
            [new BonesPlayerId(2)] = 50,
            [new BonesPlayerId(3)] = 40,
            [new BonesPlayerId(4)] = 30,
        };

        var matchResult = new BonesMatchResult(
            new BonesGameId("g1"),
            new BonesPlayerId(1),
            cumulativeScores,
            Array.Empty<BonesMatchRoundRecord>(),
            Array.Empty<BonesEvent>());

        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = new BonesStrategyId("s1"),
            [2] = new BonesStrategyId("s2"),
            [3] = new BonesStrategyId("s3"),
            [4] = new BonesStrategyId("s4"),
        };

        pool.RecordOutcome(matchResult, seatToStrategyId);

        // After RecordOutcome, all participants should have 1 match played
        Assert.Equal(1, pool.Pool[new BonesStrategyId("s1")].MatchesPlayed);
        Assert.Equal(1, pool.Pool[new BonesStrategyId("s2")].MatchesPlayed);
        Assert.Equal(1, pool.Pool[new BonesStrategyId("s3")].MatchesPlayed);
        Assert.Equal(1, pool.Pool[new BonesStrategyId("s4")].MatchesPlayed);

        // Avg rating should have changed (not all still 1500)
        var avgRating = pool.Pool.Values.Average(e => e.Rating);
        Assert.NotEqual(1500.0, avgRating);
    }

    // ════════════════════════════════════════════
    // R4: Unify pool instance between Status API, Web pool, and runtime
    // ════════════════════════════════════════════

    [Fact]
    public void StrategyPool_Instance_PointsToDiResolvedSingletonAfterContainerBuild()
    {
        // Build a minimal DI container that registers StrategyPool as a singleton.
        var services = new ServiceCollection();
        services.AddSingleton<StrategyPool>();
        var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<StrategyPool>();

        // The static Instance must reference the same object as the DI-resolved singleton.
        // This ensures /api/bones/status (DI-injected) and /bones/pool (StrategyPool.Instance)
        // read the same pool data.
        Assert.NotNull(StrategyPool.Instance);
        Assert.Same(resolved, StrategyPool.Instance);
    }
}

/// <summary>
/// Minimal stub for DI construction test.
/// </summary>
internal sealed class StubBonesPlayTurnModelProvider
    : IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>
{
    public ValueTask<ModelProviderResponse<BonesPlayTurnResult>> ExecuteAsync(
        ModelProviderRequest<BonesPlayTurnRequest> request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var payload = new BonesPlayTurnResult(request.Payload.AllowedMoves[0].MoveId.Value);
        return ValueTask.FromResult(
            new ModelProviderResponse<BonesPlayTurnResult>(
                payload, "stub", "stub",
                new ModelProviderUsage(64, 16), null));
    }
}
