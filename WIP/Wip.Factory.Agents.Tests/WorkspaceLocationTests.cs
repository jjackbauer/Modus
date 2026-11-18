using System.Diagnostics;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Wip.Workspaces;
using Xunit;

namespace Wip.Factory.Agents.Tests;

/// <summary>
/// D-06: session worktrees belong under the factory control root, never inside the operator's repository.
/// </summary>
public sealed class WorkspaceLocationTests
{
    private static async Task<string> RepositoryAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "factory-wt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        await GitAsync(path, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(path, "README.md"), "# target");
        await GitAsync(path, "add", ".");
        await GitAsync(path, "-c", "user.email=t@example.test", "-c", "user.name=T", "commit", "-m", "init");
        return path;
    }

    private static async Task GitAsync(string workingDirectory, params string[] arguments)
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

        using var process = Process.Start(info) ?? throw new InvalidOperationException("git");
        await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("git failed: " + error);
        }
    }

    [Fact]
    public async Task Workspace_GivenWorktreeRootOutsideRepository_KeepsFactoryStateOutOfTheTarget()
    {
        var repository = await RepositoryAsync();
        var controlRoot = Path.Combine(Path.GetTempPath(), "factory-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(controlRoot);

        var provisioner = new FactoryWorktreeProvisioner(worktreeRoot: Path.Combine(controlRoot, "worktrees"));
        var (lease, reason) = await provisioner.ProvisionAsync(repository, new SessionId("session-wt"), "main");

        Assert.NotNull(lease);
        Assert.Null(reason);

        // The worktree is under the control root ...
        var expectedRoot = Path.GetFullPath(Path.Combine(controlRoot, "worktrees"));
        Assert.StartsWith(expectedRoot, Path.GetFullPath(lease!.WorktreePath), StringComparison.OrdinalIgnoreCase);

        // ... and the operator's repository contains no factory state at all.
        Assert.False(Directory.Exists(Path.Combine(repository, ".wip")), "the target repository must not hold factory state");
    }

    [Fact]
    public async Task Workspace_GivenNoWorktreeRoot_KeepsTheHistoricalLocation()
    {
        var repository = await RepositoryAsync();

        var provisioner = new FactoryWorktreeProvisioner();
        var (lease, reason) = await provisioner.ProvisionAsync(repository, new SessionId("session-legacy"), "main");

        Assert.NotNull(lease);
        Assert.Null(reason);

        // The default is unchanged so existing callers keep their behaviour.
        Assert.Contains(Path.Combine(".wip", "worktrees"), lease!.WorktreePath, StringComparison.OrdinalIgnoreCase);
    }
}
