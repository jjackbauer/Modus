using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;

namespace Wip.Runtime.Runtime;

public sealed record PlanOnlyAgentRequest(AgentExecutionContext Context);

public sealed record PlanOnlyAgentResult(
    string Markdown,
    IReadOnlyList<string> Steps,
    ArtifactDescriptor PlanArtifact);

public sealed class PlanOnlyAgent : IAgent<PlanOnlyAgentRequest, PlanOnlyAgentResult>
{
    private const string ProducerType = "Wip.Runtime.PlanOnlyAgent";
    private const string ProducerVersion = "1.0.0";

    private readonly IArtifactStore _artifactStore;

    public PlanOnlyAgent(IArtifactStore artifactStore)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
    }

    public async ValueTask<PlanOnlyAgentResult> ExecuteAsync(
        PlanOnlyAgentRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);

        if (request.Context.SessionId != context.SessionId)
        {
            throw new InvalidOperationException(
                $"Agent context session '{request.Context.SessionId}' does not match capability session '{context.SessionId}'.");
        }

        if (!string.Equals(request.Context.WorktreePath, context.WorktreePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Agent context worktree path does not match capability context worktree path.");
        }

        if (string.IsNullOrWhiteSpace(request.Context.Task))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(request.Context.Task));

        var steps = BuildActionableSteps();
        var markdown = BuildMarkdown(request.Context, steps);
        var producedAtUtc = DateTimeOffset.UtcNow;

        var artifact = await _artifactStore.SaveAsync(
            request.Context.SessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"agent-plan-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Markdown,
                fileName: "agent-plan",
                content: markdown,
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: producedAtUtc),
            cancellationToken);

        return new PlanOnlyAgentResult(markdown, steps, artifact);
    }

    private static IReadOnlyList<string> BuildActionableSteps()
    {
        return
        [
            "Inspect current worktree state and identify impacted files.",
            "Implement code changes in the active worktree using registered tools.",
            "Run validators and capture failing/passing evidence.",
            "Produce review-ready summary and prepare for approval/merge gates."
        ];
    }

    private static string BuildMarkdown(AgentExecutionContext context, IReadOnlyList<string> steps)
    {
        var lines = new List<string>
        {
            "# Plan",
            string.Empty,
            $"Task: {context.Task}",
            $"Session: {context.SessionId.Value}",
            $"Workflow: {context.WorkflowId.Value}",
            $"Repository: {context.RepositoryPath}",
            $"Worktree: {context.WorktreePath}",
            $"Policy: {context.PolicyId.Value}",
            string.Empty,
            "## Tools",
            string.Empty
        };

        lines.AddRange(context.Tools.Select(tool => $"- {tool.CapabilityId.Value} ({tool.DisplayName})"));
        lines.Add(string.Empty);
        lines.Add("## Validators");
        lines.Add(string.Empty);
        lines.AddRange(context.Validators.Select(validator => $"- {validator.CapabilityId.Value} ({validator.DisplayName})"));
        lines.Add(string.Empty);
        lines.Add("## Actionable Steps");
        lines.Add(string.Empty);

        for (var i = 0; i < steps.Count; i++)
            lines.Add($"{i + 1}. {steps[i]}");

        return string.Join(Environment.NewLine, lines);
    }
}