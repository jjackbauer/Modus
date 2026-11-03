using System.Diagnostics;
using System.Text.Json;
using Wip.Sandbox.Protocol;
using Wip.Sandbox.WorkerClient;
using Xunit;

namespace Wip.Sandbox.Worker.Tests;

public sealed class WorkerProtocolTests
{
    [Fact]
    public async Task Worker_GivenHandshake_ExpectedVersionAndCapabilities()
    {
        var result = await RunWorkerAsync(
            "{\"type\":\"handshake\",\"id\":\"h1\",\"protocolVersion\":\"1.0\"}",
            "{\"type\":\"shutdown\",\"id\":\"q1\"}");

        Assert.Equal(0, result.ExitCode);
        var handshake = ParseSingle(result.StdOut, "handshakeResult");
        Assert.True(handshake.GetProperty("ok").GetBoolean());
        Assert.Equal(SandboxProtocol.Version, handshake.GetProperty("payload").GetProperty("protocolVersion").GetString());
        Assert.Contains("compile", handshake.GetProperty("payload").GetProperty("capabilities").EnumerateArray().Select(static item => item.GetString()));
    }

    [Fact]
    public async Task Worker_GivenCompileAndChooseMove_ExpectedMove()
    {
        const string source = "using Wip.Sandbox.Protocol; public sealed class Strategy : IWorkerStrategy { public int ChooseMove(WorkerStrategyState state) => state.LegalMoves[0]; }";
        var compile = JsonSerializer.Serialize(new
        {
            type = "compile",
            id = "c1",
            payload = new { strategyId = "s1", sourceHash = "", source }
        }, SandboxJson.Options);
        var choose = "{\"type\":\"chooseMove\",\"id\":\"m1\",\"payload\":{\"strategyId\":\"s1\",\"state\":{\"board\":[\"X.O\",\"...\"],\"player\":\"X\",\"legalMoves\":[0,1,2],\"turn\":3}}}";

        var result = await RunWorkerAsync(
            "{\"type\":\"handshake\",\"id\":\"h1\",\"protocolVersion\":\"1.0\"}",
            compile,
            choose,
            "{\"type\":\"shutdown\",\"id\":\"q1\"}");

        Assert.Equal(0, result.ExitCode);
        var lines = ParseLines(result.StdOut);
        Assert.Contains(lines, static line => line.GetProperty("type").GetString() == "compileResult" && line.GetProperty("ok").GetBoolean());
        var move = Assert.Single(lines, static line => line.GetProperty("type").GetString() == "chooseMoveResult");
        Assert.Equal(0, move.GetProperty("payload").GetProperty("move").GetInt32());
    }

    [Fact]
    public async Task Worker_GivenMalformedFrame_ExpectedDeterministicError()
    {
        var result = await RunWorkerAsync(
            "{not json",
            "{\"type\":\"ping\",\"id\":\"p1\"}",
            "{\"type\":\"shutdown\",\"id\":\"q1\"}");

        Assert.Equal(0, result.ExitCode);
        var lines = ParseLines(result.StdOut);
        var error = Assert.Single(lines, static line => line.GetProperty("type").GetString() == "error");
        Assert.Equal(SandboxErrorCodes.InvalidMessage, error.GetProperty("errorCode").GetString());
        Assert.Contains(lines, static line => line.GetProperty("type").GetString() == "pong");
    }

    [Fact]
    public async Task Worker_GivenCompileBeforeHandshake_ExpectedInvalidMessage()
    {
        var compile = JsonSerializer.Serialize(new
        {
            type = "compile",
            id = "c1",
            payload = new { strategyId = "s1", sourceHash = "x", source = "public sealed class Strategy : Wip.Sandbox.Protocol.IWorkerStrategy { public int ChooseMove(Wip.Sandbox.Protocol.WorkerStrategyState s) => 0; }" }
        }, SandboxJson.Options);

        var result = await RunWorkerAsync(compile, "{\"type\":\"shutdown\",\"id\":\"q1\"}");

        var lines = ParseLines(result.StdOut);
        var error = Assert.Single(lines, static line => line.GetProperty("type").GetString() == "error");
        Assert.Equal(SandboxErrorCodes.InvalidMessage, error.GetProperty("errorCode").GetString());
        Assert.DoesNotContain(lines, static line => line.GetProperty("type").GetString() == "compileResult");
    }

