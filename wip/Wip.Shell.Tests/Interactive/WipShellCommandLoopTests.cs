using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Builder;
using Wip.Modus.Hosting;
using Wip.Runtime.Runtime;
using Wip.Shell.Interactive;
using Xunit;

namespace Wip.Shell.Tests.Interactive;

public sealed class WipShellCommandLoopTests
{
    private const string ChecklistItem = "Replace the transition-driven shell UX with the MVP command vocabulary and prompt behavior: `init`, `repo`, `sessions`, `session start \"<task>\"`, `session attach <session-id>`, `use workflow <id>`, `plan`, `run`, `diff`, `checkpoint`, `validate`, `review`, `approve`, `merge`, `artifacts`, `detach`, `archive`, `abort`, and `exit` [prerequisite for the published shell transcript and user-facing MVP flow]";
    private const string RepositoryBootstrapChecklistItem = "Implement repository bootstrap and discovery commands so `init` creates `.wip/config.json`, `.wip/sessions/`, `.wip/worktrees/`, and `.wip/plugins/`, while `repo`, `sessions`, and `config` surface deterministic repository truth from disk and effective configuration [depends on shell command vocabulary]";
    private const string WorkflowSelectionChecklistItem = "Wire workflow selection and execution into the interactive shell so `workflows`, `use workflow`, `plan`, and `run` operate on the same builder/runtime registrations already discovered through Modus plugins and produce user-visible stage results plus artifacts [depends on typed builder and runtime workflow compiler already in place]";
    private const string GovernanceChecklistItem = "Surface diff, checkpoint, validation, review, approval, merge, and artifact listing as shell-visible governance operations that render deterministic summaries and persist required artifacts for the active session [depends on runtime promotion and enriched session metadata]";
    private const string ApprovalChecklistItem = "Add explicit approval confirmation and stale-state rejection UX so `approve` prompts the user, `merge` rejects stale diff or target drift with actionable messages, and `review`/`validation` staleness is visible before merge is attempted [depends on review generator, approval token factory, and merge preview primitives]";
    private const string ModelProviderChecklistItem = "Wire PlanOnlyAgent execution path to call a registered model provider when available, while preserving deterministic fallback behavior when no provider is configured [depends on model-provider registration and DeepSeek implementation]";
    private const string DotNetDeveloperFlowChecklistItem = "Extend shell command surface with a first-class .NET developer flow command contract (restore/build/test/validate sequence with deterministic usage guidance) [depends on workspace bootstrap]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task Help_GivenFreshShell_ListsMvpGlobalAndSessionCommandsWithoutTransitionEscapeHatch()
    {
        var orchestrator = BuildOrchestrator();
        using var reader = new StringReader("help\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Available commands:", output, StringComparison.Ordinal);
        Assert.Contains("init", output, StringComparison.Ordinal);
        Assert.Contains("repo", output, StringComparison.Ordinal);
        Assert.Contains("config", output, StringComparison.Ordinal);
        Assert.Contains("sessions", output, StringComparison.Ordinal);
        Assert.Contains("session start \"<task>\"", output, StringComparison.Ordinal);
        Assert.Contains("session attach <session-id>", output, StringComparison.Ordinal);
        Assert.Contains("use workflow <id>", output, StringComparison.Ordinal);
        Assert.Contains("plan", output, StringComparison.Ordinal);
        Assert.Contains("run", output, StringComparison.Ordinal);
        Assert.Contains("diff", output, StringComparison.Ordinal);
        Assert.Contains("checkpoint", output, StringComparison.Ordinal);
        Assert.Contains("validate", output, StringComparison.Ordinal);
        Assert.Contains("review", output, StringComparison.Ordinal);
        Assert.Contains("approve", output, StringComparison.Ordinal);
        Assert.Contains("merge", output, StringComparison.Ordinal);
        Assert.Contains("artifacts", output, StringComparison.Ordinal);
        Assert.Contains("detach", output, StringComparison.Ordinal);
        Assert.Contains("archive", output, StringComparison.Ordinal);
        Assert.Contains("abort", output, StringComparison.Ordinal);
        Assert.Contains("exit", output, StringComparison.Ordinal);
        Assert.DoesNotContain("transition", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("start <workflow-id>", output, StringComparison.Ordinal);
        Assert.DoesNotContain("attach <repository-path>", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task PromptRendering_GivenNoActiveSession_ExpectedGlobalPrompt()
    {
        var orchestrator = BuildOrchestrator();
        using var reader = new StringReader("exit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Product:", output, StringComparison.Ordinal);
        Assert.Contains("Hint: run 'help' to list available commands.", output, StringComparison.Ordinal);
        Assert.Contains("wip> ", output, StringComparison.Ordinal);
        Assert.DoesNotContain("wip[", output, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(output, "wip> "));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task PromptRendering_GivenAttachedSession_ExpectedPromptIncludesSessionIdAcrossPlanRunValidateReviewAndMergeCommands()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var orchestrator = BuildOrchestrator();
            using var reader = new StringReader("init\n"
                + "use workflow workflow.linear\n"
                + "session start \"Ship MVP prompt flow\""
                + "\nplan\ndiff\nvalidate\nreview\napprove\nyes\nmerge\narchive\nexit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, CreateBuilderWithWorkflows(), repositoryPath);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("Repository initialized:", output, StringComparison.Ordinal);
            Assert.Contains("Session started:", output, StringComparison.Ordinal);
            Assert.Contains("Plan generated:", output, StringComparison.Ordinal);
            Assert.Contains("Diff context:", output, StringComparison.Ordinal);
            Assert.Contains("worktree files:", output, StringComparison.Ordinal);
            Assert.Contains("Command 'validate' completed", output, StringComparison.Ordinal);
            Assert.Contains("Command 'review' completed", output, StringComparison.Ordinal);
            Assert.Contains("Command 'approve' completed", output, StringComparison.Ordinal);
            Assert.Contains("Command 'merge' completed", output, StringComparison.Ordinal);
            Assert.Contains("Session archived from shell:", output, StringComparison.Ordinal);
            Assert.Contains("wip[", output, StringComparison.Ordinal);
            Assert.Contains("]> ", output, StringComparison.Ordinal);
            Assert.True(CountOccurrences(output, "wip[") >= 5);
            Assert.EndsWith("wip>", output.TrimEnd(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task PromptRendering_GivenNoActiveSession_ExpectedGlobalPromptAndSessionCommandsRejectedWithGuidance()
    {
        var orchestrator = BuildOrchestrator();
        using var reader = new StringReader("plan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Product:", output, StringComparison.Ordinal);
        Assert.Contains("wip> ", output, StringComparison.Ordinal);
        Assert.Contains("requires an active session", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session start \"<task>\"", output, StringComparison.Ordinal);
        Assert.Contains("session attach <session-id>", output, StringComparison.Ordinal);
        Assert.DoesNotContain("transition", output, StringComparison.OrdinalIgnoreCase);
        Assert.True(CountOccurrences(output, "wip> ") >= 2);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task Validate_GivenNoActiveSession_ReturnsDeterministicGuidanceWithoutStateMutation()
    {
        var orchestrator = BuildOrchestrator();
        using var reader = new StringReader("validate\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Command 'validate' requires an active session.", output, StringComparison.Ordinal);
        Assert.Contains("Start a session with: session start \"<task>\"", output, StringComparison.Ordinal);
        Assert.Contains("Attach a session with: session attach <session-id>", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Command 'validate' completed", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Session started:", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", DotNetDeveloperFlowChecklistItem)]
    public async Task Help_GivenDotNetFlowSurface_ExpectedCommandCatalogIncludesRuntimeSupportedCommandsOnly()
    {
        var orchestrator = BuildOrchestrator();
        using var reader = new StringReader("help\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("dotnet <restore|build|test|validate>", output, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", output, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet pack", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", DotNetDeveloperFlowChecklistItem)]
    public async Task RunAsync_GivenMalformedDotNetFlowCommand_ExpectedUsageGuidanceWithoutSessionMutation()
    {
        var orchestrator = BuildOrchestrator();
        using var reader = new StringReader("dotnet publish\ndotnet\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: dotnet <restore|build|test|validate>", output, StringComparison.Ordinal);
        Assert.Contains("Sequence: dotnet restore -> dotnet build -> dotnet test -> dotnet validate", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Session started:", output, StringComparison.Ordinal);
        Assert.DoesNotContain("DotNet flow stage completed", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", DotNetDeveloperFlowChecklistItem)]
    public async Task RunAsync_GivenRestoreBuildTestValidateCommands_ExpectedShellRoutesEachCommandToDeterministicRuntimeStage()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var orchestrator = BuildOrchestrator();
            using var reader = new StringReader("init\nsession start \"Execute dotnet flow\"\nplan\ndotnet restore\ndotnet build\ndotnet test\ndotnet validate\nexit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, repositoryRoot: repositoryPath);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("DotNet flow stage completed: stage=restore mode=simulated", output, StringComparison.Ordinal);
            Assert.Contains("DotNet flow stage completed: stage=build mode=simulated", output, StringComparison.Ordinal);
            Assert.Contains("DotNet flow stage completed: stage=test mode=simulated", output, StringComparison.Ordinal);
            Assert.Contains("DotNet flow stage completed: stage=test", output, StringComparison.Ordinal);
            Assert.True(
                output.Contains("Validation completed:", StringComparison.Ordinal)
                || output.Contains("Validation failed:", StringComparison.Ordinal),
                $"Expected validate stage output in transcript:{Environment.NewLine}{output}");

            var restoreIndex = output.IndexOf("DotNet flow stage completed: stage=restore", StringComparison.Ordinal);
            var buildIndex = output.IndexOf("DotNet flow stage completed: stage=build", StringComparison.Ordinal);
            var testIndex = output.IndexOf("DotNet flow stage completed: stage=test", StringComparison.Ordinal);
            var validateIndex = output.IndexOf("Validation completed:", StringComparison.Ordinal);
            if (validateIndex < 0)
                validateIndex = output.IndexOf("Validation failed:", StringComparison.Ordinal);

            Assert.True(restoreIndex >= 0);
            Assert.True(buildIndex > restoreIndex);
            Assert.True(testIndex > buildIndex);
            Assert.True(validateIndex > testIndex);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task UseWorkflow_GivenUnknownWorkflowId_RejectsSelectionWithDeterministicErrorContract()
    {
        var orchestrator = BuildOrchestrator();
        var builder = CreateBuilderWithWorkflows();
        using var reader = new StringReader("use workflow workflow.unknown\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow 'workflow.unknown' is not registered in the active builder.", output, StringComparison.Ordinal);
        Assert.Contains("Use 'workflows' to inspect available workflow IDs.", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Workflow selected:", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", RepositoryBootstrapChecklistItem)]
    public async Task Init_GivenGitRepositoryWithoutWipMetadata_CreatesDeterministicWipFolderLayoutAndConfigFile()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var orchestrator = BuildOrchestrator();
            using var reader = new StringReader("init\nexit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, CreateBuilderWithWorkflows(), repositoryPath);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();
            var configPath = Path.Combine(repositoryPath, ".wip", "config.json");
            using var configDocument = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));

            Assert.Equal(0, exitCode);
            Assert.True(Directory.Exists(Path.Combine(repositoryPath, ".wip", "sessions")));
            Assert.True(Directory.Exists(Path.Combine(repositoryPath, ".wip", "worktrees")));
            Assert.True(Directory.Exists(Path.Combine(repositoryPath, ".wip", "plugins")));
            Assert.True(File.Exists(configPath));
            Assert.Contains($"Repository initialized: {repositoryPath}", output, StringComparison.Ordinal);
            Assert.Contains($"- config: created path={configPath}", output, StringComparison.Ordinal);
            Assert.Equal(repositoryPath, configDocument.RootElement.GetProperty("RepositoryPath").GetString());
            Assert.Equal(repositoryPath, configDocument.RootElement.GetProperty("WorkspaceRoot").GetString());
            Assert.Equal(Path.Combine(repositoryPath, ".wip"), configDocument.RootElement.GetProperty("WipRoot").GetString());
            Assert.Equal(Path.Combine(repositoryPath, ".wip", "sessions"), configDocument.RootElement.GetProperty("SessionsPath").GetString());
            Assert.Equal(Path.Combine(repositoryPath, ".wip", "worktrees"), configDocument.RootElement.GetProperty("WorktreesPath").GetString());
            Assert.Equal(Path.Combine(repositoryPath, ".wip", "plugins"), configDocument.RootElement.GetProperty("PluginsPath").GetString());
            Assert.Equal("workflow.linear", configDocument.RootElement.GetProperty("DefaultWorkflowId").GetString());
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", RepositoryBootstrapChecklistItem)]
    public async Task Repo_GivenInitializedRepository_RendersRepositoryPathPolicyPluginPathsAndWorkspaceRootFromEffectiveConfig()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var startOrchestrator = BuildOrchestrator();
            using var startReader = new StringReader("init\nsession start \"Inspect repo\"\ndetach\nexit\n");
            using var startWriter = new StringWriter();
            var startLoop = new WipShellCommandLoop(startOrchestrator, startReader, startWriter, builder, repositoryPath);

            var startExitCode = await startLoop.RunAsync(CancellationToken.None);
            var startOutput = startWriter.ToString();
            var sessionId = ExtractSessionId(startOutput);
            var configPath = Path.Combine(repositoryPath, ".wip", "config.json");

            Assert.Equal(0, startExitCode);

            using (var configDocument = JsonDocument.Parse(await File.ReadAllTextAsync(configPath)))
            {
                var configRoot = configDocument.RootElement;
                var configOverrideJson = JsonSerializer.Serialize(new
                {
                    RepositoryPath = configRoot.GetProperty("RepositoryPath").GetString(),
                    WorkspaceRoot = configRoot.GetProperty("WorkspaceRoot").GetString(),
                    WipRoot = configRoot.GetProperty("WipRoot").GetString(),
                    SessionsPath = configRoot.GetProperty("SessionsPath").GetString(),
                    WorktreesPath = configRoot.GetProperty("WorktreesPath").GetString(),
                    PluginsPath = configRoot.GetProperty("PluginsPath").GetString(),
                    DefaultWorkflowId = "workflow.safe-change"
                });

                await File.WriteAllTextAsync(configPath, configOverrideJson);
            }

            var inspectOrchestrator = BuildOrchestrator();
            using var inspectReader = new StringReader("repo\nsessions\nconfig\nexit\n");
            using var inspectWriter = new StringWriter();
            var inspectLoop = new WipShellCommandLoop(inspectOrchestrator, inspectReader, inspectWriter, builder, repositoryPath);

            var inspectExitCode = await inspectLoop.RunAsync(CancellationToken.None);
            var inspectOutput = inspectWriter.ToString();

            Assert.Equal(0, inspectExitCode);
            Assert.Contains($"Repository path: {repositoryPath}", inspectOutput, StringComparison.Ordinal);
            Assert.Contains($"workspaceRoot: {repositoryPath}", inspectOutput, StringComparison.Ordinal);
            Assert.Contains($"configPath: {configPath} exists=True", inspectOutput, StringComparison.Ordinal);
            Assert.Contains("defaultWorkflowId: workflow.safe-change", inspectOutput, StringComparison.Ordinal);
            Assert.Contains("selectedWorkflow: workflow.safe-change", inspectOutput, StringComparison.Ordinal);
            Assert.Contains($"pluginsPath: {Path.Combine(repositoryPath, ".wip", "plugins")} exists=True", inspectOutput, StringComparison.Ordinal);
            Assert.Contains("Persisted sessions:", inspectOutput, StringComparison.Ordinal);
            Assert.Contains($"- {sessionId} task=\"Inspect repo\" state=Created workflow=workflow.linear", inspectOutput, StringComparison.Ordinal);
            Assert.Contains("Effective configuration:", inspectOutput, StringComparison.Ordinal);
            Assert.Contains("configSource: disk", inspectOutput, StringComparison.Ordinal);
            Assert.Contains($"sessionsPath: {Path.Combine(repositoryPath, ".wip", "sessions")}", inspectOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", RepositoryBootstrapChecklistItem)]
    public async Task Sessions_GivenPersistedDetachedSessions_ListsIdsTasksStatesAndUpdatedTimestampsFromDisk()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var startOrchestrator = BuildOrchestrator();
            using var startReader = new StringReader("init\nsession start \"Inspect sessions\"\ndetach\nexit\n");
            using var startWriter = new StringWriter();
            var startLoop = new WipShellCommandLoop(startOrchestrator, startReader, startWriter, builder, repositoryPath);

            Assert.Equal(0, await startLoop.RunAsync(CancellationToken.None));

            var sessionId = ExtractSessionId(startWriter.ToString());

            var inspectOrchestrator = BuildOrchestrator();
            using var inspectReader = new StringReader("sessions\nexit\n");
            using var inspectWriter = new StringWriter();
            var inspectLoop = new WipShellCommandLoop(inspectOrchestrator, inspectReader, inspectWriter, builder, repositoryPath);

            var inspectExitCode = await inspectLoop.RunAsync(CancellationToken.None);
            var inspectOutput = inspectWriter.ToString();

            Assert.Equal(0, inspectExitCode);
            Assert.Contains("Persisted sessions:", inspectOutput, StringComparison.Ordinal);
            Assert.Contains($"- {sessionId} task=\"Inspect sessions\" state=Created workflow=workflow.linear updated=", inspectOutput, StringComparison.Ordinal);
            Assert.Matches($"(?s){Regex.Escape(sessionId)} task=\"Inspect sessions\" state=Created workflow=workflow\\.linear updated=\\d{{4}}-\\d{{2}}-\\d{{2}}T", inspectOutput);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task RunAndAbort_GivenPersistedSession_ExecutesWorkflowAndAllowsSessionRecoveryWithoutTransitionCommand()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var startOrchestrator = BuildOrchestrator();
            using var startReader = new StringReader("init\nsession start \"Recoverable task\"\nabort\nexit\n");
            using var startWriter = new StringWriter();
            var startLoop = new WipShellCommandLoop(startOrchestrator, startReader, startWriter, builder, repositoryPath);

            var startExitCode = await startLoop.RunAsync(CancellationToken.None);
            var startOutput = startWriter.ToString();
            var sessionId = ExtractSessionId(startOutput);

            Assert.Equal(0, startExitCode);
            Assert.Contains($"Session aborted from shell: {sessionId} state=Aborted.", startOutput, StringComparison.Ordinal);

            var attachOrchestrator = BuildOrchestrator();
            using var attachReader = new StringReader($"sessions\nsession attach {sessionId}\nrun\nartifacts\nexit\n");
            using var attachWriter = new StringWriter();
            var attachLoop = new WipShellCommandLoop(attachOrchestrator, attachReader, attachWriter, builder, repositoryPath);

            var attachExitCode = await attachLoop.RunAsync(CancellationToken.None);
            var attachOutput = attachWriter.ToString();

            Assert.Equal(0, attachExitCode);
            Assert.Contains("Persisted sessions:", attachOutput, StringComparison.Ordinal);
            Assert.Contains($"- {sessionId} task=\"Recoverable task\" state=Aborted workflow=workflow.linear", attachOutput, StringComparison.Ordinal);
            Assert.Contains($"Session attached: {sessionId}", attachOutput, StringComparison.Ordinal);
            Assert.Contains("Run failed:", attachOutput, StringComparison.Ordinal);
            Assert.Contains("Aborted", attachOutput, StringComparison.Ordinal);
            Assert.Contains($"Artifacts for session {sessionId}:", attachOutput, StringComparison.Ordinal);
            Assert.Contains("session-abort-", attachOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("transition <", attachOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task PluginsCommand_GivenLoadedDiagnostics_PrintsPluginsCapabilitiesAndPermissions()
    {
        var orchestrator = BuildOrchestrator();
        var bridge = new DiagnosticsBridge(new RunManifest(
            DateTimeOffset.UtcNow,
            [
                new PluginManifestEntry(
                    PluginId: "plugin.test",
                    PluginName: "TestPlugin",
                    PluginVersion: "1.2.3",
                    AssemblyName: "Plugin.Test",
                    AssemblyVersion: "1.2.3.0",
                    Capabilities: ["tool.exec", "validator.dotnet"],
                    RequiredPermissions: ["RegisterOperation", "SubscribeEvents"])
            ],
            Array.Empty<WorkflowManifestEntry>()),
            ["Failed to activate plugin type 'BrokenPlugin'."]);

        using var reader = new StringReader("plugins\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer, diagnosticsBridge: bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Loaded plugins:", output, StringComparison.Ordinal);
        Assert.Contains("plugin.test [TestPlugin] v1.2.3", output, StringComparison.Ordinal);
        Assert.Contains("capabilities: tool.exec, validator.dotnet", output, StringComparison.Ordinal);
        Assert.Contains("permissions: RegisterOperation, SubscribeEvents", output, StringComparison.Ordinal);
        Assert.Contains("Plugin diagnostics:", output, StringComparison.Ordinal);
        Assert.Contains("Failed to activate plugin type 'BrokenPlugin'.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkflowsCommand_GivenLoadedDiagnostics_PrintsRegisteredWorkflows()
    {
        var orchestrator = BuildOrchestrator();
        var bridge = new DiagnosticsBridge(new RunManifest(
            DateTimeOffset.UtcNow,
            Array.Empty<PluginManifestEntry>(),
            [
                new WorkflowManifestEntry(
                    WorkflowId: "workflow.safe-change",
                    DisplayName: "Safe Change",
                    RequestType: "Tests.WorkflowRequest",
                    ResultType: "Tests.WorkflowResult")
            ]),
            Array.Empty<string>());

        using var reader = new StringReader("workflows\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer, diagnosticsBridge: bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Registered workflows:", output, StringComparison.Ordinal);
        Assert.Contains("workflow.safe-change [Safe Change]", output, StringComparison.Ordinal);
        Assert.Contains("request=Tests.WorkflowRequest", output, StringComparison.Ordinal);
        Assert.Contains("result=Tests.WorkflowResult", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", WorkflowSelectionChecklistItem)]
    public async Task Workflows_GivenLoadedPluginCapabilities_ListsBuilderRegisteredWorkflowIdsForInteractiveSelection()
    {
        var orchestrator = BuildOrchestrator();
        var builder = CreateBuilderWithWorkflows();
        var bridge = CreateWorkflowDiagnosticsBridge(builder);

        using var reader = new StringReader("workflows\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, diagnosticsBridge: bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Registered workflows:", output, StringComparison.Ordinal);
        Assert.Contains("workflow.linear [Linear workflow]", output, StringComparison.Ordinal);
        Assert.Contains("workflow.safe-change [Safe change workflow]", output, StringComparison.Ordinal);
        Assert.Contains($"request={typeof(WorkflowRequest).FullName}", output, StringComparison.Ordinal);
        Assert.Contains($"request={typeof(SafeChangeWorkflowRequest).FullName}", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", WorkflowSelectionChecklistItem)]
    public async Task UseWorkflow_GivenValidWorkflowId_BindsSelectedWorkflowToActiveSessionAndStatusOutput()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var bridge = CreateWorkflowDiagnosticsBridge(builder);
            var orchestrator = BuildOrchestrator();

            using var reader = new StringReader("init\n"
                + "session start \"Switch workflow\"\n"
                + "use workflow workflow.safe-change\n"
                + "repo\n"
                + "plan\n"
                + "artifacts\n"
                + "exit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, repositoryPath, bridge);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();
            var sessionId = ExtractSessionId(output);
            using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(repositoryPath, ".wip", "sessions", sessionId, "session-state.json")));

            Assert.Equal(0, exitCode);
            Assert.Contains("Workflow selected: workflow.safe-change", output, StringComparison.Ordinal);
            Assert.Contains("Active session workflow: workflow.safe-change state=Created", output, StringComparison.Ordinal);
            Assert.Contains($"activeSession: {sessionId} state=Created workflow=workflow.safe-change", output, StringComparison.Ordinal);
            Assert.Contains("Plan generated: workflow=workflow.safe-change state=Editing", output, StringComparison.Ordinal);
            Assert.Contains("Plan artifact:", output, StringComparison.Ordinal);
            Assert.Contains("Plan steps:", output, StringComparison.Ordinal);
            Assert.Contains("Artifacts for session", output, StringComparison.Ordinal);
            Assert.Contains("agent-plan-", output, StringComparison.Ordinal);
            Assert.Equal("workflow.safe-change", stateDocument.RootElement.GetProperty("WorkflowId").GetString());
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ModelProviderChecklistItem)]
    public async Task Plan_GivenRegisteredModelProvider_UsesProviderPathAndRendersProviderDraft()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            DeterministicPlanModelProvider.Reset(shouldFail: false);

            var builder = CreateBuilderWithWorkflows(includeModelProvider: true);
            var orchestrator = BuildOrchestrator();
            using var reader = new StringReader("init\nsession start \"Use provider plan\"\nplan\nexit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, repositoryPath);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Equal(1, DeterministicPlanModelProvider.ExecutionCount);
            Assert.Contains("Plan generated:", output, StringComparison.Ordinal);
            Assert.Contains("Model provider used: provider=deepseek model=deepseek-chat", output, StringComparison.Ordinal);
            Assert.Contains("- Gather implementation constraints from active workflow and policy context.", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ModelProviderChecklistItem)]
    public async Task Plan_GivenNoRegisteredModelProvider_PreservesDeterministicFallbackPlanBehavior()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            DeterministicPlanModelProvider.Reset(shouldFail: false);

            var builder = CreateBuilderWithWorkflows();
            var orchestrator = BuildOrchestrator();
            using var reader = new StringReader("init\nsession start \"Use fallback plan\"\nplan\nexit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, repositoryPath);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Equal(0, DeterministicPlanModelProvider.ExecutionCount);
            Assert.Contains("Plan generated:", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Model provider used:", output, StringComparison.Ordinal);
            Assert.Contains("- Inspect current worktree state and identify impacted files.", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ModelProviderChecklistItem)]
    public async Task Plan_GivenRegisteredModelProviderFailure_EmitsDeterministicFailureContractAndDoesNotFallback()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            DeterministicPlanModelProvider.Reset(shouldFail: true);

            var builder = CreateBuilderWithWorkflows(includeModelProvider: true);
            var orchestrator = BuildOrchestrator();
            using var reader = new StringReader("init\nsession start \"Fail provider plan\"\nplan\nexit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, repositoryPath);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();
            var sessionId = ExtractSessionId(output);
            var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId, "session-state.json");
            using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));

            Assert.Equal(0, exitCode);
            Assert.Equal(1, DeterministicPlanModelProvider.ExecutionCount);
            Assert.Contains("Plan failed: Model-provider plan generation failed: Deterministic test provider failure.", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Plan generated:", output, StringComparison.Ordinal);
            Assert.Equal("Created", stateDocument.RootElement.GetProperty("State").GetString());
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", WorkflowSelectionChecklistItem)]
    public async Task Run_GivenSelectedWorkflow_ExecutesMappedStagesPersistsArtifactsAndRendersStageSummary()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var bridge = CreateWorkflowDiagnosticsBridge(builder);
            var orchestrator = BuildOrchestrator();

            using var reader = new StringReader("init\n"
                + "workflows\n"
                + "use workflow workflow.safe-change\n"
                + "session start \"Execute safe change\"\n"
                + "run\n"
                + "artifacts\n"
                + "exit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, repositoryPath, bridge);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();
            var sessionId = ExtractSessionId(output);
            using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(repositoryPath, ".wip", "sessions", sessionId, "session-state.json")));

            Assert.Equal(0, exitCode);
            Assert.Contains("workflow.safe-change [Safe change workflow]", output, StringComparison.Ordinal);
            Assert.Contains("Workflow executed: workflow.safe-change", output, StringComparison.Ordinal);
            Assert.Contains("Workflow artifact:", output, StringComparison.Ordinal);
            Assert.Contains("- Plan state=Editing transition=applied", output, StringComparison.Ordinal);
            Assert.Contains($"mappedInput={typeof(SafeChangeWorkflowRequest).FullName}", output, StringComparison.Ordinal);
            Assert.Contains("- Merge state=Merged transition=applied", output, StringComparison.Ordinal);
            Assert.Contains("Active session state: Merged", output, StringComparison.Ordinal);
            Assert.Contains("workflow-execution-", output, StringComparison.Ordinal);
            Assert.Equal("workflow.safe-change", stateDocument.RootElement.GetProperty("WorkflowId").GetString());
            Assert.Equal("Merged", stateDocument.RootElement.GetProperty("State").GetString());
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalChecklistItem)]
    public async Task Approve_GivenUserDeclinesConfirmation_DoesNotCreateApprovalTokenAndLeavesSessionAwaitingApproval()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var orchestrator = BuildOrchestrator();

            using (var startReader = new StringReader("init\nsession start \"Decline approval\"\nplan\nexit\n"))
            using (var startWriter = new StringWriter())
            {
                var startLoop = new WipShellCommandLoop(orchestrator, startReader, startWriter, builder, repositoryPath);
                Assert.Equal(0, await startLoop.RunAsync(CancellationToken.None));

                var sessionId = ExtractSessionId(startWriter.ToString());
                var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId, "session-state.json");
                using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
                var worktreePath = stateDocument.RootElement.GetProperty("WorktreePath").GetString();
                var artifactDirectory = stateDocument.RootElement.GetProperty("ArtifactDirectory").GetString();

                await File.WriteAllTextAsync(Path.Combine(worktreePath!, "approval.txt"), "candidate\n");

                var attachOrchestrator = BuildOrchestrator();
                using var attachReader = new StringReader($"session attach {sessionId}\ndiff\nvalidate\nreview\napprove\nno\nartifacts\nexit\n");
                using var attachWriter = new StringWriter();
                var attachLoop = new WipShellCommandLoop(attachOrchestrator, attachReader, attachWriter, builder, repositoryPath);

                var exitCode = await attachLoop.RunAsync(CancellationToken.None);
                var output = attachWriter.ToString();

                Assert.Equal(0, exitCode);
                Assert.Contains("Approval confirmation required:", output, StringComparison.Ordinal);
                Assert.Contains("reviewStale: False", output, StringComparison.Ordinal);
                Assert.Contains("validation: Passed", output, StringComparison.Ordinal);
                Assert.Contains("Type 'yes' to create an approval token", output, StringComparison.Ordinal);
                Assert.Contains($"Approval cancelled for session {sessionId}.", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Approval completed:", output, StringComparison.Ordinal);

                Assert.False(string.IsNullOrWhiteSpace(artifactDirectory));
                Assert.Empty(Directory.GetFiles(artifactDirectory!, "approval-token-*", SearchOption.TopDirectoryOnly));

                using var refreshedStateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
                Assert.Equal("AwaitingApproval", refreshedStateDocument.RootElement.GetProperty("State").GetString());
                Assert.Equal("AwaitingApproval", refreshedStateDocument.RootElement.GetProperty("ApprovalStatus").GetString());
            }
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalChecklistItem)]
    public async Task Approve_GivenCurrentReviewValidationAndConfirmedPrompt_CreatesApprovalTokenBoundToDiffHashAndTargetCommit()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var orchestrator = BuildOrchestrator();

            using (var startReader = new StringReader("init\nsession start \"Confirm approval\"\nplan\nexit\n"))
            using (var startWriter = new StringWriter())
            {
                var startLoop = new WipShellCommandLoop(orchestrator, startReader, startWriter, builder, repositoryPath);
                Assert.Equal(0, await startLoop.RunAsync(CancellationToken.None));

                var sessionId = ExtractSessionId(startWriter.ToString());
                var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId, "session-state.json");
                using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
                var worktreePath = stateDocument.RootElement.GetProperty("WorktreePath").GetString();
                var artifactDirectory = stateDocument.RootElement.GetProperty("ArtifactDirectory").GetString();
                var targetCommit = stateDocument.RootElement.GetProperty("TargetCommit").GetString();

                await File.WriteAllTextAsync(Path.Combine(worktreePath!, "approved.txt"), "candidate\n");

                var attachOrchestrator = BuildOrchestrator();
                using var attachReader = new StringReader($"session attach {sessionId}\ndiff\nvalidate\nreview\napprove\nyes\nexit\n");
                using var attachWriter = new StringWriter();
                var attachLoop = new WipShellCommandLoop(attachOrchestrator, attachReader, attachWriter, builder, repositoryPath);

                var exitCode = await attachLoop.RunAsync(CancellationToken.None);
                var output = attachWriter.ToString();

                Assert.Equal(0, exitCode);
                Assert.Contains("Approval confirmation required:", output, StringComparison.Ordinal);
                Assert.Contains("reviewStale: False", output, StringComparison.Ordinal);
                Assert.Contains("validation: Passed", output, StringComparison.Ordinal);
                Assert.Contains("Approval completed:", output, StringComparison.Ordinal);
                Assert.Contains("diffHash:", output, StringComparison.Ordinal);
                Assert.Contains("Approval artifact:", output, StringComparison.Ordinal);

                Assert.False(string.IsNullOrWhiteSpace(artifactDirectory));
                var approvalArtifact = Assert.Single(Directory.GetFiles(artifactDirectory!, "approval-token-*", SearchOption.TopDirectoryOnly), static path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));
                using var approvalDocument = JsonDocument.Parse(await File.ReadAllTextAsync(approvalArtifact));
                Assert.Equal(targetCommit, approvalDocument.RootElement.GetProperty("Binding").GetProperty("TargetCommit").GetString());
                Assert.False(string.IsNullOrWhiteSpace(approvalDocument.RootElement.GetProperty("Binding").GetProperty("DiffHash").GetString()));
            }
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalChecklistItem)]
    public async Task Merge_GivenDiffChangedAfterReviewOrApproval_RejectsWithDeterministicStaleReasonAndNoBranchMutation()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var orchestrator = BuildOrchestrator();

            using (var startReader = new StringReader("init\nsession start \"Reject stale diff merge\"\nplan\nexit\n"))
            using (var startWriter = new StringWriter())
            {
                var startLoop = new WipShellCommandLoop(orchestrator, startReader, startWriter, builder, repositoryPath);
                Assert.Equal(0, await startLoop.RunAsync(CancellationToken.None));

                var sessionId = ExtractSessionId(startWriter.ToString());
                var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId, "session-state.json");
                using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
                var worktreePath = stateDocument.RootElement.GetProperty("WorktreePath").GetString();
                var artifactDirectory = stateDocument.RootElement.GetProperty("ArtifactDirectory").GetString();

                await File.WriteAllTextAsync(Path.Combine(worktreePath!, "candidate.txt"), "v1\n");

                var approveOrchestrator = BuildOrchestrator();
                using (var approveReader = new StringReader($"session attach {sessionId}\ndiff\nvalidate\nreview\napprove\nyes\nexit\n"))
                using (var approveWriter = new StringWriter())
                {
                    var approveLoop = new WipShellCommandLoop(approveOrchestrator, approveReader, approveWriter, builder, repositoryPath);
                    Assert.Equal(0, await approveLoop.RunAsync(CancellationToken.None));
                    Assert.Contains("Approval completed:", approveWriter.ToString(), StringComparison.Ordinal);
                }

                await File.AppendAllTextAsync(Path.Combine(worktreePath, "candidate.txt"), "v2\n");

                var mergeOrchestrator = BuildOrchestrator();
                using var mergeReader = new StringReader($"session attach {sessionId}\nmerge\nexit\n");
                using var mergeWriter = new StringWriter();
                var mergeLoop = new WipShellCommandLoop(mergeOrchestrator, mergeReader, mergeWriter, builder, repositoryPath);

                var exitCode = await mergeLoop.RunAsync(CancellationToken.None);
                var output = mergeWriter.ToString();

                Assert.Equal(0, exitCode);
                Assert.Contains("Merge preflight:", output, StringComparison.Ordinal);
                Assert.Contains("reviewStale: True", output, StringComparison.Ordinal);
                Assert.Contains("validationCurrentDiffMatch: False", output, StringComparison.Ordinal);
                Assert.Contains("approvalCurrentDiffMatch: False", output, StringComparison.Ordinal);
                Assert.Contains("targetBaselineMatchesApproval: True", output, StringComparison.Ordinal);
                Assert.Contains("Merge failed:", output, StringComparison.Ordinal);
                Assert.Contains("approval is stale", output, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Run 'diff', 'validate', 'review', and 'approve' again.", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Merge completed:", output, StringComparison.Ordinal);

                Assert.False(string.IsNullOrWhiteSpace(artifactDirectory));
                Assert.Empty(Directory.GetFiles(artifactDirectory!, "merge-result-*", SearchOption.TopDirectoryOnly));

                using var refreshedStateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
                Assert.Equal("Approved", refreshedStateDocument.RootElement.GetProperty("State").GetString());
            }
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalChecklistItem)]
    public async Task Merge_GivenTargetBranchDriftAfterApproval_RejectsWithRefreshGuidanceAndPreservesSessionForRevalidation()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var orchestrator = BuildOrchestrator();

            using (var startReader = new StringReader("init\nsession start \"Reject target drift merge\"\nplan\nexit\n"))
            using (var startWriter = new StringWriter())
            {
                var startLoop = new WipShellCommandLoop(orchestrator, startReader, startWriter, builder, repositoryPath);
                Assert.Equal(0, await startLoop.RunAsync(CancellationToken.None));

                var sessionId = ExtractSessionId(startWriter.ToString());
                var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId, "session-state.json");
                using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
                var worktreePath = stateDocument.RootElement.GetProperty("WorktreePath").GetString();
                var artifactDirectory = stateDocument.RootElement.GetProperty("ArtifactDirectory").GetString();

                await File.WriteAllTextAsync(Path.Combine(worktreePath!, "candidate.txt"), "drift\n");

                var approveOrchestrator = BuildOrchestrator();
                using (var approveReader = new StringReader($"session attach {sessionId}\ndiff\nvalidate\nreview\napprove\nyes\nexit\n"))
                using (var approveWriter = new StringWriter())
                {
                    var approveLoop = new WipShellCommandLoop(approveOrchestrator, approveReader, approveWriter, builder, repositoryPath);
                    Assert.Equal(0, await approveLoop.RunAsync(CancellationToken.None));
                    Assert.Contains("Approval completed:", approveWriter.ToString(), StringComparison.Ordinal);
                }

                await OverwritePersistedTargetCommitAsync(statePath, "target-commit-drifted");

                var mergeOrchestrator = BuildOrchestrator();
                using var mergeReader = new StringReader($"session attach {sessionId}\nmerge\nexit\n");
                using var mergeWriter = new StringWriter();
                var mergeLoop = new WipShellCommandLoop(mergeOrchestrator, mergeReader, mergeWriter, builder, repositoryPath);

                var exitCode = await mergeLoop.RunAsync(CancellationToken.None);
                var output = mergeWriter.ToString();

                Assert.Equal(0, exitCode);
                Assert.Contains("Merge preflight:", output, StringComparison.Ordinal);
                Assert.Contains("reviewStale: False", output, StringComparison.Ordinal);
                Assert.Contains("validationCurrentDiffMatch: True", output, StringComparison.Ordinal);
                Assert.Contains("approvalCurrentDiffMatch: True", output, StringComparison.Ordinal);
                Assert.Contains("targetBaselineMatchesApproval: False", output, StringComparison.Ordinal);
                Assert.Contains("Merge failed:", output, StringComparison.Ordinal);
                Assert.Contains("target branch drift", output, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Target branch drift recovery requires rebase, revalidate, and refresh before retrying merge.", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Merge completed:", output, StringComparison.Ordinal);

                Assert.False(string.IsNullOrWhiteSpace(artifactDirectory));
                Assert.Empty(Directory.GetFiles(artifactDirectory!, "merge-result-*", SearchOption.TopDirectoryOnly));

                using var refreshedStateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
                Assert.Equal("Approved", refreshedStateDocument.RootElement.GetProperty("State").GetString());
            }
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", GovernanceChecklistItem)]
    public async Task Diff_GivenModifiedWorktree_PrintsChangedFilesAndPersistsWorkspaceDiffAndDiffHashArtifacts()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var orchestrator = BuildOrchestrator();

            using (var startReader = new StringReader("init\nsession start \"Collect governance diff\"\nplan\nexit\n"))
            using (var startWriter = new StringWriter())
            {
                var startLoop = new WipShellCommandLoop(orchestrator, startReader, startWriter, builder, repositoryPath);
                Assert.Equal(0, await startLoop.RunAsync(CancellationToken.None));

                var sessionId = ExtractSessionId(startWriter.ToString());
                var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId, "session-state.json");
                using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
                var worktreePath = stateDocument.RootElement.GetProperty("WorktreePath").GetString();
                var artifactDirectory = stateDocument.RootElement.GetProperty("ArtifactDirectory").GetString();

                Assert.False(string.IsNullOrWhiteSpace(worktreePath));
                Assert.False(string.IsNullOrWhiteSpace(artifactDirectory));
                var artifactDirectoryPath = artifactDirectory!;

                await File.WriteAllTextAsync(Path.Combine(worktreePath!, "README.md"), "governance diff evidence\n");

                var attachOrchestrator = BuildOrchestrator();
                using var attachReader = new StringReader($"session attach {sessionId}\ndiff\nartifacts\nexit\n");
                using var attachWriter = new StringWriter();
                var attachLoop = new WipShellCommandLoop(attachOrchestrator, attachReader, attachWriter, builder, repositoryPath);

                var exitCode = await attachLoop.RunAsync(CancellationToken.None);
                var output = attachWriter.ToString();

                Assert.Equal(0, exitCode);
                Assert.Contains($"Diff context: session={sessionId}", output, StringComparison.Ordinal);
                Assert.Contains("diffHash:", output, StringComparison.Ordinal);
                Assert.Contains("Diff artifact:", output, StringComparison.Ordinal);
                Assert.Contains("Diff summary artifact:", output, StringComparison.Ordinal);
                Assert.Contains("Changed files:", output, StringComparison.Ordinal);
                Assert.Contains("- README.md", output, StringComparison.Ordinal);
                Assert.Contains("id=workspace-diff-", output, StringComparison.Ordinal);
                Assert.Contains("id=diff-summary-", output, StringComparison.Ordinal);
                Assert.Contains("type=Patch", output, StringComparison.Ordinal);
                Assert.Contains("type=Json", output, StringComparison.Ordinal);

                var diffPatch = Assert.Single(Directory.GetFiles(artifactDirectoryPath, "workspace-diff-*", SearchOption.TopDirectoryOnly), static path => path.EndsWith(".patch", StringComparison.OrdinalIgnoreCase));
                var diffSummary = Assert.Single(Directory.GetFiles(artifactDirectoryPath, "diff-summary-*", SearchOption.TopDirectoryOnly), static path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));
                Assert.Contains("README.md", await File.ReadAllTextAsync(diffPatch), StringComparison.Ordinal);

                using var diffSummaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(diffSummary));
                Assert.Contains("README.md", diffSummaryDocument.RootElement.GetProperty("ChangedFiles").EnumerateArray().Select(static item => item.GetString()).ToArray());
                Assert.False(string.IsNullOrWhiteSpace(diffSummaryDocument.RootElement.GetProperty("DiffHash").GetString()));
            }
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", GovernanceChecklistItem)]
    public async Task Review_GivenPassingValidationAndCurrentDiff_WritesMarkdownReportWithStalenessStatusAndRelevantArtifacts()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var orchestrator = BuildOrchestrator();

            using (var startReader = new StringReader("init\nsession start \"Generate review evidence\"\nplan\nexit\n"))
            using (var startWriter = new StringWriter())
            {
                var startLoop = new WipShellCommandLoop(orchestrator, startReader, startWriter, builder, repositoryPath);
                Assert.Equal(0, await startLoop.RunAsync(CancellationToken.None));

                var sessionId = ExtractSessionId(startWriter.ToString());
                var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId, "session-state.json");
                using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
                var worktreePath = stateDocument.RootElement.GetProperty("WorktreePath").GetString();
                var artifactDirectory = stateDocument.RootElement.GetProperty("ArtifactDirectory").GetString();

                await File.WriteAllTextAsync(Path.Combine(worktreePath!, "src.txt"), "review me\n");

                var attachOrchestrator = BuildOrchestrator();
                using var attachReader = new StringReader($"session attach {sessionId}\ndiff\nvalidate\nreview\nexit\n");
                using var attachWriter = new StringWriter();
                var attachLoop = new WipShellCommandLoop(attachOrchestrator, attachReader, attachWriter, builder, repositoryPath);

                var exitCode = await attachLoop.RunAsync(CancellationToken.None);
                var output = attachWriter.ToString();

                Assert.Equal(0, exitCode);
                Assert.Contains("Validation completed:", output, StringComparison.Ordinal);
                Assert.Contains("Validation artifact:", output, StringComparison.Ordinal);
                Assert.Contains("Review generated:", output, StringComparison.Ordinal);
                Assert.Contains("Review artifact:", output, StringComparison.Ordinal);
                Assert.Contains("stale=False", output, StringComparison.Ordinal);
                Assert.Contains("validation=Passed", output, StringComparison.Ordinal);

                Assert.False(string.IsNullOrWhiteSpace(artifactDirectory));
                var artifactDirectoryPath = artifactDirectory!;

                var reviewReport = Assert.Single(Directory.GetFiles(artifactDirectoryPath, "review-report-*", SearchOption.TopDirectoryOnly), static path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
                var reviewSummary = Assert.Single(Directory.GetFiles(artifactDirectoryPath, "review-summary-*", SearchOption.TopDirectoryOnly), static path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));
                var validationReport = Assert.Single(Directory.GetFiles(artifactDirectoryPath, "validation-report-*", SearchOption.TopDirectoryOnly), static path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));

                var reviewMarkdown = await File.ReadAllTextAsync(reviewReport);
                Assert.Contains("## Staleness", reviewMarkdown, StringComparison.Ordinal);
                Assert.Contains("Stale: No", reviewMarkdown, StringComparison.Ordinal);
                Assert.Contains("- src.txt", reviewMarkdown, StringComparison.Ordinal);

                using var reviewSummaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(reviewSummary));
                Assert.False(reviewSummaryDocument.RootElement.GetProperty("IsStale").GetBoolean());
                Assert.EndsWith(".md", reviewSummaryDocument.RootElement.GetProperty("ReviewArtifactPath").GetString(), StringComparison.Ordinal);

                using var validationDocument = JsonDocument.Parse(await File.ReadAllTextAsync(validationReport));
                Assert.True(validationDocument.RootElement.GetProperty("BuildSucceeded").GetBoolean());
                Assert.True(validationDocument.RootElement.GetProperty("TestSucceeded").GetBoolean());
            }
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", GovernanceChecklistItem)]
    public async Task Artifacts_GivenSessionWithGovernanceEvidence_ListsArtifactIdsKindsProducersAndPathsFromStore()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var builder = CreateBuilderWithWorkflows();
            var orchestrator = BuildOrchestrator();

