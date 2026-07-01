using Microsoft.Extensions.Logging;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Host;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesLearningLoopHostObservabilityTests
{
    private const string ChecklistItemE1 =
        "Replace empty `catch {}` blocks in `TryReadLastPromotionOutcomeAsync`, `TryReadAllSeatPromotionOutcomesAsync`, and `BonesLearningLoopStageMonitor.MonitorAsync` with `ILogger.LogWarning` calls that include the exception message and artifact path so corruption is never silent. [mandatory — observability]";

    /// <summary>
    /// Simple in-memory log entry collector.
    /// </summary>
    private sealed class LogCollector : ILoggerProvider, ILogger
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }

        void IDisposable.Dispose() { }
    }

    public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    /// <summary>
    /// Fake IArtifactStore that returns preconfigured descriptors.
    /// File I/O happens against the real filesystem.
    /// </summary>
    private sealed class FakeArtifactStore : IArtifactStore
    {
        private readonly IReadOnlyList<ArtifactDescriptor> _descriptors;

        public FakeArtifactStore(IReadOnlyList<ArtifactDescriptor> descriptors)
        {
            _descriptors = descriptors;
        }

        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(
            SessionId sessionId,
            CancellationToken cancellationToken)
        {
            return new ValueTask<IReadOnlyList<ArtifactDescriptor>>(_descriptors);
        }

        public ValueTask<ArtifactDescriptor> SaveAsync(
            SessionId sessionId,
            ArtifactContent artifact,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("FakeArtifactStore does not support SaveAsync.");
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItemE1)]
    public async Task TryReadLastPromotionOutcomeAsync_GivenCorruptJson_LogsWarningAndReturnsNull()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var artifactRelativePath = "sessions/s1/artifacts/bones-strategy-promotion-abc.json";
        // Production code uses Path.GetFullPath on DataDirectory, so match that.
        var fullArtifactPath = Path.Combine(Path.GetFullPath(tempDir.Path), artifactRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullArtifactPath)!);

        // Write corrupt JSON
        await File.WriteAllTextAsync(fullArtifactPath, "{ this is not valid json");

        var descriptor = new ArtifactDescriptor(
            new ArtifactId("bones-strategy-promotion-abc"),
            new SessionId("s1"),
            ArtifactKind.Json,
            artifactRelativePath,
            "test",
            "1.0.0",
            DateTimeOffset.UtcNow);

        var logCollector = new LogCollector();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logCollector));
        var logger = loggerFactory.CreateLogger<BonesLearningLoopHost>();
        var fakeStore = new FakeArtifactStore(new[] { descriptor });

        var host = (BonesLearningLoopHost)System.Runtime.CompilerServices
            .RuntimeHelpers.GetUninitializedObject(typeof(BonesLearningLoopHost));

        SetPrivateField(host, "_options", new BonesHostOptions { DataDirectory = tempDir.Path });
        SetPrivateField(host, "_artifactStore", fakeStore);
        SetPrivateField(host, "_logger", logger);

        // Act
        var result = await InvokePrivateAsync<string?>(host, "TryReadLastPromotionOutcomeAsync",
            new SessionId("s1"), CancellationToken.None);

        // Assert
        Assert.Null(result);

        var warnings = logCollector.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Single(warnings);
        Assert.Contains("Failed to read promotion outcome", warnings[0].Message);
        // The message includes the artifact path — verify a file-system path separator is present
        Assert.Contains(Path.DirectorySeparatorChar.ToString(), warnings[0].Message);
        Assert.NotNull(warnings[0].Exception);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(warnings[0].Exception);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItemE1)]
    public async Task TryReadAllSeatPromotionOutcomesAsync_GivenCorruptJsonInOneDescriptor_LogsWarningAndSkipsDescriptor()
    {
        // Arrange
        using var tempDir = new TempDirectory();

        // Descriptor 1: corrupt JSON
        var corruptRelativePath = "sessions/s1/artifacts/bones-strategy-promotion-corrupt.json";
        var corruptFullPath = Path.Combine(Path.GetFullPath(tempDir.Path), corruptRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(corruptFullPath)!);
        await File.WriteAllTextAsync(corruptFullPath, "{ not valid }");

        // Descriptor 2: valid JSON with Seat property
        var validRelativePath = "sessions/s1/artifacts/bones-strategy-promotion-valid.json";
        var validFullPath = Path.Combine(Path.GetFullPath(tempDir.Path), validRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(validFullPath)!);
        await File.WriteAllTextAsync(validFullPath, """{"Seat": 1, "Outcome": "Promoted"}""");

        var descriptors = new ArtifactDescriptor[]
        {
            new(new ArtifactId("corrupt"), new SessionId("s1"), ArtifactKind.Json,
                corruptRelativePath, "test", "1.0.0", DateTimeOffset.UtcNow),
            new(new ArtifactId("valid"), new SessionId("s1"), ArtifactKind.Json,
                validRelativePath, "test", "1.0.0", DateTimeOffset.UtcNow),
        };

        var logCollector = new LogCollector();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logCollector));
        var logger = loggerFactory.CreateLogger<BonesLearningLoopHost>();
        var fakeStore = new FakeArtifactStore(descriptors);

        var host = (BonesLearningLoopHost)System.Runtime.CompilerServices
            .RuntimeHelpers.GetUninitializedObject(typeof(BonesLearningLoopHost));

        SetPrivateField(host, "_options", new BonesHostOptions { DataDirectory = tempDir.Path });
        SetPrivateField(host, "_artifactStore", fakeStore);
        SetPrivateField(host, "_logger", logger);

        // Act
        var result = await InvokePrivateAsync<IReadOnlyList<BonesSeatPromotionOutcome>>(
            host, "TryReadAllSeatPromotionOutcomesAsync",
            new SessionId("s1"), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(1, result[0].Seat);
        Assert.Equal("Promoted", result[0].Outcome);

        var warnings = logCollector.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Single(warnings);
        Assert.Contains("Failed to read promotion outcome", warnings[0].Message);
        Assert.Contains(Path.DirectorySeparatorChar.ToString(), warnings[0].Message);
        Assert.NotNull(warnings[0].Exception);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(warnings[0].Exception);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItemE1)]
    public async Task MonitorAsync_GivenListAsyncThrows_LogsWarningAndContinuesPolling()
    {
        // Arrange
        var logCollector = new LogCollector();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logCollector));
        var logger = loggerFactory.CreateLogger("BonesLearningLoopStageMonitor");

        var state = new BonesLoopState();
        var throwingStore = new ThrowingArtifactStore();
        using var cts = new CancellationTokenSource();

        // Act: start MonitorAsync and let it attempt one poll cycle
        var monitorTask = BonesLearningLoopStageMonitor.MonitorAsync(
            throwingStore,
            new SessionId("s1"),
            iterationNumber: 1,
            workflowSessionId: null,
            matchId: null,
            state,
            cts.Token,
            logger);

        // Wait for polling to execute and catch the transient error
        await Task.Delay(200, CancellationToken.None);

        // Cancel and wait for graceful exit
        cts.Cancel();
        try
        {
            await monitorTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        var warnings = logCollector.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.NotEmpty(warnings);
        Assert.Contains("Transient error listing artifacts", warnings[0].Message);
        Assert.Contains("s1", warnings[0].Message);
        Assert.NotNull(warnings[0].Exception);
    }

    private sealed class ThrowingArtifactStore : IArtifactStore
    {
        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(
            SessionId sessionId,
            CancellationToken cancellationToken)
        {
            throw new IOException("Simulated transient IO error.");
        }

        public ValueTask<ArtifactDescriptor> SaveAsync(
            SessionId sessionId,
            ArtifactContent artifact,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static async Task<T?> InvokePrivateAsync<T>(
        object target,
        string methodName,
        params object?[] parameters)
    {
        var method = target.GetType().GetMethod(methodName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var result = method!.Invoke(target, parameters);
        if (result is Task<T?> typedTask)
            return await typedTask;

        if (result is Task task)
        {
            await task;
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty is not null ? (T?)resultProperty.GetValue(task) : default;
        }

        throw new InvalidOperationException(
            $"Method {methodName} did not return a Task or Task<T>.");
    }
}

/// <summary>
/// Opaque temp directory that cleans up on dispose.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"bones-obs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
