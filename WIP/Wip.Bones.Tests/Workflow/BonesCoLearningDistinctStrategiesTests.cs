using Wip.Artifacts.Local;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Workflow;

public sealed class BonesCoLearningDistinctStrategiesTests
{
    private const string CoLearningProofItem =
        BonesRequirementsChecklistItems.CoLearningDistinctStrategiesProof;

    [Fact]
    [Trait("ChecklistItem", CoLearningProofItem)]
    public void BonesCoLearning_GivenLibraryPopulatedWithDistinctStrategies_ExpectedAllFourSeatsHaveDistinctSources()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"modus-colearning-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);

        try
        {
            var library = new BonesStrategyLibrary(dataDir);

            var sources = new Dictionary<int, string>
            {
                [1] = "using Wip.Bones.Domain;\nusing Wip.Bones.Engine;\npublic sealed class SeatStrategy : IBonesPlayerSlot {\n    public BonesMove ChooseMove(BonesRoundState s, IReadOnlyList<BonesMove> m) => m[1];\n}",
                [2] = "using Wip.Bones.Domain;\nusing Wip.Bones.Engine;\npublic sealed class SeatStrategy : IBonesPlayerSlot {\n    public BonesMove ChooseMove(BonesRoundState s, IReadOnlyList<BonesMove> m) => m[m.Count / 2];\n}",
                [3] = "using Wip.Bones.Domain;\nusing Wip.Bones.Engine;\npublic sealed class SeatStrategy : IBonesPlayerSlot {\n    public BonesMove ChooseMove(BonesRoundState s, IReadOnlyList<BonesMove> m) => m[^1];\n}",
                [4] = "using Wip.Bones.Domain;\nusing Wip.Bones.Engine;\npublic sealed class SeatStrategy : IBonesPlayerSlot {\n    public BonesMove ChooseMove(BonesRoundState s, IReadOnlyList<BonesMove> m) => m[0];\n}",
            };

            var now = DateTimeOffset.UtcNow;

            // Upsert distinct strategies for all 4 seats into the library
            for (var seat = 1; seat <= 4; seat++)
            {
                var playerId = new BonesPlayerId(seat);
                var strategyId = new BonesStrategyId($"distinct-seat-{seat}-v1");
                var artifact = new BonesStrategyArtifact(
                    strategyId, playerId, BonesStrategyKind.Script, sources[seat],
                    BonesPromotionStatus.Active);
                var effectiveness = new BonesStrategyEffectivenessRecord(
                    strategyId, playerId, matchesPlayed: 3, wins: 2, losses: 1,
                    cumulativeScoreDifferential: 5);

                var upserted = library.UpsertBestStrategy(playerId, artifact, effectiveness);
                Assert.True(upserted, $"Seat {seat} strategy should be upserted.");
            }

            // Item 8: Verify LoadBestStrategiesForAllSeats returns all 4 entries
            var allStrategies = library.LoadBestStrategiesForAllSeats();
            Assert.Equal(4, allStrategies.Count);

            // All 4 seats must be present
            for (var seat = 1; seat <= 4; seat++)
                Assert.True(allStrategies.ContainsKey(new BonesPlayerId(seat)),
                    $"Seat {seat} should be in LoadBestStrategiesForAllSeats.");

            // All 4 sources must differ from each other (distinct strategies proof)
            var loadedSources = allStrategies.Values.Select(s => s.Source).ToList();
            var distinctSources = loadedSources.Distinct(StringComparer.Ordinal).ToList();
            Assert.Equal(4, distinctSources.Count);

            // No source should be a trivial legalMoves[0] pattern
            foreach (var source in loadedSources)
            {
                Assert.False(
                    source.Contains("legalMoves[0]") &&
                    !source.Contains("legalMoves[1]") &&
                    !source.Contains("legalMoves[^1]") &&
                    !source.Contains("legalMoves[m.Count"),
                    "Sources should not all be trivial FirstLegalMove.");
            }

            // Verify integrity
            var integrity = library.VerifyIntegrity();
            Assert.Equal(4, integrity.Count);
            Assert.All(integrity, i => Assert.Equal(
                BonesLibrarySeatIntegrityStatus.Present, i.Status));

            // All entries accessible via AllEntries
            var allEntries = library.AllEntries;
            Assert.Equal(4, allEntries.Count);

            // Each source differs from FirstLegalMove pattern
            foreach (var entry in allEntries)
                Assert.False(
                    entry.Source.Contains("legalMoves[0]") &&
                    !entry.Source.Contains("m[1]") &&
                    !entry.Source.Contains("m[m.Count") &&
                    !entry.Source.Contains("m[^1]"),
                    $"Seat {entry.PlayerId.Seat} source should not be trivial.");
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, recursive: true);
        }
    }
}
