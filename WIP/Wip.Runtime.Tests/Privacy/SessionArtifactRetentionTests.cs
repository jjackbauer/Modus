using Wip.Abstractions.Privacy;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Privacy;

public sealed class SessionArtifactRetentionTests
{
    [Fact]
    public async Task PurgeAsync_GivenOldAndRecentArtifacts_ExpectedOnlyOldDeleted()
    {
        using var repository = TestRepository.Create();
        var oldPath = repository.WriteArtifact("session-1", "old.json", DateTimeOffset.UtcNow.AddDays(-40));
        var recentPath = repository.WriteArtifact("session-1", "recent.json", DateTimeOffset.UtcNow.AddHours(-1));

        var result = await new SessionArtifactRetention().PurgeAsync(
            repository.Path,
            new RetentionPolicy(TimeSpan.FromDays(30)),
            CancellationToken.None);

        Assert.Equal(1, result.DeletedFiles);
        Assert.Equal(1, result.RetainedFiles);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(recentPath));
    }

    [Fact]
    public async Task PurgeAsync_GivenDisabledPolicy_ExpectedNothingDeleted()
    {
        using var repository = TestRepository.Create();
        var oldPath = repository.WriteArtifact("session-1", "old.json", DateTimeOffset.UtcNow.AddDays(-400));

        var result = await new SessionArtifactRetention().PurgeAsync(
            repository.Path,
            RetentionPolicy.Disabled,
            CancellationToken.None);

        Assert.Equal(0, result.DeletedFiles);
        Assert.Equal(1, result.RetainedFiles);
        Assert.True(File.Exists(oldPath));
    }

    [Fact]
    public async Task GetStatusAsync_GivenArtifacts_ExpectedCountsAndOldest()
    {
        using var repository = TestRepository.Create();
        var oldest = DateTimeOffset.UtcNow.AddDays(-10);
        repository.WriteArtifact("session-1", "a.json", oldest);
        repository.WriteArtifact("session-2", "b.json", DateTimeOffset.UtcNow.AddDays(-2));

        var status = await new SessionArtifactRetention().GetStatusAsync(repository.Path, CancellationToken.None);

        Assert.Equal(2, status.SessionDirectories);
        Assert.Equal(2, status.ArtifactFiles);
        Assert.True(status.TotalBytes > 0);
        Assert.NotNull(status.OldestArtifactUtc);
        Assert.True(status.OldestArtifactUtc <= oldest.AddMinutes(1));
    }

    private sealed class TestRepository : IDisposable
    {
        private TestRepository(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public static TestRepository Create()
            => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wip-retention-{Guid.NewGuid():N}"));

        public string WriteArtifact(string sessionId, string fileName, DateTimeOffset lastWriteUtc)
        {
            var directory = System.IO.Path.Combine(Path, ".wip", "sessions", sessionId, "artifacts");
            Directory.CreateDirectory(directory);
            var file = System.IO.Path.Combine(directory, fileName);
            File.WriteAllText(file, "{\"content\":\"artifact\"}");
            File.SetLastWriteTimeUtc(file, lastWriteUtc.UtcDateTime);
            return file;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
