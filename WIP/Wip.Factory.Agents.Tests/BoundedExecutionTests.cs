using Wip.Factory.Wip.Isolation;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class BoundedExecutionTests
{
    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wip-bounded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task Isolation_GivenImplementationShellExecution_IsBoundedByResourceLimits()
    {
        var runner = new BoundedProcessRunner();
        var workingDirectory = TempDirectory();

        try
        {
            // 1. A command that would run far past its budget is terminated at the wall-time bound.
            var wallClock = await runner.RunAsync(
                "python",
                ["-c", "import time; time.sleep(30)"],
                workingDirectory,
                new ProcessBounds(WallTime: TimeSpan.FromSeconds(2), MaxProcessMemoryBytes: 64L * 1024 * 1024));

            Assert.True(wallClock.TerminatedByWallTime);
            Assert.False(wallClock.Completed);
            Assert.True(wallClock.IsolationApplied, "the job object was not applied to the child process");
            Assert.True(
                wallClock.Elapsed < TimeSpan.FromSeconds(20),
                $"the command outlived its bound: {wallClock.Elapsed}");

            // 2. A command that tries to exceed the process-memory limit does not succeed.
            var memory = await runner.RunAsync(
                "python",
                ["-c", "x = bytearray(300 * 1024 * 1024); print(len(x))"],
                workingDirectory,
                new ProcessBounds(
                    WallTime: TimeSpan.FromSeconds(30),
                    MaxProcessMemoryBytes: 64L * 1024 * 1024));

            Assert.False(memory.Completed);
            Assert.NotEqual(0, memory.ExitCode ?? -1);
            Assert.True(memory.IsolationApplied);

            // 3. A benign command inside the same bounds still succeeds, so the bound is not simply
            //    blocking everything.
            var allowed = await runner.RunAsync(
                "python",
                ["-c", "print('bounded-ok')"],
                workingDirectory,
                new ProcessBounds(WallTime: TimeSpan.FromSeconds(20), MaxProcessMemoryBytes: 128L * 1024 * 1024));

            Assert.True(allowed.Completed, allowed.StandardError);
            Assert.Contains("bounded-ok", allowed.StandardOutput, StringComparison.Ordinal);
            Assert.False(allowed.TerminatedByWallTime);
        }
        finally
        {
            try
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
