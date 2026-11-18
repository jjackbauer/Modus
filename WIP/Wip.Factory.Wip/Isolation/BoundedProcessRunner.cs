using System.Diagnostics;
using Wip.Sandbox.WorkerClient;

namespace Wip.Factory.Wip.Isolation;

/// <summary>Limits applied to one bounded implementation-shell execution (F-N.5).</summary>
public sealed record ProcessBounds(
    TimeSpan WallTime,
    long MaxProcessMemoryBytes,
    int MaxProcesses = 4)
{
    public static ProcessBounds Default { get; } = new(TimeSpan.FromSeconds(30), 256L * 1024 * 1024);
}

/// <summary>What a bounded execution did.</summary>
public sealed record BoundedExecutionResult(
    bool Completed,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Elapsed,
    bool TerminatedByWallTime,
    bool TerminatedByMemoryLimit,
    bool IsolationApplied);

/// <summary>
/// Runs an implementation shell command under real OS resource bounds (F-N.5). It reuses WIP's existing
/// job-object primitive for memory and process-count limits and enforces wall time itself, so untrusted
/// generated commands cannot run unbounded in the privileged host.
/// </summary>
/// <remarks>
/// Filesystem and network bounds are the AppContainer path's responsibility; this runner covers wall
/// time, process memory and process count. It reports <see cref="BoundedExecutionResult.IsolationApplied"/>
/// so a caller can tell a genuinely bounded run from one where the OS refused to apply limits.
/// </remarks>
public sealed class BoundedProcessRunner
{
    public async ValueTask<BoundedExecutionResult> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        ProcessBounds bounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(bounds);

        var info = new ProcessStartInfo(command)
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

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"Could not start '{command}'.");

        var job = WindowsJobObject.Create(process, bounds.MaxProcessMemoryBytes, bounds.MaxProcesses);
        var isolationApplied = job != IntPtr.Zero;

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(bounds.WallTime);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            var terminatedByWallTime = false;

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                terminatedByWallTime = true;
                TryKill(process);
            }

            var stdout = await SafeAsync(stdoutTask).ConfigureAwait(false);
            var stderr = await SafeAsync(stderrTask).ConfigureAwait(false);
            stopwatch.Stop();

            var exitedOnItsOwn = !terminatedByWallTime;
            var exitCode = exitedOnItsOwn ? process.ExitCode : (int?)null;

            // A process the job object kills for memory reports a non-zero exit rather than a signal we
            // can read, so success is decided by exit code and the wall-time flag only.
            var terminatedByMemory = exitedOnItsOwn && exitCode != 0 && LooksLikeMemoryTermination(stderr);

            return new BoundedExecutionResult(
                Completed: exitedOnItsOwn && exitCode == 0,
                ExitCode: exitCode,
                StandardOutput: stdout,
                StandardError: stderr,
                Elapsed: stopwatch.Elapsed,
                TerminatedByWallTime: terminatedByWallTime,
                TerminatedByMemoryLimit: terminatedByMemory,
                IsolationApplied: isolationApplied);
        }
        finally
        {
            if (job != IntPtr.Zero)
            {
                WindowsJobObject.Close(job);
            }
        }
    }

    private static async Task<string> SafeAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static bool LooksLikeMemoryTermination(string stderr)
        => stderr.Contains("MemoryError", StringComparison.OrdinalIgnoreCase)
           || stderr.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase)
           || stderr.Contains("Cannot allocate memory", StringComparison.OrdinalIgnoreCase);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
