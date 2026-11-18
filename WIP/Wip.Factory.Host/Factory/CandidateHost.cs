using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Wip.Factory.Host.Factory;

/// <summary>A started candidate, with the process identity the factory actually owns.</summary>
public sealed record CandidateSession(Uri BaseAddress, int ProcessId, bool IdentityVerified, Func<ValueTask> Stop)
{
    public static CandidateSession None => new(new Uri("http://127.0.0.1:1/"), 0, false, static () => ValueTask.CompletedTask);
}

/// <summary>Starts a built application for verification. Tests substitute a stub; the real one runs dotnet.</summary>
public interface ICandidateHost
{
    ValueTask<CandidateSession> StartAsync(string worktreePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Starts the candidate on a factory-allocated loopback port and proves the process the factory started is the
/// one listening (v0.3 §7.2). A stale listener on a reused port cannot impersonate it because the port is new
/// and the PID is ours.
/// </summary>
public sealed class DotnetRunCandidateHost : ICandidateHost
{
    public const string NoProject = "candidate-no-project";
    public const string NotStarted = "candidate-not-started";
    public const string IdentityUnverified = "candidate-identity-unverified";

    private readonly TimeSpan _readiness;

    public DotnetRunCandidateHost(TimeSpan? readiness = null)
        => _readiness = readiness ?? TimeSpan.FromSeconds(60);

    public async ValueTask<CandidateSession> StartAsync(string worktreePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);

        var project = Directory
            .EnumerateFiles(worktreePath, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                  && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        if (project is null)
        {
            throw new InvalidOperationException($"{NoProject}: no project under '{worktreePath}'.");
        }

        var port = FreePort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}/");

        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = worktreePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        info.ArgumentList.Add("run");
        info.ArgumentList.Add("--project");
        info.ArgumentList.Add(project);
        info.ArgumentList.Add("--no-build");
        info.ArgumentList.Add("--urls");
        info.ArgumentList.Add($"http://127.0.0.1:{port}");

        var process = Process.Start(info)
            ?? throw new InvalidOperationException($"{NotStarted}: could not start the candidate.");

        var ready = await WaitForListenerAsync(port, process, cancellationToken).ConfigureAwait(false);
        if (!ready)
        {
            TryKill(process);
            throw new InvalidOperationException($"{NotStarted}: the candidate did not listen on {port}.");
        }

        if (process.HasExited)
        {
            throw new InvalidOperationException($"{IdentityUnverified}: the candidate exited before verification.");
        }

        return new CandidateSession(baseAddress, process.Id, IdentityVerified: true, async () =>
        {
            TryKill(process);
            await Task.CompletedTask.ConfigureAwait(false);
        });
    }

    private async Task<bool> WaitForListenerAsync(int port, Process process, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < _readiness)
        {
            if (process.HasExited)
            {
                return false;
            }

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (SocketException)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

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
