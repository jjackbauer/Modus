using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Wip.Sandbox.Protocol;

namespace Wip.Sandbox.WorkerClient;

public sealed class SandboxWorkerOptions
{
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan CompileTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MoveTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public long MaxMemoryBytes { get; init; } = 268_435_456;
    public int MaxProcesses { get; init; } = 16;

    /// <summary>When true, the worker must be launched inside an OS containment layer or fail closed.</summary>
    public bool RequireOsContainment { get; init; }

    /// <summary>Linux unshare/chroot wrapper. Required when RequireOsContainment is true on Linux.</summary>
    public string? LinuxSandboxScript { get; init; }
}

public sealed class SandboxWorkerException : Exception
{
    public SandboxWorkerException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}

public sealed class SandboxWorkerClient : IScriptExecutor
{
    private readonly Process _process;
    private readonly StreamWriter _input;
    private readonly StreamReader _output;
    private readonly IntPtr _job;
    private readonly SandboxWorkerOptions _options;
    private int _nextId;

    private SandboxWorkerClient(Process process, StreamWriter input, StreamReader output, IntPtr job, SandboxWorkerOptions options)
    {
        _process = process;
        _input = input;
        _output = output;
        _job = job;
        _options = options;
    }

    public bool IsRunning => !_process.HasExited;

    public static bool IsOsContainmentAvailable()
    {
        if (OperatingSystem.IsLinux())
        {
            return SandboxWorkerLauncher.FindUnshare() is not null;
        }

        return OperatingSystem.IsWindows() && WindowsHelperLauncher.IsAvailable();
    }

    public static SandboxWorkerClient Start(string workerPath, SandboxWorkerOptions? options = null)
    {
        var resolved = options ?? new SandboxWorkerOptions();
        var temp = Path.Combine(Path.GetTempPath(), "wip-sandbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        if (resolved.RequireOsContainment && OperatingSystem.IsWindows())
        {
            var contained = WindowsHelperLauncher.Start(workerPath, resolved, temp);
            return new SandboxWorkerClient(contained.Process, contained.Input, contained.Output, contained.Job, resolved);
        }

        var dotnet = SandboxWorkerLauncher.ResolveDotnet();
        var launch = resolved.RequireOsContainment
            ? SandboxWorkerLauncher.ResolveOsContained(
                workerPath,
                resolved,
                dotnet,
                resolved.LinuxSandboxScript)
            : SandboxWorkerLauncher.Resolve(
                workerPath,
                resolved,
                dotnet,
                OperatingSystem.IsLinux(),
                SandboxWorkerLauncher.FindPrlimit());
        var startInfo = new ProcessStartInfo(launch.FileName)
        {
            WorkingDirectory = temp,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in launch.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var pair in SandboxWorkerEnvironment.Build(temp))
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the sandbox worker.");
        var job = OperatingSystem.IsWindows()
            ? WindowsJobObject.Create(process, resolved.MaxMemoryBytes, resolved.MaxProcesses)
            : IntPtr.Zero;
        return new SandboxWorkerClient(process, process.StandardInput, process.StandardOutput, job, resolved);
    }

    public async Task HandshakeAsync(CancellationToken cancellationToken = default)
    {
        var payload = await SendAsync(new
        {
            type = "handshake",
            id = NextId(),
            protocolVersion = SandboxProtocol.Version
        }, _options.StartupTimeout, cancellationToken);
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new SandboxWorkerException(SandboxErrorCodes.InvalidMessage, "Handshake payload was not an object.");
        }
    }

    public async Task CompileAsync(string strategyId, string source, CancellationToken cancellationToken = default)
    {
        await SendAsync(new
        {
            type = "compile",
            id = NextId(),
            payload = new { strategyId, sourceHash = string.Empty, source }
        }, _options.CompileTimeout, cancellationToken);
    }

    public async Task<int> ChooseMoveAsync(string strategyId, WorkerStrategyState state, CancellationToken cancellationToken = default)
    {
        var payload = await SendAsync(new
        {
            type = "chooseMove",
            id = NextId(),
            payload = new { strategyId, state }
        }, _options.MoveTimeout, cancellationToken);
        return payload.GetProperty("move").GetInt32();
    }

    public async Task CompileBonesAsync(string strategyId, string source, CancellationToken cancellationToken = default)
    {
        await SendAsync(new
        {
            type = "compileBones",
            id = NextId(),
            payload = new { strategyId, sourceHash = string.Empty, source }
        }, _options.CompileTimeout, cancellationToken);
    }

    public async Task<BonesWorkerMove> ChooseBonesMoveAsync(
        string strategyId,
        BonesWorkerState state,
        IReadOnlyList<BonesWorkerMove> legalMoves,
        CancellationToken cancellationToken = default)
    {
        var payload = await SendAsync(new
        {
            type = "chooseBonesMove",
            id = NextId(),
            payload = new { strategyId, state, legalMoves }
        }, _options.MoveTimeout, cancellationToken);
        return payload.GetProperty("move").Deserialize<BonesWorkerMove>(SandboxJson.Options)
            ?? throw new SandboxWorkerException(SandboxErrorCodes.InternalError, "Worker returned no Bones move.");
    }

    private async Task<JsonElement> SendAsync(object request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, SandboxJson.Options);
            await _input.WriteLineAsync(json.AsMemory(), cancellationToken);
            await _input.FlushAsync(cancellationToken);
            var line = await _output.ReadLineAsync(cancellationToken).AsTask().WaitAsync(timeout, cancellationToken);
            if (line is null)
            {
                throw new SandboxWorkerException(SandboxErrorCodes.WorkerCrash, "Worker closed the output stream.");
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.GetProperty("ok").GetBoolean())
            {
                throw new SandboxWorkerException(
                    root.GetProperty("errorCode").GetString() ?? SandboxErrorCodes.InternalError,
                    root.GetProperty("errorMessage").GetString() ?? "Worker request failed.");
            }

            return root.TryGetProperty("payload", out var payload) ? payload.Clone() : default;
        }
        catch (TimeoutException)
        {
            Kill();
            throw;
        }
        catch
        {
            Kill();
            throw;
        }
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5_000);
            }
        }
        catch
        {
            // Best-effort kill; the Job Object close below is the backstop.
        }
    }

    public ValueTask DisposeAsync()
    {
        Kill();
        if (_job != IntPtr.Zero)
        {
            WindowsJobObject.Close(_job);
        }

        _process.Dispose();
        return ValueTask.CompletedTask;
    }

    private string NextId() => Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public static class SandboxWorkerEnvironment
{
    public static IReadOnlyDictionary<string, string> Build(string tempDirectory)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_CLI_HOME"] = tempDirectory,
            ["TMP"] = tempDirectory,
            ["TEMP"] = tempDirectory,
            ["TMPDIR"] = tempDirectory
        };
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            environment["DOTNET_ROOT"] = dotnetRoot;
        }

        return environment;
    }
}
