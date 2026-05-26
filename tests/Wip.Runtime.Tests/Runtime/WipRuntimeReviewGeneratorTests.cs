using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class WipRuntimeReviewGeneratorTests
{
    [Fact]
    public async Task ReviewAsync_GivenCurrentDiffAndValidation_WritesMarkdownReportWithDiffSummaryChangedFilesAndValidationStatus()
    {
        var artifactStore = new InMemoryArtifactStore();
        var generator = new WipRuntimeReviewGenerator(artifactStore);
        var request = new ReviewRequest(
            SessionId: new SessionId("session-review"),
            CurrentDiffHash: "abc123",
            ChangedFiles: ["src/Feature.cs", "tests/FeatureTests.cs"],
            Validation: new ReviewValidationStatus(
                BuildSucceeded: true,
                TestSucceeded: true,
                DiffHash: "abc123"));

        var result = await generator.ReviewAsync(request, CancellationToken.None);

        Assert.False(result.Staleness.IsStale);
        Assert.Equal(ArtifactKind.Markdown, result.ReportArtifact.Kind);
        Assert.Contains("Validation: Passed", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("Current diff hash: abc123", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("- src/Feature.cs", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("- tests/FeatureTests.cs", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("Stale: No", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewAsync_GivenValidationDiffHashMismatch_MarksReviewAsStaleWithDeterministicReason()
    {
        var artifactStore = new InMemoryArtifactStore();
        var generator = new WipRuntimeReviewGenerator(artifactStore);
        var request = new ReviewRequest(
            SessionId: new SessionId("session-stale-review"),
            CurrentDiffHash: "newhash",
            ChangedFiles: ["src/Feature.cs"],
            Validation: new ReviewValidationStatus(
                BuildSucceeded: true,
                TestSucceeded: true,
                DiffHash: "oldhash"));

        var result = await generator.ReviewAsync(request, CancellationToken.None);

        Assert.True(result.Staleness.IsStale);
        Assert.Contains("does not match", result.Staleness.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stale: Yes", result.Markdown, StringComparison.Ordinal);
    }

    private sealed class InMemoryArtifactStore : IArtifactStore
    {
        private readonly List<ArtifactDescriptor> _descriptors = [];

        public ValueTask<ArtifactDescriptor> SaveAsync(SessionId sessionId, ArtifactContent artifact, CancellationToken cancellationToken)
        {
            var descriptor = new ArtifactDescriptor(
                artifact.ArtifactId,
                sessionId,
                artifact.Kind,
                relativePath: $".wip/artifacts/{sessionId.Value}/{artifact.FileName}.md",
                artifact.ProducerType,
                artifact.ProducerVersion,
                artifact.ProducedAtUtc);

            _descriptors.Add(descriptor);
            return ValueTask.FromResult(descriptor);
        }

        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>(
                _descriptors.Where(x => x.SessionId.Equals(sessionId)).ToArray());
    }
}