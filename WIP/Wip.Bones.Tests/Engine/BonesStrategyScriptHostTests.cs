using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Engine;

public sealed class BonesStrategyScriptHostTests
{
    private const string ChecklistItem =
        BonesRequirementsChecklistItems.ScriptHost;

    private const string ScriptStrategiesCoverageItem =
        BonesScriptStrategiesRequirementsChecklistItems.TestsCoverage;

    private const string CacheChecklistItem =
        BonesRequirementsChecklistItems.CompiledStrategyCache;

    private static readonly BonesStrategyId DefaultStrategyId = new("test-strategy-v1");

    private const string ValidScriptSource =
        """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[0];
            }
        }
        """;

    private const string LastLegalMoveScriptSource =
        """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[^1];
            }
        }
        """;

    private const string DisallowedIoScriptSource =
        """
        using System.IO;
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                _ = File.ReadAllText("secrets.txt");
                return legalMoves[0];
            }
        }
        """;

    private const string InfiniteLoopScriptSource =
        """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                while (true)
                {
                }
            }
        }
        """;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", ScriptStrategiesCoverageItem)]
    public void BonesStrategyScriptHost_GivenValidIbonesPlayerSlotSource_ExpectedCompilesAndReturnsInstance()
    {
        var host = new BonesStrategyScriptHost();

        var result = host.TryCompile(DefaultStrategyId, ValidScriptSource);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.NotNull(result.Compiled);
        Assert.NotNull(result.Compiled.PlayerSlot);
        Assert.Equal(DefaultStrategyId, result.Compiled.StrategyId);
        Assert.False(string.IsNullOrWhiteSpace(result.Compiled.SourceHash));
        Assert.Null(result.FailureReason);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", ScriptStrategiesCoverageItem)]
    public void BonesStrategyScriptHost_GivenSourceWithDisallowedNamespace_ExpectedCompileFailure()
    {
        var host = new BonesStrategyScriptHost();

        var result = host.TryCompile(DefaultStrategyId, DisallowedIoScriptSource);

        Assert.False(result.Succeeded);
        Assert.Null(result.Compiled);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("System.IO", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptHost_GivenValidCompiledScriptAndLegalMoves_ExpectedReturnsMoveFromLegalSet()
    {
        var host = new BonesStrategyScriptHost();
        var compileResult = host.TryCompile(DefaultStrategyId, LastLegalMoveScriptSource);
        Assert.True(compileResult.Succeeded, compileResult.FailureReason);
        Assert.NotNull(compileResult.Compiled);

        var state = CreateMidgameState();
        var legalMoves = CreateLegalMoves();

        var selectedMove = host.ExecuteChooseMove(compileResult.Compiled, state, legalMoves);

        Assert.Contains(selectedMove, legalMoves);
        Assert.Equal(legalMoves[^1], selectedMove);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptHost_GivenScriptExceedingExecutionTimeout_ExpectedAbortsWithoutMutatingEngineState()
    {
        var host = new BonesStrategyScriptHost(new BonesStrategyScriptHostOptions
        {
            CompilationTimeout = TimeSpan.FromSeconds(10),
            ExecutionTimeout = TimeSpan.FromMilliseconds(250),
        });

        var compileResult = host.TryCompile(DefaultStrategyId, InfiniteLoopScriptSource);
        Assert.True(compileResult.Succeeded, compileResult.FailureReason);
        Assert.NotNull(compileResult.Compiled);

        var state = CreateMidgameState();
        var legalMoves = CreateLegalMoves();
        var stateSnapshot = state;

        var exception = Assert.Throws<BonesStrategyScriptExecutionTimeoutException>(
            () => host.ExecuteChooseMove(compileResult.Compiled, state, legalMoves));

        Assert.Equal(TimeSpan.FromMilliseconds(250), exception.Timeout);
        Assert.Equal(stateSnapshot, state);
        Assert.Equal(stateSnapshot.Board, state.Board);
        Assert.Equal(stateSnapshot.Hands, state.Hands);
        Assert.Equal(stateSnapshot.EventLog, state.EventLog);
    }

    [Fact]
    [Trait("ChecklistItem", CacheChecklistItem)]
    public void BonesStrategyScriptHost_GivenSameSourceHash_ExpectedReusesCompiledStrategyCache()
    {
        var host = new BonesStrategyScriptHost();

        var firstResult = host.TryCompile(DefaultStrategyId, ValidScriptSource);
        var secondResult = host.TryCompile(DefaultStrategyId, ValidScriptSource);

        Assert.True(firstResult.Succeeded, firstResult.FailureReason);
        Assert.True(secondResult.Succeeded);
        Assert.NotNull(firstResult.Compiled);
        Assert.NotNull(secondResult.Compiled);
        Assert.Same(firstResult.Compiled, secondResult.Compiled);
        Assert.Equal(DefaultStrategyId, secondResult.Compiled.StrategyId);
        Assert.Equal(firstResult.Compiled.SourceHash, secondResult.Compiled.SourceHash);
        Assert.Equal(1, host.CompileInvocationCount);
    }

    [Fact]
    [Trait("ChecklistItem", CacheChecklistItem)]
    public void BonesStrategyScriptHost_GivenDifferentStrategyIdsWithSameSource_ExpectedSeparateCacheEntries()
    {
        var host = new BonesStrategyScriptHost();
        var firstStrategyId = new BonesStrategyId("seat-2-script-v1");
        var secondStrategyId = new BonesStrategyId("seat-3-script-v1");

        var firstResult = host.TryCompile(firstStrategyId, ValidScriptSource);
        var secondResult = host.TryCompile(secondStrategyId, ValidScriptSource);

        Assert.True(firstResult.Succeeded, firstResult.FailureReason);
        Assert.True(secondResult.Succeeded);
        Assert.NotNull(firstResult.Compiled);
        Assert.NotNull(secondResult.Compiled);
        Assert.NotSame(firstResult.Compiled, secondResult.Compiled);
        Assert.Equal(firstStrategyId, firstResult.Compiled.StrategyId);
        Assert.Equal(secondStrategyId, secondResult.Compiled.StrategyId);
        Assert.Equal(firstResult.Compiled.SourceHash, secondResult.Compiled.SourceHash);
        Assert.Equal(2, host.CompileInvocationCount);
    }

    private static BonesRoundState CreateMidgameState() =>
        new(
            new BonesGameId("script-host-test"),
            BonesBoardChainBuilder.FromLegacyCanonicalChain([new BonesTile(new(3), new(3))]),
            new BonesHands(new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(3), new(5))]),
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(0), new(1))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            }.ToImmutableDictionary()),
            new BonesPlayerId(2),
            new BonesTile(new(3), new(3)),
            passStreak: 0,
            eventLog: []);

    private static IReadOnlyList<BonesMove> CreateLegalMoves() =>
    [
        BonesMove.Play(new BonesMoveId("move-1"), new BonesPlayerId(2), new BonesTile(new(3), new(4)), BonesBoardSide.Right),
        BonesMove.Play(new BonesMoveId("move-2"), new BonesPlayerId(2), new BonesTile(new(3), new(4)), BonesBoardSide.Left),
    ];
}
