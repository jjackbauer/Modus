using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Builder;
using Wip.Tools.Repo;
using Xunit;

namespace Wip.Runtime.Tests.Repo;

public sealed class RepoAgentToolsTests
{
    [Fact]
    public async Task WriteThenReadThenDelete_GivenWorktreePaths_ExpectedFileLifecycle()
    {
        using var worktree = TempWorktree.Create();
        var read = new RepoReadFileTool();
        var write = new RepoWriteFileTool();
        var delete = new RepoDeleteFileTool();

        await write.ExecuteAsync(
            new RepoWriteFileRequest("docs/agent-note.md", "hello agent"),
            worktree.Context,
            CancellationToken.None);

        var fullPath = Path.Combine(worktree.Path, "docs", "agent-note.md");
        Assert.True(File.Exists(fullPath));
        Assert.Equal("hello agent", await File.ReadAllTextAsync(fullPath));

        var readResult = await read.ExecuteAsync(
            new RepoReadFileRequest("docs/agent-note.md"),
            worktree.Context,
            CancellationToken.None);
        Assert.Equal("hello agent", readResult.Content);
        Assert.False(readResult.Truncated);

        var deleteResult = await delete.ExecuteAsync(
            new RepoDeleteFileRequest("docs/agent-note.md"),
            worktree.Context,
            CancellationToken.None);
        Assert.True(deleteResult.Deleted);
        Assert.False(File.Exists(fullPath));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/tmp/escape.txt")]
    [InlineData(".git/config")]
    public async Task Write_GivenPathOutsideWorktreeOrGitInternals_ExpectedUnauthorizedAccess(string path)
    {
        using var worktree = TempWorktree.Create();
        var write = new RepoWriteFileTool();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => write.ExecuteAsync(
            new RepoWriteFileRequest(path, "x"),
            worktree.Context,
            CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Search_GivenPattern_ExpectedMatchingLinesWithoutBinObj()
    {
        using var worktree = TempWorktree.Create();
        Directory.CreateDirectory(Path.Combine(worktree.Path, "docs"));
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "docs", "readme.md"), "agent runtime\nother");
        Directory.CreateDirectory(Path.Combine(worktree.Path, "bin"));
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "bin", "ignored.md"), "agent runtime");

        var result = await new RepoSearchTool().ExecuteAsync(
            new RepoSearchRequest("agent runtime"),
            worktree.Context,
            CancellationToken.None);

        var match = Assert.Single(result.Matches);
        Assert.Equal("docs/readme.md", match.Path.Replace('\\', '/'));
        Assert.Equal(1, match.Line);
    }

    [Fact]
    public void AddRepoAgentTools_GivenBuilder_ExpectedFiveToolsWithWorkspacePermissions()
    {
        var builder = new WipBuilder(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        builder.AddRepoAgentTools();

        Assert.Equal(5, builder.CapabilityDescriptors.Count);
        var write = builder.CapabilityDescriptors.Single(descriptor => descriptor.CapabilityId.Value == "tool.repo.write_file");
        Assert.Contains(write.RequiredPermissions, permission => permission.Value == "workspace.write");
        var status = builder.CapabilityDescriptors.Single(descriptor => descriptor.CapabilityId.Value == "tool.repo.git_status");
        Assert.Contains(status.RequiredPermissions, permission => permission.Value == "process");
    }

    private sealed class TempWorktree : IDisposable
    {
        private TempWorktree(string path)
        {
            Path = path;
            Context = new CapabilityContext(new SessionId($"session-repo-{Guid.NewGuid():N}"), path);
        }

        public string Path { get; }

        public CapabilityContext Context { get; }

        public static TempWorktree Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wip-repo-tools-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempWorktree(path);
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