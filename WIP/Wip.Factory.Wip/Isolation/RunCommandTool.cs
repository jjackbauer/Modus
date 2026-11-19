using Wip.Abstractions.Capabilities;

namespace Wip.Factory.Wip.Isolation;

public sealed record RunCommandRequest(string Subcommand, IReadOnlyList<string> Arguments);

public sealed record RunCommandResult(
    string Subcommand,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TerminatedByWallTime,
    bool IsolationApplied);

/// <summary>
/// Runs a bounded <c>dotnet</c> subcommand inside the session worktree (v0.3 §6.1). There is no shell: the
/// subcommand and its arguments are passed as an argument vector, so metacharacters are literal. The
/// subcommand allowlist and the fail-closed isolation requirement are both enforced here rather than trusted
/// to the caller.
/// </summary>
public sealed class RunCommandTool : ITool<RunCommandRequest, RunCommandResult>
{
    public const string SubcommandNotAllowedCategory = "subcommand-not-allowed";
    public const string IsolationUnavailableCategory = "isolation-unavailable";

    private static readonly HashSet<string> AllowedSubcommands = new(StringComparer.Ordinal)
    {
        "new", "restore", "build", "test", "run", "publish"
    };

    /// <summary>The closed interface allowlist, exposed so the security policy can verify what actually enforces it.</summary>
    public static IReadOnlyCollection<string> AllowedSubcommandsForPolicy => AllowedSubcommands.ToArray();

    private readonly IProcessRunner _runner;
    private readonly string _dotnetPath;

    public RunCommandTool(IProcessRunner? runner = null, string? dotnetPath = null)
    {
        _runner = runner ?? new BoundedProcessRunner();
        _dotnetPath = dotnetPath ?? "dotnet";
    }

    public async ValueTask<RunCommandResult> ExecuteAsync(
        RunCommandRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!AllowedSubcommands.Contains(request.Subcommand))
        {
            throw new InvalidOperationException($"{SubcommandNotAllowedCategory}: '{request.Subcommand}'.");
        }

        if (string.IsNullOrWhiteSpace(context.WorktreePath))
        {
            throw new InvalidOperationException($"{WorkspacePathsBridge.NoWorktreeCategory}: the session has no worktree.");
        }

        var arguments = new List<string> { request.Subcommand };
        arguments.AddRange(request.Arguments);

        var result = await _runner
            .RunAsync(
                _dotnetPath,
                arguments,
                context.WorktreePath,
                new ProcessBounds(TimeSpan.FromMinutes(5), 2L * 1024 * 1024 * 1024),
                cancellationToken)
            .ConfigureAwait(false);

        // Fail closed: a run without OS resource controls is not a bounded run (v0.3 §6.3).
        if (!result.IsolationApplied)
        {
            throw new InvalidOperationException($"{IsolationUnavailableCategory}: resource controls were not applied.");
        }

        return new RunCommandResult(
            request.Subcommand,
            result.ExitCode,
            Trim(result.StandardOutput),
            Trim(result.StandardError),
            result.TerminatedByWallTime,
            result.IsolationApplied);
    }

    private static string Trim(string text)
        => text.Length <= 8000 ? text : text[..8000] + "\n[truncated]";
}

/// <summary>Keeps the no-worktree category in one place across assembly boundaries.</summary>
public static class WorkspacePathsBridge
{
    public const string NoWorktreeCategory = "no-worktree";
}
