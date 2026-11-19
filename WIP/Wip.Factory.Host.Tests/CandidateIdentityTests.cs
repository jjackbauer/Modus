using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P1-01 / K-09: a listener is the candidate only when the OS listener table names the launched process or one
/// of its descendants. These tests exercise the real Windows prover against real processes.
/// </summary>
public sealed class CandidateIdentityTests
{
    private static Process StartChild(string arguments)
        => Process.Start(new ProcessStartInfo("powershell")
        {
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("could not start the child process");

    private static Process StartSleeper() => StartChild("-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 60\"");

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    [Fact]
    public void Candidate_GivenUnrelatedProcessStealsPort_RefusesIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var sleeper = StartSleeper();
        try
        {
            var proof = new WindowsTcpListenerIdentityProver().Prove(port, sleeper.Id);

            Assert.False(proof.Verified);
            Assert.Equal(ListenerIdentity.ForeignListener, proof.Proof);
            Assert.Equal(Environment.ProcessId, proof.ListenerProcessId);
        }
        finally
        {
            Kill(sleeper);
            sleeper.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void Candidate_GivenLauncherAliveButWrongListener_RefusesIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var unrelatedLauncher = StartSleeper();
        try
        {
            Assert.False(unrelatedLauncher.HasExited);

            var proof = new WindowsTcpListenerIdentityProver().Prove(port, unrelatedLauncher.Id);

            Assert.False(proof.Verified);
            Assert.Contains("not the launcher", proof.Detail, StringComparison.Ordinal);
        }
        finally
        {
            Kill(unrelatedLauncher);
            unrelatedLauncher.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void Candidate_GivenOwnedProcessTreeOnAllocatedPort_ProvesIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var port = FreePort();
        var helper = StartChild(
            "-NoProfile -NonInteractive -Command \"$l=[System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, "
            + port
            + "); $l.Start(); Start-Sleep -Seconds 60\"");

        try
        {
            Assert.True(WaitForListener(port, TimeSpan.FromSeconds(20)), "the child listener did not start");

            var proof = new WindowsTcpListenerIdentityProver().Prove(port, Environment.ProcessId);

            Assert.True(proof.Verified, proof.Detail);
            Assert.NotEqual(Environment.ProcessId, proof.ListenerProcessId);
            Assert.StartsWith(WindowsTcpListenerIdentityProver.ProofKind, proof.Proof, StringComparison.Ordinal);
            Assert.True(WindowsTcpListenerIdentityProver.IsInProcessTree(proof.ListenerProcessId, Environment.ProcessId));
        }
        finally
        {
            Kill(helper);
            helper.Dispose();
        }
    }

    [Fact]
    public void Candidate_GivenNoListenerOnPort_RefusesIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var port = FreePort();
        var proof = new WindowsTcpListenerIdentityProver().Prove(port, Environment.ProcessId);

        Assert.False(proof.Verified);
        Assert.Equal(ListenerIdentity.NoListener, proof.Proof);
    }

    private static bool WaitForListener(int port, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, port);
                return true;
            }
            catch (SocketException)
            {
                Thread.Sleep(100);
            }
        }

        return false;
    }
}
