using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Sandbox.WorkerClient;
using Xunit;

namespace Wip.Bones.Tests.Engine;

/// <summary>
/// Serialized collection: the isolation tests compare the host AppDomain before and after an
/// execution, so no other test may load an assembly during the observation window.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BonesWorkerModeCollection
{
    public const string Name = "BonesWorkerMode";
}

/// <summary>
/// Starts one sandbox worker per test class. Every test in this class must keep the worker
/// healthy: the client kills the process when any protocol request returns an error.
/// </summary>
public sealed class BonesSandboxWorkerFixture : IAsyncLifetime
{
    public SandboxWorkerClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var worker = Path.Combine(
            BonesStrategyScriptHostWorkerModeTests.RepositoryRoot,
            "WIP",
            "Wip.Sandbox.Worker",
            "bin",
            "Release",
            "net10.0",
            "Wip.Sandbox.Worker.dll");

        Assert.True(
            File.Exists(worker),
            $"The sandbox worker must be built in Release before these tests run; not found at {worker}.");

        Client = SandboxWorkerClient.Start(worker, new SandboxWorkerOptions
        {
            StartupTimeout = TimeSpan.FromSeconds(20),
            CompileTimeout = TimeSpan.FromSeconds(20),
            MoveTimeout = TimeSpan.FromSeconds(10),
        });

        await Client.HandshakeAsync();
    }

    public async Task DisposeAsync() => await Client.DisposeAsync();
}

/// <summary>
/// WP-1.3 worker-mode coverage: board/hands/current player/pass streak round-trip, legal-move
/// selection, the public player-slot legality contract, and proof that the host process never
/// loads the model-authored assembly in Worker mode.
/// </summary>
[Collection(BonesWorkerModeCollection.Name)]
[Trait("ChecklistItem", BonesRequirementsChecklistItems.ScriptHost)]
public sealed class BonesStrategyScriptHostWorkerModeTests : IClassFixture<BonesSandboxWorkerFixture>
{
    private static readonly BonesPlayerId ActivePlayer = new(2);

    private static readonly BonesMove[] LegalMoves =
    [
        BonesMove.Play(new BonesMoveId("legal-right"), ActivePlayer, new BonesTile(new(3), new(4)), BonesBoardSide.Right),
        BonesMove.Play(new BonesMoveId("legal-left"), ActivePlayer, new BonesTile(new(3), new(5)), BonesBoardSide.Left),
    ];

    /// <summary>
    /// Selects the legal move at index (board tile count + current player hand size + pass streak)
    /// modulo the legal move count. Every component of the state therefore changes the selection.
    /// </summary>
    private const string StateAwareScriptSource =
        """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class StateAwareStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                var index = (state.Board.Tiles.Length
                    + state.Hands.GetHand(state.CurrentPlayer).Tiles.Length
                    + state.PassStreak) % legalMoves.Count;

                return legalMoves[index];
            }
        }
        """;

    private const string SimpleLegalMoveScriptSource =
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

    private const string IllegalMoveScriptSource =
        """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;
        using Wip.Bones.Identifiers;

        public sealed class IllegalMoveStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return new BonesMove(
                    new BonesMoveId("never-legal"),
                    state.CurrentPlayer,
                    new BonesTile(new BonesPipCount(3), new BonesPipCount(4)),
                    BonesBoardSide.Right);
            }
        }
        """;

    /// <summary>
    /// The host deny-list only rejects System.Diagnostics.Process, so this source reaches the
    /// worker, which rejects it on its own forbidden-fragment rule.
    /// </summary>
    private const string WorkerRejectedScriptSource =
        """
        using System.Diagnostics;
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class RejectedStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                _ = Stopwatch.StartNew();
                return legalMoves[0];
            }
        }
        """;

    private readonly BonesSandboxWorkerFixture _fixture;

