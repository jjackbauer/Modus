using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class PlanOnlyAgentTests
{
    [Fact]
    public async Task PlanOnlyAgent_ExecuteAsync_GivenTaskAndRepositoryContext_WritesAgentPlanArtifactWithActionableSteps()
    {
        var artifactStore = new InMemoryArtifactStore();
        var agent = new PlanOnlyAgent(artifactStore);
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());
        var builder = CreateBuilderWithToolValidatorAndPolicy();

        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-plan-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var snapshot = await orchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "s1"),
                cancellationToken: CancellationToken.None);

            var executionContext = await orchestrator.CreateAgentExecutionContextAsync(
                sessionId: snapshot.SessionId,
                task: "Implement plugin upload telemetry diagnostics",
                builder: builder,
                policyId: new PolicyId("policy.local-safe"),
                cancellationToken: CancellationToken.None);

            var result = await agent.ExecuteAsync(
                new PlanOnlyAgentRequest(executionContext),
                new CapabilityContext(snapshot.SessionId, snapshot.WorktreePath),
                CancellationToken.None);

            Assert.Equal(ArtifactKind.Markdown, result.PlanArtifact.Kind);
            Assert.Equal(4, result.Steps.Count);
            Assert.Contains("Implement plugin upload telemetry diagnostics", result.Markdown, StringComparison.Ordinal);
            Assert.Contains("1. Inspect current worktree state", result.Markdown, StringComparison.Ordinal);
            Assert.Contains("2. Implement code changes", result.Markdown, StringComparison.Ordinal);
            Assert.Contains("3. Run validators", result.Markdown, StringComparison.Ordinal);
            Assert.Contains("4. Produce review-ready summary", result.Markdown, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task AgentContext_GivenRuntimeInvocation_ProvidesSessionWorktreeToolsValidatorsAndPolicyContext()
    {
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpSessionEventPublisher());
        var builder = CreateBuilderWithToolValidatorAndPolicy();

        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-agent-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var snapshot = await orchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "s2"),
                cancellationToken: CancellationToken.None);

            var context = await orchestrator.CreateAgentExecutionContextAsync(
                sessionId: snapshot.SessionId,
                task: "Create implementation plan",
                builder: builder,
                policyId: new PolicyId("policy.local-safe"),
                cancellationToken: CancellationToken.None);

            Assert.Equal(snapshot.SessionId, context.SessionId);
            Assert.Equal(snapshot.WorkflowId, context.WorkflowId);
            Assert.Equal(snapshot.WorktreePath, context.WorktreePath);
            Assert.Equal(snapshot.RepositoryPath, context.RepositoryPath);
            Assert.Equal("Create implementation plan", context.Task);
            Assert.Equal("policy.local-safe", context.PolicyId.Value);

            var tool = Assert.Single(context.Tools);
            var validator = Assert.Single(context.Validators);
            Assert.Equal("tool.shell", tool.CapabilityId.Value);
            Assert.Equal("validator.dotnet", validator.CapabilityId.Value);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    private static WipBuilder CreateBuilderWithToolValidatorAndPolicy()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddTool<StubTool, StubToolRequest, StubToolResult>(new CapabilityId("tool.shell"), "Shell Tool");
        builder.AddValidator<StubValidator, StubValidatorRequest, StubValidatorResult>(new CapabilityId("validator.dotnet"), "DotNet Validator");
        builder.AddPolicy<StubPolicy, StubPolicyRequest>(new PolicyId("policy.local-safe"));

        return builder;
    }

    private sealed class InMemoryArtifactStore : IArtifactStore
    {
        public ValueTask<ArtifactDescriptor> SaveAsync(SessionId sessionId, ArtifactContent artifact, CancellationToken cancellationToken)
        {
            var descriptor = new ArtifactDescriptor(
                artifact.ArtifactId,
                sessionId,
                artifact.Kind,
                relativePath: $".wip/artifacts/{sessionId.Value}/{artifact.FileName}.md",
                artifact.ProducerType,
                artifact.ProducerVersion,
                artifact.ProducedAtUtc);

            return ValueTask.FromResult(descriptor);
        }

        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>([]);
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly Dictionary<SessionId, SessionSnapshot> _sessions = [];

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult(_sessions.TryGetValue(sessionId, out var snapshot) ? snapshot : (SessionSnapshot?)null);
    }

    private sealed class NoOpSessionEventPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed record StubToolRequest(string Command);

    private sealed record StubToolResult(string Output);

    private sealed class StubTool : ITool<StubToolRequest, StubToolResult>
    {
        public ValueTask<StubToolResult> ExecuteAsync(StubToolRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new StubToolResult(request.Command));
    }

    private sealed record StubValidatorRequest(string Name);

    private sealed record StubValidatorResult(bool IsValid);

    private sealed class StubValidator : IValidator<StubValidatorRequest, StubValidatorResult>
    {
        public ValueTask<StubValidatorResult> ExecuteAsync(StubValidatorRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new StubValidatorResult(true));
    }

    private sealed record StubPolicyRequest(string Command);

    private sealed class StubPolicy : IPolicy<StubPolicyRequest>
    {
        public PolicyId PolicyId => new("policy.local-safe");

        public ValueTask<PolicyDecision> EvaluateAsync(StubPolicyRequest request, PolicyContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(PolicyDecision.Allow());
    }
}