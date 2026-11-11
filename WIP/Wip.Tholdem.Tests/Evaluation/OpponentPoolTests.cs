using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Wip.Tholdem.Tests.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// Round-two frozen opponent population contract: five distinct archetypes, stable compositions,
/// a content-addressed pool hash and behaviour that is actually distinct at the table.
/// </summary>
public sealed class OpponentPoolTests
{
    private const string SelectionPoolHash = "f5c1f058e45f21ba360fe9af349e6dc0de960e32130730c70c2ec289a7b44fc1";

    private const ulong ArchetypeSeed = 20261110UL;

    [Fact]
    public void SelectionV1_GivenPool_HasFiveDistinctArchetypesAndIds()
    {
        var pool = FrozenOpponentPool.SelectionV1;

        Assert.Equal("tholdem.opponents.v1", pool.PoolId);
        Assert.Equal("1.0.0", pool.Version);
        Assert.Equal(5, pool.Opponents.Count);
        Assert.Equal(5, pool.Opponents.Select(spec => spec.Archetype).Distinct().Count());
        Assert.Equal(5, pool.Opponents.Select(spec => spec.OpponentId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(pool.Opponents, spec => Assert.Equal(1, spec.Version));
        Assert.Equal(4, pool.Compositions.Count);
        Assert.Equal(
            4,
            pool.Compositions.Select(composition => composition.CompositionId)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public void Create_GivenOpponentId_ReturnsAgentWithThatName()
    {
        var pool = FrozenOpponentPool.SelectionV1;

        foreach (var spec in pool.Opponents)
        {
            var agent = pool.Create(spec.OpponentId);

            Assert.Equal(spec.OpponentId, agent.Name);
            var strategy = Assert.IsType<StrategyAgent>(agent);
            Assert.Equal(spec.Genome, strategy.Genome);
        }
    }

    [Fact]
    public void PoolHash_GivenSamePool_IsStable_AndHoldoutDiffers()
    {
        var selection = FrozenOpponentPool.SelectionV1;

        Assert.Equal(SelectionPoolHash, selection.PoolHash);
        Assert.Equal(selection.PoolHash, FrozenOpponentPool.SelectionV1.PoolHash);
        Assert.Equal(StrategyArtifact.ComputeHash(selection.ToCanonicalJson()), selection.PoolHash);

        var holdout = FrozenOpponentPool.HoldoutV1;
        Assert.NotEqual(selection.PoolHash, holdout.PoolHash);
        Assert.Matches("^[0-9a-f]{64}$", selection.PoolHash);
        Assert.Matches("^[0-9a-f]{64}$", holdout.PoolHash);
    }

    [Fact]
    public void Compositions_GivenEveryComposition_ReferencesOnlyOwnPoolOpponents()
    {
        foreach (var pool in new[] { FrozenOpponentPool.SelectionV1, FrozenOpponentPool.HoldoutV1 })
        {
            var ownIds = pool.Opponents.Select(spec => spec.OpponentId).ToHashSet(StringComparer.Ordinal);

            foreach (var composition in pool.Compositions)
            {
                Assert.Equal(3, composition.OpponentIds.Count);
                Assert.All(composition.OpponentIds, opponentId => Assert.Contains(opponentId, ownIds));
            }
        }

        Assert.Equal(
            FrozenOpponentPool.SelectionV1.Compositions.Select(composition => composition.CompositionId),
            FrozenOpponentPool.HoldoutV1.Compositions.Select(composition => composition.CompositionId));
    }

    [Fact]
    public void HoldoutV1_GivenSelectionPool_HasNoSharedOpponentIds()
    {
        var selection = FrozenOpponentPool.SelectionV1;
        var holdout = FrozenOpponentPool.HoldoutV1;

        var selectionIds = selection.Opponents.Select(spec => spec.OpponentId).ToHashSet(StringComparer.Ordinal);
        var shared = holdout.Opponents
            .Select(spec => spec.OpponentId)
            .Where(selectionIds.Contains)
            .ToArray();

        Assert.Empty(shared);
        Assert.Equal(5, holdout.Opponents.Count);
        Assert.Equal(5, holdout.Opponents.Select(spec => spec.Archetype).Distinct().Count());
        Assert.Equal("tholdem.opponents.holdout.v1", holdout.PoolId);
        Assert.Equal("1.0.0-holdout", holdout.Version);
    }

    [Fact]
    public void Archetypes_GivenSameSeededTable_ShowDistinctActionFrequencies()
    {
        var pool = FrozenOpponentPool.SelectionV1;
        var tagSpec = pool.Opponents.Single(spec => spec.Archetype == OpponentArchetype.TightAggressive);
        var counts = new Dictionary<OpponentArchetype, (int Calls, int Aggressive)>();

        foreach (var archetype in Enum.GetValues<OpponentArchetype>())
        {
            var spec = pool.Opponents.Single(opponent => opponent.Archetype == archetype);
            var focal = new RecordingAgent(pool.Create(spec.OpponentId));
            var agents = new IPokerAgent[]
            {
                focal,
                pool.Create(tagSpec.OpponentId),
                pool.Create(tagSpec.OpponentId),
                pool.Create(tagSpec.OpponentId),
            };

            MatchSimulator.Run(agents, new SimulationOptions(ArchetypeSeed, 60));

            var calls = focal.Turns.Count(turn => turn.Action.Kind == ActionKind.Call);
            var aggressive = focal.Turns.Count(
                turn => turn.Action.Kind is ActionKind.Bet or ActionKind.Raise or ActionKind.AllIn);
            counts[archetype] = (calls, aggressive);
        }

        var tightPassive = counts[OpponentArchetype.TightPassive];
        var tightAggressive = counts[OpponentArchetype.TightAggressive];
        var looseAggressive = counts[OpponentArchetype.LooseAggressive];
        var callingStation = counts[OpponentArchetype.CallingStation];
        var adversarialRaiser = counts[OpponentArchetype.AdversarialRaiser];

        Assert.True(
            callingStation.Calls > tightAggressive.Calls,
            $"calling-station calls {callingStation.Calls} must exceed tight-aggressive calls {tightAggressive.Calls}");
        Assert.True(
            looseAggressive.Aggressive > tightAggressive.Aggressive,
            $"loose-aggressive aggressive {looseAggressive.Aggressive} must exceed tight-aggressive aggressive {tightAggressive.Aggressive}");
        Assert.True(
            adversarialRaiser.Aggressive > tightAggressive.Aggressive,
            $"adversarial-raiser aggressive {adversarialRaiser.Aggressive} must exceed tight-aggressive aggressive {tightAggressive.Aggressive}");
        Assert.True(
            tightPassive.Aggressive < tightAggressive.Aggressive,
            $"tight-passive aggressive {tightPassive.Aggressive} must stay below tight-aggressive aggressive {tightAggressive.Aggressive}");
    }
}
