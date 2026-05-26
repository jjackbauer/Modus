using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Plugins;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Modus.Hosting;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

public sealed class ShellHostE2EHarnessTests
{
    [Fact]
    public async Task ShellReadme_GivenHelpCommand_OutputListsSupportedCommandsIncludingConfigAndDiagnostics()
    {
        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "help",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Available commands:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("plugins [load|unload], workflows, debug-logs, config, effective-config, exit", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellReadme_GivenInvalidTransitionSyntax_UsageMessageReturnedWithoutStateMutation()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var worktreePath = Path.Combine(fixture.RepositoryPath, ".wip", "worktrees", "usage-path");

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"start workflow.linear {fixture.RepositoryPath} {worktreePath}",
                "status",
                "transition nope",
                "status",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: transition <Created|Editing|Validating|AwaitingApproval|Approved|Merged>", result.StdOut, StringComparison.Ordinal);
        var createdStatusCount = CountOccurrences(result.StdOut, " is Created for workflow workflow.linear.");
        Assert.True(createdStatusCount >= 2, $"Expected session state to remain Created before and after invalid transition. Output: {result.StdOut}");
    }

    [Fact]
    public async Task ShellHostReadme_GivenConfigFileAndCliPluginsPath_CliOverrideWinsInEffectiveConfigurationOutput()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var configPluginsPath = Path.Combine(fixture.RepositoryPath, "plugins-from-config");
        var cliPluginsPath = Path.Combine(fixture.RepositoryPath, "plugins-from-cli");
        var workspaceRoot = Path.Combine(fixture.RepositoryPath, "workspace-root");

        Directory.CreateDirectory(configPluginsPath);
        Directory.CreateDirectory(cliPluginsPath);
        Directory.CreateDirectory(workspaceRoot);

        var configDir = Path.Combine(fixture.RepositoryPath, ".wip");
        Directory.CreateDirectory(configDir);

        var configJson = """
        {
          "pluginsPath": "plugins-from-config",
          "workspaceRoot": "workspace-root",
          "policyId": "policy.from.config",
          "validationCommands": ["dotnet build -c Release", "dotnet test --no-build"]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(configDir, "config.json"), configJson, CancellationToken.None);

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "config",
                "exit"
            ],
            CancellationToken.None,
            args: [cliPluginsPath],
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Effective configuration:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("policy: policy.from.config", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"pluginsPath: {Path.GetFullPath(cliPluginsPath)}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"workspaceRoot: {Path.GetFullPath(workspaceRoot)}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("validationCommands: dotnet build -c Release | dotnet test --no-build", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain($"pluginsPath: {Path.GetFullPath(configPluginsPath)}", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellReadme_GivenUnknownCommand_DeterministicUnknownCommandMessageIncludesHelpHint()
    {
        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "bogus-command",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Unknown command 'bogus-command'. Use 'help' to list commands.", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellReadme_GivenDiagnosticsCommands_DiagnosticsBridgeOutputIsSurfaced()
    {
        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "plugins",
                "workflows",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(
            result.StdOut.Contains("Loaded plugins:", StringComparison.Ordinal)
            || result.StdOut.Contains("No plugins are currently loaded.", StringComparison.Ordinal),
            $"Expected plugin diagnostics bridge output. StdOut: {result.StdOut}");
        Assert.True(
            result.StdOut.Contains("Registered workflows:", StringComparison.Ordinal)
            || result.StdOut.Contains("No workflows are currently registered.", StringComparison.Ordinal),
            $"Expected workflow diagnostics bridge output. StdOut: {result.StdOut}");
    }

    [Fact]
    public async Task ShellProcess_GivenInitToMergeHappyPath_ExpectedArtifactsValidationApprovalAndMergeEvidenceRecorded()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var worktreePath = Path.Combine(fixture.RepositoryPath, ".wip", "worktrees", "approved-path");

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "help",
                $"start workflow.linear {fixture.RepositoryPath} {worktreePath}",
                "transition Editing",
                "transition Validating",
                "transition AwaitingApproval",
                "transition Approved",
                "transition Merged",
                "debug-logs",
                "status",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Session transitioned to Editing.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Session transitioned to Validating.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Session transitioned to AwaitingApproval.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Available commands:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Session started:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Session transitioned to Approved.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Session transitioned to Merged.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("is Merged for workflow workflow.linear.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Host run correlation:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("wip[", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellProcess_GivenUnapprovedMergeAttempt_ExpectedNegativeSafetyGateRejectsUnsafeTransitionPath()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var worktreePath = Path.Combine(fixture.RepositoryPath, ".wip", "worktrees", "safety-path");

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"start workflow.linear {fixture.RepositoryPath} {worktreePath}",
                "transition Editing",
                "transition Validating",
                "transition AwaitingApproval",
                "transition Merged",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Failed to transition session:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Invalid transition", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Expected next state is Approved.", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellProcess_GivenDiffMutationAfterApproval_ExpectedMergeRejectedWithStaleApprovalEvidence()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var worktreePath = Path.Combine(fixture.RepositoryPath, ".wip", "worktrees", "stale-approval-path");

        var approvalRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"start workflow.linear {fixture.RepositoryPath} {worktreePath}",
                "transition Editing",
                "transition Validating",
                "transition AwaitingApproval",
                "transition Approved",
                "status",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, approvalRun.ExitCode);
        Assert.Contains("Session transitioned to Approved.", approvalRun.StdOut, StringComparison.Ordinal);
        var sessionId = ExtractSessionId(approvalRun.StdOut);

        await File.AppendAllTextAsync(
            Path.Combine(fixture.RepositoryPath, "README.md"),
            $"mutation-{Guid.NewGuid():N}{Environment.NewLine}",
            CancellationToken.None);
        await fixture.MarkPersistedSessionStateAsync(sessionId, SessionState.AwaitingApproval);

        var mergeRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"attach {fixture.RepositoryPath} {sessionId}",
                "transition Merged",
                "status",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, mergeRun.ExitCode);
        Assert.Contains("Session attached:", mergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Failed to transition session:", mergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Invalid transition", mergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Expected next state is Approved.", mergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("is AwaitingApproval for workflow workflow.linear.", mergeRun.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Session transitioned to Merged.", mergeRun.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellProcess_GivenAmbiguousTypedInferencePlugin_ExpectedPluginLoadFailureAndShellRemainsUsable()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var pluginPath = await StagePluginAssembliesAsync(CancellationToken.None);

        try
        {
            var result = await ShellHostProcessDriver.ExecuteScriptAsync(
                [
                    "plugins load",
                    "plugins",
                    "help",
                    "exit"
                ],
                CancellationToken.None,
                args: [pluginPath],
                workingDirectory: fixture.RepositoryPath);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Plugins loaded:", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Loaded plugins:", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("wip.e2e.typed-registration", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Plugin diagnostics:", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Failed to activate plugin type", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("AmbiguousInferenceFailurePlugin", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("AmbiguousPlanAgent", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("found multiple", result.StdOut, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Available commands:", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (Directory.Exists(pluginPath))
                    Directory.Delete(pluginPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static Task<string> StagePluginAssembliesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourceDirectory = Path.GetDirectoryName(typeof(ShellHostE2EHarnessTests).Assembly.Location)
            ?? throw new InvalidOperationException("Unable to resolve test assembly directory.");
        var pluginDirectory = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-e2e-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pluginDirectory);

        foreach (var assemblyPath in Directory.EnumerateFiles(sourceDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = Path.Combine(pluginDirectory, Path.GetFileName(assemblyPath));
            File.Copy(assemblyPath, destinationPath, overwrite: true);
        }

        return Task.FromResult(pluginDirectory);
    }

    private static class ShellHostProcessDriver
    {
        public static async Task<ShellProcessResult> ExecuteScriptAsync(
            IReadOnlyList<string> commands,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? args = null,
            string? workingDirectory = null)
        {
            var shellHostAssemblyPath = typeof(WipShellHost).Assembly.Location;
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(shellHostAssemblyPath)
                    ?? throw new InvalidOperationException("Unable to resolve shell host working directory.")
            };
            startInfo.ArgumentList.Add(shellHostAssemblyPath);
            if (args is not null)
            {
                foreach (var arg in args)
                    startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start shell host process.");

            foreach (var command in commands)
            {
                await process.StandardInput.WriteLineAsync(command);
            }

            process.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            var stdOutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stdErrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            var stdOut = await stdOutTask;
            var stdErr = await stdErrTask;

            return new ShellProcessResult(process.ExitCode, stdOut, stdErr);
        }
    }

    private static int CountOccurrences(string value, string token)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token))
            return 0;

        var count = 0;
        var start = 0;
        while (true)
        {
            var index = value.IndexOf(token, start, StringComparison.Ordinal);
            if (index < 0)
                return count;

            count++;
            start = index + token.Length;
        }
    }

    private static string ExtractSessionId(string stdOut)
    {
        const string prefix = "Session started: ";
        using var reader = new StringReader(stdOut);
        while (reader.ReadLine() is { } line)
        {
            var markerIndex = line.IndexOf(prefix, StringComparison.Ordinal);
            if (markerIndex >= 0)
                return line[(markerIndex + prefix.Length)..].Trim();
        }

        throw new InvalidOperationException($"Could not locate session id in shell output. StdOut: {stdOut}");
    }

    private sealed class TempGitRepositoryFixture : IAsyncDisposable
    {
        private TempGitRepositoryFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async ValueTask<TempGitRepositoryFixture> CreateAsync()
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-e2e-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var fixture = new TempGitRepositoryFixture(repositoryPath);
            await fixture.RunGitAsync("init", "--initial-branch=main", ".");
            await fixture.RunGitAsync("config", "user.email", "wip-shell-e2e@example.test");
            await fixture.RunGitAsync("config", "user.name", "Wip Shell E2E Tests");

            await File.WriteAllTextAsync(Path.Combine(repositoryPath, "README.md"), "base\n", CancellationToken.None);
            await fixture.RunGitAsync("add", ".");
            await fixture.RunGitAsync("commit", "-m", "initial commit");

            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(RepositoryPath))
                    Directory.Delete(RepositoryPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            await ValueTask.CompletedTask;
        }

        public async ValueTask MarkPersistedSessionStateAsync(string sessionId, SessionState state)
        {
            var statePath = Path.Combine(RepositoryPath, ".wip", "sessions", sessionId, "session-state.json");
            if (!File.Exists(statePath))
            {
                throw new InvalidOperationException(
                    $"Expected persisted session state at '{statePath}', but no state file was found.");
            }

            var json = await File.ReadAllTextAsync(statePath, CancellationToken.None);
            var previousToken = "\"State\":\"Approved\"";
            var nextToken = $"\"State\":\"{state}\"";

            if (!json.Contains(previousToken, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Expected persisted state token '{previousToken}' in '{statePath}'. Actual payload: {json}");
            }

            json = json.Replace(previousToken, nextToken, StringComparison.Ordinal);
            await File.WriteAllTextAsync(statePath, json, CancellationToken.None);
        }

        private async ValueTask<string> RunGitAsync(params string[] args)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start git process.");

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(CancellationToken.None);
            var stdOut = (await stdOutTask).Trim();
            var stdErr = (await stdErrTask).Trim();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Git command failed ({string.Join(" ", args)}). ExitCode={process.ExitCode}. StdOut={stdOut}. StdErr={stdErr}");
            }

            return stdOut;
        }
    }

    private sealed record ShellProcessResult(int ExitCode, string StdOut, string StdErr);
}

public sealed class TypedRegistrationProofPlugin : IWipHostPluginContract, IPluginLifecycle, IPluginOperationCatalog
{
    public TypedRegistrationProofPlugin()
    {
        var builder = new WipBuilder(new ServiceCollection());
        builder.AddAgent<TypedPlanAgent, TypedPlanRequest, TypedPlanResult>(
            capabilityId: new CapabilityId("wip.e2e.typed-registration.agent"),
            displayName: "Typed registration agent");
    }

    public PluginId PluginId => new("wip.e2e.typed-registration");
    public ContractName ContractName => new("Wip.E2E.TypedRegistration");
    public Version ContractVersion => new(1, 0, 0);

    public IReadOnlyCollection<OperationName> SupportedOperations =>
    [
        new OperationName("wip.e2e.typed-registration.agent")
    ];

    public void Load(PluginLoadContext context)
    {
    }

    public void Start(PluginStartContext context)
    {
    }

    public void Stop(PluginStopContext context)
    {
    }

    public void Unload(PluginUnloadContext context)
    {
    }
}

public sealed class AmbiguousInferenceFailurePlugin : IWipHostPluginContract
{
    public AmbiguousInferenceFailurePlugin()
    {
        var builder = new WipBuilder(new ServiceCollection());
        builder.AddAgent<AmbiguousPlanAgent>(
            capabilityId: new CapabilityId("wip.e2e.ambiguous-inference.agent"),
            displayName: "Ambiguous inference agent");
    }

    public PluginId PluginId => new("wip.e2e.ambiguous-inference");
    public ContractName ContractName => new("Wip.E2E.AmbiguousInference");
    public Version ContractVersion => new(1, 0, 0);
}

public sealed record TypedPlanRequest(string Goal);

public sealed record TypedPlanResult(string Plan);

public sealed record AlternatePlanRequest(string Goal);

public sealed record AlternatePlanResult(string Plan);

public sealed class TypedPlanAgent : IAgent<TypedPlanRequest, TypedPlanResult>
{
    public ValueTask<TypedPlanResult> ExecuteAsync(TypedPlanRequest request, CapabilityContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(new TypedPlanResult($"plan:{request.Goal}"));
}

public sealed class AmbiguousPlanAgent :
    IAgent<TypedPlanRequest, TypedPlanResult>,
    IAgent<AlternatePlanRequest, AlternatePlanResult>
{
    ValueTask<TypedPlanResult> ICapability<TypedPlanRequest, TypedPlanResult>.ExecuteAsync(TypedPlanRequest request, CapabilityContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(new TypedPlanResult($"typed:{request.Goal}"));

    ValueTask<AlternatePlanResult> ICapability<AlternatePlanRequest, AlternatePlanResult>.ExecuteAsync(AlternatePlanRequest request, CapabilityContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(new AlternatePlanResult($"alternate:{request.Goal}"));
}
