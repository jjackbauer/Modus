using System.Diagnostics;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Wip.Workspaces;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class WorktreeIsolationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wip-factory-worktree-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<string> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {stderr}");
        return stdout;
    }

    private async Task<string> CreateRepositoryAsync()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repositoryPath);

        await RunGitAsync(repositoryPath, "init", "-b", "main");
        await RunGitAsync(repositoryPath, "config", "user.email", "factory@example.test");
        await RunGitAsync(repositoryPath, "config", "user.name", "Factory Tests");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "README.md"), "fixture");
        await RunGitAsync(repositoryPath, "add", ".");
        await RunGitAsync(repositoryPath, "commit", "-m", "initial");

        return repositoryPath;
    }

    [Fact]
    public async Task Workspace_GivenTwoModifyingSessions_AllocatesDistinctWorktreesAndBranches()
    {
        var repositoryPath = await CreateRepositoryAsync();
        var provisioner = new FactoryWorktreeProvisioner();

        var first = await provisioner.ProvisionAsync(repositoryPath, new SessionId("session-a"), "main");
        var second = await provisioner.ProvisionAsync(repositoryPath, new SessionId("session-b"), "main");

        Assert.Null(first.Reason);
        Assert.Null(second.Reason);
        Assert.NotNull(first.Lease);
        Assert.NotNull(second.Lease);

        // Distinct worktrees and distinct session branches: the isolation boundary is real on disk.
        Assert.NotEqual(
            Path.GetFullPath(first.Lease!.WorktreePath),
            Path.GetFullPath(second.Lease!.WorktreePath));
        Assert.NotEqual(first.Lease.SessionBranch, second.Lease.SessionBranch);

        Assert.True(Directory.Exists(first.Lease.WorktreePath), "first worktree was not created");
        Assert.True(Directory.Exists(second.Lease.WorktreePath), "second worktree was not created");

        // Both worktrees carry the fixture content, so they are working trees rather than empty folders.
        Assert.True(File.Exists(Path.Combine(first.Lease.WorktreePath, "README.md")));
        Assert.True(File.Exists(Path.Combine(second.Lease.WorktreePath, "README.md")));

        // The primary working tree is untouched by either allocation.
        Assert.True(File.Exists(Path.Combine(repositoryPath, "README.md")));

        Assert.Equal(2, provisioner.OutstandingLeases.Count);
        Assert.Equal(new SessionId("session-a"), provisioner.OutstandingLeases[Path.GetFullPath(first.Lease.WorktreePath)]);
        Assert.Equal(new SessionId("session-b"), provisioner.OutstandingLeases[Path.GetFullPath(second.Lease.WorktreePath)]);
    }
}
