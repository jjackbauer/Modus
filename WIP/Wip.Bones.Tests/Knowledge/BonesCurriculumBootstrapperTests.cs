using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Knowledge;

public sealed class BonesCurriculumBootstrapperTests
{
    [Fact]
    [Trait("ChecklistItem", "T3.5")]
    public void Bootstrap_GivenNoLibraryEntry_StartsWithSimplifiedGame()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-curriculum-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(dataDir);
            var bootstrapper = new BonesCurriculumBootstrapper(library);
            var playerId = new BonesPlayerId(1);

            var tier = bootstrapper.GetCurrentTier(playerId);

            Assert.Equal(BonesCurriculumTier.Simplified, tier);
            var config = BonesCurriculumBootstrapper.GetConfig(tier);
            Assert.Equal(2, config.PlayerCount);
            Assert.Equal(4, config.TargetScore);
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "T3.5")]
    public void Bootstrap_GivenSimplifiedGameSuccess_AdvancesToNextDifficulty()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-curriculum-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(dataDir);
            var bootstrapper = new BonesCurriculumBootstrapper(library);

            var advanced = bootstrapper.AdvanceToNextTier(BonesCurriculumTier.Simplified);
            Assert.Equal(BonesCurriculumTier.Intermediate, advanced);

            var config = BonesCurriculumBootstrapper.GetConfig(advanced);
            Assert.Equal(3, config.PlayerCount);
            Assert.Equal(6, config.TargetScore);
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "T3.5")]
    public void AdvanceToNextTier_GivenFull_StaysAtFull()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-curriculum-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(dataDir);
            var bootstrapper = new BonesCurriculumBootstrapper(library);

            var advanced = bootstrapper.AdvanceToNextTier(BonesCurriculumTier.Full);
            Assert.Equal(BonesCurriculumTier.Full, advanced);
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "T3.5")]
    public void GetConfig_ReturnsCorrectGameParameters()
    {
        var simplified = BonesCurriculumBootstrapper.GetConfig(BonesCurriculumTier.Simplified);
        Assert.Equal(2, simplified.PlayerCount);
        Assert.Equal(4, simplified.TargetScore);

        var intermediate = BonesCurriculumBootstrapper.GetConfig(BonesCurriculumTier.Intermediate);
        Assert.Equal(3, intermediate.PlayerCount);
        Assert.Equal(6, intermediate.TargetScore);

        var full = BonesCurriculumBootstrapper.GetConfig(BonesCurriculumTier.Full);
        Assert.Equal(4, full.PlayerCount);
        Assert.Equal(8, full.TargetScore);
    }

    [Fact]
    [Trait("ChecklistItem", "T3.5")]
    public void GetCurrentTier_GivenLibraryWithMatchesPlayed_ReturnsHigherTier()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-curriculum-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(dataDir);
            var playerId = new BonesPlayerId(1);
            var strategyId = new BonesStrategyId("test-strategy-v1");

            var artifact = new BonesStrategyArtifact(
                strategyId, playerId, BonesStrategyKind.Script,
                "// Test script", BonesPromotionStatus.Active);
            var metrics = new BonesStrategyEffectivenessRecord(
                strategyId, playerId, 5, 3, 2, 10);
            library.UpsertBestStrategy(playerId, artifact, metrics);

            var bootstrapper = new BonesCurriculumBootstrapper(library);
            var tier = bootstrapper.GetCurrentTier(playerId);

            Assert.Equal(BonesCurriculumTier.Full, tier);
            Assert.True(bootstrapper.HasGraduated(playerId));
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "T3.5")]
    public void HasGraduated_GivenSimplifiedTier_ReturnsFalse()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"bones-curriculum-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(dataDir);
            var bootstrapper = new BonesCurriculumBootstrapper(library);
            var playerId = new BonesPlayerId(2);

            Assert.False(bootstrapper.HasGraduated(playerId));
        }
        finally
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, true);
        }
    }
}