    public BonesStrategyScriptHostWorkerModeTests(BonesSandboxWorkerFixture fixture)
    {
        _fixture = fixture;
    }

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    [Theory]
    [InlineData(1, 2, 0, 1)]
    [InlineData(2, 2, 0, 0)]
    [InlineData(1, 1, 0, 0)]
    [InlineData(1, 2, 1, 0)]
    public void BonesStrategyScriptHost_GivenWorkerModeStateDependentScript_ExpectedMoveSelectedFromSerializedState(
        int boardTileCount,
        int activeHandTileCount,
        int passStreak,
        int expectedLegalMoveIndex)
    {
        var strategyId = new BonesStrategyId($"worker-state-{boardTileCount}-{activeHandTileCount}-{passStreak}");
        var host = CreateWorkerHost();

        var result = host.TryCompile(strategyId, StateAwareScriptSource);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.NotNull(result.Compiled);

        var state = CreateState(boardTileCount, activeHandTileCount, passStreak);
        var move = host.ExecuteChooseMoveDirect(result.Compiled!, state, LegalMoves);

        Assert.Equal(LegalMoves[expectedLegalMoveIndex], move);
        Assert.Equal(ActivePlayer, move.PlayerId);
    }

    [Fact]
    public void BonesScriptStrategyPlayerSlot_GivenWorkerModeIllegalMove_ExpectedLegalFallbackAndWarningArtifact()
    {
        var strategyId = new BonesStrategyId("worker-illegal-move");
        var host = CreateWorkerHost();
        var result = host.TryCompile(strategyId, IllegalMoveScriptSource);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.NotNull(result.Compiled);

        var sink = new RecordingWarningSink();
        var slot = new BonesScriptStrategyPlayerSlot(host, result.Compiled!, sink);

        var move = slot.ChooseMove(CreateState(1, 2, 0), LegalMoves);

        Assert.Equal(LegalMoves[0], move);

        var warning = Assert.Single(sink.Warnings);
        Assert.Equal(strategyId, warning.StrategyId);
        Assert.Equal(
            new BonesMove(
                new BonesMoveId("never-legal"),
                ActivePlayer,
                new BonesTile(new BonesPipCount(3), new BonesPipCount(4)),
                BonesBoardSide.Right),
            warning.RejectedMove);
        Assert.Equal(LegalMoves[0], warning.AppliedMove);
        Assert.Equal(
            "Script selected a move that is not in the legal set. Falling back to first legal move.",
            warning.Message);
    }

