using Wip.Factory.Wip.Delivery;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class DeliveryEnvironmentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wip-factory-delivery-tests", Guid.NewGuid().ToString("N"));

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
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task DeliveryEnvironment_GivenFailedCandidate_RollsBackToPreviousReleasedArtifact()
    {
        var root1 = Path.Combine(_root, "v1");
        var root2 = Path.Combine(_root, "v2");
        Directory.CreateDirectory(root1);
        Directory.CreateDirectory(root2);
        await File.WriteAllTextAsync(Path.Combine(root1, "index.html"), "<html>release 1</html>");
        await File.WriteAllTextAsync(Path.Combine(root2, "index.html"), "<html>release 2</html>");

        var port1 = FreePort();
        var port2 = FreePort();
        using var environment = new LocalProcessDeliveryEnvironment();

        static DeliveryCandidate Server(int version, int port, string root) => new(
            Version: version,
            Command: "python",
            Arguments:
            [
                "-m", "http.server", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--bind", "127.0.0.1", "--directory", root
            ],
            HealthUrl: $"http://127.0.0.1:{port}/",
            StartupTimeout: TimeSpan.FromSeconds(25));

        // Version 1 genuinely serves HTTP and becomes live.
        var released1 = await environment.ReleaseAsync(Server(1, port1, root1));
        Assert.True(released1.Released, released1.Detail);
        Assert.Equal(1, environment.LiveVersion);
        Assert.True((await environment.ProbeAsync()).Healthy);

        // Version 2 also becomes live, so version 1 is now the previous release.
        var released2 = await environment.ReleaseAsync(Server(2, port2, root2));
        Assert.True(released2.Released, released2.Detail);
        Assert.Equal(2, environment.LiveVersion);

        // A broken candidate must not become live and must not disturb the live version.
        var broken = new DeliveryCandidate(
            Version: 3,
            Command: "python",
            Arguments: ["-c", "raise SystemExit(1)"],
            HealthUrl: $"http://127.0.0.1:{FreePort()}/",
            StartupTimeout: TimeSpan.FromSeconds(5));

        var failed = await environment.ReleaseAsync(broken);
        Assert.False(failed.Released);
        Assert.StartsWith(LocalProcessDeliveryEnvironment.ProbeFailedCategory, failed.Detail, StringComparison.Ordinal);
        Assert.Equal(2, environment.LiveVersion);
        Assert.DoesNotContain(3, environment.ReleasedVersions);

        // The rollback is genuinely exercised: version 1 is restarted and re-probed, and version 2's
        // process is gone.
        var rollback = await environment.RollBackAsync();
        Assert.True(rollback.RolledBack, rollback.Detail);
        Assert.Equal(1, rollback.RestoredVersion);
        Assert.Equal(1, environment.LiveVersion);

        var afterRollback = await environment.ProbeAsync();
        Assert.True(afterRollback.Healthy, afterRollback.Detail);
        Assert.Equal(1, afterRollback.Version);

        using var probeV2 = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await probeV2.GetAsync($"http://127.0.0.1:{port2}/"));
    }
}