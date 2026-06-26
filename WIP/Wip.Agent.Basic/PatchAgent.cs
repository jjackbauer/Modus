using Wip.Abstractions.Capabilities;

namespace Wip.Agent.Basic;

public sealed record PatchAgentRequest(
    AgentExecutionContext Context,
    string DiffHash,
    IReadOnlyList<string> ChangedFiles);

public sealed record PatchAgentResult(
    string DiffHash,
    IReadOnlyList<string> ChangedFiles,
    string Patch);

public sealed class PatchAgent : IAgent<PatchAgentRequest, PatchAgentResult>
{
    public ValueTask<PatchAgentResult> ExecuteAsync(
        PatchAgentRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);

        if (request.Context.SessionId != context.SessionId)
        {
            throw new InvalidOperationException(
                $"Patch agent context session '{request.Context.SessionId}' does not match capability session '{context.SessionId}'.");
        }

        if (!string.Equals(request.Context.WorktreePath, context.WorktreePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Patch agent context worktree path does not match capability context worktree path.");
        }

        if (string.IsNullOrWhiteSpace(request.DiffHash))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(request.DiffHash));

        var orderedFiles = request.ChangedFiles
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        var patch = BuildDeterministicPatch(request.Context.Task, request.DiffHash, orderedFiles);
        return ValueTask.FromResult(new PatchAgentResult(request.DiffHash, orderedFiles, patch));
    }

    private static string BuildDeterministicPatch(string task, string diffHash, IReadOnlyList<string> changedFiles)
    {
        var lines = new List<string>
        {
            $"# Deterministic Patch Plan for task: {task}",
            $"# Diff Hash: {diffHash}",
            "# Changed Files:"
        };

        if (changedFiles.Count == 0)
        {
            lines.Add("# - (none)");
        }
        else
        {
            lines.AddRange(changedFiles.Select(static file => $"# - {file}"));
        }

        lines.Add("# Apply file edits in listed order and keep hash validation stable.");
        return string.Join(Environment.NewLine, lines);
    }
}
