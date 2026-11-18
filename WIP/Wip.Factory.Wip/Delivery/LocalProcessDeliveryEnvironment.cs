using System.Diagnostics;
using System.Net.Http;

namespace Wip.Factory.Wip.Delivery;

/// <summary>A deployable candidate: a command to run plus the URL that proves it is serving.</summary>
public sealed record DeliveryCandidate(
    int Version,
    string Command,
    IReadOnlyList<string> Arguments,
    string HealthUrl,
    TimeSpan StartupTimeout);

/// <summary>Outcome of a release attempt.</summary>
public sealed record DeliveryReleaseResult(bool Released, int Version, string Detail);

/// <summary>Outcome of a readiness probe.</summary>
public sealed record DeliveryProbeResult(bool Healthy, int? Version, string Detail);

/// <summary>Outcome of a rollback, including the version that is now live.</summary>
public sealed record DeliveryRollbackResult(bool RolledBack, int? RestoredVersion, string Detail);

/// <summary>
/// Local delivery environment (F-G.10). It starts the candidate as a contained child process and waits
/// on a real HTTP readiness probe, so "released" means "a process is answering", not "a record was
/// written". Rollback restarts the previous released candidate and re-probes it.
/// </summary>
public sealed class LocalProcessDeliveryEnvironment : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<int, DeliveryCandidate> _released = [];
    private Process? _live;
    private int? _liveVersion;
    private int? _previousVersion;

    public int? LiveVersion => _liveVersion;

    public IReadOnlyCollection<int> ReleasedVersions => _released.Keys.ToArray();

    public const string ProbeFailedCategory = "readiness-probe-failed";
    public const string RollbackFailedCategory = "rollback-probe-failed";
    public const string NothingToRollBackCategory = "nothing-to-roll-back";

    public async ValueTask<DeliveryReleaseResult> ReleaseAsync(
        DeliveryCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var (started, detail) = await StartAndProbeAsync(candidate, cancellationToken).ConfigureAwait(false);

        if (!started)
        {
            return new DeliveryReleaseResult(false, candidate.Version, ProbeFailedCategory + ":" + detail);
        }

        _previousVersion = _liveVersion;
        _released[candidate.Version] = candidate;
        _liveVersion = candidate.Version;

        return new DeliveryReleaseResult(true, candidate.Version, detail);
    }

    public async ValueTask<DeliveryProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (_liveVersion is not { } version || !_released.TryGetValue(version, out var candidate))
        {
            return new DeliveryProbeResult(false, null, "no live release");
        }

        if (!await IsHealthyAsync(candidate.HealthUrl, cancellationToken).ConfigureAwait(false))
        {
            return new DeliveryProbeResult(false, version, "health endpoint did not answer 200");
        }

        return new DeliveryProbeResult(true, version, "health endpoint returned 200");
    }

    public async ValueTask<DeliveryRollbackResult> RollBackAsync(CancellationToken cancellationToken = default)
    {
        if (_previousVersion is not { } previous || !_released.TryGetValue(previous, out var candidate))
        {
            return new DeliveryRollbackResult(false, _liveVersion, NothingToRollBackCategory);
        }

        StopLive();

        var (started, detail) = await StartAndProbeAsync(candidate, cancellationToken).ConfigureAwait(false);
        if (!started)
        {
            return new DeliveryRollbackResult(false, _liveVersion, RollbackFailedCategory + ":" + detail);
        }

        var from = _liveVersion;
        _liveVersion = previous;
        _previousVersion = from;

        return new DeliveryRollbackResult(true, previous, $"restored {previous} after {from}: {detail}");
    }

    private async ValueTask<(bool Started, string Detail)> StartAndProbeAsync(
        DeliveryCandidate candidate,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(candidate.Command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in candidate.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Process? process = null;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (false, "process-start-failed: " + exception.Message);
        }

        if (process is null)
        {
            return (false, "process-start-returned-null");
        }

        var deadline = DateTimeOffset.UtcNow.Add(candidate.StartupTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                process.Dispose();
                return (false, "process-exited-before-ready");
            }

            if (await IsHealthyAsync(candidate.HealthUrl, cancellationToken).ConfigureAwait(false))
            {
                StopLive();
                _live = process;
                return (true, $"process {process.Id} answered {candidate.HealthUrl}");
            }

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        TryKill(process);
        process.Dispose();
        return (false, "startup-timeout");
    }

    private async ValueTask<bool> IsHealthyAsync(string healthUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(healthUrl, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private void StopLive()
    {
        if (_live is null)
        {
            return;
        }

        TryKill(_live);
        _live.Dispose();
        _live = null;
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

    public void Dispose()
    {
        StopLive();
        _http.Dispose();
    }
}