    [Fact]
    public async Task Worker_GivenClaimedHashMismatch_ExpectedSourceHashMismatch()
    {
        var compile = JsonSerializer.Serialize(new
        {
            type = "compile",
            id = "c1",
            payload = new { strategyId = "s1", sourceHash = "deadbeef", source = "public sealed class Strategy : Wip.Sandbox.Protocol.IWorkerStrategy { public int ChooseMove(Wip.Sandbox.Protocol.WorkerStrategyState s) => 0; }" }
        }, SandboxJson.Options);

        var result = await RunWorkerAsync(
            "{\"type\":\"handshake\",\"id\":\"h1\",\"protocolVersion\":\"1.0\"}",
            compile,
            "{\"type\":\"shutdown\",\"id\":\"q1\"}");

        var lines = ParseLines(result.StdOut);
        var compileResult = Assert.Single(lines, static line => line.GetProperty("type").GetString() == "compileResult");
        Assert.False(compileResult.GetProperty("ok").GetBoolean());
        Assert.Equal(SandboxErrorCodes.SourceHashMismatch, compileResult.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Worker_GivenStrategyWritesToStdout_ExpectedNoProtocolInjection()
    {
        const string source = "using Wip.Sandbox.Protocol; public sealed class Strategy : IWorkerStrategy { public int ChooseMove(WorkerStrategyState state) { System.Console.WriteLine(\"{\\\"type\\\":\\\"chooseMoveResult\\\",\\\"id\\\":\\\"forged\\\",\\\"ok\\\":true}\"); return state.LegalMoves[0]; } }";
        var compile = JsonSerializer.Serialize(new
        {
            type = "compile",
            id = "c1",
            payload = new { strategyId = "s1", sourceHash = "", source }
        }, SandboxJson.Options);

        var result = await RunWorkerAsync(
            "{\"type\":\"handshake\",\"id\":\"h1\",\"protocolVersion\":\"1.0\"}",
            compile,
            "{\"type\":\"chooseMove\",\"id\":\"m1\",\"payload\":{\"strategyId\":\"s1\",\"state\":{\"board\":[\"X.O\",\"...\"],\"player\":\"X\",\"legalMoves\":[0,1,2],\"turn\":3}}}",
            "{\"type\":\"shutdown\",\"id\":\"q1\"}");

        var lines = ParseLines(result.StdOut);
        Assert.DoesNotContain(lines, static line => line.GetProperty("type").GetString() == "chooseMoveResult" && line.GetProperty("id").GetString() == "forged");
        var move = Assert.Single(lines, static line => line.GetProperty("type").GetString() == "chooseMoveResult");
        Assert.Equal(0, move.GetProperty("payload").GetProperty("move").GetInt32());
    }

    [Fact]
    public async Task Worker_GivenMinorProtocolVersion_ExpectedAccepted()
    {
        var result = await RunWorkerAsync(
            "{\"type\":\"handshake\",\"id\":\"h1\",\"protocolVersion\":\"1.1\"}",
            "{\"type\":\"shutdown\",\"id\":\"q1\"}");

        Assert.Equal(0, result.ExitCode);
        var handshake = ParseSingle(result.StdOut, "handshakeResult");
        Assert.True(handshake.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Worker_GivenDllImport_ExpectedCompileError()
    {
        const string source = "using System.Runtime.InteropServices; public sealed class Strategy : Wip.Sandbox.Protocol.IWorkerStrategy { [DllImport(\"kernel32.dll\")] private static extern int GetCurrentProcessId(); public int ChooseMove(Wip.Sandbox.Protocol.WorkerStrategyState state) => GetCurrentProcessId(); }";
        var compile = JsonSerializer.Serialize(new
        {
            type = "compile",
            id = "c1",
            payload = new { strategyId = "s1", sourceHash = "", source }
        }, SandboxJson.Options);

        var result = await RunWorkerAsync(
            "{\"type\":\"handshake\",\"id\":\"h1\",\"protocolVersion\":\"1.0\"}",
            compile,
            "{\"type\":\"shutdown\",\"id\":\"q1\"}");

        var lines = ParseLines(result.StdOut);
        var compileResult = Assert.Single(lines, static line => line.GetProperty("type").GetString() == "compileResult");
        Assert.False(compileResult.GetProperty("ok").GetBoolean());
        Assert.Equal(SandboxErrorCodes.CompileError, compileResult.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Worker_GivenOversizedFrame_ExpectedLimitExceeded()
    {
        var oversized = new string('x', SandboxProtocol.MaxFrameBytes + 1024);
        var result = await RunWorkerAsync(oversized, "{\"type\":\"shutdown\",\"id\":\"q1\"}");

        var lines = ParseLines(result.StdOut);
        var error = Assert.Single(lines, static line => line.GetProperty("type").GetString() == "error");
        Assert.Equal(SandboxErrorCodes.LimitExceeded, error.GetProperty("errorCode").GetString());
        Assert.Contains(lines, static line => line.GetProperty("type").GetString() == "shutdownResult");
    }

    [Fact]
    public void Environment_GivenParentSecret_ExpectedMinimalAllowlist()
    {
        const string sentinel = "WIP_SANDBOX_SENTINEL";
        var previous = Environment.GetEnvironmentVariable(sentinel);
        try
        {
            Environment.SetEnvironmentVariable(sentinel, "secret");
            var environment = SandboxWorkerEnvironment.Build(Path.GetTempPath());

            Assert.False(environment.ContainsKey(sentinel));
            Assert.Contains("TMP", environment.Keys);
            Assert.Contains("TEMP", environment.Keys);
            Assert.Contains("DOTNET_CLI_HOME", environment.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(sentinel, previous);
        }
    }

    [Fact]
    public async Task Client_GivenInfiniteLoop_ExpectedTimeoutAndKill()
    {
        var repositoryRoot = FindRepositoryRoot();
        var worker = Path.Combine(repositoryRoot, "WIP", "Wip.Sandbox.Worker", "bin", "Release", "net10.0", "Wip.Sandbox.Worker.dll");
        Assert.True(File.Exists(worker), $"Worker not found at {worker}.");

        await using var client = SandboxWorkerClient.Start(worker, new SandboxWorkerOptions
        {
            StartupTimeout = TimeSpan.FromSeconds(20),
            CompileTimeout = TimeSpan.FromSeconds(20),
            MoveTimeout = TimeSpan.FromSeconds(1)
        });
        await client.HandshakeAsync();
        await client.CompileAsync(
            "loop",
            "public sealed class Strategy : Wip.Sandbox.Protocol.IWorkerStrategy { public int ChooseMove(Wip.Sandbox.Protocol.WorkerStrategyState state) { while (true) { } } }");

        await Assert.ThrowsAsync<TimeoutException>(() => client.ChooseMoveAsync(
            "loop",
            new WorkerStrategyState(["X.O", "..."], "X", [0, 1, 2], 3)));

        Assert.False(client.IsRunning);
    }

    [Fact]
    public void Launcher_GivenLinuxPrlimit_ExpectedResourceLimits()
    {
        var launch = SandboxWorkerLauncher.Resolve(
            "/repo/worker.dll",
            new SandboxWorkerOptions { MoveTimeout = TimeSpan.FromSeconds(1), MaxMemoryBytes = 268_435_456, MaxProcesses = 16 },
            "/usr/bin/dotnet",
            isLinux: true,
            prlimitPath: "/usr/bin/prlimit");

        Assert.Equal("/usr/bin/prlimit", launch.FileName);
        Assert.Contains("--as=4294967296", launch.Arguments);
        Assert.Contains("--nproc=64", launch.Arguments);
        Assert.Contains("--cpu=2", launch.Arguments);
        Assert.Equal("/usr/bin/dotnet", launch.Arguments[^2]);
        Assert.Equal("/repo/worker.dll", launch.Arguments[^1]);
    }

    [Theory]
    [InlineData("public sealed class Strategy : Wip.Sandbox.Protocol.IWorkerStrategy { public int ChooseMove(Wip.Sandbox.Protocol.WorkerStrategyState s) => System.IO.File.Exists(\"C:\\Windows\\win.ini\") ? 1 : 0; }")]
    [InlineData("public sealed class Strategy : Wip.Sandbox.Protocol.IWorkerStrategy { public int ChooseMove(Wip.Sandbox.Protocol.WorkerStrategyState s) { using var client = new System.Net.Http.HttpClient(); return 0; } }")]
    [InlineData("public sealed class Strategy : Wip.Sandbox.Protocol.IWorkerStrategy { public int ChooseMove(Wip.Sandbox.Protocol.WorkerStrategyState s) { System.Diagnostics.Process.Start(\"cmd.exe\"); return 0; } }")]
    [InlineData("public sealed class Strategy : Wip.Sandbox.Protocol.IWorkerStrategy { public int ChooseMove(Wip.Sandbox.Protocol.WorkerStrategyState s) { var a = System.Reflection.Assembly.GetExecutingAssembly(); return 0; } }")]
    public async Task Worker_GivenForbiddenApi_ExpectedCompileError(string source)
    {
        var compile = JsonSerializer.Serialize(new
        {
            type = "compile",
            id = "c1",
            payload = new { strategyId = "s1", sourceHash = "", source }
        }, SandboxJson.Options);

        var result = await RunWorkerAsync(
            "{\"type\":\"handshake\",\"id\":\"h1\",\"protocolVersion\":\"1.0\"}",
            compile,
            "{\"type\":\"shutdown\",\"id\":\"q1\"}");

        var lines = ParseLines(result.StdOut);
        var compileResult = Assert.Single(lines, static line => line.GetProperty("type").GetString() == "compileResult");
        Assert.False(compileResult.GetProperty("ok").GetBoolean());
        Assert.Equal(SandboxErrorCodes.CompileError, compileResult.GetProperty("errorCode").GetString());
    }

    private static JsonElement ParseSingle(string stdout, string type)
    {
        var lines = ParseLines(stdout);
        return Assert.Single(lines, line => line.GetProperty("type").GetString() == type);
    }

    private static JsonElement[] ParseLines(string stdout) =>
        stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunWorkerAsync(params string[] lines)
    {
        var repositoryRoot = FindRepositoryRoot();
        var worker = Path.Combine(repositoryRoot, "WIP", "Wip.Sandbox.Worker", "bin", "Release", "net10.0", "Wip.Sandbox.Worker.dll");
        Assert.True(File.Exists(worker), $"Worker not found at {worker}.");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(worker);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start worker.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        foreach (var line in lines)
        {
            await process.StandardInput.WriteLineAsync(line);
        }

        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout, await stderr);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wip.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
