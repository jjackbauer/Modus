using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// One exclusive writer per control root (v0.3 §5.3). The lock is an OS handle, so this exercises real
/// file locking rather than a process-local flag.
/// </summary>
public sealed class HostLockTests
{
    private const string Requirement = """
        ---
        title: Ping HTTP API
        supportedClass: aspnetcore-http-net10
        ---

        Create an HTTP JSON API with a health endpoint.

        ## Acceptance expectations
        - GET /ping returns 200.
        """;

    private static FactoryControlRoot Root(out string lockPath)
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-lock-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);

        root!.EnsureCreated();
        lockPath = Path.Combine(root.LocksPath, "run.lock");
        return root;
    }

    [Fact]
    public async Task Host_GivenConcurrentRun_ExitsHostLockHeld()
    {
        var root = Root(out var lockPath);

        // Simulate another process holding the run lock.
        await using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var (reports, error) = await new FactoryRuntime(root).RunExclusiveAsync();

        Assert.Equal("host-lock-held", error);
        Assert.Empty(reports);
    }

    [Fact]
    public async Task Host_GivenLockReleased_RunsNormally()
    {
        var root = Root(out var lockPath);
        var runtime = new FactoryRuntime(root);
        await runtime.SubmitAsync(Requirement);

        var (reports, error) = await runtime.RunExclusiveAsync();

        Assert.Null(error);
        Assert.NotEmpty(reports);

        // The lock is released, so a later run can take it.
        await using var again = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.True(again.CanWrite);
    }
}