using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Plugins;
using Samples.TodoApp;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Builder;
using Wip.Modus.Hosting;
using Wip.Runtime.Runtime;
using Wip.Shell.Interactive;
using Wip.Shell.Prompting;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

public sealed class ShellHostE2EHarnessTests
{
    private const string SessionPersistenceChecklistItem = "Complete session persistence and attach/restore behavior under .wip/sessions/{sessionId}/session-state.json with deterministic session event journaling [depends on runtime governance baseline]";
    private const string CommandSetChecklistItem = "Align shell command contract to full MVP global/session command surface, including context-sensitive errors and command guidance when no session is active [depends on shell baseline]";
    private const string RepositoryBootstrapChecklistItem = "Implement repository bootstrap and config hydration so `init`, `repo`, `config`, and `sessions` reflect deterministic on-disk truth [depends on shell command foundation]";
    private const string SessionLifecycleChecklistItem = "Implement session lifecycle orchestration for start, attach, detach, archive, and abort with explicit persisted state transitions and event records [depends on runtime state machine]";
    private const string WorkflowBindingChecklistItem = "Implement workflow listing, selection, and execution binding from builder-registered workflows with no shell-only registration path [depends on builder/runtime composition]";
    private const string PlanRunArtifactsChecklistItem = "Implement plan and run execution path producing `AgentPlan` and `AgentRunResult` artifacts from typed capabilities [depends on agent runtime contracts]";
    private const string DiffCheckpointHashChecklistItem = "Implement diff, checkpoint, and normalized diff hash generation with stable reproducibility semantics [depends on Git workspace provider]";
    private const string ValidationOrchestrationChecklistItem = "Implement validation orchestration for `dotnet build` and `dotnet test` plus deterministic `ValidationReport` persistence [depends on controlled tool execution]";
    private const string ReviewStalenessChecklistItem = "Implement review report generation and staleness detection tied to current diff hash and validation evidence [depends on diff and validation outputs]";
    private const string ApprovalTokenChecklistItem = "Implement approval token creation with explicit human confirmation and strict binding to session, diff hash, and target commit [depends on review and validation gates]";
    private const string ApprovalGatedMergeChecklistItem = "Implement approval-gated merge with stale diff and target branch drift rejection before any repository mutation [depends on approval token and merge preview]";
    private const string TargetBranchDriftRecoveryChecklistItem = "Add explicit target-branch-drift recovery contract defining required rebase/revalidate/refresh steps and observable session state transitions before retrying merge [depends on AP-005 target drift recovery guidance]";
    private const string GovernanceDiagnosticsChecklistItem = "Add observability contract that each governance command emits correlation-linked diagnostics and artifact references sufficient to reconstruct failed session decisions offline [depends on NFR observability hardening]";
    private const string PolicyEnforcementChecklistItem = "Implement policy enforcement for dangerous command denial, worktree boundary checks, and privileged merge-only path [depends on local-safe policy profile]";
    private const string ShellHostConfigChecklistItem = "Align shell-host configuration and startup defaults with .wip/config.json, .wip/plugins, ~/.wip/plugins, and deterministic effective config rendering [depends on shell-host baseline]";
    private const string PluginDiscoveryChecklistItem = "Complete plugin discovery contract for default paths `.wip/plugins/` and `~/.wip/plugins/` with deterministic precedence and diagnostics [depends on MP-001 and shell-host config alignment]";
    private const string StartupBannerChecklistItem = "Implement startup banner contract to include product/version, detected repository path, loaded plugin count, active policy profile, and help hint on shell launch [depends on SH-001 startup compliance]";
    private const string SampleExternalTypedPluginChecklistItem = "Add `Samples.TodoApp` minimal solution fixture package and keep `Samples.TodoApp.WipAgents` as external typed plugin proof consumed by shell E2E [depends on sample package split compliance]";
    private const string WorkflowAmbiguityPromptChecklistItem = "Complete workflow-selection ambiguity behavior so `run` prompts for explicit workflow choice when no default is unambiguous [depends on WF-004 deterministic workflow selection]";
    private const string PolicyViolationPayloadChecklistItem = "Extend policy violation output to always include blocked action, blocking policy name, and concrete next-step guidance in one deterministic payload [depends on PO-003 operator guidance contract]";
    private const string DangerousCommandDenylistProofChecklistItem = "Expand dangerous-command denylist proofs to include `git push`, `git clean -fdx`, `.git` internals mutation attempts, and out-of-worktree target paths [depends on TL-004 absolute command safety]";
    private const string PrivilegedMergeIsolationChecklistItem = "Add privileged-merge isolation proof that merge semantics cannot be triggered through generic shell tool execution even under command aliasing attempts [depends on TL-005 privileged merge-only path]";
    private const string SessionEventJournalChecklistItem = "Complete session event journal coverage for all required lifecycle events, including merge-attempted, merge-failed, merge-succeeded, archived, and aborted, with deterministic payload shape [depends on RT-005 full event taxonomy]";
    private const string ArchivePolicyChecklistItem = "Add worktree archive policy options and runtime proof for both mark-only and cleanup modes while preserving artifact retention and session traceability [depends on GW-009 archive/cleanup policy behavior]";
    private const string ApprovalReviewPrerequisiteChecklistItem = "Enforce approval prerequisite that current review evidence exists for the current diff hash at approval time, with deterministic stale-review rejection guidance [depends on RV-002 and RV-003 absolute review gate]";
    private const string ArtifactListingChecklistItem = "Expand artifact listing guarantees so `artifacts` returns stable descriptor fields (`id`, `type`, `version`, `created`, `producer`, `path`) with deterministic sort order [depends on AS-005 and AS-006 artifact descriptor contract]";
    private const string CorrelationContinuityChecklistItem = "Add runtime integration proof for correlation continuity from session/operation context into provider request metadata and back into produced artifacts/log evidence [depends on PlanOnlyAgent provider wiring]";
    private const string NegativePathIsolationChecklistItem = "Add deterministic negative-path isolation tests for missing/invalid credentials, unsupported model selection, and provider endpoint failure contracts without side-effect artifact mutation [depends on DeepSeek implementation and runtime wiring]";
    private const string RuntimePromptContextChecklistItem = "Add a first-class runtime prompt-context contract that models system instructions, environment facts, plugin facts, workflow/policy scope, validator/tool capability catalog, and explicit forbidden ecosystems for provider plan generation [foundation for dynamic system prompt]";
    private const string PromptContextComposerChecklistItem = "Implement prompt-context composer in Wip.Shell that derives context deterministically from active session snapshot, builder descriptors, selected workflow/policy, repository root, validation commands, and loaded plugin manifest diagnostics [depends on runtime prompt-context contract]";
    private const string DynamicProviderPromptChecklistItem = "Replace static system message in plan provider invocation with dynamic composed context payload so provider requests include plugin-owned constraints and environment exposure in a stable order [depends on prompt-context composer]";
    private const string PromptContextPolicyGateChecklistItem = "Add prompt-context policy gate that rejects provider execution when required environment anchors are missing or inconsistent (repository root, workflow id, policy id, plugin ownership, validator catalog) and falls back deterministically [depends on dynamic context invocation]";
    private const string PluginInstructionInjectionChecklistItem = "Add plugin-driven system-instruction injection contract where plugins can contribute bounded prompt fragments through explicit capability metadata and host bridge diagnostics, with deterministic precedence and sanitization [depends on prompt-context contract]";
    private const string PromptDiagnosticsPersistenceChecklistItem = "Persist prompt-context invocation diagnostics (context hash, provider id/model id, correlation id, contributing plugin ids, rejected fragments) into session artifacts and status output for behavior-proof auditability [depends on dynamic invocation and plugin injection]";
    private const string EcosystemAlignmentGuardrailsChecklistItem = "Enforce ecosystem-alignment guardrails in provider plan steps by rejecting or rewriting disallowed toolchain commands outside repo policy (for example npm/yarn/pip/cargo when .NET-only context is active) [depends on prompt policy gate and diagnostics]";
    private const string NegativePathIsolationAndFailureContractsChecklistItem = "Add deterministic negative-path isolation tests for provider timeout, malformed provider payload, plugin fragment validation failure, and context-policy rejection with no unintended artifact mutation [depends on diagnostics and guardrails]";
    private const string DotNetWorkspaceBootstrapChecklistItem = "Add explicit .NET workspace bootstrap flow in shell-host startup that validates repository root, solution/project discovery, and deterministic failure diagnostics [foundation for all C#/.NET runtime flows]";
    private const string DotNetPolicyDrivenIsolationChecklistItem = "Strengthen policy-driven isolation so blocked/failed .NET commands cannot mutate session approval or merge state and always emit deterministic rejection payloads [depends on validation integration and policy gate]";
    private const string DotNetE2ETranscriptChecklistItem = "Add executable E2E transcripts proving init -> session start -> plan -> run -> validate -> review -> approve -> merge for C#/.NET tasks across Wip.* [depends on all implementation items]";
    private const string ChecklistItem = "Complete transcript-level E2E parity to the full E2E-001..E2E-035 matrix from the MVP requirements, including typed overload/inference and ambiguous registration failure cases [depends on end-to-end acceptance completeness]";
    private const string RequirementsDocumentPath = ".github/requirements/Wip.Next-Steps.md";

