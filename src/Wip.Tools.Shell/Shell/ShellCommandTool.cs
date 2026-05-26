using System.Diagnostics;
using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;

namespace Wip.Tools.Shell.Shell;

public sealed record ShellCommandRequest(
    string Command,
    WorkflowId WorkflowId,
    string? RelativeWorkingDirectory = null,
    TimeSpan? Timeout = null,
    string? ShellExecutablePath = null);

public sealed record ShellCommandPolicyRequest(
    string Command,
    string WorkingDirectory,
    TimeSpan Timeout);

public sealed record ShellCommandResult(
    bool IsBlocked,
    string? BlockReason,
    string Command,
    string WorkingDirectory,
    int ExitCode,
    bool TimedOut,
    string StandardOutput,
    string StandardError,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    ArtifactDescriptor LogArtifact);

public sealed class ShellCommandTool : ITool<ShellCommandRequest, ShellCommandResult>
{
    private const string ProducerType = "Wip.Tools.Shell";
    private const string ProducerVersion = "1.0.0";
    private const string OperationName = "Wip.Tools.Shell.Execute";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly IPolicy<ShellCommandPolicyRequest> _policy;
    private readonly IArtifactStore _artifactStore;

    public ShellCommandTool(IPolicy<ShellCommandPolicyRequest> policy, IArtifactStore artifactStore)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
    }

    public async ValueTask<ShellCommandResult> ExecuteAsync(
        ShellCommandRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Command))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(request.Command));

        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request.Timeout), "Timeout must be greater than zero.");

        var startedAtUtc = DateTimeOffset.UtcNow;
        var workingDirectory = ResolveWorkingDirectory(context.WorktreePath, request.RelativeWorkingDirectory);
        var policyDecision = await _policy.EvaluateAsync(
            new ShellCommandPolicyRequest(request.Command, workingDirectory, timeout),
            new PolicyContext(context.SessionId, request.WorkflowId, context.WorktreePath, OperationName),
            cancellationToken);

        if (!policyDecision.IsAllowed)
        {
            return await BuildBlockedResultAsync(
                context.SessionId,
                request,
                workingDirectory,
                startedAtUtc,
                policyDecision.Reason,
                cancellationToken);
        }

        var execution = await RunCommandAsync(request, workingDirectory, timeout, cancellationToken);
        var artifact = await SaveExecutionArtifactAsync(
            context.SessionId,
            request,
            execution.ExitCode,
            execution.TimedOut,
            execution.StandardOutput,
            execution.StandardError,
            startedAtUtc,
            execution.CompletedAtUtc,
            blockedReason: null,
            isBlocked: false,
            workingDirectory,
            cancellationToken);

        return new ShellCommandResult(
            IsBlocked: false,
            BlockReason: null,
            Command: request.Command,
            WorkingDirectory: workingDirectory,
            ExitCode: execution.ExitCode,
            TimedOut: execution.TimedOut,
            StandardOutput: execution.StandardOutput,
            StandardError: execution.StandardError,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: execution.CompletedAtUtc,
            LogArtifact: artifact);
    }

    private async ValueTask<ShellExecution> RunCommandAsync(
        ShellCommandRequest request,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var shell = request.ShellExecutablePath ?? ResolveDefaultShellPath();
        var startInfo = new ProcessStartInfo(shell)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in ResolveShellArguments(request.Command))
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{shell}'.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        var timedOut = false;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        var exitCode = timedOut ? -1 : process.ExitCode;

        return new ShellExecution(
            ExitCode: exitCode,
            TimedOut: timedOut,
            StandardOutput: stdOut,
            StandardError: stdErr,
            CompletedAtUtc: DateTimeOffset.UtcNow);
    }

    private async ValueTask<ShellCommandResult> BuildBlockedResultAsync(
        SessionId sessionId,
        ShellCommandRequest request,
        string workingDirectory,
        DateTimeOffset startedAtUtc,
        string reason,
        CancellationToken cancellationToken)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        var artifact = await SaveExecutionArtifactAsync(
            sessionId,
            request,
            exitCode: -1,
            timedOut: false,
            standardOutput: string.Empty,
            standardError: string.Empty,
            startedAtUtc,
            completedAtUtc,
            blockedReason: reason,
            isBlocked: true,
            workingDirectory,
            cancellationToken);

        return new ShellCommandResult(
            IsBlocked: true,
            BlockReason: reason,
            Command: request.Command,
            WorkingDirectory: workingDirectory,
            ExitCode: -1,
            TimedOut: false,
            StandardOutput: string.Empty,
            StandardError: string.Empty,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            LogArtifact: artifact);
    }

    private async ValueTask<ArtifactDescriptor> SaveExecutionArtifactAsync(
        SessionId sessionId,
        ShellCommandRequest request,
        int exitCode,
        bool timedOut,
        string standardOutput,
        string standardError,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string? blockedReason,
        bool isBlocked,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var payload = new ShellCommandExecutionLog(
            SessionId: sessionId,
            WorkflowId: request.WorkflowId,
            Command: request.Command,
            WorkingDirectory: workingDirectory,
            IsBlocked: isBlocked,
            BlockReason: blockedReason,
            TimedOut: timedOut,
            ExitCode: exitCode,
            StandardOutput: standardOutput,
            StandardError: standardError,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            TimeoutMilliseconds: (long)(request.Timeout ?? DefaultTimeout).TotalMilliseconds);

        return await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"shell-command-log-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "shell-command-log",
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: completedAtUtc),
            cancellationToken);
    }

    private static string ResolveWorkingDirectory(string worktreePath, string? relativeWorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(relativeWorkingDirectory))
            return Path.GetFullPath(worktreePath);

        return Path.GetFullPath(Path.Combine(worktreePath, relativeWorkingDirectory));
    }

    private static string ResolveDefaultShellPath()
        => OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";

    private static IReadOnlyList<string> ResolveShellArguments(string command)
        => OperatingSystem.IsWindows()
            ? ["/d", "/c", command]
            : ["-lc", command];

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cancellation only.
        }
    }

    private sealed record ShellExecution(
        int ExitCode,
        bool TimedOut,
        string StandardOutput,
        string StandardError,
        DateTimeOffset CompletedAtUtc);

    private sealed record ShellCommandExecutionLog(
        SessionId SessionId,
        WorkflowId WorkflowId,
        string Command,
        string WorkingDirectory,
        bool IsBlocked,
        string? BlockReason,
        bool TimedOut,
        int ExitCode,
        string StandardOutput,
        string StandardError,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        long TimeoutMilliseconds);
}
