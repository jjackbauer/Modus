using Wip.Bones.Agents.Enhance;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesStrategyVersioningTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.StrategyVersioningCollisionAvoidance;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenInitialSeatV1_ProducesInitialSeatV2()
    {
        var current = new BonesStrategyId("initial-seat-1-v1");
        var playerId = new BonesPlayerId(1);

        var result = BonesStrategyVersioning.NextVersion(current, playerId);

        Assert.Equal("initial-seat-1-v2", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenInitialSeatV5_ProducesInitialSeatV6()
    {
        var current = new BonesStrategyId("initial-seat-3-v5");
        var playerId = new BonesPlayerId(3);

        var result = BonesStrategyVersioning.NextVersion(current, playerId);

        Assert.Equal("initial-seat-3-v6", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenNonStandardName_NoExistingIds_ProducesV1()
    {
        var current = new BonesStrategyId("custom-strategy-name");
        var playerId = new BonesPlayerId(2);

        var result = BonesStrategyVersioning.NextVersion(current, playerId);

        Assert.Equal("initial-seat-2-v1", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenNonStandardName_WithExistingV1_ProducesV2()
    {
        var current = new BonesStrategyId("custom-strategy-name");
        var playerId = new BonesPlayerId(1);
        var existingIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "initial-seat-1-v1",
        };

        var result = BonesStrategyVersioning.NextVersion(current, playerId, existingIds);

        Assert.Equal("initial-seat-1-v2", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenNonStandardName_WithExistingV1ThroughV4_ProducesV5()
    {
        var current = new BonesStrategyId("custom-strategy");
        var playerId = new BonesPlayerId(2);
        var existingIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "initial-seat-2-v1",
            "initial-seat-2-v2",
            "initial-seat-2-v3",
            "initial-seat-2-v4",
        };

        var result = BonesStrategyVersioning.NextVersion(current, playerId, existingIds);

        Assert.Equal("initial-seat-2-v5", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenNonStandardName_WithGappedVersions_SkipsPastHighest()
    {
        // v1, v3, v7 exist — should produce v8 (past highest, not first gap)
        var current = new BonesStrategyId("some-name");
        var playerId = new BonesPlayerId(3);
        var existingIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "initial-seat-3-v1",
            "initial-seat-3-v3",
            "initial-seat-3-v7",
        };

        var result = BonesStrategyVersioning.NextVersion(current, playerId, existingIds);

        Assert.Equal("initial-seat-3-v8", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenStandardName_WithExistingNextVersion_SkipsCollision()
    {
        // Current is initial-seat-1-v2, next would be v3, but v3 exists.
        // Should skip to v4.
        var current = new BonesStrategyId("initial-seat-1-v2");
        var playerId = new BonesPlayerId(1);
        var existingIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "initial-seat-1-v3",
        };

        var result = BonesStrategyVersioning.NextVersion(current, playerId, existingIds);

        Assert.Equal("initial-seat-1-v4", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenStandardName_WithExistingV3ThroughV7_SkipsToV8()
    {
        var current = new BonesStrategyId("initial-seat-1-v2");
        var playerId = new BonesPlayerId(1);
        var existingIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "initial-seat-1-v3",
            "initial-seat-1-v4",
            "initial-seat-1-v5",
            "initial-seat-1-v6",
            "initial-seat-1-v7",
        };

        var result = BonesStrategyVersioning.NextVersion(current, playerId, existingIds);

        Assert.Equal("initial-seat-1-v8", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenNullExistingIds_BehavesLikeEmpty()
    {
        var current = new BonesStrategyId("initial-seat-4-v1");
        var playerId = new BonesPlayerId(4);

        var result = BonesStrategyVersioning.NextVersion(current, playerId, null);

        Assert.Equal("initial-seat-4-v2", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenEmptyExistingIds_BehavesNormally()
    {
        var current = new BonesStrategyId("initial-seat-1-v3");
        var playerId = new BonesPlayerId(1);
        var existingIds = new HashSet<string>(StringComparer.Ordinal);

        var result = BonesStrategyVersioning.NextVersion(current, playerId, existingIds);

        Assert.Equal("initial-seat-1-v4", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenNonStandardName_WithNullExistingIds_ProducesV1()
    {
        var current = new BonesStrategyId("custom-name");
        var playerId = new BonesPlayerId(3);

        var result = BonesStrategyVersioning.NextVersion(current, playerId, null);

        Assert.Equal("initial-seat-3-v1", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenNonStandardName_WithEmptyExistingIds_ProducesV1()
    {
        var current = new BonesStrategyId("another-custom");
        var playerId = new BonesPlayerId(2);
        var existingIds = new HashSet<string>(StringComparer.Ordinal);

        var result = BonesStrategyVersioning.NextVersion(current, playerId, existingIds);

        Assert.Equal("initial-seat-2-v1", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenBaseNameWithVInside_StillParsesCorrectly()
    {
        // Strategy base name contains "v" as part of its name.
        // LastIndexOf should find the trailing "-vN" pattern.
        var current = new BonesStrategyId("strategy-variant-v2");
        var playerId = new BonesPlayerId(1);

        var result = BonesStrategyVersioning.NextVersion(current, playerId);

        Assert.Equal("strategy-variant-v3", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenMultiDigitVersion_IncrementsCorrectly()
    {
        var current = new BonesStrategyId("initial-seat-1-v99");
        var playerId = new BonesPlayerId(1);

        var result = BonesStrategyVersioning.NextVersion(current, playerId);

        Assert.Equal("initial-seat-1-v100", result.Value);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void NextVersion_GivenStandardName_NoExistingIds_NoCollisionCheckNeeded()
    {
        var current = new BonesStrategyId("my-strategy-v10");
        var playerId = new BonesPlayerId(4);

        var result = BonesStrategyVersioning.NextVersion(current, playerId);

        Assert.Equal("my-strategy-v11", result.Value);
    }
}
