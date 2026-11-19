using Wip.Abstractions.Capabilities;

namespace Wip.Factory.Wip.Isolation;

public sealed record RunCommandRequest(string Subcommand, IReadOnlyList<string> Arguments);

public sealed record RunCommandResult(
    string Subcommand,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TerminatedByWallTime,
    bool IsolationApplied,
    int Redactions = 0);

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

    /// <summary>The redaction policy applied to child-process output before it becomes an observation.</summary>
    public static string RedactionPolicyVersion => SecretRedactor.PolicyVersion;

    private readonly IProcessRunner _runner;
    private readonly ISecretRedactor _redactor;
    private readonly string _dotnetPath;

    public RunCommandTool(IProcessRunner? runner = null, string? dotnetPath = null, ISecretRedactor? redactor = null)
    {
        _runner = runner ?? new BoundedProcessRunner();
        _dotnetPath = dotnetPath ?? "dotnet";
        _redactor = redactor ?? new SecretRedactor();
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

        // Child-process output can contain credentials. Redact before it reaches a model observation or evidence;
        // an unredactable result is a named refusal, never raw output (v0.4 FAC-P1-06.5).
        var redactions = 0;
        var standardOutput = Redact(result.StandardOutput, ref redactions);
        var standardError = Redact(result.StandardError, ref redactions);

        return new RunCommandResult(
            request.Subcommand,
            result.ExitCode,
            Trim(standardOutput),
            Trim(standardError),
            result.TerminatedByWallTime,
            result.IsolationApplied,
            redactions);
    }

    private string Redact(string value, ref int redactions)
    {
        try
        {
            var redacted = _redactor.Redact(value, out var count);
            redactions += count;
            return redacted;
        }
        catch (SecretRedactionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SecretRedactionException(exception.GetType().Name);
        }
    }

    private static string Trim(string text)
        => text.Length <= 8000 ? text : text[..8000] + "\n[truncated]";
}

/// <summary>Keeps the no-worktree category in one place across assembly boundaries.</summary>
public static class WorkspacePathsBridge
{
    public const string NoWorktreeCategory = "no-worktree";
}