    [Fact]
    public void BonesStrategyScriptHost_GivenWorkerMode_ExpectedHostLoadsNoModelAssembly()
    {
        var host = CreateWorkerHost();
        var assembliesBefore = LoadedAssemblyNames();

        var result = host.TryCompile(new BonesStrategyId("worker-no-model-load"), SimpleLegalMoveScriptSource);
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.NotNull(result.Compiled);

        var move = host.ExecuteChooseMoveDirect(result.Compiled!, CreateState(1, 2, 0), LegalMoves);
        Assert.Equal(LegalMoves[0], move);

        var addedAssemblies = LoadedAssemblyNames()
            .Except(assembliesBefore, StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(
            addedAssemblies,
            name => name.StartsWith("BonesStrategyScript_", StringComparison.Ordinal));

        // The only host code path that compiles and loads model code is the in-process one.
        Assert.Equal(0, host.CompileInvocationCount);

        // The cached slot must be the host-owned bridge to the worker, not model code.
        Assert.Equal(typeof(BonesStrategyScriptHost).Assembly, result.Compiled!.PlayerSlot.GetType().Assembly);
        Assert.Equal("WorkerBonesPlayerSlot", result.Compiled.PlayerSlot.GetType().Name);
    }

    [Fact]
    public void BonesStrategyScriptHost_GivenInProcessMode_ExpectedHostLoadsStrategyAssembly()
    {
        // Positive control: proves the AppDomain detector can observe a model assembly load,
        // so the Worker-mode absence assertion is falsifiable.
        var host = new BonesStrategyScriptHost();
        var assembliesBefore = LoadedAssemblyNames();

        var result = host.TryCompile(new BonesStrategyId("inprocess-positive-control"), SimpleLegalMoveScriptSource);
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.NotNull(result.Compiled);

        _ = host.ExecuteChooseMoveDirect(result.Compiled!, CreateState(1, 2, 0), LegalMoves);

        var addedAssemblies = LoadedAssemblyNames()
            .Except(assembliesBefore, StringComparer.Ordinal)
            .ToArray();

        Assert.Single(
            addedAssemblies,
            name => name.StartsWith("BonesStrategyScript_", StringComparison.Ordinal));
        Assert.Equal(1, host.CompileInvocationCount);
    }

    [Fact]
    public void BonesStrategyScriptHost_GivenWorkerMode_ExpectedCompileCacheReusesBridge()
    {
        var strategyId = new BonesStrategyId("worker-cache");
        var host = CreateWorkerHost();

        var first = host.TryCompile(strategyId, SimpleLegalMoveScriptSource);
        var second = host.TryCompile(strategyId, SimpleLegalMoveScriptSource);

        Assert.True(first.Succeeded, first.FailureReason);
        Assert.True(second.Succeeded, second.FailureReason);
        Assert.Same(first.Compiled, second.Compiled);
        Assert.Equal(1, host.CompiledScriptsCached);
    }

    [Fact]
    public async Task BonesStrategyScriptHost_GivenWorkerModeForbiddenSource_ExpectedCompileFailsClosed()
    {
        // The client kills the worker on any protocol error, so this test owns its client.
        var worker = Path.Combine(
            RepositoryRoot,
            "WIP",
            "Wip.Sandbox.Worker",
            "bin",
            "Release",
            "net10.0",
            "Wip.Sandbox.Worker.dll");

        var client = SandboxWorkerClient.Start(worker, new SandboxWorkerOptions
        {
            StartupTimeout = TimeSpan.FromSeconds(20),
            CompileTimeout = TimeSpan.FromSeconds(20),
            MoveTimeout = TimeSpan.FromSeconds(10),
        });

        try
        {
            await client.HandshakeAsync();
            var host = new BonesStrategyScriptHost(new BonesStrategyScriptHostOptions
            {
                Mode = BonesScriptHostMode.Worker,
                TrustedOnly = true,
                WorkerClient = client,
                CompilationTimeout = TimeSpan.FromSeconds(10),
                ExecutionTimeout = TimeSpan.FromSeconds(10),
            });

            var result = host.TryCompile(new BonesStrategyId("worker-forbidden"), WorkerRejectedScriptSource);

            Assert.False(result.Succeeded);
            Assert.Null(result.Compiled);
            Assert.Contains("forbidden", result.FailureReason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, host.CompileInvocationCount);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private BonesStrategyScriptHost CreateWorkerHost() =>
        new(new BonesStrategyScriptHostOptions
        {
            Mode = BonesScriptHostMode.Worker,
            TrustedOnly = true,
            WorkerClient = _fixture.Client,
            CompilationTimeout = TimeSpan.FromSeconds(10),
            ExecutionTimeout = TimeSpan.FromSeconds(10),
        });

    /// <summary>
    /// Builds a four-seat state where seats 1, 3 and 4 hold two tiles and seat 2 (the active
    /// player) holds <paramref name="activeHandTileCount"/> tiles, so selecting the wrong seat
    /// also changes the state-aware script's move index.
    /// </summary>
    private static BonesRoundState CreateState(int boardTileCount, int activeHandTileCount, int passStreak)
    {
        var boardTiles = boardTileCount == 1
            ? new[] { new BonesTile(new(3), new(3)) }
            : new[] { new BonesTile(new(3), new(3)), new BonesTile(new(3), new(4)) };

        var hands = new Dictionary<BonesPlayerId, BonesHand>
        {
            [new(1)] = new([new BonesTile(new(0), new(1)), new BonesTile(new(0), new(2))]),
            [ActivePlayer] = new(activeHandTileCount == 1
                ? [new BonesTile(new(3), new(5))]
                : [new BonesTile(new(3), new(5)), new BonesTile(new(3), new(6))]),
            [new(3)] = new([new BonesTile(new(1), new(2)), new BonesTile(new(1), new(3))]),
            [new(4)] = new([new BonesTile(new(2), new(2)), new BonesTile(new(2), new(3))]),
        };

        return new BonesRoundState(
            new BonesGameId($"worker-state-{boardTileCount}-{activeHandTileCount}-{passStreak}"),
            BonesBoardChainBuilder.FromLegacyCanonicalChain(boardTiles),
            new BonesHands(hands),
            ActivePlayer,
            new BonesTile(new(3), new(3)),
            passStreak,
            []);
    }

    private static HashSet<string> LoadedAssemblyNames() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wip.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class RecordingWarningSink : IBonesScriptStrategyExecutionWarningSink
    {
        public List<BonesScriptStrategyExecutionWarning> Warnings { get; } = [];

        public void Emit(BonesScriptStrategyExecutionWarning warning) => Warnings.Add(warning);
    }
}