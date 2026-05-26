using System.Diagnostics;
using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Workspaces.Git;

namespace Wip.Validation.DotNet.DotNet;

public sealed record DotNetValidationRequest(
    string BuildProjectPath,
    string TestProjectPath,
    string RepositoryPath,
    string? TargetBranch = null,
    string? SessionBranch = null,
    TimeSpan? CommandTimeout = null,
    string DotNetExecutablePath = "dotnet");

public sealed record ValidationCommandResult(
    string Command,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

public sealed record ValidationReport(
    SessionId SessionId,
    DateTimeOffset ProducedAtUtc,
    string WorktreePath,
    ValidationCommandResult Build,
    ValidationCommandResult Test,
    string? DiffHash);

public sealed record DotNetValidationResult(
    ValidationReport Report,
    ArtifactDescriptor ReportArtifact)
{
    public bool Succeeded => Report.Build.Succeeded && Report.Test.Succeeded;
}

public sealed class DotNetValidationValidator : IValidator<DotNetValidationRequest, DotNetValidationResult>
{
    private const string ProducerType = "Wip.Validation.DotNet";
    private const string ProducerVersion = "1.0.0";
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(5);

    private readonly IArtifactStore _artifactStore;
    private readonly WipWorkspaceProviderGit _workspaceProvider;

    public DotNetValidationValidator(IArtifactStore artifactStore, WipWorkspaceProviderGit workspaceProvider)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _workspaceProvider = workspaceProvider ?? throw new ArgumentNullException(nameof(workspaceProvider));
    }

    public async ValueTask<DotNetValidationResult> ExecuteAsync(
        DotNetValidationRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateRequired(request.BuildProjectPath, nameof(request.BuildProjectPath));
        ValidateRequired(request.TestProjectPath, nameof(request.TestProjectPath));
        ValidateRequired(request.RepositoryPath, nameof(request.RepositoryPath));
        ValidateRequired(request.DotNetExecutablePath, nameof(request.DotNetExecutablePath));
        var commandTimeout = request.CommandTimeout ?? DefaultCommandTimeout;
        if (commandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request.CommandTimeout), "Command timeout must be greater than zero.");

        var buildResult = await RunDotNetAsync(
            request.DotNetExecutablePath,
            context.WorktreePath,
            ["build", request.BuildProjectPath, "--nologo", "-v", "minimal"],
            commandTimeout,
            cancellationToken);

        var testResult = await RunDotNetAsync(
            request.DotNetExecutablePath,
            context.WorktreePath,
            ["test", request.TestProjectPath, "--no-build", "--nologo", "-v", "minimal"],
            commandTimeout,
            cancellationToken);

        string? diffHash = null;
        if (!string.IsNullOrWhiteSpace(request.TargetBranch) && !string.IsNullOrWhiteSpace(request.SessionBranch))
        {
            diffHash = await _workspaceProvider.ComputeNormalizedDiffHashAsync(
                new DiffHashRequest(
                    request.RepositoryPath,
                    request.TargetBranch,
                    request.SessionBranch),
                cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var report = new ValidationReport(
            SessionId: context.SessionId,
            ProducedAtUtc: now,
            WorktreePath: context.WorktreePath,
            Build: buildResult,
            Test: testResult,
            DiffHash: diffHash);

        var reportArtifact = await _artifactStore.SaveAsync(
            context.SessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"validation-report-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "validation-report",
                content: JsonSerializer.Serialize(report),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: now),
            cancellationToken);

        return new DotNetValidationResult(report, reportArtifact);
    }

    private static async ValueTask<ValidationCommandResult> RunDotNetAsync(
        string executable,
        string worktreePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = worktreePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{executable}'.");

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
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort kill; command timeout still reported even if process exits between checks.
            }

            await process.WaitForExitAsync(cancellationToken);
        }

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;
        if (timedOut)
        {
            var timeoutMessage = $"Command timed out after {timeout.TotalMilliseconds:0} ms.";
            stdErr = string.IsNullOrWhiteSpace(stdErr)
                ? timeoutMessage
                : $"{stdErr}{Environment.NewLine}{timeoutMessage}";
        }

        return new ValidationCommandResult(
            Command: string.Join(" ", [executable, ..arguments]),
            ExitCode: timedOut ? -1 : process.ExitCode,
            StandardOutput: stdOut,
            StandardError: stdErr,
            TimedOut: timedOut,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: DateTimeOffset.UtcNow);
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
    }
}
