using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;

namespace Wip.Abstractions.Tools;

/// <summary>
/// Invokes a model-proposed tool call by name (WP-3.2). Implementations resolve the tool, enforce
/// permissions, bound the execution time, and never throw for expected failures: they return a
/// deterministic <see cref="ToolResult"/> instead.
/// </summary>
public interface IToolInvoker
{
    ValueTask<ToolResult> InvokeAsync(
        ToolCall call,
        CapabilityContext context,
        CancellationToken cancellationToken);
}

public sealed record ToolInvocationOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxObservationCharacters { get; init; } = 8_000;

    public bool RecordArtifacts { get; init; } = true;
}