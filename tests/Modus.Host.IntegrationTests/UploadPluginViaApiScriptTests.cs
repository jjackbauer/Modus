using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class UploadPluginViaApiScriptTests
{
    [Fact]
    [Trait("ChecklistItem", "Add helper script to call upload API and poll operation status until completion [mandatory - operator gate script]")]
    public async Task UploadPluginViaApiScript_GivenValidPackage_SubmitsUploadAndPollsUntilCompletion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-upload-script-success-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        await using var api = await FakeUploadApiServer.StartAsync(authFailure: false);

        try
        {
            var packagePath = Path.Combine(root, "plugin.upload.zip");
            var signaturePath = Path.Combine(root, "plugin.upload.sig");
            await File.WriteAllBytesAsync(packagePath, "package-bytes"u8.ToArray());
            await File.WriteAllBytesAsync(signaturePath, "signature-bytes"u8.ToArray());

            var scriptPath = Path.Combine(FindRepositoryRoot(), "scripts", "upload-plugin-via-api.ps1");
            var execution = await RunPowerShellScriptAsync(
                scriptPath,
                [
                    "-ApiBaseUrl", api.BaseUrl,
                    "-PackagePath", packagePath,
                    "-SignaturePath", signaturePath,
                    "-PollIntervalMilliseconds", "10",
                    "-MaxPollAttempts", "20"
                ],
                root);

            Assert.True(
                execution.ExitCode == 0,
                $"Expected script to succeed but got exit code {execution.ExitCode}. StdOut: {execution.StdOut} StdErr: {execution.StdErr}");
            Assert.Contains("OperationId=", execution.StdOut, StringComparison.Ordinal);
            Assert.Contains("FinalStage=Completed", execution.StdOut, StringComparison.Ordinal);
            Assert.Contains("FinalIsSuccess=True", execution.StdOut, StringComparison.Ordinal);
            Assert.Equal(1, api.PostUploadsCount);
            Assert.Equal(2, api.GetStatusCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Add helper script to call upload API and poll operation status until completion [mandatory - operator gate script]")]
    public async Task UploadPluginViaApiScript_GivenApiAuthorizationFailure_StopsAndPrintsRemediationHint()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-upload-script-auth-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        await using var api = await FakeUploadApiServer.StartAsync(authFailure: true);

        try
        {
            var packagePath = Path.Combine(root, "plugin.upload.zip");
            var signaturePath = Path.Combine(root, "plugin.upload.sig");
            await File.WriteAllBytesAsync(packagePath, "package-bytes"u8.ToArray());
            await File.WriteAllBytesAsync(signaturePath, "signature-bytes"u8.ToArray());

            var scriptPath = Path.Combine(FindRepositoryRoot(), "scripts", "upload-plugin-via-api.ps1");
            var execution = await RunPowerShellScriptAsync(
                scriptPath,
                [
                    "-ApiBaseUrl", api.BaseUrl,
                    "-PackagePath", packagePath,
                    "-SignaturePath", signaturePath,
                    "-PollIntervalMilliseconds", "10",
                    "-MaxPollAttempts", "5"
                ],
                root);

            Assert.NotEqual(0, execution.ExitCode);
            Assert.Contains("authorization", execution.StdOut + execution.StdErr, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Check API credentials", execution.StdOut + execution.StdErr, StringComparison.Ordinal);
            Assert.Equal(1, api.PostUploadsCount);
            Assert.Equal(0, api.GetStatusCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunPowerShellScriptAsync(
        string scriptPath,
        IReadOnlyList<string> scriptArguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell" : "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in scriptArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdOut, stdErr);
    }

    private static string FindRepositoryRoot()
    {
        var current = Directory.GetCurrentDirectory();

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "Modus.slnx")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FakeUploadApiServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _shutdown;
        private readonly Task _pumpTask;
        private readonly bool _authFailure;
        private readonly Guid _operationId;
        private int _statusResponseIndex;

        private FakeUploadApiServer(HttpListener listener, bool authFailure)
        {
            _listener = listener;
            _shutdown = new CancellationTokenSource();
            _authFailure = authFailure;
            _operationId = Guid.NewGuid();
            _pumpTask = Task.Run(PumpAsync);
        }

        public string BaseUrl => _listener.Prefixes.Single().TrimEnd('/');

        public int PostUploadsCount { get; private set; }

        public int GetStatusCount { get; private set; }

        public static Task<FakeUploadApiServer> StartAsync(bool authFailure)
        {
            var port = FindAvailablePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            return Task.FromResult(new FakeUploadApiServer(listener, authFailure));
        }

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            _listener.Stop();

            try
            {
                await _pumpTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when disposal cancels the request loop.
            }
            finally
            {
                _listener.Close();
                _shutdown.Dispose();
            }
        }

        private async Task PumpAsync()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(_shutdown.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException) when (_shutdown.IsCancellationRequested)
                {
                    break;
                }

                await HandleContextAsync(context);
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath.Equals("/management/plugins/uploads", StringComparison.Ordinal) == true)
            {
                PostUploadsCount++;
                if (_authFailure)
                {
                    await WriteJsonAsync(response, (int)HttpStatusCode.Unauthorized, "{\"error\":\"Missing operator authorization.\"}");
                    return;
                }

                response.AddHeader("Location", $"/management/plugins/uploads/{_operationId}");
                await WriteJsonAsync(response, (int)HttpStatusCode.Accepted, $"{{\"operationId\":\"{_operationId}\",\"status\":\"Queued\"}}");
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath.Equals($"/management/plugins/uploads/{_operationId}", StringComparison.Ordinal) == true)
            {
                GetStatusCount++;

                if (_statusResponseIndex == 0)
                {
                    _statusResponseIndex++;
                    await WriteJsonAsync(
                        response,
                        (int)HttpStatusCode.OK,
                        $"{{\"operationId\":\"{_operationId}\",\"stage\":\"Extracting\",\"progressPercent\":30,\"isTerminal\":false,\"isSuccess\":false,\"failureReason\":null,\"diagnostics\":[\"stage=extract outcome=running\"]}}");
                    return;
                }

                await WriteJsonAsync(
                    response,
                    (int)HttpStatusCode.OK,
                    $"{{\"operationId\":\"{_operationId}\",\"stage\":\"Completed\",\"progressPercent\":100,\"isTerminal\":true,\"isSuccess\":true,\"failureReason\":null,\"diagnostics\":[\"stage=run outcome=success\"]}}");
                return;
            }

            await WriteJsonAsync(response, (int)HttpStatusCode.NotFound, "{\"error\":\"Not found\"}");
        }

        private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, string json)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(json);
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            response.ContentLength64 = bodyBytes.Length;
            await response.OutputStream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
            response.Close();
        }

        private static int FindAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
