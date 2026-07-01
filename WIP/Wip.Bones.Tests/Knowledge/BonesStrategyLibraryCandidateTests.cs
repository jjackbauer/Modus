using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Knowledge;

public sealed class BonesStrategyLibraryCandidateTests
{
    [Fact]
    [Trait("ChecklistItem", "M1")]
    public void UpsertBestStrategy_GivenRejectedCandidateWithHighScore_StoresAsCandidateForReconsideration()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-candidate-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(dataDir);
            var playerId = new BonesPlayerId(1);
            var strategyId = new BonesStrategyId("candidate-high-score-v1");

            var artifact = new BonesStrategyArtifact(
                strategyId, playerId, BonesStrategyKind.Script,
                "// High-scoring candidate that lost evaluation",
                BonesPromotionStatus.Rejected);
            var metrics = new BonesPromotionEvaluationMetrics(
                EvaluationMatchCount: 20, CandidateWins: 9, IncumbentWins: 11,
                CandidateWinRate: 0.45, IncumbentWinRate: 0.55, WinRateImprovement: -0.10,
                CandidateAverageScore: 25.0, IncumbentAverageScore: 20.0,
                ScoreDifferentialDelta: 5.0, StandardError: 0.1, ConfidenceInterval95: 0.2);

            var stored = library.UpsertCandidate(playerId, artifact, metrics);
            Assert.True(stored);

            var candidates = library.LoadCandidates(playerId);
            Assert.Single(candidates);
            Assert.Equal(strategyId, candidates[0].StrategyId);
            Assert.Equal(5.0, candidates[0].EvaluationMetrics.ScoreDifferentialDelta);
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "M1")]
    public void LoadCandidates_GivenNoCandidates_ReturnsEmptyList()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-candidate-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(dataDir);
            var playerId = new BonesPlayerId(1);
            Assert.Empty(library.LoadCandidates(playerId));
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "M1")]
    public void UpsertCandidate_GivenMultipleRejectedStrategies_KeepsTopByScoreDelta()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-candidate-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(dataDir);
            var playerId = new BonesPlayerId(1);

            for (var i = 0; i < 3; i++)
            {
                var sid = new BonesStrategyId($"candidate-v{i + 1}");
                var art = new BonesStrategyArtifact(sid, playerId, BonesStrategyKind.Script,
                    $"// Candidate {i + 1}", BonesPromotionStatus.Rejected);
                var met = new BonesPromotionEvaluationMetrics(
                    20, 0, 0, 0, 0, 0, 0, 0, (i + 1) * 5.0, 0, 0);
                library.UpsertCandidate(playerId, art, met);
            }

            var candidates = library.LoadCandidates(playerId);
            Assert.Equal(3, candidates.Count);
            Assert.Equal(15.0, candidates[0].EvaluationMetrics.ScoreDifferentialDelta);
            Assert.Equal(10.0, candidates[1].EvaluationMetrics.ScoreDifferentialDelta);
            Assert.Equal(5.0, candidates[2].EvaluationMetrics.ScoreDifferentialDelta);
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "M1")]
    public void UpsertCandidate_GivenDuplicateStrategyId_ReplacesExisting()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-candidate-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(dataDir);
            var playerId = new BonesPlayerId(1);
            var strategyId = new BonesStrategyId("duplicate-candidate");

            var art1 = new BonesStrategyArtifact(strategyId, playerId, BonesStrategyKind.Script,
                "// First version", BonesPromotionStatus.Rejected);
            library.UpsertCandidate(playerId, art1,
                new BonesPromotionEvaluationMetrics(20, 0, 0, 0, 0, 0, 0, 0, 3.0, 0, 0));

            var art2 = new BonesStrategyArtifact(strategyId, playerId, BonesStrategyKind.Script,
                "// Updated version", BonesPromotionStatus.Rejected);
            library.UpsertCandidate(playerId, art2,
                new BonesPromotionEvaluationMetrics(20, 0, 0, 0, 0, 0, 0, 0, 7.0, 0, 0));

            var candidates = library.LoadCandidates(playerId);
            Assert.Single(candidates);
            Assert.Equal(7.0, candidates[0].EvaluationMetrics.ScoreDifferentialDelta);
            Assert.Equal("// Updated version", candidates[0].Source);
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "M1")]
    public void LoadBestStrategy_GivenCandidateOutperformsIncumbentInReEvaluation_ReturnsCandidate()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-candidate-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(dataDir);
            var playerId = new BonesPlayerId(1);
            var candidateId = new BonesStrategyId("strong-candidate");

            var candidateArt = new BonesStrategyArtifact(candidateId, playerId, BonesStrategyKind.Script,
                "// Strong candidate", BonesPromotionStatus.Rejected);
            library.UpsertCandidate(playerId, candidateArt,
                new BonesPromotionEvaluationMetrics(20, 9, 11, 0.45, 0.55, -0.10, 30.0, 20.0, 10.0, 0.1, 0.2));

            var candidates = library.LoadCandidates(playerId);
            Assert.Single(candidates);

            var effMetrics = new BonesStrategyEffectivenessRecord(candidateId, playerId, 20, 15, 5, 50);
            var promoted = library.UpsertBestStrategy(playerId, candidateArt, effMetrics);
            Assert.True(promoted);

            var best = library.LoadBestStrategy(playerId);
            Assert.NotNull(best);
            Assert.Equal(candidateId, best.StrategyId);
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, true);
        }
    }
}
