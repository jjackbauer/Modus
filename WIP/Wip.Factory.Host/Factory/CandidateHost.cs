using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Wip.Factory.Host.Factory;

/// <summary>A started candidate, with the process identity the factory actually owns.</summary>
public sealed record CandidateSession(
    Uri BaseAddress,
    int ProcessId,
    bool IdentityVerified,
    Func<ValueTask> Stop,
    string? IdentityProof = null,
    Func<bool>? IsAlive = null)
{
    public static CandidateSession None => new(new Uri("http://127.0.0.1:1/"), 0, false, static () => ValueTask.CompletedTask);

    /// <summary>True when the process is still running. A session with no probe is treated as alive (test stubs).</summary>
    public bool IsAliveNow() => IsAlive is null || IsAlive();
}

/// <summary>Starts a built application for verification. Tests substitute a stub; the real one runs dotnet.</summary>
public interface ICandidateHost
{
    ValueTask<CandidateSession> StartAsync(string worktreePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Starts the candidate on a factory-allocated loopback port and proves that the listener belongs to the process tree
/// the factory launched (v0.3 §7.2, v0.4 FAC-P1-01). A stale or competing listener is refused instead of being
/// treated as the candidate.
/// </summary>
public sealed class DotnetRunCandidateHost : ICandidateHost
{
    public const string NoProject = "candidate-no-project";
    public const string NotStarted = "candidate-not-started";
    public const string IdentityUnverified = "candidate-identity-unverified";

    private readonly TimeSpan _readiness;
    private readonly IListenerIdentityProver _identityProver;

    public DotnetRunCandidateHost(TimeSpan? readiness = null, IListenerIdentityProver? identityProver = null)
    {
        _readiness = readiness ?? TimeSpan.FromSeconds(60);
        _identityProver = identityProver ?? new WindowsTcpListenerIdentityProver();
    }

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

        // Connectivity alone is not identity. The OS listener table must name our process or a descendant.
        var identity = _identityProver.Prove(port, process.Id);
        if (!identity.Verified)
        {
            TryKill(process);
            throw new InvalidOperationException(
                $"{IdentityUnverified}:{identity.Proof}:{identity.Detail}");
        }

        return new CandidateSession(
            baseAddress,
            process.Id,
            IdentityVerified: true,
            Stop: async () =>
            {
                TryKill(process);
                await Task.CompletedTask.ConfigureAwait(false);
            },
            IdentityProof: identity.Proof + ":" + identity.ListenerProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IsAlive: () => !CandidateProcess.HasExited(process));
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
