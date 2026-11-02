using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Artifacts.Local;
using Xunit;

namespace Wip.Artifacts.Local.Tests.Artifacts;

public sealed class WipArtifactStoreLocalTests
{
    [Fact]
    public async Task SaveAsync_GivenRequiredArtifactTypes_ExpectedFilesPersistedUnderSessionArtifactLayout()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new WipArtifactStoreLocal(repositoryPath);
            var sessionId = new SessionId("session-artifacts");

            var jsonDescriptor = await store.SaveAsync(
                sessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId("validation-report"),
                    kind: ArtifactKind.Json,
                    fileName: "validation-report",
                    content: "{\"status\":\"ok\"}",
                    producerType: "Wip.Validation.DotNet",
                    producerVersion: "1.0.0",
                    producedAtUtc: new DateTimeOffset(2026, 05, 24, 8, 0, 0, TimeSpan.Zero)),
                CancellationToken.None);

            var markdownDescriptor = await store.SaveAsync(
                sessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId("review-summary"),
                    kind: ArtifactKind.Markdown,
                    fileName: "review-summary",
                    content: "# Review\nAll checks passed.",
                    producerType: "Wip.Runtime.Review",
                    producerVersion: "1.0.0",
                    producedAtUtc: new DateTimeOffset(2026, 05, 24, 8, 1, 0, TimeSpan.Zero)),
                CancellationToken.None);

            var patchDescriptor = await store.SaveAsync(
                sessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId("candidate-diff"),
                    kind: ArtifactKind.Patch,
                    fileName: "candidate-diff",
                    content: "diff --git a/file.txt b/file.txt\n+hello",
                    producerType: "Wip.Workspaces.Git",
                    producerVersion: "1.0.0",
                    producedAtUtc: new DateTimeOffset(2026, 05, 24, 8, 2, 0, TimeSpan.Zero)),
                CancellationToken.None);

            Assert.Equal(".wip/sessions/session-artifacts/artifacts/validation-report-validation-report.json", jsonDescriptor.RelativePath);
            Assert.Equal(".wip/sessions/session-artifacts/artifacts/review-summary-review-summary.md", markdownDescriptor.RelativePath);
            Assert.Equal(".wip/sessions/session-artifacts/artifacts/candidate-diff-candidate-diff.patch", patchDescriptor.RelativePath);

            var jsonPath = Path.Combine(repositoryPath, jsonDescriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var markdownPath = Path.Combine(repositoryPath, markdownDescriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var patchPath = Path.Combine(repositoryPath, patchDescriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var patchDescriptorPath = $"{patchPath}.descriptor.json";

            Assert.Equal("{\"status\":\"ok\"}", await File.ReadAllTextAsync(jsonPath, CancellationToken.None));
            Assert.Equal("# Review\nAll checks passed.", await File.ReadAllTextAsync(markdownPath, CancellationToken.None));
            Assert.Equal("diff --git a/file.txt b/file.txt\n+hello", await File.ReadAllTextAsync(patchPath, CancellationToken.None));
            Assert.True(File.Exists(patchDescriptorPath));

            var persistedDescriptorJson = await File.ReadAllTextAsync(patchDescriptorPath, CancellationToken.None);
            Assert.Contains("\"ArtifactId\":\"candidate-diff\"", persistedDescriptorJson, StringComparison.Ordinal);
            Assert.Contains("\"Kind\":\"Patch\"", persistedDescriptorJson, StringComparison.Ordinal);
            Assert.Contains("\"ProducerType\":\"Wip.Workspaces.Git\"", persistedDescriptorJson, StringComparison.Ordinal);
            Assert.Contains("\"ProducerCapabilityId\":\"Wip.Workspaces.Git\"", persistedDescriptorJson, StringComparison.Ordinal);
            Assert.Contains("\"FileName\":\"candidate-diff\"", persistedDescriptorJson, StringComparison.Ordinal);
            Assert.Contains("\"RelativePath\":\".wip/sessions/session-artifacts/artifacts/candidate-diff-candidate-diff.patch\"", persistedDescriptorJson, StringComparison.Ordinal);

            var listed = await store.ListAsync(sessionId, CancellationToken.None);
            Assert.Collection(
                listed,
                descriptor =>
                {
                    Assert.Equal("validation-report", descriptor.ArtifactId.Value);
                    Assert.Equal(ArtifactKind.Json, descriptor.Kind);
                },
                descriptor =>
                {
                    Assert.Equal("review-summary", descriptor.ArtifactId.Value);
                    Assert.Equal(ArtifactKind.Markdown, descriptor.Kind);
                },
                descriptor =>
                {
                    Assert.Equal("candidate-diff", descriptor.ArtifactId.Value);
                    Assert.Equal(ArtifactKind.Patch, descriptor.Kind);
                });
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task ListAsync_GivenMultipleArtifacts_ExpectedDescriptorsIncludeIdTypeProducerTimestampAndPath()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new WipArtifactStoreLocal(repositoryPath);
            var sessionId = new SessionId("session-metadata");
            var expectedTimestamp = new DateTimeOffset(2026, 05, 24, 10, 30, 0, TimeSpan.Zero);

            await store.SaveAsync(
                sessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId("review-evidence"),
                    kind: ArtifactKind.Markdown,
                    fileName: "review-evidence",
                    content: "# Review",
                    producerType: "Wip.Runtime.Review",
                    producerVersion: "2.1.0",
                    producedAtUtc: expectedTimestamp.AddMinutes(-1)),
                CancellationToken.None);

            await store.SaveAsync(
                sessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId("build-evidence"),
                    kind: ArtifactKind.Json,
                    fileName: "build-evidence",
                    content: "{\"build\":\"pass\"}",
                    producerType: "Wip.Validation.DotNet",
                    producerVersion: "2.1.0",
                    producedAtUtc: expectedTimestamp),
                CancellationToken.None);

            var descriptors = await store.ListAsync(sessionId, CancellationToken.None);
            Assert.Collection(
                descriptors,
                descriptor =>
                {
                    Assert.Equal("review-evidence", descriptor.ArtifactId.Value);
                    Assert.Equal(ArtifactKind.Markdown, descriptor.Kind);
                    Assert.Equal("Wip.Runtime.Review", descriptor.ProducerType);
                    Assert.Equal("2.1.0", descriptor.ProducerVersion);
                    Assert.Equal(expectedTimestamp.AddMinutes(-1), descriptor.ProducedAtUtc);
                    Assert.Equal(".wip/sessions/session-metadata/artifacts/review-evidence-review-evidence.md", descriptor.RelativePath);
                },
                descriptor =>
                {
                    Assert.Equal("build-evidence", descriptor.ArtifactId.Value);
                    Assert.Equal(ArtifactKind.Json, descriptor.Kind);
                    Assert.Equal("Wip.Validation.DotNet", descriptor.ProducerType);
                    Assert.Equal("2.1.0", descriptor.ProducerVersion);
                    Assert.Equal(expectedTimestamp, descriptor.ProducedAtUtc);
                    Assert.Equal(".wip/sessions/session-metadata/artifacts/build-evidence-build-evidence.json", descriptor.RelativePath);
                });
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_GivenFileNameWithDirectorySegments_ThrowsArgumentException()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new WipArtifactStoreLocal(repositoryPath);
            var sessionId = new SessionId("session-layout-guard");

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => store.SaveAsync(
                        sessionId,
                        new ArtifactContent(
                            artifactId: new ArtifactId("unsafe-artifact"),
                            kind: ArtifactKind.Json,
                            fileName: "..\\unsafe-artifact",
                            content: "{}",
                            producerType: "Wip.Runtime",
                            producerVersion: "1.0.0",
                            producedAtUtc: new DateTimeOffset(2026, 05, 24, 11, 0, 0, TimeSpan.Zero)),
                        CancellationToken.None)
                    .AsTask());

            Assert.Equal("fileName", exception.ParamName);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_GivenSameFileNameWithDifferentArtifactIds_PersistsDeterministicDistinctArtifactsWithMetadata()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new WipArtifactStoreLocal(repositoryPath);
            var sessionId = new SessionId("session-deterministic");

            var first = await store.SaveAsync(
                sessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId("report-a"),
                    kind: ArtifactKind.Json,
                    fileName: "validation-report",
                    content: "{\"artifact\":\"a\"}",
                    producerType: "validator.dotnet",
                    producerVersion: "1.2.3",
                    producedAtUtc: new DateTimeOffset(2026, 05, 30, 9, 0, 0, TimeSpan.Zero)),
                CancellationToken.None);

            var second = await store.SaveAsync(
                sessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId("report-b"),
                    kind: ArtifactKind.Json,
                    fileName: "validation-report",
                    content: "{\"artifact\":\"b\"}",
                    producerType: "validator.dotnet",
                    producerVersion: "1.2.4",
                    producedAtUtc: new DateTimeOffset(2026, 05, 30, 9, 1, 0, TimeSpan.Zero)),
                CancellationToken.None);

            Assert.NotEqual(first.RelativePath, second.RelativePath);

            var firstPath = Path.Combine(repositoryPath, first.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var secondPath = Path.Combine(repositoryPath, second.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.Equal("{\"artifact\":\"a\"}", await File.ReadAllTextAsync(firstPath, CancellationToken.None));
            Assert.Equal("{\"artifact\":\"b\"}", await File.ReadAllTextAsync(secondPath, CancellationToken.None));

            var firstDescriptorPath = $"{firstPath}.descriptor.json";
            var descriptorJson = await File.ReadAllTextAsync(firstDescriptorPath, CancellationToken.None);
            Assert.Contains("\"ArtifactId\":\"report-a\"", descriptorJson, StringComparison.Ordinal);
            Assert.Contains("\"ProducerType\":\"validator.dotnet\"", descriptorJson, StringComparison.Ordinal);
            Assert.Contains("\"ProducerCapabilityId\":\"validator.dotnet\"", descriptorJson, StringComparison.Ordinal);
            Assert.Contains("\"ProducerVersion\":\"1.2.3\"", descriptorJson, StringComparison.Ordinal);
            Assert.Contains("\"FileName\":\"validation-report\"", descriptorJson, StringComparison.Ordinal);
            Assert.Contains($"\"RelativePath\":\"{first.RelativePath}\"", descriptorJson, StringComparison.Ordinal);

            var listed = await store.ListAsync(sessionId, CancellationToken.None);
            Assert.Collection(
                listed,
                descriptor => Assert.Equal("report-a", descriptor.ArtifactId.Value),
                descriptor => Assert.Equal("report-b", descriptor.ArtifactId.Value));
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }
}