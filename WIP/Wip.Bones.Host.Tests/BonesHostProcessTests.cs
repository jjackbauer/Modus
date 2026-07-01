using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Wip.Shell.E2E.Compliance;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostProcessTests
{
    private const string ProcessSmokeTestItem = BonesHostRequirementsChecklistItems.ProcessSmokeTest;
    private const string OperatorDocumentationItem = BonesHostRequirementsChecklistItems.OperatorDocumentation;

    [Fact]
    [Trait("ChecklistItem", ProcessSmokeTestItem)]
    [Trait("ChecklistItem", OperatorDocumentationItem)]
    public async Task BonesHostProcess_GivenEphemeralPort_ExpectedTwoAutoIterationsThenStopWithExitCodeZero()
    {
        var repositoryRoot = BonesHostTestPaths.FindRepositoryRoot();
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        var listenUri = BonesHostTestNetwork.AllocateListenUrl();
        var projectPath = Path.Combine(repositoryRoot, "WIP", "Wip.Bones.Host", "Wip.Bones.Host.csproj");

        await BonesHostProcessTestSupport.EnsureHostBuiltAsync(repositoryRoot, projectPath);

        using var process = StartHostProcess(repositoryRoot, projectPath, dataDirectory, listenUri);
        try
        {
            using var client = new HttpClient { BaseAddress = listenUri, Timeout = TimeSpan.FromSeconds(15) };

            await WaitForHttpAsync(client, "/health", HttpStatusCode.OK, TimeSpan.FromSeconds(120));

            await BonesHostStatusPolling.WaitForStatusAsync(
                client,
                response => response.IterationCount >= 2,
                TimeSpan.FromMinutes(8));

            await BonesHostStatusClient.StopAsync(client);

            if (!process.WaitForExit((int)TimeSpan.FromSeconds(60).TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("Host process did not exit within the expected timeout after stop was requested.");
            }

            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            if (Directory.Exists(dataDirectory))
            {
                try
                {
                    Directory.Delete(dataDirectory, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static Process StartHostProcess(string repositoryRoot, string projectPath, string dataDirectory, Uri listenUri)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--no-build");

        startInfo.Environment["BonesHost__DataDirectory"] = dataDirectory;
        startInfo.Environment["BonesHost__ListenUrl"] = listenUri.ToString();
        startInfo.Environment["BonesHost__ModelProvider"] = "stub";
        startInfo.Environment["BonesHost__IterationDelayMs"] = "0";
        startInfo.Environment["BonesHost__DefaultParameters__GameCount"] = "1";
        startInfo.Environment["BonesHost__DefaultParameters__TargetScore"] = "6";
        startInfo.Environment["BonesHost__DefaultParameters__Seed"] = "4242";
        startInfo.Environment["BonesHost__DefaultParameters__LearningPlayerId"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.Environment["ASPNETCORE_URLS"] = listenUri.ToString();
        startInfo.Environment["BONES_HOST_STOP_SHUTDOWN"] = "1";

        E2EMatrixComplianceRegistry.ConfigureNestedTestProcessEnvironment(startInfo);

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start host process for '{projectPath}'.");

        // Drain redirected streams so the child process cannot block when buffers fill.
        process.OutputDataReceived += (_, e) => { _ = e.Data; };
        process.ErrorDataReceived += (_, e) => { _ = e.Data; };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private static async Task WaitForHttpAsync(HttpClient client, string path, HttpStatusCode expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync(path);
                if (response.StatusCode == expected)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            catch (SocketException)
            {
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Timed out waiting for {path} to return {expected}.");
    }
}

internal static class BonesHostProcessTestSupport
{
    public static async Task EnsureHostBuiltAsync(string repositoryRoot, string projectPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to build host project '{projectPath}'.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Host build failed with exit code {process.ExitCode}.{Environment.NewLine}{await stdout}{await stderr}");
        }
    }
}