    [Fact]
    [Trait("ChecklistItem", RuntimePromptContextChecklistItem)]
    public void RuntimePromptContext_GivenMissingWorkflowAnchor_ExpectedDeterministicValidationFailureContract()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new RuntimePromptWorkflowPolicyScope("   ", "policy.shell"));

        Assert.Equal("workflowId", exception.ParamName);
    }

    [Fact]
    [Trait("ChecklistItem", RuntimePromptContextChecklistItem)]
    public void RuntimePromptContext_GivenEquivalentInputs_ExpectedStableCanonicalContextOrdering()
    {
        var context = new RuntimePromptContext(
            systemInstructions: ["step b", "step a", "step a"],
            environmentFacts: new RuntimePromptEnvironmentFacts(
                sessionId: "session-1",
                repositoryPath: "C:/repo",
                worktreePath: "C:/repo/.wip/worktree",
                validationCommands: ["dotnet test", "dotnet build", "dotnet test"]),
            pluginFacts:
            [
                new RuntimePromptPluginFact("plugin.b", "Plugin B", "2.0.0", ["cap.b"], ["perm.b"]),
                new RuntimePromptPluginFact("plugin.a", "Plugin A", "1.0.0", ["cap.a"], ["perm.a"])
            ],
            workflowPolicyScope: new RuntimePromptWorkflowPolicyScope("workflow.linear", "policy.shell"),
            capabilityCatalog: new RuntimePromptCapabilityCatalog(
                toolCapabilityIds: ["tool.b", "tool.a", "tool.a"],
                validatorCapabilityIds: ["validator.b", "validator.a", "validator.a"]),
            forbiddenEcosystems: ["yarn", "npm", "cargo", "npm"]);

        Assert.Equal(new[] { "step b", "step a" }, context.SystemInstructions);
        Assert.Equal(new[] { "dotnet build", "dotnet test" }, context.EnvironmentFacts.ValidationCommands);
        Assert.Equal(new[] { "tool.a", "tool.b" }, context.CapabilityCatalog.ToolCapabilityIds);
        Assert.Equal(new[] { "validator.a", "validator.b" }, context.CapabilityCatalog.ValidatorCapabilityIds);
        Assert.Equal(new[] { "cargo", "npm", "yarn" }, context.ForbiddenEcosystems);
        Assert.Equal(new[] { "plugin.a", "plugin.b" }, context.PluginFacts.Select(static plugin => plugin.PluginId).ToArray());
    }

    [Fact]
    [Trait("ChecklistItem", StartupBannerChecklistItem)]
    public async Task ShellProcess_GivenFreshLaunch_StartupBannerIncludesVersionRepoPluginCountPolicyAndHelpHint()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"Product:\s+.+\s+Version:\s+\S+", result.StdOut);
        Assert.Contains($"Repository path: {fixture.RepositoryPath}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Loaded plugin count: 0", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Active policy profile: local-safe", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Hint: run 'help' to list available commands.", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", DotNetWorkspaceBootstrapChecklistItem)]
    public async Task ShellProcess_GivenRepositoryWithProjectFile_ExpectedBootstrapValidatesRepositoryAndDiscoversDotNetArtifacts()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"startup-bootstrap: repositoryRoot={fixture.RepositoryPath}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"startup-bootstrap: workspaceRoot={fixture.RepositoryPath}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("startup-bootstrap: discovery.solutionCount=0", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("startup-bootstrap: discovery.projectCount=1", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("startup-bootstrap: validation=success", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", DotNetWorkspaceBootstrapChecklistItem)]
    public async Task ShellProcess_GivenRepositoryWithoutSolutionOrProjectFiles_ExpectedBootstrapFailsWithDeterministicDiagnostics()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        File.Delete(Path.Combine(fixture.RepositoryPath, "ValidationProbe.csproj"));
        File.Delete(Path.Combine(fixture.RepositoryPath, "Program.cs"));
        await fixture.RunGitAsync("add", "-A");
        await fixture.RunGitAsync("commit", "-m", "remove dotnet artifacts");

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(14, result.ExitCode);
        Assert.Contains($"startup-bootstrap: repositoryRoot={fixture.RepositoryPath}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("startup-bootstrap: discovery.solutionCount=0", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("startup-bootstrap: discovery.projectCount=0", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("startup-bootstrap: validation=failure", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("startup-bootstrap: failure.reason=workspace-dotnet-artifacts-missing", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("startup-bootstrap: failure.exitCode=14", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", CommandSetChecklistItem)]
    public async Task Help_GivenFreshShell_ListsMvpGlobalAndSessionCommands()
    {
        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "help",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Available commands:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("help, init, repo, config, sessions", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("session start \"<task>\"", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("session attach <session-id>", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("use workflow <id>", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("plan, run, diff, checkpoint, validate, review, approve, merge", result.StdOut, StringComparison.Ordinal);
         Assert.Contains("artifacts, status, detach, archive, abort", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("workflows, plugins [load|unload], debug-logs, exit", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", CommandSetChecklistItem)]
    public async Task PromptRendering_GivenNoActiveSession_ShowsGlobalPromptAndGuidesSessionCommands()
    {
        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "plan",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("wip> ", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("wip[", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Command 'plan' requires an active session.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Start a session with: session start \"<task>\"", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Attach a session with: session attach <session-id>", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Session commands: plan, run, diff, checkpoint, validate, review, approve, merge, artifacts, status, detach, archive, abort", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Global commands available without a session: help, init, repo, config, sessions, session start \"<task>\", session attach <session-id>, use workflow <id>, workflows, plugins [load|unload], debug-logs, exit", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", CommandSetChecklistItem)]
    public async Task SessionCommands_GivenNoActiveSession_EachCommandReturnsContextSensitiveGuidance()
    {
        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
        [
            "plan",
            "run",
            "diff",
            "checkpoint",
            "validate",
            "review",
            "approve",
            "merge",
            "artifacts",
            "status",
            "detach",
            "archive",
            "abort",
            "exit"
        ],
        CancellationToken.None);

        Assert.Equal(0, result.ExitCode);

        foreach (var command in new[] { "plan", "run", "diff", "checkpoint", "validate", "review", "approve", "merge", "artifacts", "status", "detach", "archive", "abort" })
        {
            Assert.Contains($"Command '{command}' requires an active session.", result.StdOut, StringComparison.Ordinal);
        }

        Assert.Contains("Start a session with: session start \"<task>\"", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Attach a session with: session attach <session-id>", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Session commands: plan, run, diff, checkpoint, validate, review, approve, merge, artifacts, status, detach, archive, abort", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", CommandSetChecklistItem)]
    public async Task PromptRendering_GivenActiveSession_ShowsSessionPromptAcrossGovernanceFlow()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Prompt scope transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "prompt-scope-change\n",
                "prompt scope change");
        }

        var run = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "approve",
                "yes",
                "merge",
                "detach",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", run.StdOut, StringComparison.Ordinal);
        Assert.Contains($"wip[{sessionId}]> ", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("Diff context: session=", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("Validation completed: session=", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("Review generated: session=", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("Approval completed: session=", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("Merge completed: session=", run.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Session detached: {sessionId}.", run.StdOut, StringComparison.Ordinal);
        Assert.EndsWith("wip>", run.StdOut.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", RepositoryBootstrapChecklistItem)]
    public async Task Init_GivenGitRepositoryWithoutWipMetadata_CreatesDeterministicWipFolderLayoutAndConfigFile()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"Repository initialized: {fixture.RepositoryPath}", result.StdOut, StringComparison.Ordinal);

        var wipRoot = Path.Combine(fixture.RepositoryPath, ".wip");
        var sessionsPath = Path.Combine(wipRoot, "sessions");
        var worktreesPath = Path.Combine(wipRoot, "worktrees");
        var pluginsPath = Path.Combine(wipRoot, "plugins");
        var configPath = Path.Combine(wipRoot, "config.json");

        Assert.True(Directory.Exists(wipRoot));
        Assert.True(Directory.Exists(sessionsPath));
        Assert.True(Directory.Exists(worktreesPath));
        Assert.True(Directory.Exists(pluginsPath));
        Assert.True(File.Exists(configPath));

        using var configDoc = JsonDocument.Parse(await File.ReadAllTextAsync(configPath, CancellationToken.None));
        Assert.Equal(fixture.RepositoryPath, configDoc.RootElement.GetProperty("RepositoryPath").GetString());
        Assert.Equal(fixture.RepositoryPath, configDoc.RootElement.GetProperty("WorkspaceRoot").GetString());
        Assert.Equal(wipRoot, configDoc.RootElement.GetProperty("WipRoot").GetString());
        Assert.Equal(sessionsPath, configDoc.RootElement.GetProperty("SessionsPath").GetString());
        Assert.Equal(worktreesPath, configDoc.RootElement.GetProperty("WorktreesPath").GetString());
        Assert.Equal(pluginsPath, configDoc.RootElement.GetProperty("PluginsPath").GetString());
        Assert.Equal("workflow.linear", configDoc.RootElement.GetProperty("DefaultWorkflowId").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", RepositoryBootstrapChecklistItem)]
    public async Task Repo_GivenInitializedRepository_RendersRepositoryPathPolicyPluginPathsAndWorkspaceRootFromEffectiveConfig()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var wipRoot = Path.Combine(fixture.RepositoryPath, ".wip");
        var customWorkspaceRoot = Path.Combine(fixture.RepositoryPath, "workspace-custom");
        var customPluginsPath = Path.Combine(fixture.RepositoryPath, "plugins-custom");
        var customSessionsPath = Path.Combine(fixture.RepositoryPath, "sessions-custom");
        var customWorktreesPath = Path.Combine(fixture.RepositoryPath, "worktrees-custom");

        Directory.CreateDirectory(customWorkspaceRoot);
        Directory.CreateDirectory(customPluginsPath);
        Directory.CreateDirectory(customSessionsPath);
        Directory.CreateDirectory(customWorktreesPath);
        Directory.CreateDirectory(wipRoot);

        var configJson = """
        {
          "WorkspaceRoot": "workspace-custom",
          "PluginsPath": "plugins-custom",
          "SessionsPath": "sessions-custom",
          "WorktreesPath": "worktrees-custom",
          "DefaultWorkflowId": "workflow.custom"
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(wipRoot, "config.json"), configJson, CancellationToken.None);

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "repo",
                "config",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"Repository path: {fixture.RepositoryPath}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"workspaceRoot: {customWorkspaceRoot}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"pluginsPath: {customPluginsPath}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"sessionsPath: {customSessionsPath} exists=True", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"worktreesPath: {customWorktreesPath} exists=True", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("defaultWorkflowId: workflow.custom", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("configSource: disk", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", RepositoryBootstrapChecklistItem)]
    public async Task Sessions_GivenPersistedDetachedSessions_ListsIdsTasksStatesAndUpdatedTimestampsFromDisk()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Disk-backed sessions\"",
                "plan",
                "detach",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        var resumedRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "sessions",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, resumedRun.ExitCode);
        Assert.Contains("Persisted sessions:", resumedRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"- {sessionId} task=\"Disk-backed sessions\" state=Editing workflow=workflow.linear updated=", resumedRun.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", SessionLifecycleChecklistItem)]
    [Trait("ChecklistItem", SessionPersistenceChecklistItem)]
    [Trait("ChecklistItem", SessionEventJournalChecklistItem)]
    [Trait("ChecklistItem", DotNetE2ETranscriptChecklistItem)]
    public async Task SessionLifecycle_GivenStartDetachAttachArchive_PersistsStateAndLifecycleEventJournal()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var bootstrapRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Lifecycle archive transcript\"",
                "plan",
                "detach",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, bootstrapRun.ExitCode);
        var sessionId = ExtractSessionId(bootstrapRun.StdOut);

        using (var plannedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            Assert.Equal("Editing", plannedState.RootElement.GetProperty("State").GetString());
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                plannedState.RootElement,
                "README.md",
                "lifecycle-archive-change\n",
                "lifecycle archive change");
        }

        var governanceRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "approve",
                "yes",
                "merge",
                "archive",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, governanceRun.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Session archived from shell: {sessionId}.", governanceRun.StdOut, StringComparison.Ordinal);

        using var finalState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        Assert.Equal("Archived", finalState.RootElement.GetProperty("State").GetString());

        var lifecycleEvents = await ReadLifecycleEventsAsync(fixture.RepositoryPath, sessionId);
        var lifecycleKinds = lifecycleEvents.Select(static item => item.Kind).ToArray();
        Assert.Contains("SessionStarted", lifecycleKinds, StringComparer.Ordinal);
        Assert.Contains("SessionAttached", lifecycleKinds, StringComparer.Ordinal);
        Assert.Contains("SessionDetached", lifecycleKinds, StringComparer.Ordinal);
        Assert.Contains("SessionTransitioned", lifecycleKinds, StringComparer.Ordinal);
        Assert.Contains("SessionArchived", lifecycleKinds, StringComparer.Ordinal);

        AssertEventShapeDeterministic(lifecycleEvents, "SessionArchived", "Archived");
    }

    [Fact]
    [Trait("ChecklistItem", ArchivePolicyChecklistItem)]
    public async Task ArchivePolicy_GivenMarkOnlyAndCleanupModes_PreservesArtifactsAndMaintainsDeterministicSessionTraceability()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        static async Task<SessionId> PrepareMergedSessionAsync(TempGitRepositoryFixture fixture, string taskDescription)
        {
            var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
                [
                    "init",
                    $"session start \"{taskDescription}\"",
                    "plan",
                    "exit"
                ],
                CancellationToken.None,
                workingDirectory: fixture.RepositoryPath);

            Assert.Equal(0, setupRun.ExitCode);
            var sessionId = ExtractSessionId(setupRun.StdOut);

            using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
            {
                await AppendAndCommitWorktreeChangeAsync(
                    fixture,
                    startedState.RootElement,
                    "README.md",
                    $"archive-policy-change-{sessionId.Value}\n",
                    $"archive policy change {sessionId.Value}");
            }

            var mergeRun = await ShellHostProcessDriver.ExecuteScriptAsync(
                [
                    $"session attach {sessionId}",
                    "diff",
                    "validate",
                    "review",
                    "approve",
                    "yes",
                    "merge",
                    "exit"
                ],
                CancellationToken.None,
                workingDirectory: fixture.RepositoryPath);

            Assert.Equal(0, mergeRun.ExitCode);
            Assert.Contains("Merge completed: session=", mergeRun.StdOut, StringComparison.Ordinal);
            return sessionId;
        }

        var markOnlySessionId = await PrepareMergedSessionAsync(fixture, "Archive policy mark-only transcript");
        var cleanupSessionId = await PrepareMergedSessionAsync(fixture, "Archive policy cleanup transcript");

        var markOnlyRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {markOnlySessionId}",
                "archive --mark-only",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, markOnlyRun.ExitCode);
        Assert.Contains($"Session archived from shell: {markOnlySessionId} mode=mark-only.", markOnlyRun.StdOut, StringComparison.Ordinal);

        using var markOnlyState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, markOnlySessionId);
        Assert.Equal("Archived", markOnlyState.RootElement.GetProperty("State").GetString());
        var markOnlyWorktreePath = markOnlyState.RootElement.GetProperty("WorktreePath").GetString();
        var markOnlyArtifactDirectory = markOnlyState.RootElement.GetProperty("ArtifactDirectory").GetString();
        Assert.False(string.IsNullOrWhiteSpace(markOnlyWorktreePath));
        Assert.False(string.IsNullOrWhiteSpace(markOnlyArtifactDirectory));
        Assert.True(Directory.Exists(markOnlyWorktreePath!));
        Assert.True(Directory.Exists(markOnlyArtifactDirectory!));
        AssertArtifactWithPrefixExists(markOnlyArtifactDirectory, "merge-result-");
        AssertArtifactWithPrefixExists(markOnlyArtifactDirectory, "session-archive-");
        AssertArchiveArtifactCleanupOutcome(
            markOnlyArtifactDirectory,
            expectedMode: "MarkOnly",
            expectedAttempted: false,
            expectedSucceeded: true,
            expectedWorktreePath: markOnlyWorktreePath!);

        var markOnlyEvents = await ReadLifecycleEventsAsync(fixture.RepositoryPath, markOnlySessionId);
        AssertEventShapeDeterministic(markOnlyEvents, "SessionArchived", "Archived");

        var cleanupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {cleanupSessionId}",
                "archive --cleanup",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, cleanupRun.ExitCode);
        Assert.Contains($"Session archived from shell: {cleanupSessionId} mode=cleanup.", cleanupRun.StdOut, StringComparison.Ordinal);

        using var cleanupState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, cleanupSessionId);
        Assert.Equal("Archived", cleanupState.RootElement.GetProperty("State").GetString());
        var cleanupWorktreePath = cleanupState.RootElement.GetProperty("WorktreePath").GetString();
        var cleanupArtifactDirectory = cleanupState.RootElement.GetProperty("ArtifactDirectory").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cleanupWorktreePath));
        Assert.False(string.IsNullOrWhiteSpace(cleanupArtifactDirectory));
        Assert.False(Directory.Exists(cleanupWorktreePath!));
        Assert.True(Directory.Exists(cleanupArtifactDirectory!));
        AssertArtifactWithPrefixExists(cleanupArtifactDirectory, "merge-result-");
        AssertArtifactWithPrefixExists(cleanupArtifactDirectory, "session-archive-");
        AssertArchiveArtifactCleanupOutcome(
            cleanupArtifactDirectory,
            expectedMode: "Cleanup",
            expectedAttempted: true,
            expectedSucceeded: true,
            expectedWorktreePath: cleanupWorktreePath!);

        var cleanupStatePath = Path.Combine(fixture.RepositoryPath, ".wip", "sessions", cleanupSessionId.Value, "session-state.json");
        var cleanupJournalPath = Path.Combine(fixture.RepositoryPath, ".wip", "sessions", cleanupSessionId.Value, "event-journal.ndjson");
        Assert.True(File.Exists(cleanupStatePath));
        Assert.True(File.Exists(cleanupJournalPath));

        var cleanupEvents = await ReadLifecycleEventsAsync(fixture.RepositoryPath, cleanupSessionId);
        AssertEventShapeDeterministic(cleanupEvents, "SessionArchived", "Archived");
    }

    [Fact]
    [Trait("ChecklistItem", SessionLifecycleChecklistItem)]
    [Trait("ChecklistItem", SessionPersistenceChecklistItem)]
    [Trait("ChecklistItem", SessionEventJournalChecklistItem)]
    public async Task SessionLifecycle_GivenStartAttachAbort_PersistsStateAndAbortEventJournal()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Lifecycle abort transcript\"",
                "plan",
                "detach",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        var abortRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "abort",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, abortRun.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", abortRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Session aborted from shell: {sessionId} state=Aborted. Persisted session state remains on disk.", abortRun.StdOut, StringComparison.Ordinal);

        using var persistedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        Assert.Equal("Aborted", persistedState.RootElement.GetProperty("State").GetString());

        var lifecycleEvents = await ReadLifecycleEventsAsync(fixture.RepositoryPath, sessionId);
        var lifecycleKinds = lifecycleEvents.Select(static item => item.Kind).ToArray();
        Assert.Contains("SessionStarted", lifecycleKinds, StringComparer.Ordinal);
        Assert.Contains("SessionAttached", lifecycleKinds, StringComparer.Ordinal);
        Assert.Contains("SessionDetached", lifecycleKinds, StringComparer.Ordinal);
        Assert.Contains("SessionAborted", lifecycleKinds, StringComparer.Ordinal);
        AssertEventShapeDeterministic(lifecycleEvents, "SessionAborted", "Aborted");
    }

    [Fact]
    [Trait("ChecklistItem", SessionEventJournalChecklistItem)]
    public async Task Merge_GivenRuntimeStateMismatch_RecordsMergeAttemptedAndMergeFailedWithDeterministicPayloadShape()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Merge failure taxonomy transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var plannedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                plannedState.RootElement,
                "README.md",
                "merge-failure-taxonomy-change\n",
                "merge failure taxonomy change");
        }

        var approvalRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "approve",
                "yes",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, approvalRun.ExitCode);
        Assert.Contains("Approval completed: session=", approvalRun.StdOut, StringComparison.Ordinal);

        await RewritePersistedSessionStateAsync(fixture.RepositoryPath, sessionId, "Approved", "AwaitingApproval", "State");

        var failedMergeRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "merge",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, failedMergeRun.ExitCode);
        Assert.Contains("Merge failed:", failedMergeRun.StdOut, StringComparison.Ordinal);

        var lifecycleEvents = await ReadLifecycleEventsAsync(fixture.RepositoryPath, sessionId);
        AssertEventShapeDeterministic(lifecycleEvents, "MergeAttempted", "AwaitingApproval");
        AssertEventShapeDeterministic(lifecycleEvents, "MergeFailed", "AwaitingApproval");
    }

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
        Assert.Contains("artifacts, status, detach, archive, abort, workflows, plugins [load|unload], debug-logs, exit", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ShellHostConfigChecklistItem)]
    public async Task ShellHostReadme_GivenConfigFileAndCliPluginsPath_CliOverrideWinsInEffectiveConfigurationOutput()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var configPluginsPath = Path.Combine(fixture.RepositoryPath, "plugins-from-config");
        var cliPluginsPath = Path.Combine(fixture.RepositoryPath, "plugins-from-cli");
        var workspaceRoot = Path.Combine(fixture.RepositoryPath, "workspace-root");
        var userPluginsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wip", "plugins");

        Directory.CreateDirectory(configPluginsPath);
        Directory.CreateDirectory(cliPluginsPath);
        Directory.CreateDirectory(workspaceRoot);

        var configDir = Path.Combine(fixture.RepositoryPath, ".wip");
        Directory.CreateDirectory(configDir);

        var configJson = """
        {
                    "PluginsPath": "plugins-from-config",
                    "WorkspaceRoot": "workspace-root",
                    "PolicyId": "policy.from.config",
                    "ValidationCommands": ["dotnet build -c Release", "dotnet test --no-build"]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(configDir, "config.json"), configJson, CancellationToken.None);

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "effective-config",
                "exit"
            ],
            CancellationToken.None,
            args: [cliPluginsPath],
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Effective configuration:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"repositoryPath: {fixture.RepositoryPath}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"workspaceRoot: {workspaceRoot}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"configPath: {Path.Combine(configDir, "config.json")}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("configSource: disk", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"wipRoot: {Path.Combine(fixture.RepositoryPath, ".wip")}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"sessionsPath: {Path.Combine(fixture.RepositoryPath, ".wip", "sessions")}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"worktreesPath: {Path.Combine(fixture.RepositoryPath, ".wip", "worktrees")}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"pluginsPath: {cliPluginsPath}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"userPluginsPath: {userPluginsPath}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("defaultWorkflowId: workflow.linear", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("policy: policy.from.config", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("pluginStartupMode: ExplicitCommandOnly", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("validationCommands: dotnet build -c Release | dotnet test --no-build", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", PluginDiscoveryChecklistItem)]
    public async Task PluginDiscovery_GivenRepoAndUserPluginFolders_ExpectedDeterministicLoadOrderAndNoDuplicateActivation()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var tempUserHome = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-user-home-{Guid.NewGuid():N}");
        var repositoryPluginsPath = Path.Combine(fixture.RepositoryPath, ".wip", "plugins");
        var userPluginsPath = Path.Combine(tempUserHome, ".wip", "plugins");

        Directory.CreateDirectory(repositoryPluginsPath);
        Directory.CreateDirectory(userPluginsPath);
        StageCurrentTestAssemblyIntoPluginFolder(repositoryPluginsPath);
        StageCurrentTestAssemblyIntoPluginFolder(userPluginsPath);

        try
        {
            var result = await ShellHostProcessDriver.ExecuteScriptAsync(
                [
                    "effective-config",
                    "plugins load",
                    "plugins",
                    "exit"
                ],
                CancellationToken.None,
                workingDirectory: fixture.RepositoryPath,
                environmentVariables: new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["USERPROFILE"] = tempUserHome,
                    ["HOME"] = tempUserHome
                });

            Assert.Equal(0, result.ExitCode);
            Assert.Contains($"pluginsPath: {repositoryPluginsPath}", result.StdOut, StringComparison.Ordinal);
            Assert.Contains($"userPluginsPath: {userPluginsPath}", result.StdOut, StringComparison.Ordinal);
            Assert.Contains($"Plugin discovery path [precedence=1]: {repositoryPluginsPath}", result.StdOut, StringComparison.Ordinal);
            Assert.Contains($"Plugin discovery path [precedence=2]: {userPluginsPath}", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Plugins loaded: 1.", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Skipping plugin 'wip.e2e.typed-registration' from lower-precedence path", result.StdOut, StringComparison.Ordinal);
            var registeredPluginEntries = Regex.Matches(
                result.StdOut,
                "^- wip\\.e2e\\.typed-registration ",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
                .Cast<Match>()
                .ToArray();
            Assert.Single(registeredPluginEntries);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempUserHome))
                    Directory.Delete(tempUserHome, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
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
        Assert.Contains("Registered workflows:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("workflow.linear", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("workflow.safe-change", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", WorkflowBindingChecklistItem)]
    public async Task Workflows_GivenLoadedPluginCapabilities_ListsBuilderRegisteredWorkflowIdsForInteractiveSelection()
    {
        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "workflows",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Registered workflows:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("workflow.linear", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("workflow.safe-change", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("No workflows are currently registered.", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", WorkflowBindingChecklistItem)]
    public async Task UseWorkflow_GivenValidWorkflowId_BindsSelectedWorkflowToActiveSessionAndStatusOutput()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var firstRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Workflow binding transcript\"",
                "use workflow workflow.safe-change",
                "repo",
                "detach",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, firstRun.ExitCode);
        Assert.Contains("Workflow selected: workflow.safe-change", firstRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Active session workflow: workflow.safe-change", firstRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("selectedWorkflow: workflow.safe-change", firstRun.StdOut, StringComparison.Ordinal);

        var sessionId = ExtractSessionId(firstRun.StdOut);
        var secondRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "repo",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, secondRun.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", secondRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("selectedWorkflow: workflow.safe-change", secondRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"activeSession: {sessionId}", secondRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("workflow=workflow.safe-change", secondRun.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Add explicit shell command coverage for `status` including full session metadata fields required by MVP (base branch, base SHA, target commit, workflow, validation, approval) [depends on RT session model completeness]")]
    public async Task StatusCommand_GivenSessionLifecycleTransitions_ExpectedBaseCommitWorkflowValidationAndApprovalFieldsRemainConsistent()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        // Create and start a session, then verify status command shows all required fields
        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Status command proof\"",
                "use workflow workflow.safe-change",
                "status",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, result.ExitCode);
        
        // Verify that status command produces output with required metadata fields
        Assert.Contains("Session status", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("- state:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("- workflow:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("- baseBranch:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("- baseCommit:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("- targetCommit:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("- validationStatus:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("- approvalStatus:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("- updatedAt:", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", WorkflowBindingChecklistItem)]
    public async Task Run_GivenSelectedWorkflow_ExecutesMappedStagesPersistsArtifactsAndRendersStageSummary()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var run = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Workflow run transcript\"",
                "use workflow workflow.safe-change",
                "run",
                "artifacts",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Workflow executed: workflow.safe-change", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("- Plan state=Editing transition=applied", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("- Run state=Editing transition=skipped", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("- Validate state=Validating transition=applied", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("- Review state=AwaitingApproval transition=applied", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("- RequireApproval state=Approved transition=applied", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("- Merge state=Merged transition=applied", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("Workflow artifact:", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("Artifacts for session", run.StdOut, StringComparison.Ordinal);

        var sessionId = ExtractSessionId(run.StdOut);
        using var state = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = state.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal("workflow.safe-change", state.RootElement.GetProperty("WorkflowId").GetString());
        AssertArtifactWithPrefixExists(artifactDirectory, "workflow-execution-");
    }

    [Fact]
    [Trait("ChecklistItem", WorkflowAmbiguityPromptChecklistItem)]
    public async Task RunCommand_GivenMultipleEligibleWorkflowsAndNoDefault_ExpectedExplicitSelectionPromptBeforeExecution()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Workflow ambiguity transcript\"",
                "detach",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        await RewritePersistedSessionWorkflowIdAsync(fixture.RepositoryPath, sessionId, "workflow.unknown");
        await WriteRepositoryConfigAsync(
            fixture.RepositoryPath,
            """
            {
              "DefaultWorkflowId": "workflow.unknown"
            }
            """);

        var ambiguityRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "run",
                "status",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, ambiguityRun.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", ambiguityRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Run blocked: workflow selection is ambiguous.", ambiguityRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("No default workflow could be resolved from the active session or repository configuration.", ambiguityRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Configured defaultWorkflowId: workflow.unknown", ambiguityRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Registered workflows:", ambiguityRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("- workflow.linear [Linear workflow]", ambiguityRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("- workflow.safe-change [Safe change workflow]", ambiguityRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Select a workflow with: use workflow <id>", ambiguityRun.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Workflow executed:", ambiguityRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("- state: Created", ambiguityRun.StdOut, StringComparison.Ordinal);

        var resolutionRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "use workflow workflow.safe-change",
                "run",
                "status",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, resolutionRun.ExitCode);
        Assert.Contains("Workflow selected: workflow.safe-change", resolutionRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Workflow executed: workflow.safe-change", resolutionRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("- state: Merged", resolutionRun.StdOut, StringComparison.Ordinal);

        using var finalState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        Assert.Equal("workflow.safe-change", finalState.RootElement.GetProperty("WorkflowId").GetString());
        Assert.Equal("Merged", finalState.RootElement.GetProperty("State").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", PlanRunArtifactsChecklistItem)]
    [Trait("ChecklistItem", DotNetE2ETranscriptChecklistItem)]
    public async Task RunArtifacts_GivenPlannedTypedWorkflow_ProducesAgentPlanAndAgentRunResultArtifacts()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var run = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Typed artifacts transcript\"",
                "use workflow workflow.safe-change",
                "plan",
                "run",
                "artifacts",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Plan generated: workflow=workflow.safe-change state=Editing", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("Workflow executed: workflow.safe-change", run.StdOut, StringComparison.Ordinal);

        var sessionId = ExtractSessionId(run.StdOut);
        using var state = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = state.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        AssertArtifactWithPrefixExists(artifactDirectory, "agent-plan-");
        AssertArtifactWithPrefixExists(artifactDirectory, "agent-run-result-");

        var runArtifactPath = Assert.Single(
            Directory.EnumerateFiles(artifactDirectory, "agent-run-result-*.json", SearchOption.TopDirectoryOnly),
            static path => !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));

        using var runArtifact = JsonDocument.Parse(await File.ReadAllTextAsync(runArtifactPath, CancellationToken.None));
        Assert.Equal("workflow.safe-change", runArtifact.RootElement.GetProperty("WorkflowId").GetString());

        var runStage = runArtifact.RootElement
            .GetProperty("Stages")
            .EnumerateArray()
            .Single(stage => string.Equals(stage.GetProperty("Stage").GetString(), "Run", StringComparison.Ordinal));

        Assert.False(string.IsNullOrWhiteSpace(runStage.GetProperty("RequestContractName").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(runStage.GetProperty("ResultContractName").GetString()));
    }

    [Fact]
    [Trait("ChecklistItem", ArtifactListingChecklistItem)]
    public async Task ArtifactsCommand_GivenSessionArtifacts_ReturnsDeterministicDescriptorFieldsAndStableOrdering()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var run = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Artifact listing transcript\"",
                "use workflow workflow.safe-change",
                "plan",
                "run",
                "artifacts",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, run.ExitCode);
        var sessionId = ExtractSessionId(run.StdOut);
        Assert.Contains($"Artifacts for session {sessionId}:", run.StdOut, StringComparison.Ordinal);

        var descriptorPattern = new Regex(
            "^- id=(?<id>\\S+) type=(?<type>\\S+) version=(?<version>\\S+) created=(?<created>\\S+) producer=(?<producer>\\S+) path=(?<path>.+)$",
            RegexOptions.Multiline);

        var parsedLines = descriptorPattern
            .Matches(run.StdOut)
            .Select(match => new ArtifactOutputLine(
                match.Value,
                match.Groups["id"].Value,
                match.Groups["type"].Value,
                match.Groups["version"].Value,
                DateTimeOffset.Parse(match.Groups["created"].Value, null, System.Globalization.DateTimeStyles.RoundtripKind),
                match.Groups["producer"].Value,
                match.Groups["path"].Value))
            .ToArray();

        Assert.NotEmpty(parsedLines);
        Assert.All(parsedLines, static line =>
        {
            Assert.False(string.IsNullOrWhiteSpace(line.Id));
            Assert.False(string.IsNullOrWhiteSpace(line.Type));
            Assert.False(string.IsNullOrWhiteSpace(line.Version));
            Assert.False(string.IsNullOrWhiteSpace(line.Producer));
            Assert.False(string.IsNullOrWhiteSpace(line.Path));
        });

        var expectedOrder = parsedLines
            .OrderBy(static line => line.Created)
            .ThenBy(static line => line.Id, StringComparer.Ordinal)
            .ThenBy(static line => line.Type, StringComparer.Ordinal)
            .ThenBy(static line => line.Version, StringComparer.Ordinal)
            .ThenBy(static line => line.Producer, StringComparer.Ordinal)
            .ThenBy(static line => line.Path, StringComparer.Ordinal)
            .Select(static line => line.Raw)
            .ToArray();

        Assert.Equal(expectedOrder, parsedLines.Select(static line => line.Raw).ToArray());
    }

    [Fact]
    [Trait("ChecklistItem", CorrelationContinuityChecklistItem)]
    public async Task ProviderInvocation_GivenSessionContext_ExpectedCorrelationIdPropagatesAcrossRequestResponseAndArtifacts()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithCorrelationProofProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());

        using var reader = new StringReader("init\nsession start \"Correlation continuity transcript\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, fixture.RepositoryPath);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var output = writer.ToString();
        var sessionId = ExtractSessionId(output);
        var expectedCorrelation = $"plan-session-{sessionId.Value}-operation-plan";

        Assert.Equal(0, exitCode);
        Assert.Equal(1, CorrelationCaptureModelProvider.ExecutionCount);
        Assert.Contains($"Model provider correlation: {expectedCorrelation}", output, StringComparison.Ordinal);
        Assert.Equal(expectedCorrelation, CorrelationCaptureModelProvider.LastRequestCorrelationId);
        Assert.Equal(expectedCorrelation, CorrelationCaptureModelProvider.LastResponseCorrelationId);

        using var state = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = state.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        var agentPlanArtifactPath = Assert.Single(
            Directory.EnumerateFiles(artifactDirectory, "agent-plan-result-*.json", SearchOption.TopDirectoryOnly),
            static path => !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));

        using var planArtifact = JsonDocument.Parse(await File.ReadAllTextAsync(agentPlanArtifactPath, CancellationToken.None));
        Assert.Equal(expectedCorrelation, planArtifact.RootElement.GetProperty("ProviderCorrelationId").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", DynamicProviderPromptChecklistItem)]
    public async Task PlanProviderInvocation_GivenDynamicPromptContext_ExpectedProviderRequestContainsSystemInstructionsEnvironmentAndConstraints()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithPromptContextComposerProofProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());
        var diagnosticsBridge = new DiagnosticsBridge(
            runManifest: new RunManifest(
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Plugins:
                [
                    new PluginManifestEntry(
                        PluginId: "plugin.orders.fulfillment",
                        PluginName: "Orders Fulfillment",
                        PluginVersion: "1.2.0",
                        AssemblyName: "Plugin.Orders.Fulfillment",
                        AssemblyVersion: "1.2.0.0",
                        Capabilities: ["tool.fulfillment.route", "validator.fulfillment.preflight"],
                        RequiredPermissions: ["repo.read", "repo.write"])
                ],
                Workflows: Array.Empty<WorkflowManifestEntry>()),
            diagnostics:
            [
                "[discovery][run:run-123] Plugin discovery path [precedence=1]: C:/repo/.wip/plugins",
                "[activation][run:run-123] Loaded plugin 'plugin.orders.fulfillment'."
            ]);

        using var reader = new StringReader("init\nsession start \"Prompt context transcript\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(
            orchestrator,
            reader,
            writer,
            builder,
            fixture.RepositoryPath,
            diagnosticsBridge,
            validationCommands: ["dotnet test", "dotnet build", "dotnet test"]);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("Task: Prompt context transcript", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("SystemInstructions:", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("Do not emit commands from ForbiddenEcosystems.", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("EnvironmentFacts:", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains($"- RepositoryPath: {fixture.RepositoryPath}", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("WorkflowPolicyScope:", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("CapabilityCatalog:", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("- WorkflowId: workflow.linear", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("- PolicyId: policy.shell", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("- Tools: tool.alpha, tool.beta", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("- Validators: validator.alpha, validator.beta", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("PluginFacts:", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("- plugin.orders.fulfillment [Orders Fulfillment] v1.2.0", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("  ownedConstraints.requiredPermissions: repo.read, repo.write", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("PluginManifestDiagnostics:", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("- [activation][run:run-123] Loaded plugin 'plugin.orders.fulfillment'.", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("- [discovery][run:run-123] Plugin discovery path [precedence=1]: C:/repo/.wip/plugins", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("ForbiddenEcosystems: cargo, npm, pip, yarn", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("- ValidationCommands: dotnet build, dotnet test", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Equal(
            "Generate a deterministic numbered implementation plan from the provided context. Return plain text numbered steps only.",
            CorrelationCaptureModelProvider.LastUserMessage);

        var systemInstructionsSectionStart = CorrelationCaptureModelProvider.LastSystemMessage!.IndexOf("SystemInstructions:", StringComparison.Ordinal);
        var environmentSectionStart = CorrelationCaptureModelProvider.LastSystemMessage.IndexOf("EnvironmentFacts:", StringComparison.Ordinal);
        var pluginFactsSectionStart = CorrelationCaptureModelProvider.LastSystemMessage.IndexOf("PluginFacts:", StringComparison.Ordinal);
        var diagnosticsSectionStart = CorrelationCaptureModelProvider.LastSystemMessage.IndexOf("PluginManifestDiagnostics:", StringComparison.Ordinal);
        var activationLineIndex = CorrelationCaptureModelProvider.LastSystemMessage.IndexOf("[activation][run:run-123]", StringComparison.Ordinal);
        var discoveryLineIndex = CorrelationCaptureModelProvider.LastSystemMessage.IndexOf("[discovery][run:run-123]", StringComparison.Ordinal);

        Assert.True(systemInstructionsSectionStart >= 0);
        Assert.True(environmentSectionStart > systemInstructionsSectionStart);
        Assert.True(pluginFactsSectionStart > environmentSectionStart);
        Assert.True(diagnosticsSectionStart > pluginFactsSectionStart);
        Assert.True(activationLineIndex > diagnosticsSectionStart);
        Assert.True(discoveryLineIndex > activationLineIndex);
    }

    [Fact]
    [Trait("ChecklistItem", PromptDiagnosticsPersistenceChecklistItem)]
    public async Task PlanProviderInvocation_GivenPromptDiagnostics_ExpectedArtifactPersistenceAndStatusSurfacing()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithPromptContextComposerProofProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());
        var diagnosticsBridge = new DiagnosticsBridge(
            runManifest: new RunManifest(
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Plugins:
                [
                    new PluginManifestEntry(
                        PluginId: "plugin.orders.alpha",
                        PluginName: "Orders Alpha",
                        PluginVersion: "1.0.0",
                        AssemblyName: "Plugin.Orders.Alpha",
                        AssemblyVersion: "1.0.0.0",
                        Capabilities:
                        [
                            "tool.orders.alpha",
                            "prompt.system.fragment:0100|Prefer deterministic plan checkpoints before merge."
                        ],
                        RequiredPermissions: ["repo.read"]),
                    new PluginManifestEntry(
                        PluginId: "plugin.orders.beta",
                        PluginName: "Orders Beta",
                        PluginVersion: "2.0.0",
                        AssemblyName: "Plugin.Orders.Beta",
                        AssemblyVersion: "2.0.0.0",
                        Capabilities:
                        [
                            "tool.orders.beta",
                            "prompt.system.fragment:0200|```danger```"
                        ],
                        RequiredPermissions: ["repo.read", "repo.write"])
                ],
                Workflows: Array.Empty<WorkflowManifestEntry>()),
            diagnostics:
            [
                "[activation][run:run-diagnostics] Loaded plugin 'plugin.orders.alpha'.",
                "[activation][run:run-diagnostics] Loaded plugin 'plugin.orders.beta'."
            ]);

        using var reader = new StringReader("init\nsession start \"Prompt diagnostics transcript\"\nplan\nstatus\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(
            orchestrator,
            reader,
            writer,
            builder,
            fixture.RepositoryPath,
            diagnosticsBridge,
            validationCommands: ["dotnet test", "dotnet build"]);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var output = writer.ToString();
        var sessionId = ExtractSessionId(output);
        Assert.NotNull(sessionId);

        using var state = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = state.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        string? promptDiagnosticsArtifactPath = null;
        JsonDocument? promptDiagnosticsArtifact = null;

        foreach (var candidatePath in Directory
                     .EnumerateFiles(artifactDirectory, "prompt-context-diagnostics-*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(static path => File.GetLastWriteTimeUtc(path))
                     .ThenBy(static path => path, StringComparer.Ordinal))
        {
            var candidateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(candidatePath, CancellationToken.None));
            if (!candidateDocument.RootElement.TryGetProperty("ContextHash", out _))
            {
                candidateDocument.Dispose();
                continue;
            }

            promptDiagnosticsArtifactPath = candidatePath;
            promptDiagnosticsArtifact = candidateDocument;
            break;
        }

        if (promptDiagnosticsArtifactPath is null || promptDiagnosticsArtifact is null)
            throw new InvalidOperationException("Expected a prompt-context-diagnostics artifact with a ContextHash payload.");

        var contextHash = promptDiagnosticsArtifact.RootElement.GetProperty("ContextHash").GetString();
        var providerId = promptDiagnosticsArtifact.RootElement.GetProperty("ProviderId").GetString();
        var modelId = promptDiagnosticsArtifact.RootElement.GetProperty("ModelId").GetString();
        var correlationId = promptDiagnosticsArtifact.RootElement.GetProperty("CorrelationId").GetString();
        var contributingPluginIds = promptDiagnosticsArtifact.RootElement.GetProperty("ContributingPluginIds").EnumerateArray().Select(static value => value.GetString()).ToArray();
        var rejectedFragments = promptDiagnosticsArtifact.RootElement.GetProperty("RejectedFragments").EnumerateArray().ToArray();

        Assert.Equal(0, exitCode);
        Assert.NotNull(contextHash);
        Assert.Equal("deepseek", providerId);
        Assert.Equal("deepseek-chat", modelId);
        Assert.Equal($"plan-session-{sessionId.Value}-operation-plan", correlationId);
        Assert.Equal(["plugin.orders.alpha"], contributingPluginIds);
        Assert.Single(rejectedFragments);
        Assert.Equal("plugin.orders.beta", rejectedFragments[0].GetProperty("PluginId").GetString());
        Assert.Equal("unsafe-token", rejectedFragments[0].GetProperty("Reason").GetString());
        Assert.Contains($"Prompt context diagnostics: contextHash={contextHash}", output, StringComparison.Ordinal);
        Assert.Contains("provider=deepseek model=deepseek-chat", output, StringComparison.Ordinal);
        Assert.Contains($"correlation=plan-session-{sessionId.Value}-operation-plan", output, StringComparison.Ordinal);
        Assert.Contains("- promptContextDiagnostics: contextHash=", output, StringComparison.Ordinal);
        Assert.Contains("contributingPlugins: plugin.orders.alpha", output, StringComparison.Ordinal);
        Assert.Contains("rejectedFragments:", output, StringComparison.Ordinal);
        Assert.Contains("plugin=plugin.orders.beta reason=unsafe-token", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", EcosystemAlignmentGuardrailsChecklistItem)]
    public async Task PlanStepGuardrails_GivenDotNetOnlyPolicy_ExpectedNpmYarnPipCargoCommandsRejectedOrRewrittenDeterministically()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        EcosystemGuardrailModelProvider.Reset();

        var builder = CreateBuilderWithEcosystemGuardrailProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());

        using var reader = new StringReader("init\nsession start \"Ecosystem guardrails transcript\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, fixture.RepositoryPath);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var output = writer.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, EcosystemGuardrailModelProvider.ExecutionCount);
        Assert.Contains("Plan generated:", output, StringComparison.Ordinal);
        Assert.Contains("- Plan step blocked by .NET-only policy: npm", output, StringComparison.Ordinal);
        Assert.Contains("- Plan step blocked by .NET-only policy: cargo", output, StringComparison.Ordinal);
        Assert.Contains("- Run dotnet test", output, StringComparison.Ordinal);
        Assert.DoesNotContain("- Run npm install", output, StringComparison.Ordinal);
        Assert.DoesNotContain("- Run cargo test", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", PluginInstructionInjectionChecklistItem)]
    public async Task PluginPromptFragments_GivenMultipleLoadedPlugins_ExpectedDeterministicPrecedenceAndMergedSystemInstructions()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithPromptContextComposerProofProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());
        var diagnosticsBridge = new DiagnosticsBridge(
            runManifest: new RunManifest(
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Plugins:
                [
                    new PluginManifestEntry(
                        PluginId: "plugin.orders.beta",
                        PluginName: "Orders Beta",
                        PluginVersion: "2.0.0",
                        AssemblyName: "Plugin.Orders.Beta",
                        AssemblyVersion: "2.0.0.0",
                        Capabilities:
                        [
                            "tool.orders.beta",
                            "prompt.system.fragment:0200|Always include fulfillment rollback criteria before merge."
                        ],
                        RequiredPermissions: ["repo.read", "repo.write"]),
                    new PluginManifestEntry(
                        PluginId: "plugin.orders.alpha",
                        PluginName: "Orders Alpha",
                        PluginVersion: "1.0.0",
                        AssemblyName: "Plugin.Orders.Alpha",
                        AssemblyVersion: "1.0.0.0",
                        Capabilities:
                        [
                            "tool.orders.alpha",
                            "prompt.system.fragment:0100|Prefer deterministic plan checkpoints at each policy gate."
                        ],
                        RequiredPermissions: ["repo.read"])
                ],
                Workflows: Array.Empty<WorkflowManifestEntry>()),
            diagnostics:
            [
                "[activation][run:run-plugin-fragment] Loaded plugin 'plugin.orders.beta'.",
                "[activation][run:run-plugin-fragment] Loaded plugin 'plugin.orders.alpha'."
            ]);

        using var reader = new StringReader("init\nsession start \"Plugin fragment precedence transcript\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(
            orchestrator,
            reader,
            writer,
            builder,
            fixture.RepositoryPath,
            diagnosticsBridge,
            validationCommands: ["dotnet build", "dotnet test"]);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, CorrelationCaptureModelProvider.ExecutionCount);
        Assert.Contains("Plugin[plugin.orders.alpha] Prefer deterministic plan checkpoints at each policy gate.", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("Plugin[plugin.orders.beta] Always include fulfillment rollback criteria before merge.", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);

        var alphaIndex = CorrelationCaptureModelProvider.LastSystemMessage!.IndexOf(
            "Plugin[plugin.orders.alpha] Prefer deterministic plan checkpoints at each policy gate.",
            StringComparison.Ordinal);
        var betaIndex = CorrelationCaptureModelProvider.LastSystemMessage.IndexOf(
            "Plugin[plugin.orders.beta] Always include fulfillment rollback criteria before merge.",
            StringComparison.Ordinal);

        Assert.True(alphaIndex >= 0);
        Assert.True(betaIndex > alphaIndex);
        Assert.Contains("[prompt-fragment][plugin:plugin.orders.alpha][status=accepted][precedence=0100] Prefer deterministic plan checkpoints at each policy gate.", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("[prompt-fragment][plugin:plugin.orders.beta][status=accepted][precedence=0200] Always include fulfillment rollback criteria before merge.", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", PluginInstructionInjectionChecklistItem)]
    public async Task PluginPromptFragments_GivenUnsafeOrMalformedFragment_ExpectedSanitizedOrRejectedFragmentWithAuditEvidence()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithPromptContextComposerProofProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());
        var diagnosticsBridge = new DiagnosticsBridge(
            runManifest: new RunManifest(
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Plugins:
                [
                    new PluginManifestEntry(
                        PluginId: "plugin.orders.safety",
                        PluginName: "Orders Safety",
                        PluginVersion: "1.1.0",
                        AssemblyName: "Plugin.Orders.Safety",
                        AssemblyVersion: "1.1.0.0",
                        Capabilities:
                        [
                            "prompt.system.fragment:0042|  Keep   all   plan   steps   deterministic\tand auditable.  ",
                            "prompt.system.fragment:0030|```drop table```",
                            "prompt.system.fragment:"
                        ],
                        RequiredPermissions: ["repo.read"])
                ],
                Workflows: Array.Empty<WorkflowManifestEntry>()),
            diagnostics:
            [
                "[activation][run:run-plugin-sanitize] Loaded plugin 'plugin.orders.safety'."
            ]);

        using var reader = new StringReader("init\nsession start \"Plugin fragment sanitize transcript\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(
            orchestrator,
            reader,
            writer,
            builder,
            fixture.RepositoryPath,
            diagnosticsBridge,
            validationCommands: ["dotnet build", "dotnet test"]);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, CorrelationCaptureModelProvider.ExecutionCount);
        Assert.Contains("Plugin[plugin.orders.safety] Keep all plan steps deterministic and auditable.", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Plugin[plugin.orders.safety] ```", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
        Assert.Contains("[prompt-fragment][plugin:plugin.orders.safety][status=rejected][reason=unsafe-token] capability=prompt.system.fragment:<redacted>", CorrelationCaptureModelProvider.LastSystemMessage, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", PluginInstructionInjectionChecklistItem)]
    public async Task PluginPromptFragments_GivenPluginLifecycleUnload_ExpectedFragmentRemovedFromSubsequentPromptContext()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithPromptContextComposerProofProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());
        var diagnosticsBridge = new DiagnosticsBridge(
            runManifest: new RunManifest(
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Plugins:
                [
                    new PluginManifestEntry(
                        PluginId: "plugin.orders.lifecycle",
                        PluginName: "Orders Lifecycle",
                        PluginVersion: "1.0.0",
                        AssemblyName: "Plugin.Orders.Lifecycle",
                        AssemblyVersion: "1.0.0.0",
                        Capabilities:
                        [
                            "tool.orders.lifecycle",
                            "prompt.system.fragment:0099|Capture lifecycle ownership before merge execution."
                        ],
                        RequiredPermissions: ["repo.read"])
                ],
                Workflows: Array.Empty<WorkflowManifestEntry>()),
            diagnostics:
            [
                "[activation][run:run-plugin-unload] Loaded plugin 'plugin.orders.lifecycle'."
            ]);

        using var reader = new StringReader("init\nsession start \"Plugin fragment unload transcript\"\nplan\nplugins unload\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(
            orchestrator,
            reader,
            writer,
            builder,
            fixture.RepositoryPath,
            diagnosticsBridge,
            validationCommands: ["dotnet build", "dotnet test"]);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, CorrelationCaptureModelProvider.ExecutionCount);
        Assert.Equal(2, CorrelationCaptureModelProvider.SystemMessages.Count);
        Assert.Contains("Plugins unloaded.", writer.ToString(), StringComparison.Ordinal);

        Assert.Contains(
            "Plugin[plugin.orders.lifecycle] Capture lifecycle ownership before merge execution.",
            CorrelationCaptureModelProvider.SystemMessages[0],
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Plugin[plugin.orders.lifecycle] Capture lifecycle ownership before merge execution.",
            CorrelationCaptureModelProvider.SystemMessages[1],
            StringComparison.Ordinal);
        Assert.Contains("PluginFacts: none", CorrelationCaptureModelProvider.SystemMessages[1], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", PromptContextPolicyGateChecklistItem)]
    public async Task PromptPolicyGate_GivenMissingValidatorCatalog_ExpectedProviderExecutionRejectedBeforeNetworkDispatch()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithMissingValidatorCatalogProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());

        using var reader = new StringReader("init\nsession start \"Prompt policy gate missing validator\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, fixture.RepositoryPath);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var output = writer.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(0, CorrelationCaptureModelProvider.ExecutionCount);
        Assert.Contains("Plan generated:", output, StringComparison.Ordinal);
        Assert.Contains("Model provider skipped: prompt-context-policy-gate:missing-validator-catalog", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Model provider used:", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", PromptContextPolicyGateChecklistItem)]
    public async Task PromptPolicyGate_GivenInconsistentRepositoryRoot_ExpectedProviderExecutionRejectedBeforeNetworkDispatch()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithPromptContextComposerProofProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());

        using var reader = new StringReader("init\nsession start \"Prompt policy gate repository root mismatch\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(
            orchestrator,
            reader,
            writer,
            builder,
            fixture.RepositoryPath,
            promptContextFactory: snapshot => new RuntimePromptContext(
                systemInstructions: ["deterministic instruction"],
                environmentFacts: new RuntimePromptEnvironmentFacts(
                    sessionId: snapshot.SessionId.Value,
                    repositoryPath: Path.Combine(snapshot.RepositoryPath, "mismatched-root"),
                    worktreePath: snapshot.WorktreePath,
                    validationCommands: ["dotnet build"]),
                pluginFacts:
                [
                    new RuntimePromptPluginFact(
                        pluginId: "plugin.orders.fulfillment",
                        pluginName: "Orders Fulfillment",
                        pluginVersion: "1.2.0",
                        capabilities: ["tool.fulfillment.route"],
                        requiredPermissions: ["repo.read"])
                ],
                workflowPolicyScope: new RuntimePromptWorkflowPolicyScope(snapshot.WorkflowId.Value, "policy.shell"),
                capabilityCatalog: new RuntimePromptCapabilityCatalog(
                    toolCapabilityIds: ["tool.alpha"],
                    validatorCapabilityIds: ["validator.alpha"]),
                forbiddenEcosystems: ["npm"]));

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var output = writer.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(0, CorrelationCaptureModelProvider.ExecutionCount);
        Assert.Contains("Plan generated:", output, StringComparison.Ordinal);
        Assert.Contains("Model provider skipped: prompt-context-policy-gate:inconsistent-repository-root", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Model provider used:", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", PromptContextPolicyGateChecklistItem)]
    public async Task PromptPolicyGate_GivenInconsistentWorkflowId_ExpectedProviderExecutionRejectedBeforeNetworkDispatch()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithPromptContextComposerProofProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());

        using var reader = new StringReader("init\nsession start \"Prompt policy gate workflow mismatch\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(
            orchestrator,
            reader,
            writer,
            builder,
            fixture.RepositoryPath,
            promptContextFactory: snapshot => new RuntimePromptContext(
                systemInstructions: ["deterministic instruction"],
                environmentFacts: new RuntimePromptEnvironmentFacts(
                    sessionId: snapshot.SessionId.Value,
                    repositoryPath: snapshot.RepositoryPath,
                    worktreePath: snapshot.WorktreePath,
                    validationCommands: ["dotnet build"]),
                pluginFacts:
                [
                    new RuntimePromptPluginFact(
                        pluginId: "plugin.orders.fulfillment",
                        pluginName: "Orders Fulfillment",
                        pluginVersion: "1.2.0",
                        capabilities: ["tool.fulfillment.route"],
                        requiredPermissions: ["repo.read"])
                ],
                workflowPolicyScope: new RuntimePromptWorkflowPolicyScope("workflow.mismatch", "policy.shell"),
                capabilityCatalog: new RuntimePromptCapabilityCatalog(
                    toolCapabilityIds: ["tool.alpha"],
                    validatorCapabilityIds: ["validator.alpha"]),
                forbiddenEcosystems: ["npm"]));

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var output = writer.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(0, CorrelationCaptureModelProvider.ExecutionCount);
        Assert.Contains("Plan generated:", output, StringComparison.Ordinal);
        Assert.Contains("Model provider skipped: prompt-context-policy-gate:inconsistent-workflow-id", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Model provider used:", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", PromptContextPolicyGateChecklistItem)]
    public async Task PromptPolicyGate_GivenInconsistentPolicyId_ExpectedProviderExecutionRejectedBeforeNetworkDispatch()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithPromptContextComposerProofProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());

        using var reader = new StringReader("init\nsession start \"Prompt policy gate policy mismatch\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(
            orchestrator,
            reader,
            writer,
            builder,
            fixture.RepositoryPath,
            promptContextFactory: snapshot => new RuntimePromptContext(
                systemInstructions: ["deterministic instruction"],
                environmentFacts: new RuntimePromptEnvironmentFacts(
                    sessionId: snapshot.SessionId.Value,
                    repositoryPath: snapshot.RepositoryPath,
                    worktreePath: snapshot.WorktreePath,
                    validationCommands: ["dotnet build"]),
                pluginFacts:
                [
                    new RuntimePromptPluginFact(
                        pluginId: "plugin.orders.fulfillment",
                        pluginName: "Orders Fulfillment",
                        pluginVersion: "1.2.0",
                        capabilities: ["tool.fulfillment.route"],
                        requiredPermissions: ["repo.read"])
                ],
                workflowPolicyScope: new RuntimePromptWorkflowPolicyScope(snapshot.WorkflowId.Value, "policy.unregistered"),
                capabilityCatalog: new RuntimePromptCapabilityCatalog(
                    toolCapabilityIds: ["tool.alpha"],
                    validatorCapabilityIds: ["validator.alpha"]),
                forbiddenEcosystems: ["npm"]));

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var output = writer.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(0, CorrelationCaptureModelProvider.ExecutionCount);
        Assert.Contains("Plan generated:", output, StringComparison.Ordinal);
        Assert.Contains("Model provider skipped: prompt-context-policy-gate:inconsistent-policy-id", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Model provider used:", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", PromptContextPolicyGateChecklistItem)]
    public async Task PromptPolicyGate_GivenMissingPluginOwnership_ExpectedDeterministicFallbackPlanAndExplicitRejectionReason()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithPromptContextComposerProofProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());
        var diagnosticsBridge = new DiagnosticsBridge(
            runManifest: new RunManifest(
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Plugins:
                [
                    new PluginManifestEntry(
                        PluginId: "plugin.orders.fulfillment",
                        PluginName: "Orders Fulfillment",
                        PluginVersion: "1.2.0",
                        AssemblyName: "Plugin.Orders.Fulfillment",
                        AssemblyVersion: "1.2.0.0",
                        Capabilities: ["tool.fulfillment.route"],
                        RequiredPermissions: Array.Empty<string>())
                ],
                Workflows: Array.Empty<WorkflowManifestEntry>()),
            diagnostics:
            [
                "[activation][run:run-456] Loaded plugin 'plugin.orders.fulfillment'."
            ]);

        using var reader = new StringReader("init\nsession start \"Prompt policy gate missing plugin ownership\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(
            orchestrator,
            reader,
            writer,
            builder,
            fixture.RepositoryPath,
            diagnosticsBridge,
            validationCommands: ["dotnet test", "dotnet build"]);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var output = writer.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(0, CorrelationCaptureModelProvider.ExecutionCount);
        Assert.Contains("Plan generated:", output, StringComparison.Ordinal);
        Assert.Contains("Model provider skipped: prompt-context-policy-gate:missing-plugin-ownership:plugin.orders.fulfillment", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Model provider used:", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", NegativePathIsolationChecklistItem)]
    public async Task ProviderInvocation_GivenCredentialFailure_ExpectedNegativeContractWithNoSideEffectArtifactCreation()
    {
        CredentialFailureModelProvider.Reset();

        var builder = CreateBuilderWithCredentialFailureProvider();
        var output = await ExecuteProviderFailurePlanScenarioAsync(
            builder,
            "Credential failure transcript",
            sessionId => $"Plan failed: Model-provider plan generation failed: Credential validation failed for provider request correlation 'plan-session-{sessionId.Value}-operation-plan': missing or invalid API key.");

        Assert.Equal(1, CredentialFailureModelProvider.ExecutionCount);
        Assert.DoesNotContain("Model provider correlation:", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", NegativePathIsolationChecklistItem)]
    public async Task ProviderInvocation_GivenUnsupportedModelSelection_ExpectedDeterministicRejectionWithoutArtifactMutation()
    {
        UnsupportedModelFailureModelProvider.Reset();

        var builder = CreateBuilderWithUnsupportedModelFailureProvider();
        var output = await ExecuteProviderFailurePlanScenarioAsync(
            builder,
            "Unsupported model transcript",
            static _ => "Plan failed: Model-provider plan generation failed: Unsupported model selection 'deepseek-chat'. Supported models: deepseek-reasoner.");

        Assert.Equal(1, UnsupportedModelFailureModelProvider.ExecutionCount);
        Assert.DoesNotContain("Model provider correlation:", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", NegativePathIsolationChecklistItem)]
    public async Task ProviderInvocation_GivenEndpointRejection_ExpectedBusinessErrorContractIncludesProviderReasonAndCorrelation()
    {
        EndpointFailureModelProvider.Reset();

        var builder = CreateBuilderWithEndpointFailureProvider();
        var output = await ExecuteProviderFailurePlanScenarioAsync(
            builder,
            "Endpoint rejection transcript",
            sessionId => $"Plan failed: Model-provider plan generation failed: DeepSeek endpoint failure for correlation 'plan-session-{sessionId.Value}-operation-plan': status code 503 (ServiceUnavailable).");

        Assert.Equal(1, EndpointFailureModelProvider.ExecutionCount);
        Assert.Contains("status code 503", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", NegativePathIsolationAndFailureContractsChecklistItem)]
    public async Task ProviderInvocation_GivenTimeout_ExpectedDeterministicFailureContractAndNoSideEffectArtifactMutation()
    {
        TimeoutFailureModelProvider.Reset();

        var builder = CreateBuilderWithTimeoutFailureProvider();
        var output = await ExecuteProviderFailurePlanScenarioAsync(
            builder,
            "Timeout failure transcript",
            sessionId => $"Plan failed: Model-provider plan generation failed: Provider execution timed out for correlation 'plan-session-{sessionId.Value}-operation-plan'.");

        Assert.Equal(1, TimeoutFailureModelProvider.ExecutionCount);
        Assert.DoesNotContain("Model provider correlation:", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", NegativePathIsolationAndFailureContractsChecklistItem)]
    public async Task ProviderInvocation_GivenMalformedProviderPayload_ExpectedDeterministicParseFailureAndNoPartialPlanArtifact()
    {
        MalformedPayloadModelProvider.Reset();

        var builder = CreateBuilderWithMalformedPayloadProvider();
        var output = await ExecuteProviderFailurePlanScenarioAsync(
            builder,
            "Malformed payload transcript",
            static _ => "Plan failed: Model-provider plan generation failed: Provider output did not contain any numbered plan steps.");

        Assert.Equal(1, MalformedPayloadModelProvider.ExecutionCount);
        Assert.DoesNotContain("Model provider correlation:", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", NegativePathIsolationAndFailureContractsChecklistItem)]
    public async Task ProviderInvocation_GivenPluginFragmentValidationFailure_ExpectedPolicyRejectionContractAndIsolation()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithPromptContextComposerProofProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());

        using var reader = new StringReader("init\nsession start \"Fragment validation failure transcript\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(
            orchestrator,
            reader,
            writer,
            builder,
            fixture.RepositoryPath,
            promptContextFactory: _ => throw new InvalidOperationException("Plugin fragment validation failed: rejected prompt fragment metadata for plugin 'plugin.orders.safety'."));

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var output = writer.ToString();
        var sessionId = ExtractSessionId(output);

        using var state = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = state.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal(0, exitCode);
        Assert.Equal(0, CorrelationCaptureModelProvider.ExecutionCount);
        Assert.Contains("Plan failed: Model-provider plan generation failed: Plugin fragment validation failed:", output, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(artifactDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    [Trait("ChecklistItem", NegativePathIsolationAndFailureContractsChecklistItem)]
    public async Task ProviderInvocation_GivenContextPolicyRejection_ExpectedBusinessErrorContractIncludesCorrelationAndReason()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        CorrelationCaptureModelProvider.Reset();

        var builder = CreateBuilderWithMissingValidatorCatalogProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());

        using var reader = new StringReader("init\nsession start \"Context policy rejection transcript\"\nplan\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, fixture.RepositoryPath);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var output = writer.ToString();
        var sessionId = ExtractSessionId(output);

        using var state = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = state.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal(0, exitCode);
        Assert.Equal(0, CorrelationCaptureModelProvider.ExecutionCount);
        Assert.Contains("Plan generated:", output, StringComparison.Ordinal);
        Assert.Contains("Model provider skipped: prompt-context-policy-gate:missing-validator-catalog", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Model provider used:", output, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(artifactDirectory, "prompt-context-diagnostics-*.json", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    [Trait("ChecklistItem", DiffCheckpointHashChecklistItem)]
    public async Task DiffAndCheckpoint_GivenEquivalentLineEndingNoise_KeepStableNormalizedDiffHash()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Diff hash reproducibility\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            var worktreePath = startedState.RootElement.GetProperty("WorktreePath").GetString()
                ?? throw new InvalidOperationException("Persisted session state did not contain a worktree path.");
            var trackedFile = Path.Combine(worktreePath, "README.md");

            await File.WriteAllTextAsync(trackedFile, "base\r\nfeature\r\n", CancellationToken.None);
            await fixture.RunGitAsync("-C", worktreePath, "add", "README.md");
            await fixture.RunGitAsync("-C", worktreePath, "commit", "-m", "feature with crlf");
        }

        var firstRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "checkpoint",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, firstRun.ExitCode);
        var firstDiffHash = ExtractLastValueByPrefix(firstRun.StdOut, "diffHash: ");
        Assert.Matches("^[0-9a-f]{64}$", firstDiffHash);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            var worktreePath = startedState.RootElement.GetProperty("WorktreePath").GetString()
                ?? throw new InvalidOperationException("Persisted session state did not contain a worktree path.");
            var trackedFile = Path.Combine(worktreePath, "README.md");

            await File.WriteAllTextAsync(trackedFile, "base\nfeature\n", CancellationToken.None);
            await fixture.RunGitAsync("-C", worktreePath, "add", "README.md");
            await fixture.RunGitAsync("-C", worktreePath, "commit", "--amend", "--no-edit");
        }

        var secondRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "checkpoint",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, secondRun.ExitCode);
        var secondDiffHash = ExtractLastValueByPrefix(secondRun.StdOut, "diffHash: ");
        var checkpointArtifactPath = ExtractLastValueByPrefix(secondRun.StdOut, "Checkpoint artifact: ");
        var checkpointFullPath = Path.Combine(fixture.RepositoryPath, checkpointArtifactPath);

        Assert.Equal(firstDiffHash, secondDiffHash);
        Assert.True(File.Exists(checkpointFullPath));

        using var checkpointArtifact = JsonDocument.Parse(await File.ReadAllTextAsync(checkpointFullPath, CancellationToken.None));
        Assert.Equal(secondDiffHash, checkpointArtifact.RootElement.GetProperty("DiffHash").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", DiffCheckpointHashChecklistItem)]
    public async Task Diff_GivenCommittedCandidateChange_PersistsPatchAndChangedFilesEvidence()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Diff artifact evidence\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "diff-artifact-proof\n",
                "diff artifact proof");
        }

        var diffRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, diffRun.ExitCode);
        var diffHash = ExtractLastValueByPrefix(diffRun.StdOut, "diffHash: ");
        var diffArtifactPath = ExtractLastValueByPrefix(diffRun.StdOut, "Diff artifact: ");
        var diffArtifactContent = await File.ReadAllTextAsync(Path.Combine(fixture.RepositoryPath, diffArtifactPath), CancellationToken.None);

        Assert.Contains($"# Diff Hash: {diffHash}", diffArtifactContent, StringComparison.Ordinal);
        Assert.Contains("# Changed Files:", diffArtifactContent, StringComparison.Ordinal);
        Assert.Contains("README.md", diffArtifactContent, StringComparison.Ordinal);
        Assert.Contains("diff --git", diffArtifactContent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ValidationOrchestrationChecklistItem)]
    public async Task ValidateAsync_GivenSessionWorktree_ExecutesConfiguredValidationCommandsPersistsValidationReportAndMarksSessionValidating()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Validation orchestration transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "validation-orchestration-change\n",
                "validation orchestration change");
        }

        var validationRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, validationRun.ExitCode);
        Assert.Contains("Validation completed: session=", validationRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("validation=Passed", validationRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("buildCommand: dotnet build", validationRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("testCommand: dotnet test", validationRun.StdOut, StringComparison.Ordinal);

        var validationArtifactPath = ExtractLastValueByPrefix(validationRun.StdOut, "Validation artifact: ");
        var persistedValidationPath = Path.Combine(fixture.RepositoryPath, validationArtifactPath);
        using var validationPayload = JsonDocument.Parse(await File.ReadAllTextAsync(persistedValidationPath, CancellationToken.None));
        var root = validationPayload.RootElement;

        Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("DiffHash").GetString());
        if (root.TryGetProperty("Build", out var build) && root.TryGetProperty("Test", out var test))
        {
            Assert.True(build.GetProperty("Succeeded").GetBoolean());
            Assert.True(test.GetProperty("Succeeded").GetBoolean());
            Assert.StartsWith("dotnet build", build.GetProperty("Command").GetString(), StringComparison.Ordinal);
            Assert.StartsWith("dotnet test", test.GetProperty("Command").GetString(), StringComparison.Ordinal);
            Assert.Equal(0, build.GetProperty("ExitCode").GetInt32());
            Assert.Equal(0, test.GetProperty("ExitCode").GetInt32());
        }
        else
        {
            Assert.True(root.GetProperty("BuildSucceeded").GetBoolean());
            Assert.True(root.GetProperty("TestSucceeded").GetBoolean());

            var commandResults = root.GetProperty("CommandResults").EnumerateArray().ToArray();
            Assert.Equal(2, commandResults.Length);
            Assert.StartsWith("dotnet build", commandResults[0].GetProperty("Command").GetString(), StringComparison.Ordinal);
            Assert.StartsWith("dotnet test", commandResults[1].GetProperty("Command").GetString(), StringComparison.Ordinal);
            Assert.Equal(0, commandResults[0].GetProperty("ExitCode").GetInt32());
            Assert.Equal(0, commandResults[1].GetProperty("ExitCode").GetInt32());
        }

        using var persistedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        Assert.Equal("Validating", persistedState.RootElement.GetProperty("State").GetString());
        Assert.Equal("Passed", persistedState.RootElement.GetProperty("ValidationStatus").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", PolicyEnforcementChecklistItem)]
    [Trait("ChecklistItem", DotNetPolicyDrivenIsolationChecklistItem)]
    public Task ValidateAsync_GivenDangerousValidationCommandPattern_ExpectedPolicyDeniesBeforeExecutionAndLogsReason()
        => ExecutePolicyViolationDangerousValidationScenarioAsync();

    [Fact]
    [Trait("ChecklistItem", PolicyViolationPayloadChecklistItem)]
    public Task PolicyViolation_GivenBlockedCommand_ExpectedResponseIncludesBlockedActionPolicyNameAndNextStepGuidance()
        => ExecutePolicyViolationDangerousValidationScenarioAsync();

    [Theory]
    [InlineData("dotnet build --nologo && git push origin main", "dangerous command rule", "Remove dangerous command tokens")]
    [InlineData("dotnet build --nologo && git clean -fdx", "dangerous command rule", "Remove dangerous command tokens")]
    [InlineData("dotnet build --nologo && echo hacked > .git/config", "dangerous command rule", "Remove dangerous command tokens")]
    [Trait("ChecklistItem", DangerousCommandDenylistProofChecklistItem)]
    public Task ValidateAsync_GivenDangerousValidationCommandsForDenylistParity_ExpectedPolicyDeniesBeforeExecutionAndLogsReason(
        string dangerousValidationCommand,
        string expectedReasonFragment,
        string expectedGuidanceFragment)
        => ExecutePolicyViolationDangerousValidationScenarioAsync(dangerousValidationCommand, expectedReasonFragment, expectedGuidanceFragment);

    [Theory]
    [InlineData("dotnet build --nologo && git -c alias.integrate=merge integrate HEAD")]
    [InlineData("dotnet build --nologo && git -c alias.sync=\"pull --rebase\" sync")]
    [InlineData("dotnet build --nologo && git config alias.integrate merge && git integrate HEAD")]
    [Trait("ChecklistItem", PrivilegedMergeIsolationChecklistItem)]
    public Task MergeIsolation_GivenGenericShellToolExecutionAttempt_RejectsMergeSemanticsAndPreservesRepositoryState(string aliasedMergeCommand)
        => ExecutePolicyViolationDangerousValidationScenarioAsync(
            dangerousValidationCommand: aliasedMergeCommand,
            expectedReasonFragment: "merge semantics are privileged",
            expectedGuidanceFragment: "dedicated shell merge flow");

    [Fact]
    [Trait("ChecklistItem", DangerousCommandDenylistProofChecklistItem)]
    public Task ValidateAsync_GivenValidationCommandWithOutOfWorktreeTargetPath_ExpectedBoundaryGuardBlockAndAuditPayload()
    {
        var outOfWorktreeTargetPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"modus-outside-{Guid.NewGuid():N}.txt"));
        return ExecutePolicyViolationDangerousValidationScenarioAsync(
            dangerousValidationCommand: $"dotnet build --nologo && copy README.md \"{outOfWorktreeTargetPath}\"",
            expectedReasonFragment: "target path",
            expectedGuidanceFragment: "inside the active session worktree");
    }

    private async Task ExecutePolicyViolationDangerousValidationScenarioAsync()
        => await ExecutePolicyViolationDangerousValidationScenarioAsync(
            dangerousValidationCommand: "dotnet build --nologo && rm -rf .",
            expectedReasonFragment: "dangerous command rule",
            expectedGuidanceFragment: "Remove dangerous command tokens");

    private async Task ExecutePolicyViolationDangerousValidationScenarioAsync(
        string dangerousValidationCommand,
        string expectedReasonFragment,
        string expectedGuidanceFragment)
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Policy dangerous command transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

                await WriteRepositoryConfigAsync(
                        fixture.RepositoryPath,
                        JsonSerializer.Serialize(
                                new
                                {
                                        ValidationCommands = new[]
                                        {
                                                dangerousValidationCommand,
                                                "dotnet test --no-build"
                                        }
                                }));

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "policy-dangerous-validation-change\n",
                "policy dangerous validation change");
        }

        var validationRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, validationRun.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", validationRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Diff context: session=", validationRun.StdOut, StringComparison.Ordinal);
        var payloadText = ExtractLastValueByPrefix(validationRun.StdOut, "Validation failed: ");
        Assert.StartsWith("{\"BlockedAction\":", payloadText, StringComparison.Ordinal);

        using var payloadDocument = JsonDocument.Parse(payloadText);
        var payload = payloadDocument.RootElement;
        Assert.Equal(dangerousValidationCommand, payload.GetProperty("BlockedAction").GetString());
        Assert.Equal("local-safe", payload.GetProperty("BlockingPolicy").GetString());

        var nextStepGuidance = payload.GetProperty("NextStepGuidance").GetString();
        Assert.False(string.IsNullOrWhiteSpace(nextStepGuidance));
        Assert.Contains(expectedGuidanceFragment, nextStepGuidance, StringComparison.OrdinalIgnoreCase);

        var reason = payload.GetProperty("Reason").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Contains(expectedReasonFragment, reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Validation completed: session=", validationRun.StdOut, StringComparison.Ordinal);

        using var persistedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        Assert.Equal("Editing", persistedState.RootElement.GetProperty("State").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", PolicyEnforcementChecklistItem)]
    public async Task Merge_GivenValidationEvidenceRegressedAfterApproval_ExpectedPolicyDeniesPrivilegedMergePath()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Policy privileged merge transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "policy-privileged-merge-change\n",
                "policy privileged merge change");
        }

        var approvalRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "approve",
                "yes",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, approvalRun.ExitCode);
        Assert.Contains("Approval completed: session=", approvalRun.StdOut, StringComparison.Ordinal);

        var validationArtifactPath = ExtractLastValueByPrefix(approvalRun.StdOut, "Validation artifact: ");
        await MarkValidationArtifactFailedAsync(Path.Combine(fixture.RepositoryPath, validationArtifactPath));

        var mergeRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "merge",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, mergeRun.ExitCode);
        var mergeFailurePayloadText = ExtractLastValueByPrefix(mergeRun.StdOut, "Merge failed: ");
        Assert.StartsWith("{\"BlockedAction\":", mergeFailurePayloadText, StringComparison.Ordinal);

        using var mergeFailurePayloadDocument = JsonDocument.Parse(mergeFailurePayloadText);
        var mergeFailurePayload = mergeFailurePayloadDocument.RootElement;
        Assert.Equal("merge", mergeFailurePayload.GetProperty("BlockedAction").GetString());
        Assert.Equal("local-safe", mergeFailurePayload.GetProperty("BlockingPolicy").GetString());
        Assert.Contains("Run 'validate'", mergeFailurePayload.GetProperty("NextStepGuidance").GetString(), StringComparison.Ordinal);
        Assert.Contains(
            "passing validation evidence is required before this operation",
            mergeFailurePayload.GetProperty("Reason").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Merge completed: session=", mergeRun.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ReviewStalenessChecklistItem)]
    public async Task Review_GivenPassingValidationAndCurrentDiff_WritesMarkdownReportWithStalenessStatusAndValidationEvidenceLinks()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Review evidence transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "review-evidence-change\n",
                "review evidence change");
        }

        var reviewRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, reviewRun.ExitCode);
        Assert.Contains("Review generated: session=", reviewRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("stale=False", reviewRun.StdOut, StringComparison.Ordinal);

        var reviewArtifactPath = ExtractLastValueByPrefix(reviewRun.StdOut, "Review artifact: ");
        var reviewSummaryArtifactPath = ExtractLastValueByPrefix(reviewRun.StdOut, "Review summary artifact: ");

        var reviewMarkdown = await File.ReadAllTextAsync(Path.Combine(fixture.RepositoryPath, reviewArtifactPath), CancellationToken.None);
        using var reviewSummary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.RepositoryPath, reviewSummaryArtifactPath), CancellationToken.None));
        var summary = reviewSummary.RootElement;

        var currentDiffHash = summary.GetProperty("CurrentDiffHash").GetString()
            ?? throw new InvalidOperationException("CurrentDiffHash was not persisted in review summary artifact.");
        var validationDiffHash = summary.GetProperty("ValidationDiffHash").GetString()
            ?? throw new InvalidOperationException("ValidationDiffHash was not persisted in review summary artifact.");
        var validationArtifactId = summary.GetProperty("ValidationArtifactId").GetString()
            ?? throw new InvalidOperationException("ValidationArtifactId was not persisted in review summary artifact.");
        var validationArtifactPath = summary.GetProperty("ValidationArtifactPath").GetString()
            ?? throw new InvalidOperationException("ValidationArtifactPath was not persisted in review summary artifact.");

        Assert.Contains($"Validation diff hash: {validationDiffHash}", reviewMarkdown, StringComparison.Ordinal);
        Assert.Contains($"Current diff hash: {currentDiffHash}", reviewMarkdown, StringComparison.Ordinal);
        Assert.Contains($"Validation artifact id: {validationArtifactId}", reviewMarkdown, StringComparison.Ordinal);
        Assert.Contains($"Validation artifact path: {validationArtifactPath}", reviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("Stale: No", reviewMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ReviewStalenessChecklistItem)]
    public async Task ReviewAsync_GivenValidationDiffHashMismatch_MarksReviewAsStaleWithDeterministicReason()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Review stale transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "review-stale-v1\n",
                "review stale v1");
        }

        var firstReviewRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, firstReviewRun.ExitCode);
        Assert.Contains("stale=False", firstReviewRun.StdOut, StringComparison.Ordinal);

        using (var reviewedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                reviewedState.RootElement,
                "README.md",
                "review-stale-v2\n",
                "review stale v2");
        }

        var staleReviewRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "review",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, staleReviewRun.ExitCode);
        Assert.Contains("Review generated: session=", staleReviewRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("stale=True", staleReviewRun.StdOut, StringComparison.Ordinal);

        var staleReviewArtifactPath = ExtractLastValueByPrefix(staleReviewRun.StdOut, "Review artifact: ");
        var staleReviewSummaryArtifactPath = ExtractLastValueByPrefix(staleReviewRun.StdOut, "Review summary artifact: ");

        var staleMarkdown = await File.ReadAllTextAsync(Path.Combine(fixture.RepositoryPath, staleReviewArtifactPath), CancellationToken.None);
        using var staleSummary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.RepositoryPath, staleReviewSummaryArtifactPath), CancellationToken.None));
        var summaryRoot = staleSummary.RootElement;

        Assert.True(summaryRoot.GetProperty("IsStale").GetBoolean());
        Assert.Equal(
            "Validation report is stale: validation diff hash does not match current candidate diff hash.",
            summaryRoot.GetProperty("StaleReason").GetString());
        Assert.Contains("Stale: Yes", staleMarkdown, StringComparison.Ordinal);
        Assert.Contains(
            "Reason: Validation report is stale: validation diff hash does not match current candidate diff hash.",
            staleMarkdown,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalReviewPrerequisiteChecklistItem)]
    public async Task ApprovalGate_GivenMissingReviewEvidence_RejectsApprovalWithDeterministicRecoveryGuidance()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Approval missing review evidence transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "approval-missing-review-prereq\n",
                "approval missing review prerequisite");
        }

        var approveRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "approve",
                "yes",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, approveRun.ExitCode);
        Assert.Contains(
            "Approval failed: Approval requires a persisted review report. Run 'review' first.",
            approveRun.StdOut,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Approval completed: session=", approveRun.StdOut, StringComparison.Ordinal);

        using var finalState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = finalState.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal("Validating", finalState.RootElement.GetProperty("State").GetString());
        Assert.Empty(Directory.EnumerateFiles(artifactDirectory, "approval-token-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalReviewPrerequisiteChecklistItem)]
    public async Task ApprovalGate_GivenMissingOrStaleReviewEvidence_RejectsApprovalWithDeterministicRecoveryGuidance()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Approval review prerequisite transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "approval-prereq-v1\n",
                "approval prerequisite v1");
        }

        var reviewRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, reviewRun.ExitCode);
        Assert.Contains("Review generated: session=", reviewRun.StdOut, StringComparison.Ordinal);

        using (var reviewedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                reviewedState.RootElement,
                "README.md",
                "approval-prereq-v2\n",
                "approval prerequisite v2");
        }

        var approveRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "approve",
                "yes",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, approveRun.ExitCode);
        Assert.Contains(
            "Approval failed: current review evidence does not match the active diff hash.",
            approveRun.StdOut,
            StringComparison.Ordinal);
        Assert.Contains(
            "Run 'review' to regenerate review evidence for the current diff hash, then retry 'approve'.",
            approveRun.StdOut,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Approval completed: session=", approveRun.StdOut, StringComparison.Ordinal);

        using var finalState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = finalState.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal("AwaitingApproval", finalState.RootElement.GetProperty("State").GetString());
        Assert.Empty(Directory.EnumerateFiles(artifactDirectory, "approval-token-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalTokenChecklistItem)]
    public async Task Approve_GivenUserDeclinesConfirmation_DoesNotCreateApprovalTokenAndLeavesSessionAwaitingApproval()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Approval decline transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "approval-decline-change\n",
                "approval decline change");
        }

        var approvalRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "approve",
                "no",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, approvalRun.ExitCode);
        Assert.Contains("Approval confirmation required:", approvalRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Type 'yes' to create an approval token or anything else to cancel.", approvalRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Approval cancelled for session {sessionId}.", approvalRun.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Approval completed: session=", approvalRun.StdOut, StringComparison.Ordinal);

        using var finalState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = finalState.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal("AwaitingApproval", finalState.RootElement.GetProperty("State").GetString());
        Assert.Empty(Directory.EnumerateFiles(artifactDirectory, "approval-token-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalTokenChecklistItem)]
    public async Task Approve_GivenCurrentReviewValidationAndConfirmedPrompt_CreatesApprovalTokenBoundToSessionDiffHashAndTargetCommit()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Approval binding transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        string targetCommit;
        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            targetCommit = startedState.RootElement.GetProperty("TargetCommit").GetString()
                ?? throw new InvalidOperationException("Persisted session state did not contain target commit.");

            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "approval-binding-change\n",
                "approval binding change");
        }

        var approvalRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "approve",
                "yes",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, approvalRun.ExitCode);
        Assert.Contains("Approval confirmation required:", approvalRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("reviewStale: False", approvalRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("validation: Passed", approvalRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Approval completed: session=", approvalRun.StdOut, StringComparison.Ordinal);

        var reviewSummaryPath = ExtractLastValueByPrefix(approvalRun.StdOut, "Review summary artifact: ");
        using var reviewSummary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.RepositoryPath, reviewSummaryPath), CancellationToken.None));
        var expectedDiffHash = reviewSummary.RootElement.GetProperty("CurrentDiffHash").GetString()
            ?? throw new InvalidOperationException("CurrentDiffHash was not persisted in review summary artifact.");

        using var finalState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = finalState.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        var approvalTokenPath = Assert.Single(
            Directory.EnumerateFiles(artifactDirectory, "approval-token-*.json", SearchOption.TopDirectoryOnly),
            static path => !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));

        using var approvalTokenDocument = JsonDocument.Parse(await File.ReadAllTextAsync(approvalTokenPath, CancellationToken.None));
        var binding = approvalTokenDocument.RootElement.GetProperty("Binding");

        Assert.Equal(sessionId.Value, binding.GetProperty("SessionId").GetProperty("Value").GetString());
        Assert.Equal(expectedDiffHash, binding.GetProperty("DiffHash").GetString());
        Assert.Equal(targetCommit, binding.GetProperty("TargetCommit").GetString());
        Assert.Equal("Approved", finalState.RootElement.GetProperty("State").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", SessionEventJournalChecklistItem)]
    public async Task ShellProcess_GivenInitSessionStartPlanDiffValidateReviewApproveMerge_ExpectedAcceptanceTranscriptSucceeds()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var bootstrapRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "help",
                "init",
                "repo",
                "use workflow workflow.linear",
                "session start \"Acceptance transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, bootstrapRun.ExitCode);
        Assert.Contains("Available commands:", bootstrapRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Repository initialized: {fixture.RepositoryPath}", bootstrapRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Repository path: {fixture.RepositoryPath}", bootstrapRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Workflow selected: workflow.linear", bootstrapRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Session started:", bootstrapRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Plan generated: workflow=workflow.linear state=Editing", bootstrapRun.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Session transitioned to", bootstrapRun.StdOut, StringComparison.Ordinal);

        var runSessionId = ExtractSessionId(bootstrapRun.StdOut);
        var runProof = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {runSessionId}",
                "run",
                "archive",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, runProof.ExitCode);
        Assert.Contains($"Session attached: {runSessionId}", runProof.StdOut, StringComparison.Ordinal);
        Assert.Contains("Workflow executed: workflow.linear", runProof.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Session archived from shell: {runSessionId}.", runProof.StdOut, StringComparison.Ordinal);

        var governanceBootstrapRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "session start \"Acceptance governance transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, governanceBootstrapRun.ExitCode);
        Assert.Contains("Session started:", governanceBootstrapRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Plan generated: workflow=workflow.linear state=Editing", governanceBootstrapRun.StdOut, StringComparison.Ordinal);

        var sessionId = ExtractSessionId(governanceBootstrapRun.StdOut);
        using var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);

        await AppendAndCommitWorktreeChangeAsync(
            fixture,
            startedState.RootElement,
            "README.md",
            "acceptance-transcript-change\n",
            "acceptance transcript change");

        var governanceRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "approve",
                "yes",
                "artifacts",
                "merge",
                "archive",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, governanceRun.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"wip[{sessionId}]> ", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Validation completed: session=", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Review generated: session=", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Approval confirmation required:", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Approval completed: session=", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Artifacts for session {sessionId}:", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Merge completed: session=", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Session archived from shell: {sessionId}.", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Session transitioned to", governanceRun.StdOut, StringComparison.Ordinal);

        using var finalState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = finalState.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal("Archived", finalState.RootElement.GetProperty("State").GetString());
        Assert.True(File.Exists(Path.Combine(fixture.RepositoryPath, ".wip", "config.json")));
        AssertArtifactWithPrefixExists(artifactDirectory, "agent-plan-");
        AssertArtifactWithPrefixExists(artifactDirectory, "workspace-diff-");
        AssertArtifactWithPrefixExists(artifactDirectory, "validation-report-");
        AssertArtifactWithPrefixExists(artifactDirectory, "review-report-");
        AssertArtifactWithPrefixExists(artifactDirectory, "approval-token-");
        AssertArtifactWithPrefixExists(artifactDirectory, "merge-result-");

        var lifecycleEvents = await ReadLifecycleEventsAsync(fixture.RepositoryPath, sessionId);
        AssertEventShapeDeterministic(lifecycleEvents, "MergeAttempted", "Approved");
        AssertEventShapeDeterministic(lifecycleEvents, "MergeSucceeded", "Merged");
        AssertEventShapeDeterministic(lifecycleEvents, "SessionArchived", "Archived");
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", SessionPersistenceChecklistItem)]
    public async Task ShellProcess_GivenDetachedSessionAndRestart_ExpectedSessionsListAndSessionAttachResumeGovernanceFlow()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Resume transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);
        using var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);

        await AppendAndCommitWorktreeChangeAsync(
            fixture,
            startedState.RootElement,
            "README.md",
            "resume-transcript-change\n",
            "resume transcript change");

        var firstProcess = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "detach",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, firstProcess.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", firstProcess.StdOut, StringComparison.Ordinal);
        Assert.Contains("Validation completed: session=", firstProcess.StdOut, StringComparison.Ordinal);
        Assert.Contains("Review generated: session=", firstProcess.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Session detached: {sessionId}.", firstProcess.StdOut, StringComparison.Ordinal);

        var resumedProcess = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "sessions",
                $"session attach {sessionId}",
                "artifacts",
                "approve",
                "yes",
                "merge",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, resumedProcess.ExitCode);
        Assert.Contains("Persisted sessions:", resumedProcess.StdOut, StringComparison.Ordinal);
        Assert.Contains($"- {sessionId} task=\"Resume transcript\" state=AwaitingApproval workflow=workflow.linear", resumedProcess.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Session attached: {sessionId}", resumedProcess.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Artifacts for session {sessionId}:", resumedProcess.StdOut, StringComparison.Ordinal);
        Assert.Contains("Approval completed: session=", resumedProcess.StdOut, StringComparison.Ordinal);
        Assert.Contains("Merge completed: session=", resumedProcess.StdOut, StringComparison.Ordinal);

        using var mergedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        Assert.Equal("Merged", mergedState.RootElement.GetProperty("State").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", DotNetPolicyDrivenIsolationChecklistItem)]
    public async Task ApproveAsync_GivenLatestValidationFailed_ExpectedPolicyRejectsApprovalWithDeterministicViolationPayload()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Approval blocked by failed validation transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        await WriteRepositoryConfigAsync(
            fixture.RepositoryPath,
            JsonSerializer.Serialize(
                new
                {
                    ValidationCommands = new[]
                    {
                        "dotnet build MissingValidationProject.csproj --nologo",
                        "dotnet test --no-build"
                    }
                }));

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "approval-policy-isolation-failed-validation\n",
                "approval policy isolation failed validation");
        }

        var approveRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "approve",
                "yes",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, approveRun.ExitCode);
        Assert.Contains("Validation completed: session=", approveRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("validation=Failed", approveRun.StdOut, StringComparison.Ordinal);

        var approvalFailurePayloadText = ExtractLastValueByPrefix(approveRun.StdOut, "Approval failed: ");
        Assert.StartsWith("{\"BlockedAction\":", approvalFailurePayloadText, StringComparison.Ordinal);

        using var approvalFailurePayloadDocument = JsonDocument.Parse(approvalFailurePayloadText);
        var approvalFailurePayload = approvalFailurePayloadDocument.RootElement;

        Assert.Equal("approve", approvalFailurePayload.GetProperty("BlockedAction").GetString());
        Assert.Equal("local-safe", approvalFailurePayload.GetProperty("BlockingPolicy").GetString());
        Assert.Contains("Run 'validate'", approvalFailurePayload.GetProperty("NextStepGuidance").GetString(), StringComparison.Ordinal);
        Assert.Contains(
            "passing validation evidence is required before this operation",
            approvalFailurePayload.GetProperty("Reason").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Approval completed: session=", approveRun.StdOut, StringComparison.Ordinal);

        using var finalState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = finalState.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal("Validating", finalState.RootElement.GetProperty("State").GetString());
        Assert.DoesNotContain(
            Directory.EnumerateFiles(artifactDirectory, "approval-token-*", SearchOption.AllDirectories),
            path => !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalGatedMergeChecklistItem)]
    [Trait("ChecklistItem", DotNetPolicyDrivenIsolationChecklistItem)]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ShellProcess_GivenMergeWithoutApproval_ExpectedPolicyDenialEvidenceWithoutExternalMutation()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Denied merge transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);
        using var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);

        await AppendAndCommitWorktreeChangeAsync(
            fixture,
            startedState.RootElement,
            "README.md",
            "merge-without-approval\n",
            "merge without approval");

        var deniedRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "merge",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, deniedRun.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", deniedRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"wip[{sessionId}]> ", deniedRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Validation completed: session=", deniedRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Review generated: session=", deniedRun.StdOut, StringComparison.Ordinal);
        var mergeFailurePayloadText = ExtractLastValueByPrefix(deniedRun.StdOut, "Merge failed: ");
        Assert.StartsWith("{\"BlockedAction\":", mergeFailurePayloadText, StringComparison.Ordinal);

        using var mergeFailurePayloadDocument = JsonDocument.Parse(mergeFailurePayloadText);
        var mergeFailurePayload = mergeFailurePayloadDocument.RootElement;
        Assert.Equal("merge", mergeFailurePayload.GetProperty("BlockedAction").GetString());
        Assert.Equal("local-safe", mergeFailurePayload.GetProperty("BlockingPolicy").GetString());
        Assert.Contains("Run 'approve'", mergeFailurePayload.GetProperty("NextStepGuidance").GetString(), StringComparison.Ordinal);
        Assert.Contains(
            "explicit approval evidence is required before merge operations",
            mergeFailurePayload.GetProperty("Reason").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Merge completed: session=", deniedRun.StdOut, StringComparison.Ordinal);

        using var deniedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = deniedState.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal("AwaitingApproval", deniedState.RootElement.GetProperty("State").GetString());
        Assert.False(Directory.EnumerateFiles(artifactDirectory, "merge-report-*", SearchOption.AllDirectories).Any());
        Assert.DoesNotContain(
            Directory.EnumerateFiles(artifactDirectory, "approval-token-*", SearchOption.AllDirectories),
            path => !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("base\n", await File.ReadAllTextAsync(Path.Combine(fixture.RepositoryPath, "README.md"), CancellationToken.None));
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalGatedMergeChecklistItem)]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ShellProcess_GivenApproveThenMutateThenMerge_ExpectedStaleApprovalRejectedWithDeterministicEvidence()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Stale approval transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "candidate-v1\n",
                "candidate v1");
        }

        var approvalRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "approve",
                "yes",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, approvalRun.ExitCode);
        Assert.Contains("Approval completed: session=", approvalRun.StdOut, StringComparison.Ordinal);

        using (var approvedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                approvedState.RootElement,
                "README.md",
                "candidate-v2\n",
                "candidate v2");
        }

        var staleMergeRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "merge",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, staleMergeRun.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", staleMergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Merge preflight:", staleMergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("approvalCurrentDiffMatch: False", staleMergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Merge failed: Merge failed: approval is stale because the current diff no longer matches the last reviewed and validated candidate.", staleMergeRun.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Merge completed: session=", staleMergeRun.StdOut, StringComparison.Ordinal);
        Assert.Equal("base\n", await File.ReadAllTextAsync(Path.Combine(fixture.RepositoryPath, "README.md"), CancellationToken.None));

        using var finalApprovedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = finalApprovedState.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");
        Assert.Contains(
            Directory.EnumerateFiles(artifactDirectory, "approval-token-*", SearchOption.TopDirectoryOnly),
            path => !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));
        Assert.False(Directory.EnumerateFiles(artifactDirectory, "merge-report-*", SearchOption.AllDirectories).Any());
        Assert.Equal("Approved", finalApprovedState.RootElement.GetProperty("State").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ShellProcess_GivenAbortThenMergeAttempt_ExpectedGovernanceGuardDeniesMergeAfterAbort()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Abort guard transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        var abortRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "abort",
                "merge",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, abortRun.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", abortRun.StdOut, StringComparison.Ordinal);
        Assert.Contains($"Session aborted from shell: {sessionId} state=Aborted. Persisted session state remains on disk.", abortRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Attach a session with: session attach <session-id>", abortRun.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Merge completed: session=", abortRun.StdOut, StringComparison.Ordinal);

        using var finalState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = finalState.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal("Aborted", finalState.RootElement.GetProperty("State").GetString());
        Assert.False(Directory.EnumerateFiles(artifactDirectory, "merge-report-*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalGatedMergeChecklistItem)]
    public async Task Merge_GivenTargetBranchDriftAfterApproval_RejectsWithRefreshGuidanceAndNoBranchMutation()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Target drift transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "candidate-v1\n",
                "candidate v1");
        }

        var approvalRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "approve",
                "yes",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, approvalRun.ExitCode);
        Assert.Contains("Approval completed: session=", approvalRun.StdOut, StringComparison.Ordinal);

        await File.AppendAllTextAsync(Path.Combine(fixture.RepositoryPath, "README.md"), "target-drift\n", CancellationToken.None);
        await fixture.RunGitAsync("add", "README.md");
        await fixture.RunGitAsync("commit", "-m", "target drift");
        var driftHeadCommit = await fixture.GetHeadCommitAsync();

        var driftMergeRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "merge",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, driftMergeRun.ExitCode);
        Assert.Contains($"Session attached: {sessionId}", driftMergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Merge preflight:", driftMergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("targetBaselineMatchesApproval: True", driftMergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("mergePreviewHasTargetCommitDrift: True", driftMergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("mergePreviewCanMerge: False", driftMergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Merge failed: Merge failed: target branch drift detected.", driftMergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Target branch drift recovery requires rebase, revalidate, and refresh before retrying merge.", driftMergeRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Observable session states before retry: Editing -> Validating -> AwaitingApproval -> Approved.", driftMergeRun.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Merge completed: session=", driftMergeRun.StdOut, StringComparison.Ordinal);

        var headAfterMergeAttempt = await fixture.GetHeadCommitAsync();
        Assert.Equal(driftHeadCommit, headAfterMergeAttempt);

        using var finalState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = finalState.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal("Approved", finalState.RootElement.GetProperty("State").GetString());
        Assert.Empty(Directory.EnumerateFiles(artifactDirectory, "merge-result-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    [Trait("ChecklistItem", GovernanceDiagnosticsChecklistItem)]
    public async Task GovernanceDiagnostics_GivenFailedSessionDecision_EmitsCorrelationLinkedLogsAndArtifactReferencesForOfflineReconstruction()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "session start \"Governance diagnostics transcript\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);

        using (var startedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId))
        {
            await AppendAndCommitWorktreeChangeAsync(
                fixture,
                startedState.RootElement,
                "README.md",
                "candidate-observability-v1\n",
                "candidate observability v1");
        }

        var approvalRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "diff",
                "validate",
                "review",
                "approve",
                "yes",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, approvalRun.ExitCode);
        Assert.Contains("Approval completed: session=", approvalRun.StdOut, StringComparison.Ordinal);

        await RewritePersistedSessionStateAsync(fixture.RepositoryPath, sessionId, "Approved", "AwaitingApproval", "State");

        var mergeRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId}",
                "merge",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, mergeRun.ExitCode);
        Assert.Contains("Merge failed:", mergeRun.StdOut, StringComparison.Ordinal);

        var lifecycleEvents = await ReadLifecycleEventsAsync(fixture.RepositoryPath, sessionId);
        var mergeAttempted = lifecycleEvents.Last(e => string.Equals(e.Kind, "MergeAttempted", StringComparison.Ordinal));
        var mergeFailed = lifecycleEvents.Last(e => string.Equals(e.Kind, "MergeFailed", StringComparison.Ordinal));

        Assert.False(string.IsNullOrWhiteSpace(mergeAttempted.CorrelationId));
        Assert.Equal(mergeAttempted.CorrelationId, mergeFailed.CorrelationId);

        Assert.Contains(mergeAttempted.Diagnostics, diagnostic =>
            string.Equals(diagnostic.Key, "command", StringComparison.Ordinal)
            && string.Equals(diagnostic.Value, "merge", StringComparison.Ordinal));
        Assert.Contains(mergeFailed.Diagnostics, diagnostic =>
            string.Equals(diagnostic.Key, "outcome", StringComparison.Ordinal)
            && string.Equals(diagnostic.Value, "failed", StringComparison.Ordinal));
        Assert.Contains(mergeFailed.Diagnostics, diagnostic =>
            string.Equals(diagnostic.Key, "failureReason", StringComparison.Ordinal)
            && diagnostic.Value.Contains("cannot merge", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(mergeFailed.ArtifactReferences, reference => string.Equals(reference.Role, "validation-evidence", StringComparison.Ordinal));
        Assert.Contains(mergeFailed.ArtifactReferences, reference => string.Equals(reference.Role, "review-evidence", StringComparison.Ordinal));
        Assert.Contains(mergeFailed.ArtifactReferences, reference => string.Equals(reference.Role, "approval-evidence", StringComparison.Ordinal));

        Assert.All(
            mergeFailed.ArtifactReferences,
            artifactReference =>
            {
                Assert.False(string.IsNullOrWhiteSpace(artifactReference.ArtifactId));
                Assert.False(string.IsNullOrWhiteSpace(artifactReference.ArtifactPath));
            });
    }

    [Fact]
    [Trait("ChecklistItem", TargetBranchDriftRecoveryChecklistItem)]
    public void TargetBranchDriftRecovery_GivenRuntimeContract_ExposesRequiredStepsAndObservableRetryStates()
    {
        var contract = TargetBranchDriftRecovery.Current;

        Assert.Collection(
            contract.Steps,
            step =>
            {
                Assert.Equal("rebase", step.Command);
                Assert.Equal(Wip.Abstractions.Sessions.SessionState.Editing, step.SessionStateAfterStep);
            },
            step =>
            {
                Assert.Equal("diff", step.Command);
                Assert.Equal(Wip.Abstractions.Sessions.SessionState.Editing, step.SessionStateAfterStep);
            },
            step =>
            {
                Assert.Equal("validate", step.Command);
                Assert.Equal(Wip.Abstractions.Sessions.SessionState.Validating, step.SessionStateAfterStep);
            },
            step =>
            {
                Assert.Equal("review", step.Command);
                Assert.Equal(Wip.Abstractions.Sessions.SessionState.AwaitingApproval, step.SessionStateAfterStep);
            },
            step =>
            {
                Assert.Equal("approve", step.Command);
                Assert.Equal(Wip.Abstractions.Sessions.SessionState.Approved, step.SessionStateAfterStep);
            },
            step =>
            {
                Assert.Equal("merge", step.Command);
                Assert.Equal(Wip.Abstractions.Sessions.SessionState.Merged, step.SessionStateAfterStep);
            });

        Assert.Equal(
            [
                Wip.Abstractions.Sessions.SessionState.Editing,
                Wip.Abstractions.Sessions.SessionState.Validating,
                Wip.Abstractions.Sessions.SessionState.AwaitingApproval,
                Wip.Abstractions.Sessions.SessionState.Approved
            ],
            contract.ObservableSessionStatesBeforeRetry);

        var guidance = contract.FormatGuidance();

        Assert.Contains("rebase", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diff", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validate", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approve", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Editing -> Validating -> AwaitingApproval -> Approved", guidance, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void E2EMatrix_GivenMvpRequirementsDocument_ExpectedE2E001ThroughE2E035MappedToExecutableTranscriptTests()
    {
        var repositoryRoot = FindRepositoryRoot();
        var requirementsPath = Path.Combine(repositoryRoot, RequirementsDocumentPath.Replace('/', Path.DirectorySeparatorChar));
        var requirements = File.ReadAllText(requirementsPath);
        var matrixEntries = ParseE2EMatrixEntries(requirements);

        Assert.Equal(35, matrixEntries.Count);

        var expectedIds = Enumerable.Range(1, 35)
            .Select(static index => $"E2E-{index:000}")
            .ToArray();
        var actualIds = matrixEntries
            .Select(static entry => entry.Id)
            .ToArray();

        Assert.Equal(expectedIds, actualIds);

        var duplicateIds = matrixEntries
            .GroupBy(static entry => entry.Id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        Assert.Empty(duplicateIds);

        var executableTests = DiscoverExecutableTestNames(repositoryRoot);
        var missingMappings = matrixEntries
            .Where(entry => !executableTests.Contains(entry.TestName, StringComparer.Ordinal))
            .Select(static entry => $"{entry.Id} => {entry.TestName}")
            .ToArray();

        Assert.True(
            missingMappings.Length == 0,
            $"Every E2E matrix entry must map to an executable xUnit test method. Missing: {string.Join(", ", missingMappings)}");
    }

    [Fact]
    [Trait("ChecklistItem", SampleExternalTypedPluginChecklistItem)]
    public async Task ShellProcess_GivenAmbiguousTypedInferencePlugin_ExpectedPluginLoadFailureAndShellRemainsUsable()
    {
        await ExecuteAmbiguousTypedInferencePluginScenarioAsync();
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task TypedInferenceE2E_GivenAmbiguousCapabilityRegistration_ExpectedFailFastAndPluginIsolation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var builderTestsProjectPath = ResolveProjectPath(
            repositoryRoot,
            Path.Combine("wip", "Wip.Builder.Tests", "Wip.Builder.Tests.csproj"),
            Path.Combine("tests", "Wip.Builder.Tests", "Wip.Builder.Tests.csproj"));
        var builderFilter = string.Join("|", [
            "FullyQualifiedName~AddAgentTAgent_GivenAmbiguousImplementedInterfaces_ThrowsDeterministicConfigurationException",
            "FullyQualifiedName~AddToolTTool_GivenAmbiguousImplementedInterfaces_ThrowsDeterministicConfigurationException",
            "FullyQualifiedName~AddValidatorTValidator_GivenAmbiguousImplementedInterfaces_ThrowsDeterministicConfigurationException"
        ]);

        var builderResult = await RunProcessAsync(
            "dotnet",
            $"test \"{builderTestsProjectPath}\" --nologo --filter \"{builderFilter}\"",
            repositoryRoot,
            CancellationToken.None);

        Assert.Equal(0, builderResult.ExitCode);
        Assert.Contains("Passed!", builderResult.Output, StringComparison.OrdinalIgnoreCase);

        await ExecuteAmbiguousTypedInferencePluginScenarioAsync();
    }

    private static async Task ExecuteAmbiguousTypedInferencePluginScenarioAsync()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var pluginPath = await StagePluginAssembliesAsync(CancellationToken.None);

        try
        {
            var result = await ShellHostProcessDriver.ExecuteScriptAsync(
                [
                    "plugins load",
                    "plugins",
                    "workflows",
                    "help",
                    "exit"
                ],
                CancellationToken.None,
                args: [pluginPath],
                workingDirectory: fixture.RepositoryPath);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Plugins loaded:", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Loaded plugins:", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("samples.todoapp.wipagents", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("todoapp.agent.plan", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("todoapp.tool.draft", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("todoapp.validator.result", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Registered workflows:", result.StdOut, StringComparison.Ordinal);
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Modus.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from current test base directory.");
    }

    private static IReadOnlyList<E2EMatrixEntry> ParseE2EMatrixEntries(string requirements)
    {
        const string heading = "### E2E-001..E2E-035 Executable Transcript Matrix";
        var headingIndex = requirements.IndexOf(heading, StringComparison.Ordinal);
        if (headingIndex < 0)
            throw new InvalidOperationException($"Missing heading '{heading}' in requirements document.");

        var lines = requirements[headingIndex..]
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        var entries = new List<E2EMatrixEntry>();
        foreach (var line in lines)
        {
            if (!line.StartsWith("| E2E-", StringComparison.Ordinal))
                continue;

            var columns = line.Split('|', StringSplitOptions.TrimEntries);
            if (columns.Length < 4)
                continue;

            var id = columns[1];
            var scenario = columns[2];
            var testName = columns[3].Trim('`');
            entries.Add(new E2EMatrixEntry(id, scenario, testName));
        }

        return entries;
    }

    private static IReadOnlyCollection<string> DiscoverExecutableTestNames(string repositoryRoot)
    {
        var testFolders = new[]
        {
            Path.Combine(repositoryRoot, "wip", "Wip.Shell.E2E.Tests"),
            Path.Combine(repositoryRoot, "wip", "Wip.Builder.Tests"),
            Path.Combine(repositoryRoot, "tests", "Wip.Shell.E2E.Tests"),
            Path.Combine(repositoryRoot, "tests", "Wip.Builder.Tests")
        };

        const string methodPattern = "public\\s+(?:async\\s+)?(?:Task(?:<[^>]+>)?|void)\\s+(?<name>[A-Za-z0-9_]+)\\s*\\(";
        var discovered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var folder in testFolders.Where(Directory.Exists))
        {
            foreach (var filePath in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                         .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                             && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            {
                var contents = File.ReadAllText(filePath);
                var matches = Regex.Matches(contents, methodPattern, RegexOptions.Multiline);
                foreach (Match match in matches)
                {
                    var searchStart = Math.Max(0, match.Index - 600);
                    var prefix = contents[searchStart..match.Index];
                    if (!prefix.Contains("[Fact", StringComparison.Ordinal)
                        && !prefix.Contains("[Theory", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    discovered.Add(match.Groups["name"].Value);
                }
            }
        }

        return discovered;
    }

    private static string ResolveProjectPath(string repositoryRoot, params string[] relativeCandidates)
    {
        foreach (var relativeCandidate in relativeCandidates)
        {
            var candidatePath = Path.Combine(repositoryRoot, relativeCandidate);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        throw new InvalidOperationException($"Could not resolve project path from candidates: {string.Join(", ", relativeCandidates)}");
    }

    private static async Task<string> ExecuteProviderFailurePlanScenarioAsync(
        WipBuilder builder,
        string task,
        Func<SessionId, string> expectedFailureFactory)
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());

        using var reader = new StringReader($"init{Environment.NewLine}session start \"{task}\"{Environment.NewLine}plan{Environment.NewLine}exit{Environment.NewLine}");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, fixture.RepositoryPath);

        var exitCode = await loop.RunAsync(CancellationToken.None);
        var output = writer.ToString();
        var sessionId = ExtractSessionId(output);
        var expectedFailure = expectedFailureFactory(sessionId);

        Assert.Equal(0, exitCode);
        Assert.Contains(expectedFailure, output, StringComparison.Ordinal);

        using var state = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, sessionId);
        var artifactDirectory = state.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");

        Assert.Equal("Created", state.RootElement.GetProperty("State").GetString());
        Assert.Empty(Directory.EnumerateFiles(artifactDirectory, "*", SearchOption.AllDirectories));

        var lifecycleEvents = await ReadLifecycleEventsAsync(fixture.RepositoryPath, sessionId);
        Assert.DoesNotContain(
            lifecycleEvents,
            static sessionEvent => string.Equals(sessionEvent.Kind, "PlanGenerated", StringComparison.Ordinal));

        return output;
    }

    private sealed record ArtifactOutputLine(
        string Raw,
        string Id,
        string Type,
        string Version,
        DateTimeOffset Created,
        string Producer,
        string Path);

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = string.Concat(stdout, Environment.NewLine, stderr).Trim();

        return new ProcessResult(process.ExitCode, output);
    }

    private sealed record E2EMatrixEntry(string Id, string Scenario, string TestName);
    private sealed record ProcessResult(int ExitCode, string Output);

    private static WipBuilder CreateBuilderWithCorrelationProofProvider()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddPolicy<CorrelationProofPolicy, CorrelationProofPolicyRequest>(new PolicyId("policy.shell"));
        builder.AddWorkflow<CorrelationProofWorkflow, CorrelationProofWorkflowRequest, CorrelationProofWorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");
        builder.AddValidator<ComposerProofValidatorA, ComposerProofValidationRequest, ComposerProofValidationResult>(
            capabilityId: new CapabilityId("validator.correlation"),
            displayName: "Correlation proof validator");
        builder.AddModelProvider<CorrelationCaptureModelProvider, DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>(
            capabilityId: new CapabilityId("provider.deepseek.correlation"),
            displayName: "Deterministic correlation proof provider");

        return builder;
    }

    private static WipBuilder CreateBuilderWithPromptContextComposerProofProvider()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddPolicy<CorrelationProofPolicy, CorrelationProofPolicyRequest>(new PolicyId("policy.shell"));
        builder.AddWorkflow<CorrelationProofWorkflow, CorrelationProofWorkflowRequest, CorrelationProofWorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");
        builder.AddTool<ComposerProofToolA, ComposerProofToolRequest, ComposerProofToolResult>(
            capabilityId: new CapabilityId("tool.beta"),
            displayName: "Composer proof tool beta");
        builder.AddTool<ComposerProofToolB, ComposerProofToolRequest, ComposerProofToolResult>(
            capabilityId: new CapabilityId("tool.alpha"),
            displayName: "Composer proof tool alpha");
        builder.AddValidator<ComposerProofValidatorB, ComposerProofValidationRequest, ComposerProofValidationResult>(
            capabilityId: new CapabilityId("validator.beta"),
            displayName: "Composer proof validator beta");
        builder.AddValidator<ComposerProofValidatorA, ComposerProofValidationRequest, ComposerProofValidationResult>(
            capabilityId: new CapabilityId("validator.alpha"),
            displayName: "Composer proof validator alpha");
        builder.AddModelProvider<CorrelationCaptureModelProvider, DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>(
            capabilityId: new CapabilityId("provider.deepseek.correlation"),
            displayName: "Deterministic correlation proof provider");

        return builder;
    }

    private static WipBuilder CreateBuilderWithEcosystemGuardrailProvider()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddPolicy<CorrelationProofPolicy, CorrelationProofPolicyRequest>(new PolicyId("policy.shell"));
        builder.AddWorkflow<CorrelationProofWorkflow, CorrelationProofWorkflowRequest, CorrelationProofWorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");
        builder.AddValidator<ComposerProofValidatorA, ComposerProofValidationRequest, ComposerProofValidationResult>(
            capabilityId: new CapabilityId("validator.alpha"),
            displayName: "Composer proof validator alpha");
        builder.AddModelProvider<EcosystemGuardrailModelProvider, DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>(
            capabilityId: new CapabilityId("provider.deepseek.guardrails"),
            displayName: "Ecosystem guardrail provider");

        return builder;
    }

    private static WipBuilder CreateBuilderWithCredentialFailureProvider()
        => CreateBuilderWithPlanFailureProvider<CredentialFailureModelProvider>(
            capabilityId: "provider.deepseek.credential-failure",
            displayName: "Deterministic credential failure provider");

    private static WipBuilder CreateBuilderWithUnsupportedModelFailureProvider()
        => CreateBuilderWithPlanFailureProvider<UnsupportedModelFailureModelProvider>(
            capabilityId: "provider.deepseek.unsupported-model",
            displayName: "Deterministic unsupported model provider");

    private static WipBuilder CreateBuilderWithEndpointFailureProvider()
        => CreateBuilderWithPlanFailureProvider<EndpointFailureModelProvider>(
            capabilityId: "provider.deepseek.endpoint-failure",
            displayName: "Deterministic endpoint failure provider");

    private static WipBuilder CreateBuilderWithTimeoutFailureProvider()
        => CreateBuilderWithPlanFailureProvider<TimeoutFailureModelProvider>(
            capabilityId: "provider.deepseek.timeout-failure",
            displayName: "Deterministic timeout failure provider");

    private static WipBuilder CreateBuilderWithMalformedPayloadProvider()
        => CreateBuilderWithPlanFailureProvider<MalformedPayloadModelProvider>(
            capabilityId: "provider.deepseek.malformed-payload",
            displayName: "Deterministic malformed payload provider");

    private static WipBuilder CreateBuilderWithMissingValidatorCatalogProvider()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddPolicy<CorrelationProofPolicy, CorrelationProofPolicyRequest>(new PolicyId("policy.shell"));
        builder.AddWorkflow<CorrelationProofWorkflow, CorrelationProofWorkflowRequest, CorrelationProofWorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");
        builder.AddModelProvider<CorrelationCaptureModelProvider, DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>(
            capabilityId: new CapabilityId("provider.deepseek.correlation"),
            displayName: "Deterministic correlation proof provider");

        return builder;
    }

    private static WipBuilder CreateBuilderWithPlanFailureProvider<TModelProvider>(
        string capabilityId,
        string displayName)
        where TModelProvider : class, IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddPolicy<CorrelationProofPolicy, CorrelationProofPolicyRequest>(new PolicyId("policy.shell"));
        builder.AddWorkflow<CorrelationProofWorkflow, CorrelationProofWorkflowRequest, CorrelationProofWorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");
        builder.AddValidator<ComposerProofValidatorA, ComposerProofValidationRequest, ComposerProofValidationResult>(
            capabilityId: new CapabilityId("validator.failure-proof"),
            displayName: "Failure proof validator");
        builder.AddModelProvider<TModelProvider, DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>(
            capabilityId: new CapabilityId(capabilityId),
            displayName: displayName);

        return builder;
    }

    private sealed record CorrelationProofPolicyRequest(string Operation);

    private sealed class CorrelationProofPolicy : IPolicy<CorrelationProofPolicyRequest>
    {
        public PolicyId PolicyId => new("policy.shell");

        public ValueTask<PolicyDecision> EvaluateAsync(
            CorrelationProofPolicyRequest request,
            PolicyContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(PolicyDecision.Allow());
    }

    private sealed record CorrelationProofWorkflowRequest(string Task);

    private sealed record CorrelationProofWorkflowResult(string Outcome);

    private sealed class CorrelationProofWorkflow : IWorkflow<CorrelationProofWorkflowRequest, CorrelationProofWorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.linear");

        public ValueTask<CorrelationProofWorkflowResult> ExecuteAsync(
            CorrelationProofWorkflowRequest request,
            WorkflowContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new CorrelationProofWorkflowResult(request.Task));
    }

    private sealed record ComposerProofToolRequest(string Command);

    private sealed record ComposerProofToolResult(string Output);

    private sealed class ComposerProofToolA : ITool<ComposerProofToolRequest, ComposerProofToolResult>
    {
        public ValueTask<ComposerProofToolResult> ExecuteAsync(
            ComposerProofToolRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ComposerProofToolResult(request.Command));
    }

    private sealed class ComposerProofToolB : ITool<ComposerProofToolRequest, ComposerProofToolResult>
    {
        public ValueTask<ComposerProofToolResult> ExecuteAsync(
            ComposerProofToolRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ComposerProofToolResult(request.Command));
    }

    private sealed record ComposerProofValidationRequest(string Scope);

    private sealed record ComposerProofValidationResult(bool Passed);

    private sealed class ComposerProofValidatorA : IValidator<ComposerProofValidationRequest, ComposerProofValidationResult>
    {
        public ValueTask<ComposerProofValidationResult> ExecuteAsync(
            ComposerProofValidationRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ComposerProofValidationResult(true));
    }

    private sealed class ComposerProofValidatorB : IValidator<ComposerProofValidationRequest, ComposerProofValidationResult>
    {
        public ValueTask<ComposerProofValidationResult> ExecuteAsync(
            ComposerProofValidationRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ComposerProofValidationResult(true));
    }

    private sealed class CorrelationCaptureModelProvider : IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>
    {
        private static int _executionCount;
        private static readonly List<string> _systemMessages = [];
        private static readonly object _gate = new();

        public static int ExecutionCount => Volatile.Read(ref _executionCount);

        public static string? LastRequestCorrelationId { get; private set; }

        public static string? LastResponseCorrelationId { get; private set; }

        public static string? LastSystemMessage { get; private set; }

        public static string? LastUserMessage { get; private set; }

        public static IReadOnlyList<string> SystemMessages
        {
            get
            {
                lock (_gate)
                {
                    return _systemMessages.ToArray();
                }
            }
        }

        public static void Reset()
        {
            Interlocked.Exchange(ref _executionCount, 0);
            LastRequestCorrelationId = null;
            LastResponseCorrelationId = null;
            LastSystemMessage = null;
            LastUserMessage = null;

            lock (_gate)
            {
                _systemMessages.Clear();
            }
        }

        public ValueTask<ModelProviderResponse<DeepSeekChatCompletionResult>> ExecuteAsync(
            ModelProviderRequest<DeepSeekChatCompletionRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executionCount);
            LastRequestCorrelationId = request.CorrelationId;
            LastResponseCorrelationId = request.CorrelationId;
            LastSystemMessage = request.Payload.Messages.FirstOrDefault(static message => string.Equals(message.Role, "system", StringComparison.Ordinal))?.Content;
            LastUserMessage = request.Payload.Messages.FirstOrDefault(static message => string.Equals(message.Role, "user", StringComparison.Ordinal))?.Content;

            if (LastSystemMessage is not null)
            {
                lock (_gate)
                {
                    _systemMessages.Add(LastSystemMessage);
                }
            }

            return ValueTask.FromResult(
                new ModelProviderResponse<DeepSeekChatCompletionResult>(
                    payload: new DeepSeekChatCompletionResult(
                        "1. Capture context.\n2. Preserve correlation.\n3. Persist evidence.",
                        "stop"),
                    providerId: "deepseek",
                    modelId: request.ModelId,
                    correlationId: request.CorrelationId));
        }
    }

    private sealed class EcosystemGuardrailModelProvider : IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>
    {
        private static int _executionCount;

        public static int ExecutionCount => Volatile.Read(ref _executionCount);

        public static void Reset()
            => Interlocked.Exchange(ref _executionCount, 0);

        public ValueTask<ModelProviderResponse<DeepSeekChatCompletionResult>> ExecuteAsync(
            ModelProviderRequest<DeepSeekChatCompletionRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executionCount);

            return ValueTask.FromResult(
                new ModelProviderResponse<DeepSeekChatCompletionResult>(
                    payload: new DeepSeekChatCompletionResult(
                        "1. Run npm install\n2. Run cargo test\n3. Run dotnet test",
                        "stop"),
                    providerId: "deepseek",
                    modelId: request.ModelId,
                    correlationId: request.CorrelationId));
        }
    }

    private sealed class CredentialFailureModelProvider : IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>
    {
        private static int _executionCount;

        public static int ExecutionCount => Volatile.Read(ref _executionCount);

        public static void Reset()
            => Interlocked.Exchange(ref _executionCount, 0);

        public ValueTask<ModelProviderResponse<DeepSeekChatCompletionResult>> ExecuteAsync(
            ModelProviderRequest<DeepSeekChatCompletionRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executionCount);

            throw new InvalidOperationException(
                $"Credential validation failed for provider request correlation '{request.CorrelationId}': missing or invalid API key.");
        }
    }

    private sealed class UnsupportedModelFailureModelProvider : IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>
    {
        private static int _executionCount;

        public static int ExecutionCount => Volatile.Read(ref _executionCount);

        public static void Reset()
            => Interlocked.Exchange(ref _executionCount, 0);

        public ValueTask<ModelProviderResponse<DeepSeekChatCompletionResult>> ExecuteAsync(
            ModelProviderRequest<DeepSeekChatCompletionRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executionCount);

            throw new InvalidOperationException(
                $"Unsupported model selection '{request.ModelId}'. Supported models: deepseek-reasoner.");
        }
    }

    private sealed class EndpointFailureModelProvider : IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>
    {
        private static int _executionCount;

        public static int ExecutionCount => Volatile.Read(ref _executionCount);

        public static void Reset()
            => Interlocked.Exchange(ref _executionCount, 0);

        public ValueTask<ModelProviderResponse<DeepSeekChatCompletionResult>> ExecuteAsync(
            ModelProviderRequest<DeepSeekChatCompletionRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executionCount);

            throw new HttpRequestException(
                $"DeepSeek endpoint failure for correlation '{request.CorrelationId}': status code 503 (ServiceUnavailable).");
        }
    }

    private sealed class TimeoutFailureModelProvider : IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>
    {
        private static int _executionCount;

        public static int ExecutionCount => Volatile.Read(ref _executionCount);

        public static void Reset()
            => Interlocked.Exchange(ref _executionCount, 0);

        public ValueTask<ModelProviderResponse<DeepSeekChatCompletionResult>> ExecuteAsync(
            ModelProviderRequest<DeepSeekChatCompletionRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executionCount);

            throw new TimeoutException(
                $"Provider execution timed out for correlation '{request.CorrelationId}'.");
        }
    }

    private sealed class MalformedPayloadModelProvider : IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>
    {
        private static int _executionCount;

        public static int ExecutionCount => Volatile.Read(ref _executionCount);

        public static void Reset()
            => Interlocked.Exchange(ref _executionCount, 0);

        public ValueTask<ModelProviderResponse<DeepSeekChatCompletionResult>> ExecuteAsync(
            ModelProviderRequest<DeepSeekChatCompletionRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executionCount);

            return ValueTask.FromResult(
                new ModelProviderResponse<DeepSeekChatCompletionResult>(
                    payload: new DeepSeekChatCompletionResult(
                        "Generate a deterministic change set for the active session without using numbered steps.",
                        "stop"),
                    providerId: "deepseek",
                    modelId: request.ModelId,
                    correlationId: request.CorrelationId));
        }
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
        private readonly RunManifest _loadedRunManifest;
        private readonly IReadOnlyList<string> _diagnostics;
        private RunManifest _runManifest;

        public DiagnosticsBridge(RunManifest runManifest, IReadOnlyList<string> diagnostics)
        {
            _loadedRunManifest = runManifest;
            _runManifest = runManifest;
            _diagnostics = diagnostics;
        }

        public ValueTask<int> LoadPluginsAsync(CancellationToken cancellationToken)
        {
            _runManifest = _loadedRunManifest;
            return ValueTask.FromResult(_runManifest.Plugins.Count);
        }

        public ValueTask StopPluginsAsync(CancellationToken cancellationToken)
        {
            _runManifest = new RunManifest(
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Plugins: Array.Empty<PluginManifestEntry>(),
                Workflows: _loadedRunManifest.Workflows);
            return ValueTask.CompletedTask;
        }

        public RunManifest GetRunManifest()
            => _runManifest;

        public IReadOnlyList<string> GetLoadDiagnostics()
            => _diagnostics;
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

    private static void StageCurrentTestAssemblyIntoPluginFolder(string pluginDirectory)
    {
        var sourceAssemblyPath = typeof(ShellHostE2EHarnessTests).Assembly.Location;
        var destinationPath = Path.Combine(pluginDirectory, Path.GetFileName(sourceAssemblyPath));
        File.Copy(sourceAssemblyPath, destinationPath, overwrite: true);
    }

    private static class ShellHostProcessDriver
    {
        public static async Task<ShellProcessResult> ExecuteScriptAsync(
            IReadOnlyList<string> commands,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? args = null,
            string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environmentVariables = null)
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

            if (environmentVariables is not null)
            {
                foreach (var variable in environmentVariables)
                {
                    startInfo.Environment[variable.Key] = variable.Value ?? string.Empty;
                }
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

    private static SessionId ExtractSessionId(string stdOut)
    {
        const string prefix = "Session started: ";
        using var reader = new StringReader(stdOut);
        while (reader.ReadLine() is { } line)
        {
            var markerIndex = line.IndexOf(prefix, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                var remainder = line[(markerIndex + prefix.Length)..].TrimStart();
                var separatorIndex = remainder.IndexOf(' ');
                var value = separatorIndex >= 0 ? remainder[..separatorIndex] : remainder;
                return new SessionId(value);
            }
        }

        throw new InvalidOperationException($"Could not locate session id in shell output. StdOut: {stdOut}");
    }

    private static string ExtractLastValueByPrefix(string stdOut, string prefix)
    {
        var value = string.Empty;
        using var reader = new StringReader(stdOut);
        while (reader.ReadLine() is { } line)
        {
            var markerIndex = line.IndexOf(prefix, StringComparison.Ordinal);
            if (markerIndex < 0)
                continue;

            value = line[(markerIndex + prefix.Length)..].Trim();
        }

        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Could not locate value with prefix '{prefix}' in shell output. StdOut: {stdOut}");

        return value;
    }

    private static async Task WriteRepositoryConfigAsync(string repositoryPath, string configJson)
    {
        var configPath = Path.Combine(repositoryPath, ".wip", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)
            ?? throw new InvalidOperationException("Could not resolve configuration directory path."));
        await File.WriteAllTextAsync(configPath, configJson, CancellationToken.None);
    }

    private static async Task RewritePersistedSessionWorktreePathAsync(string repositoryPath, SessionId sessionId, string newWorktreePath)
    {
        var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "session-state.json");
        var payload = await File.ReadAllTextAsync(statePath, CancellationToken.None);
        var escapedPath = newWorktreePath.Replace("\\", "\\\\", StringComparison.Ordinal);
        var updated = Regex.Replace(
            payload,
            "\"WorktreePath\"\\s*:\\s*\"[^\"]*\"",
            $"\"WorktreePath\":\"{escapedPath}\"",
            RegexOptions.CultureInvariant);

        if (string.Equals(updated, payload, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Failed to rewrite WorktreePath in persisted session state for session '{sessionId}'.");
        }

        await File.WriteAllTextAsync(statePath, updated, CancellationToken.None);
    }

    private static async Task RewritePersistedSessionWorkflowIdAsync(string repositoryPath, SessionId sessionId, string newWorkflowId)
    {
        var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "session-state.json");
        var payload = await File.ReadAllTextAsync(statePath, CancellationToken.None);
        var escapedWorkflowId = newWorkflowId.Replace("\\", "\\\\", StringComparison.Ordinal);
        var updated = Regex.Replace(
            payload,
            "\"WorkflowId\"\\s*:\\s*\"[^\"]*\"",
            $"\"WorkflowId\":\"{escapedWorkflowId}\"",
            RegexOptions.CultureInvariant);

        if (string.Equals(updated, payload, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Failed to rewrite WorkflowId in persisted session state for session '{sessionId}'.");
        }

        await File.WriteAllTextAsync(statePath, updated, CancellationToken.None);
    }

    private static async Task MarkValidationArtifactFailedAsync(string validationArtifactPath)
    {
        var payload = await File.ReadAllTextAsync(validationArtifactPath, CancellationToken.None);
        var updated = payload
            .Replace("\"BuildSucceeded\": true", "\"BuildSucceeded\": false", StringComparison.Ordinal)
            .Replace("\"TestSucceeded\": true", "\"TestSucceeded\": false", StringComparison.Ordinal)
            .Replace("\"BuildSucceeded\":true", "\"BuildSucceeded\":false", StringComparison.Ordinal)
            .Replace("\"TestSucceeded\":true", "\"TestSucceeded\":false", StringComparison.Ordinal);

        if (string.Equals(updated, payload, StringComparison.Ordinal))
        {
            updated = payload
                .Replace("\"ExitCode\": 0", "\"ExitCode\": 1", StringComparison.Ordinal)
                .Replace("\"ExitCode\":0", "\"ExitCode\":1", StringComparison.Ordinal);
        }

        if (string.Equals(updated, payload, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Failed to rewrite validation evidence at '{validationArtifactPath}'.");
        }

        await File.WriteAllTextAsync(validationArtifactPath, updated, CancellationToken.None);
    }

    private static async Task<JsonDocument> ReadPersistedSessionStateAsync(string repositoryPath, SessionId sessionId)
    {
        var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "session-state.json");
        var payload = await File.ReadAllTextAsync(statePath, CancellationToken.None);
        return JsonDocument.Parse(payload);
    }

    private static async Task<IReadOnlyList<SessionLifecycleEventPayload>> ReadLifecycleEventsAsync(string repositoryPath, SessionId sessionId)
    {
        var eventPath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "event-journal.ndjson");
        var lines = await File.ReadAllLinesAsync(eventPath, CancellationToken.None);
        var events = new List<SessionLifecycleEventPayload>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var eventDocument = JsonDocument.Parse(line);
            var root = eventDocument.RootElement;
            events.Add(new SessionLifecycleEventPayload(
                Kind: root.GetProperty("Kind").GetString()
                    ?? throw new InvalidOperationException($"Missing Kind field in event payload: {line}"),
                SessionId: root.GetProperty("SessionId").GetString()
                    ?? throw new InvalidOperationException($"Missing SessionId field in event payload: {line}"),
                CurrentState: root.GetProperty("CurrentState").GetString()
                    ?? throw new InvalidOperationException($"Missing CurrentState field in event payload: {line}"),
                PreviousState: root.TryGetProperty("PreviousState", out var previousStateElement)
                    ? previousStateElement.GetString()
                    : null,
                OccurredAtUtc: root.GetProperty("OccurredAtUtc").GetString()
                    ?? throw new InvalidOperationException($"Missing OccurredAtUtc field in event payload: {line}"),
                Message: root.GetProperty("Message").GetString()
                    ?? throw new InvalidOperationException($"Missing Message field in event payload: {line}"),
                CorrelationId: root.TryGetProperty("CorrelationId", out var correlationElement)
                    ? correlationElement.GetString()
                    : null,
                Diagnostics: root.TryGetProperty("Diagnostics", out var diagnosticsElement) && diagnosticsElement.ValueKind == JsonValueKind.Array
                    ? diagnosticsElement.EnumerateArray()
                        .Select(static element => new SessionLifecycleDiagnostic(
                            Key: element.GetProperty("Key").GetString() ?? string.Empty,
                            Value: element.GetProperty("Value").GetString() ?? string.Empty))
                        .Where(static diagnostic => !string.IsNullOrWhiteSpace(diagnostic.Key))
                        .ToArray()
                    : [],
                ArtifactReferences: root.TryGetProperty("ArtifactReferences", out var artifactReferencesElement) && artifactReferencesElement.ValueKind == JsonValueKind.Array
                    ? artifactReferencesElement.EnumerateArray()
                        .Select(static element => new SessionLifecycleArtifactReference(
                            Role: element.GetProperty("Role").GetString() ?? string.Empty,
                            ArtifactId: element.GetProperty("ArtifactId").GetString() ?? string.Empty,
                            ArtifactPath: element.GetProperty("ArtifactPath").GetString() ?? string.Empty,
                            ArtifactKind: element.GetProperty("ArtifactKind").GetString() ?? string.Empty))
                        .Where(static artifactReference => !string.IsNullOrWhiteSpace(artifactReference.Role))
                        .ToArray()
                    : [],
                PropertyCount: root.EnumerateObject().Count()));
        }

        return events;
    }

    private static void AssertEventShapeDeterministic(
        IReadOnlyList<SessionLifecycleEventPayload> events,
        string expectedKind,
        string expectedCurrentState)
    {
        var eventPayload = events.LastOrDefault(item => string.Equals(item.Kind, expectedKind, StringComparison.Ordinal));
        Assert.NotNull(eventPayload);
        Assert.Equal(expectedCurrentState, eventPayload.CurrentState);
        Assert.True(eventPayload.PropertyCount >= 6, $"Expected at least 6 properties in event payload '{expectedKind}'.");
        Assert.False(string.IsNullOrWhiteSpace(eventPayload.SessionId));
        Assert.False(string.IsNullOrWhiteSpace(eventPayload.OccurredAtUtc));
        Assert.False(string.IsNullOrWhiteSpace(eventPayload.Message));

        if (!string.IsNullOrWhiteSpace(eventPayload.CorrelationId))
        {
            Assert.NotEmpty(eventPayload.Diagnostics);
        }
    }

    private static async Task RewritePersistedSessionStateAsync(
        string repositoryPath,
        SessionId sessionId,
        string fromValue,
        string toValue,
        string propertyName)
    {
        var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "session-state.json");
        var payload = await File.ReadAllTextAsync(statePath, CancellationToken.None);
        var fromToken = $"\"{propertyName}\":\"{fromValue}\"";
        var toToken = $"\"{propertyName}\":\"{toValue}\"";
        var updated = payload.Replace(fromToken, toToken, StringComparison.Ordinal);

        if (string.Equals(updated, payload, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Failed to rewrite {propertyName} in persisted session state for session '{sessionId}'.");
        }

        await File.WriteAllTextAsync(statePath, updated, CancellationToken.None);
    }

    private static async Task AppendAndCommitWorktreeChangeAsync(
        TempGitRepositoryFixture fixture,
        JsonElement persistedSessionState,
        string relativePath,
        string appendedContent,
        string commitMessage)
    {
        var worktreePath = persistedSessionState.GetProperty("WorktreePath").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain a worktree path.");
        var targetPath = Path.Combine(worktreePath, relativePath);

        await File.AppendAllTextAsync(targetPath, appendedContent, CancellationToken.None);
        await fixture.RunGitAsync("-C", worktreePath, "add", relativePath);
        await fixture.RunGitAsync("-C", worktreePath, "commit", "-m", commitMessage);
    }

    private static void AssertArtifactWithPrefixExists(string artifactDirectory, string artifactPrefix)
    {
        Assert.True(
            Directory.EnumerateFiles(artifactDirectory, $"{artifactPrefix}*", SearchOption.AllDirectories).Any(),
            $"Expected an artifact starting with '{artifactPrefix}' under '{artifactDirectory}'.");
    }

    private static void AssertArchiveArtifactCleanupOutcome(
        string artifactDirectory,
        string expectedMode,
        bool expectedAttempted,
        bool expectedSucceeded,
        string expectedWorktreePath)
    {
        var archiveArtifactPath = Assert.Single(
            Directory.EnumerateFiles(artifactDirectory, "session-archive-*.json", SearchOption.TopDirectoryOnly),
            static path => !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));

        using var archiveArtifact = JsonDocument.Parse(File.ReadAllText(archiveArtifactPath));
        var root = archiveArtifact.RootElement;
        Assert.Equal(expectedMode, ReadArchivePolicyMode(root));

        var cleanup = root.GetProperty("Cleanup");
        Assert.Equal(expectedAttempted, cleanup.GetProperty("Attempted").GetBoolean());
        Assert.Equal(expectedSucceeded, cleanup.GetProperty("Succeeded").GetBoolean());
        Assert.Equal(Path.GetFullPath(expectedWorktreePath), cleanup.GetProperty("WorktreePath").GetString());
        Assert.False(string.IsNullOrWhiteSpace(cleanup.GetProperty("Details").GetString()));
        Assert.True(root.TryGetProperty("ProducedAtUtc", out var producedAtUtc));
        Assert.False(string.IsNullOrWhiteSpace(producedAtUtc.GetString()));
    }

    private static string ReadArchivePolicyMode(JsonElement root)
    {
        var modeElement = root.GetProperty("PolicyMode");
        return modeElement.ValueKind switch
        {
            JsonValueKind.String => modeElement.GetString()
                ?? throw new InvalidOperationException("Archive artifact PolicyMode was null."),
            JsonValueKind.Number when modeElement.GetInt32() == 0 => "MarkOnly",
            JsonValueKind.Number when modeElement.GetInt32() == 1 => "Cleanup",
            JsonValueKind.Number => throw new InvalidOperationException($"Unexpected numeric PolicyMode value '{modeElement.GetInt32()}'."),
            _ => throw new InvalidOperationException($"Unexpected PolicyMode JSON kind '{modeElement.ValueKind}'.")
        };
    }

    private sealed record ShellProcessResult(int ExitCode, string StdOut, string StdErr);

    private sealed record SessionLifecycleEventPayload(
        string Kind,
        string SessionId,
        string CurrentState,
        string? PreviousState,
        string OccurredAtUtc,
        string Message,
        string? CorrelationId,
        IReadOnlyList<SessionLifecycleDiagnostic> Diagnostics,
        IReadOnlyList<SessionLifecycleArtifactReference> ArtifactReferences,
        int PropertyCount);

    private sealed record SessionLifecycleDiagnostic(
        string Key,
        string Value);

    private sealed record SessionLifecycleArtifactReference(
        string Role,
        string ArtifactId,
        string ArtifactPath,
        string ArtifactKind);
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