            using (var startReader = new StringReader("init\nsession start \"List governance artifacts\"\nplan\nexit\n"))
            using (var startWriter = new StringWriter())
            {
                var startLoop = new WipShellCommandLoop(orchestrator, startReader, startWriter, builder, repositoryPath);
                Assert.Equal(0, await startLoop.RunAsync(CancellationToken.None));

                var sessionId = ExtractSessionId(startWriter.ToString());
                var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId, "session-state.json");
                using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
                var worktreePath = stateDocument.RootElement.GetProperty("WorktreePath").GetString();

                await File.WriteAllTextAsync(Path.Combine(worktreePath!, "artifact.txt"), "artifact listing\n");

                var attachOrchestrator = BuildOrchestrator();
                using var attachReader = new StringReader($"session attach {sessionId}\ndiff\ncheckpoint\nvalidate\nreview\napprove\nyes\nmerge\nartifacts\nexit\n");
                using var attachWriter = new StringWriter();
                var attachLoop = new WipShellCommandLoop(attachOrchestrator, attachReader, attachWriter, builder, repositoryPath);

                var exitCode = await attachLoop.RunAsync(CancellationToken.None);
                var output = attachWriter.ToString();

                Assert.Equal(0, exitCode);
                Assert.Contains("Checkpoint artifact:", output, StringComparison.Ordinal);
                Assert.Contains("Approval completed:", output, StringComparison.Ordinal);
                Assert.Contains("Approval artifact:", output, StringComparison.Ordinal);
                Assert.Contains("Merge completed:", output, StringComparison.Ordinal);
                Assert.Contains("Merge artifact:", output, StringComparison.Ordinal);
                Assert.Contains($"Artifacts for session {sessionId}:", output, StringComparison.Ordinal);
                Assert.Contains("id=workspace-diff-", output, StringComparison.Ordinal);
                Assert.Contains("id=checkpoint-", output, StringComparison.Ordinal);
                Assert.Contains("id=validation-report-", output, StringComparison.Ordinal);
                Assert.Contains("id=review-report-", output, StringComparison.Ordinal);
                Assert.Contains("id=approval-token-", output, StringComparison.Ordinal);
                Assert.Contains("id=merge-result-", output, StringComparison.Ordinal);
                Assert.Contains("producer=Wip.Runtime", output, StringComparison.Ordinal);
                Assert.Contains("producer=Wip.Shell", output, StringComparison.Ordinal);
                Assert.Contains("path=.wip/sessions/", output.Replace('\\', '/'), StringComparison.Ordinal);
            }
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    private static WipRuntimeOrchestrator BuildOrchestrator()
        => new(new InMemorySessionStore(), new NoOpSessionEventPublisher());

    private static WipBuilder CreateBuilderWithWorkflows(bool includeModelProvider = false)
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);
        builder.AddPolicy<AllowAllPolicy, AllowAllPolicyRequest>(new PolicyId("policy.shell"));
        builder.AddValidator<DeterministicShellValidator, ShellValidationRequest, ShellValidationResult>(
            capabilityId: new CapabilityId("validator.shell.tests"),
            displayName: "Deterministic shell validator");
        builder.AddWorkflow<LinearWorkflow, WorkflowRequest, WorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");
        builder.AddWorkflow<SafeChangeWorkflow, SafeChangeWorkflowRequest, SafeChangeWorkflowResult>(
            workflowId: new WorkflowId("workflow.safe-change"),
            displayName: "Safe change workflow");

        if (includeModelProvider)
        {
            builder.AddModelProvider<DeterministicPlanModelProvider, DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>(
                capabilityId: new CapabilityId("provider.deepseek.tests"),
                displayName: "Deterministic DeepSeek provider");
        }

        return builder;
    }

    private static DiagnosticsBridge CreateWorkflowDiagnosticsBridge(WipBuilder builder)
        => new(
            new RunManifest(
                DateTimeOffset.UtcNow,
                [
                    new PluginManifestEntry(
                        PluginId: "plugin.workflow.tests",
                        PluginName: "WorkflowTestPlugin",
                        PluginVersion: "1.0.0",
                        AssemblyName: "Tests.WorkflowPlugin",
                        AssemblyVersion: "1.0.0.0",
                        Capabilities: ["workflow.exec"],
                        RequiredPermissions: ["RegisterOperation"])
                ],
                builder.WorkflowRegistrations
                    .Select(static registration => new WorkflowManifestEntry(
                        registration.WorkflowId.Value,
                        registration.Descriptor.DisplayName,
                        registration.RequestType.FullName ?? registration.RequestType.Name,
                        registration.ResultType.FullName ?? registration.ResultType.Name))
                    .ToArray()),
            Array.Empty<string>());

    private static string ExtractSessionId(string output)
    {
        var match = Regex.Match(output, "Session started: (?<id>[a-f0-9]{32})", RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Expected session id in output:{Environment.NewLine}{output}");
        return match.Groups["id"].Value;
    }

    private static async Task OverwritePersistedTargetCommitAsync(string statePath, string targetCommit)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
        var root = document.RootElement;
        var payload = new Dictionary<string, object?>();

        foreach (var property in root.EnumerateObject())
            payload[property.Name] = property.Value.Deserialize<object>();

        payload["TargetCommit"] = targetCommit;

        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(payload));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<SessionId, SessionSnapshot> _sessions = new();

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            if (_sessions.TryGetValue(sessionId, out var snapshot))
                return ValueTask.FromResult<SessionSnapshot?>(snapshot);

            return ValueTask.FromResult<SessionSnapshot?>(null);
        }
    }

    private sealed class NoOpSessionEventPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class DiagnosticsBridge : IModusWipBridge
    {
        private readonly RunManifest _runManifest;
        private readonly IReadOnlyList<string> _diagnostics;

        public DiagnosticsBridge(RunManifest runManifest, IReadOnlyList<string> diagnostics)
        {
            _runManifest = runManifest;
            _diagnostics = diagnostics;
        }

        public ValueTask<int> LoadPluginsAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(_runManifest.Plugins.Count);

        public ValueTask StopPluginsAsync(CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public RunManifest GetRunManifest()
            => _runManifest;

        public IReadOnlyList<string> GetLoadDiagnostics()
            => _diagnostics;
    }

    private sealed record WorkflowRequest(string Task);

    private sealed record WorkflowResult(string Outcome);

    private sealed record SafeChangeWorkflowRequest(string Task);

    private sealed record SafeChangeWorkflowResult(string Outcome);

    private sealed record ShellValidationRequest(string Command);

    private sealed record ShellValidationResult(bool IsValid);

    private sealed record AllowAllPolicyRequest(string Operation);

    private sealed class AllowAllPolicy : IPolicy<AllowAllPolicyRequest>
    {
        public PolicyId PolicyId => new("policy.shell");

        public ValueTask<PolicyDecision> EvaluateAsync(
            AllowAllPolicyRequest request,
            PolicyContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(PolicyDecision.Allow());
    }

    private sealed class DeterministicShellValidator : IValidator<ShellValidationRequest, ShellValidationResult>
    {
        public ValueTask<ShellValidationResult> ExecuteAsync(
            ShellValidationRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ShellValidationResult(IsValid: true));
    }

    private sealed class LinearWorkflow : IWorkflow<WorkflowRequest, WorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.linear");

        public ValueTask<WorkflowResult> ExecuteAsync(WorkflowRequest request, WorkflowContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new WorkflowResult(request.Task));
    }

    private sealed class SafeChangeWorkflow : IWorkflow<SafeChangeWorkflowRequest, SafeChangeWorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.safe-change");

        public ValueTask<SafeChangeWorkflowResult> ExecuteAsync(
            SafeChangeWorkflowRequest request,
            WorkflowContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new SafeChangeWorkflowResult($"safe:{request.Task}"));
    }

    private sealed class DeterministicPlanModelProvider : IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>
    {
        private static int _executionCount;
        private static bool _shouldFail;

        public static int ExecutionCount => Volatile.Read(ref _executionCount);

        public static void Reset(bool shouldFail)
        {
            _shouldFail = shouldFail;
            Interlocked.Exchange(ref _executionCount, 0);
        }

        public ValueTask<ModelProviderResponse<DeepSeekChatCompletionResult>> ExecuteAsync(
            ModelProviderRequest<DeepSeekChatCompletionRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executionCount);

            if (_shouldFail)
                throw new InvalidOperationException("Deterministic test provider failure.");

            var payload = new DeepSeekChatCompletionResult(
                "1. Gather implementation constraints from active workflow and policy context.\n2. Apply the smallest change that satisfies the requirement.\n3. Execute targeted build and tests before wider validation.",
                "stop");

            return ValueTask.FromResult(
                new ModelProviderResponse<DeepSeekChatCompletionResult>(
                    payload,
                    providerId: "deepseek",
                    modelId: request.ModelId,
                    correlationId: request.CorrelationId));
        }
    }
}
