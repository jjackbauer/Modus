using Microsoft.Extensions.Logging;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

/// <summary>
/// Serialized collection: the isolation assertion compares the host AppDomain before and after an
/// execution, so no other test may load an assembly during the observation window.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BonesScriptHostRuntimeCollection
{
    public const string Name = "BonesScriptHostRuntime";
}

/// <summary>
/// WP-1.6: the ScriptHost flag switches the host execution mode, keeps InProcess as the safe
/// default, logs the trust boundary, and fails closed for Worker without a worker or trust.
/// </summary>
[Collection(BonesScriptHostRuntimeCollection.Name)]
public sealed class BonesScriptHostRuntimeTests
{
    private static readonly BonesPlayerId ActivePlayer = new(2);

    private static readonly BonesMove[] LegalMoves =
    [
        BonesMove.Play(new BonesMoveId("legal-right"), ActivePlayer, new BonesTile(new(3), new(4)), BonesBoardSide.Right),
        BonesMove.Play(new BonesMoveId("legal-left"), ActivePlayer, new BonesTile(new(3), new(5)), BonesBoardSide.Left),
    ];

    private const string SimpleScriptSource =
        """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[1];
            }
        }
        """;

    [Fact]
    public void BonesScriptHostRuntime_GivenInProcessMode_ExpectedWarningAndWorkingInProcessHost()
    {
        var logger = new RecordingLoggerFactory();
        var runtime = BonesScriptHostRuntime.Create(new BonesStrategyScriptHostOptions(), logger);

        try
        {
            Assert.Equal(BonesScriptHostMode.InProcess, runtime.Mode);
            Assert.Null(runtime.WorkerClient);

            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Warning
                    && entry.Message.Contains("InProcess", StringComparison.Ordinal));

            var compiled = runtime.Host.TryCompile(new BonesStrategyId("host-inprocess-mode"), SimpleScriptSource);

            Assert.True(compiled.Succeeded, compiled.FailureReason);
            Assert.Equal(1, runtime.Host.CompileInvocationCount);
        }
        finally
        {
            runtime.Dispose();
        }
    }

    [Fact]
    public void BonesScriptHostRuntime_GivenWorkerModeWithoutTrustedOnly_ExpectedFailsClosedBeforeStartingAWorker()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BonesScriptHostRuntime.Create(
            new BonesStrategyScriptHostOptions
            {
                Mode = BonesScriptHostMode.Worker,
                TrustedOnly = false,
            }));

        Assert.Contains("trusted-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BonesScriptHostRuntime_GivenWorkerModeWithMissingWorker_ExpectedFailsClosedWithThePath()
    {
        var missingWorker = Path.Combine(
            Path.GetTempPath(),
            "wip-missing-worker-" + Guid.NewGuid().ToString("N") + ".dll");

        var exception = Assert.Throws<InvalidOperationException>(() => BonesScriptHostRuntime.Create(
            new BonesStrategyScriptHostOptions
            {
                Mode = BonesScriptHostMode.Worker,
                TrustedOnly = true,
                WorkerPath = missingWorker,
            }));

        Assert.Contains(missingWorker, exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(missingWorker));
    }

    [Fact]
    public async Task BonesScriptHostRuntime_GivenWorkerModeWithBuiltWorker_ExpectedStrategiesExecutedInWorker()
    {
        var workerPath = Path.Combine(
            RepositoryRoot,
            "WIP",
            "Wip.Sandbox.Worker",
            "bin",
            "Release",
            "net10.0",
            "Wip.Sandbox.Worker.dll");
        Assert.True(File.Exists(workerPath), $"The sandbox worker must be built in Release; not found at {workerPath}.");

        var assembliesBefore = LoadedAssemblyNames();
        var runtime = BonesScriptHostRuntime.Create(new BonesStrategyScriptHostOptions
        {
            Mode = BonesScriptHostMode.Worker,
            TrustedOnly = true,
            WorkerPath = workerPath,
            ExecutionTimeout = TimeSpan.FromSeconds(10),
        });

        try
        {
            Assert.Equal(BonesScriptHostMode.Worker, runtime.Mode);
            Assert.NotNull(runtime.WorkerClient);

            var compiled = runtime.Host.TryCompile(new BonesStrategyId("host-worker-mode"), SimpleScriptSource);
            Assert.True(compiled.Succeeded, compiled.FailureReason);
            Assert.NotNull(compiled.Compiled);

            var move = runtime.Host.ExecuteChooseMoveDirect(compiled.Compiled!, CreateState(), LegalMoves);
            Assert.Equal(LegalMoves[1], move);

            var addedAssemblies = LoadedAssemblyNames()
                .Except(assembliesBefore, StringComparer.Ordinal)
                .ToArray();
            Assert.DoesNotContain(
                addedAssemblies,
                name => name.StartsWith("BonesStrategyScript_", StringComparison.Ordinal));
            Assert.Equal("WorkerBonesPlayerSlot", compiled.Compiled.PlayerSlot.GetType().Name);
            Assert.True(runtime.WorkerClient!.IsRunning);
        }
        finally
        {
            await runtime.DisposeAsync();
        }
    }

    private static BonesRoundState CreateState() =>
        new(
            new BonesGameId("host-worker-mode"),
            BonesBoardChainBuilder.FromLegacyCanonicalChain([new BonesTile(new(3), new(3))]),
            new BonesHands(new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(1))]),
                [ActivePlayer] = new([new BonesTile(new(3), new(4)), new BonesTile(new(3), new(5))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(3))]),
            }),
            ActivePlayer,
            new BonesTile(new(3), new(3)),
            0,
            []);

    private static HashSet<string> LoadedAssemblyNames() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

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

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Add((logLevel, formatter(state, exception)));
        }
    }
}