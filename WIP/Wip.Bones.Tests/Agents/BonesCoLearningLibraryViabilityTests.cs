using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesCoLearningLibraryViabilityTests
{
    private const string ChecklistItemT1_3 = "T1.3: IsLibraryEntryViable accepts Markdown entries with matchesPlayed >= 1";

    [Fact]
    [Trait("ChecklistItem", ChecklistItemT1_3)]
    public async Task IsLibraryEntryViable_GivenScriptEntryWithMatchesPlayed_ReturnsTrue()
    {
        using var fixture = CreateFixture(
            seat: 2,
            kind: BonesStrategyKind.Script,
            matchesPlayed: 3,
            wins: 2);

        var completedSeats = await RunCoLearningPonderAndGetProcessedSeatsAsync(fixture);

        Assert.DoesNotContain(2, completedSeats);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItemT1_3)]
    public async Task IsLibraryEntryViable_GivenMarkdownEntryWithMatchesPlayed_ReturnsTrue()
    {
        using var fixture = CreateFixture(
            seat: 2,
            kind: BonesStrategyKind.Markdown,
            matchesPlayed: 3,
            wins: 2);

        var completedSeats = await RunCoLearningPonderAndGetProcessedSeatsAsync(fixture);

        Assert.DoesNotContain(2, completedSeats);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItemT1_3)]
    public async Task IsLibraryEntryViable_GivenMarkdownEntryWithZeroMatches_ReturnsFalse()
    {
        using var fixture = CreateFixture(
            seat: 2,
            kind: BonesStrategyKind.Markdown,
            matchesPlayed: 0,
            wins: 0);

        var completedSeats = await RunCoLearningPonderAndGetProcessedSeatsAsync(fixture);

        Assert.Contains(2, completedSeats);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItemT1_3)]
    public async Task IsLibraryEntryViable_GivenEmptyLibrary_ReturnsFalse()
    {
        using var fixture = CreateEmptyFixture();

        var completedSeats = await RunCoLearningPonderAndGetProcessedSeatsAsync(fixture);

        Assert.Equal(4, completedSeats.Count);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItemT1_3)]
    public async Task IsLibraryEntryViable_GivenNullLibrary_ReturnsFalse()
    {
        using var fixture = CreateNullLibraryFixture();

        var completedSeats = await RunCoLearningPonderAndGetProcessedSeatsAsync(fixture);

        Assert.Equal(4, completedSeats.Count);
        Assert.Contains(1, completedSeats);
        Assert.Contains(2, completedSeats);
        Assert.Contains(3, completedSeats);
        Assert.Contains(4, completedSeats);
    }

    private static async Task<IReadOnlyList<int>> RunCoLearningPonderAndGetProcessedSeatsAsync(
        ViabilityFixture fixture)
    {
        var request = new BonesPonderRequest(
            PlayerId: new BonesPlayerId(1),
            RepositoryPath: fixture.LibraryDir,
            AllSeatsLearning: true);

        var context = new CapabilityContext(
            new SessionId($"bones-viability-{Guid.NewGuid():N}"),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        // The co-learning ponder agent loops seats 1-4 calling the single-seat agent.
        // The single-seat agent records each processed seat via the model provider stub.
        await fixture.CoLearningAgent.ExecuteAsync(request, context, CancellationToken.None);

        return fixture.TrackingModelProvider.ProcessedSeats;
    }

    private static ViabilityFixture CreateFixture(
        int seat,
        BonesStrategyKind kind,
        int matchesPlayed,
        int wins)
    {
        var libraryDir = Path.Combine(Path.GetTempPath(), $"bones-viability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(libraryDir);

        var library = new BonesStrategyLibrary(libraryDir);
        var playerId = new BonesPlayerId(seat);
        var strategyId = new BonesStrategyId($"seat-{seat}-v1");
        var artifact = new BonesStrategyArtifact(
            strategyId,
            playerId,
            kind,
            kind == BonesStrategyKind.Script
                ? "public sealed class SeatStrategy : IBonesPlayerSlot { /* ... */ }"
                : "# Markdown Strategy",
            BonesPromotionStatus.Active);
        var metrics = new BonesStrategyEffectivenessRecord(
            strategyId,
            playerId,
            matchesPlayed,
            wins,
            matchesPlayed - wins,
            cumulativeScoreDifferential: 0);

        library.UpsertBestStrategy(playerId, artifact, metrics);

        return BuildFixture(libraryDir, library);
    }

    private static ViabilityFixture CreateEmptyFixture()
    {
        var libraryDir = Path.Combine(Path.GetTempPath(), $"bones-viability-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(libraryDir);
        var library = new BonesStrategyLibrary(libraryDir);
        return BuildFixture(libraryDir, library);
    }

    private static ViabilityFixture CreateNullLibraryFixture()
    {
        var libraryDir = Path.Combine(Path.GetTempPath(), $"bones-viability-null-{Guid.NewGuid():N}");
        Directory.CreateDirectory(libraryDir);

        var artifactStore = new StubArtifactStore();
        var trackingProvider = new TrackingModelProvider();
        var singleSeatAgent = new BonesPonderAgent(
            artifactStore,
            trackingProvider,
            scriptHost: null,
            compileRetryOptions: null);
        var coLearningAgent = new BonesCoLearningPonderAgent(
            singleSeatAgent,
            strategyLibrary: null,
            artifactStore: artifactStore);

        return new ViabilityFixture(libraryDir, trackingProvider, coLearningAgent);
    }

    private static ViabilityFixture BuildFixture(
        string libraryDir,
        BonesStrategyLibrary library)
    {
        var artifactStore = new StubArtifactStore();
        var trackingProvider = new TrackingModelProvider();
        var singleSeatAgent = new BonesPonderAgent(
            artifactStore,
            trackingProvider,
            scriptHost: null,
            compileRetryOptions: null);
        var coLearningAgent = new BonesCoLearningPonderAgent(
            singleSeatAgent,
            strategyLibrary: library,
            artifactStore: artifactStore);

        return new ViabilityFixture(libraryDir, trackingProvider, coLearningAgent);
    }

    private sealed class ViabilityFixture : IDisposable
    {
        private readonly string _libraryDir;

        public ViabilityFixture(
            string libraryDir,
            TrackingModelProvider trackingModelProvider,
            BonesCoLearningPonderAgent coLearningAgent)
        {
            _libraryDir = libraryDir;
            TrackingModelProvider = trackingModelProvider;
            CoLearningAgent = coLearningAgent;
        }

        public string LibraryDir => _libraryDir;
        public TrackingModelProvider TrackingModelProvider { get; }
        public BonesCoLearningPonderAgent CoLearningAgent { get; }

        public void Dispose()
        {
            if (Directory.Exists(_libraryDir))
                Directory.Delete(_libraryDir, recursive: true);
        }
    }

    private sealed class TrackingModelProvider
        : IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>
    {
        private readonly HashSet<int> _processedSeats = [];

        public IReadOnlyList<int> ProcessedSeats => _processedSeats.ToList();

        public async ValueTask<ModelProviderResponse<BonesStrategyAuthoringResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyAuthoringRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask;

            // Extract seat from correlation id pattern "bones-ponder-seat-{seat}-..."
            if (request.CorrelationId is { } correlationId)
            {
                var prefix = "bones-ponder-seat-";
                var prefixIndex = correlationId.IndexOf(prefix, StringComparison.Ordinal);
                if (prefixIndex >= 0)
                {
                    var startIndex = prefixIndex + prefix.Length;
                    var dashIndex = correlationId.IndexOf('-', startIndex);
                    var seatStr = dashIndex >= 0
                        ? correlationId[startIndex..dashIndex]
                        : correlationId[startIndex..];

                    if (int.TryParse(seatStr, out var seat))
                        _processedSeats.Add(seat);
                }
            }

            return new ModelProviderResponse<BonesStrategyAuthoringResult>(
                new BonesStrategyAuthoringResult("```csharp\npublic sealed class SeatStrategy : IBonesPlayerSlot { public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves) { return legalMoves[0]; } }\n```"),
                "stub-provider",
                request.ModelId,
                usage: new ModelProviderUsage(10, 50),
                correlationId: request.CorrelationId);
        }
    }

    private sealed class StubArtifactStore : IArtifactStore
    {
        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(
            SessionId sessionId,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>(Array.Empty<ArtifactDescriptor>());
        }

        public ValueTask<ArtifactDescriptor> SaveAsync(
            SessionId sessionId,
            ArtifactContent content,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ArtifactDescriptor(
                new ArtifactId("stub-save"),
                sessionId,
                ArtifactKind.Json,
                "stub-save",
                "stub-producer",
                "1.0.0",
                DateTimeOffset.UtcNow));
        }
    }
}
