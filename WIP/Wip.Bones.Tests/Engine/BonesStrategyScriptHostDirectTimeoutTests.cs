using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Engine;

public sealed class BonesStrategyScriptHostDirectTimeoutTests
{
    private static readonly BonesStrategyId DefaultStrategyId = new("direct-timeout-test-v1");

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

    private const string ThrowingScriptSource =
        """
        using System;
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                throw new InvalidOperationException("script-boom");
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

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.ScriptHost)]
    public void ExecuteChooseMoveDirect_GivenInfiniteLoopScriptAndDirectTimeoutEnabled_ExpectedThrowsTimeoutWithinBoundedDeadline()
    {
        var host = new BonesStrategyScriptHost(new BonesStrategyScriptHostOptions
        {
            CompilationTimeout = TimeSpan.FromSeconds(10),
            ExecutionTimeout = TimeSpan.FromMilliseconds(200),
            EnableDirectExecutionTimeout = true,
        });

        var compileResult = host.TryCompile(DefaultStrategyId, InfiniteLoopScriptSource);
        Assert.True(compileResult.Succeeded, compileResult.FailureReason);
        Assert.NotNull(compileResult.Compiled);

        var state = CreateMidgameState();
        var legalMoves = CreateLegalMoves();

        BonesStrategyScriptExecutionTimeoutException? observedTimeout = null;

        var invocation = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    _ = host.ExecuteChooseMoveDirect(compileResult.Compiled, state, legalMoves);
                }
                catch (BonesStrategyScriptExecutionTimeoutException exception)
                {
                    observedTimeout = exception;
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        var returnedWithinDeadline = invocation.Wait(TimeSpan.FromSeconds(5));

        Assert.True(
            returnedWithinDeadline,
            "ExecuteChooseMoveDirect did not return within 5 s; the direct script path is not bounded by ExecutionTimeout.");
        Assert.NotNull(observedTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(200), observedTimeout.Timeout);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.ScriptHost)]
    public void ExecuteChooseMoveDirect_GivenScriptThatThrowsAndDirectTimeoutEnabled_ExpectedRethrowsOriginalException()
    {
        var host = new BonesStrategyScriptHost(new BonesStrategyScriptHostOptions
        {
            CompilationTimeout = TimeSpan.FromSeconds(10),
            ExecutionTimeout = TimeSpan.FromMilliseconds(200),
            EnableDirectExecutionTimeout = true,
        });

        var compileResult = host.TryCompile(DefaultStrategyId, ThrowingScriptSource);
        Assert.True(compileResult.Succeeded, compileResult.FailureReason);
        Assert.NotNull(compileResult.Compiled);

        var exception = Assert.Throws<InvalidOperationException>(
            () => host.ExecuteChooseMoveDirect(compileResult.Compiled, CreateMidgameState(), CreateLegalMoves()));

        Assert.Equal("script-boom", exception.Message);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.ScriptHost)]
    public void ExecuteChooseMoveDirect_GivenDefaultOptions_ExpectedReturnsMoveWithoutDirectTimeout()
    {
        var host = new BonesStrategyScriptHost();
        var compileResult = host.TryCompile(DefaultStrategyId, LastLegalMoveScriptSource);
        Assert.True(compileResult.Succeeded, compileResult.FailureReason);
        Assert.NotNull(compileResult.Compiled);

        var legalMoves = CreateLegalMoves();
        var selectedMove = host.ExecuteChooseMoveDirect(compileResult.Compiled, CreateMidgameState(), legalMoves);

        Assert.Equal(legalMoves[^1], selectedMove);
    }

    private static BonesRoundState CreateMidgameState() =>
        new(
            new BonesGameId("script-host-direct-timeout-test"),
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
