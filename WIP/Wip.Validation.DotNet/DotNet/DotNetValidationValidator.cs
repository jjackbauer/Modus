using System.ComponentModel;
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
    string? AstProjectPath = null,
    bool EnableAstValidation = false,
    TimeSpan? AstTimeout = null,
    string? TargetBranch = null,
    string? SessionBranch = null,
    string? DiffHashOverride = null,
    TimeSpan? CommandTimeout = null,
    string DotNetExecutablePath = "dotnet",
    int RequiredSdkMajorVersion = 10)
{
    public DotNetValidationProjectGraph ProjectGraph => new(
        RepositoryPath,
        BuildProjectPath,
        TestProjectPath,
        AstProjectPath);

    public DotNetCommandTimeoutPolicy TimeoutPolicy => new(
        CommandTimeout ?? TimeSpan.FromMinutes(5),
        AstTimeout);

    public DotNetExecutableResolutionPolicy ExecutableResolutionPolicy => new(
        DotNetExecutablePath,
        RequiredSdkMajorVersion);
}

public sealed record DotNetValidationProjectGraph(
    string RepositoryPath,
    string BuildProjectPath,
    string TestProjectPath,
    string? AstProjectPath = null);

public sealed record DotNetCommandTimeoutPolicy(
    TimeSpan CommandTimeout,
    TimeSpan? AstTimeout = null);

public sealed record DotNetExecutableResolutionPolicy(
    string ExecutablePath,
    int RequiredMajorVersion);

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

public sealed record ValidationCommandSequenceEntry(
    int Sequence,
    string Command,
    int ExitCode,
    bool TimedOut,
    bool Succeeded,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string StandardOutputSummary,
    string StandardErrorSummary,
    string StandardOutput,
    string StandardError);

public sealed record ValidationCommandRollupEntry(
    int Sequence,
    string Command,
    bool Succeeded,
    int ExitCode,
    bool TimedOut,
    string StandardOutputSummary,
    string StandardErrorSummary);

public sealed record ValidationEffectiveConfiguration(
    string RepositoryPath,
    string WorktreePath,
    string BuildProjectPath,
    string TestProjectPath,
    string? AstProjectPath,
    bool AstValidationEnabled,
    string DotNetExecutablePath,
    int RequiredSdkMajorVersion,
    TimeSpan CommandTimeout,
    TimeSpan? AstTimeout,
    string? TargetBranch,
    string? SessionBranch);

public sealed record ValidationCommandCatalogEntry(
    string Name,
    string Command,
    bool Enabled,
    bool Executed,
    bool Succeeded,
    int ExitCode,
    bool TimedOut,
    string Summary);

public sealed record ValidationRuntimePolicyEntry(
    string Policy,
    string Status,
    string Reason);

public sealed record ValidationExecutionStatus(
    string CorrelationId,
    bool Succeeded,
    ValidationProgressionOutcome ProgressionOutcome,
    string ProgressionReason,
    DateTimeOffset ProducedAtUtc);

public sealed record ValidationOperationalDiagnostics(
    ValidationEffectiveConfiguration EffectiveConfiguration,
    IReadOnlyList<ValidationCommandCatalogEntry> CommandCatalog,
    IReadOnlyList<ValidationRuntimePolicyEntry> ActiveRuntimePolicies,
    ValidationExecutionStatus Status);

public enum ValidationProgressionOutcome
{
    Allowed,
    Failed,
    Blocked,
}

public sealed record ValidationProgressionPolicy(
    ValidationProgressionOutcome Outcome,
    bool ReleaseReady,
    bool DeploymentReady,
    string Reason)
{
    public const string PolicyName = "validation-isolation";

    public static ValidationProgressionPolicy Allow(string reason)
        => new(ValidationProgressionOutcome.Allowed, ReleaseReady: true, DeploymentReady: true, reason);

    public static ValidationProgressionPolicy Fail(string reason)
        => new(ValidationProgressionOutcome.Failed, ReleaseReady: false, DeploymentReady: false, reason);

    public static ValidationProgressionPolicy Block(string reason)
        => new(ValidationProgressionOutcome.Blocked, ReleaseReady: false, DeploymentReady: false, reason);
}

public sealed record ValidationReport(
    SessionId SessionId,
    DateTimeOffset ProducedAtUtc,
    string WorktreePath,
    ValidationCommandResult Restore,
    ValidationCommandResult Build,
    ValidationCommandResult Test,
    string? DiffHash,
    string CorrelationId,
    AstValidationResult? Ast = null)
{
    public IReadOnlyList<ValidationCommandSequenceEntry> CommandResults { get; init; } = [];

    public IReadOnlyList<ValidationCommandRollupEntry> CommandRollup { get; init; } = [];

    public ValidationOperationalDiagnostics? OperationalDiagnostics { get; init; }
        = null;

    public ValidationProgressionPolicy ProgressionPolicy { get; init; } = ValidationProgressionPolicy.Block("Validation progression policy was not evaluated.");
}

public sealed record DotNetValidationResult(
    ValidationReport Report,
    ArtifactDescriptor ReportArtifact,
    SessionValidationResult? SessionValidation = null)
{
    public bool Succeeded => SessionValidation?.Succeeded
        ?? (Report.Restore.Succeeded && Report.Build.Succeeded && Report.Test.Succeeded && (Report.Ast?.Succeeded ?? true));

    public ValidationProgressionPolicy ProgressionPolicy
        => SessionValidation?.ProgressionPolicy
            ?? ValidationProgressionPolicyEvaluator.Evaluate(Report.Restore, Report.Build, Report.Test, Report.Ast);
}

public sealed record SessionValidationResult(
    DotNetValidationProjectGraph ProjectGraph,
    DotNetCommandTimeoutPolicy TimeoutPolicy,
    ValidationCommandResult Restore,
    ValidationCommandResult Build,
    ValidationCommandResult Test,
    string? DiffHash,
    AstValidationResult? Ast,
    ArtifactId ReportArtifactId,
    bool Succeeded,
    ValidationProgressionPolicy ProgressionPolicy);

internal static class ValidationProgressionPolicyEvaluator
{
    public static ValidationProgressionPolicy Evaluate(
        ValidationCommandResult restore,
        ValidationCommandResult build,
        ValidationCommandResult test,
        AstValidationResult? ast)
    {
        if (IsBlocked(restore) || IsBlocked(build) || IsBlocked(test))
        {
            return ValidationProgressionPolicy.Block(
                "Validation commands were blocked or skipped; release-ready and deployment-ready progression is denied.");
        }

        if (!restore.Succeeded || !build.Succeeded || !test.Succeeded)
        {
            return ValidationProgressionPolicy.Fail(
                "Validation failed; release-ready and deployment-ready progression is denied.");
        }

        if (ast is { Succeeded: false })
        {
            return ValidationProgressionPolicy.Fail(
                "AST validation failed; release-ready and deployment-ready progression is denied.");
        }

        return ValidationProgressionPolicy.Allow(
            "Validation succeeded; release-ready and deployment-ready progression is allowed.");
    }

    private static bool IsBlocked(ValidationCommandResult command)
        => command.ExitCode == -2;
}

public sealed class DotNetValidationValidator : IValidator<DotNetValidationRequest, DotNetValidationResult>
{
    private const string ProducerType = "Wip.Validation.DotNet";
    private const string ProducerVersion = "1.0.0";
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(5);

    private readonly IArtifactStore _artifactStore;
    private readonly WipWorkspaceProviderGit _workspaceProvider;
    private readonly IRoslynAstAnalyzer _astAnalyzer;

    public DotNetValidationValidator(IArtifactStore artifactStore, WipWorkspaceProviderGit workspaceProvider)
        : this(artifactStore, workspaceProvider, new RoslynAstAnalyzer())
    {
    }

    public DotNetValidationValidator(
        IArtifactStore artifactStore,
        WipWorkspaceProviderGit workspaceProvider,
        IRoslynAstAnalyzer astAnalyzer)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _workspaceProvider = workspaceProvider ?? throw new ArgumentNullException(nameof(workspaceProvider));
        _astAnalyzer = astAnalyzer ?? throw new ArgumentNullException(nameof(astAnalyzer));
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
        if (request.EnableAstValidation)
            ValidateRequired(request.AstProjectPath ?? string.Empty, nameof(request.AstProjectPath));

        if (request.RequiredSdkMajorVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.RequiredSdkMajorVersion), "Required SDK major version must be greater than zero.");

        if (request.AstTimeout is { } astTimeout && astTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request.AstTimeout), "AST timeout must be greater than zero.");

        var commandTimeout = request.CommandTimeout ?? DefaultCommandTimeout;
        if (commandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request.CommandTimeout), "Command timeout must be greater than zero.");

        await ResolveDotNetExecutableAsync(
            request.ExecutableResolutionPolicy,
            commandTimeout,
            cancellationToken);

        var restoreArguments = new[] { "restore", request.BuildProjectPath, "--nologo", "-v", "minimal" };
        var restoreResult = await RunDotNetAsync(
            request.DotNetExecutablePath,
            context.WorktreePath,
            restoreArguments,
            commandTimeout,
            cancellationToken);

        var buildArguments = new[] { "build", request.BuildProjectPath, "--nologo", "-v", "minimal" };
        var buildResult = restoreResult.Succeeded
            ? await RunDotNetAsync(
                request.DotNetExecutablePath,
                context.WorktreePath,
                buildArguments,
                commandTimeout,
                cancellationToken)
            : CreateSkippedCommandResult(
                request.DotNetExecutablePath,
                buildArguments,
                "Skipped because restore failed or timed out.");

        var testArguments = new[] { "test", request.TestProjectPath, "--no-build", "--nologo", "-v", "minimal" };
        var testResult = buildResult.Succeeded
            ? await RunDotNetAsync(
                request.DotNetExecutablePath,
                context.WorktreePath,
                testArguments,
                commandTimeout,
                cancellationToken)
            : CreateSkippedCommandResult(
                request.DotNetExecutablePath,
                testArguments,
                "Skipped because build failed or timed out.");

        string? diffHash = string.IsNullOrWhiteSpace(request.DiffHashOverride)
            ? null
            : request.DiffHashOverride;
        if (string.IsNullOrWhiteSpace(diffHash)
            && !string.IsNullOrWhiteSpace(request.TargetBranch)
            && !string.IsNullOrWhiteSpace(request.SessionBranch))
        {
            diffHash = await _workspaceProvider.ComputeNormalizedDiffHashAsync(
                new DiffHashRequest(
                    request.RepositoryPath,
                    request.TargetBranch,
                    request.SessionBranch),
                cancellationToken);
        }

        AstValidationResult? astResult = null;
        if (request.EnableAstValidation)
        {
            astResult = await _astAnalyzer.AnalyzeAsync(
                new AstValidationRequest(
                    RepositoryPath: request.RepositoryPath,
                    ProjectPath: request.AstProjectPath!,
                    Timeout: request.AstTimeout),
                cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var commandResults = BuildCommandSequence(restoreResult, buildResult, testResult);
        var correlationId = context.SessionId.Value;
        var progressionPolicy = ValidationProgressionPolicyEvaluator.Evaluate(restoreResult, buildResult, testResult, astResult);
        var report = new ValidationReport(
            SessionId: context.SessionId,
            ProducedAtUtc: now,
            WorktreePath: context.WorktreePath,
            Restore: restoreResult,
            Build: buildResult,
            Test: testResult,
            DiffHash: diffHash,
            CorrelationId: correlationId,
            Ast: astResult)
        {
            CommandResults = commandResults,
            CommandRollup = commandResults
                .Select(static result => new ValidationCommandRollupEntry(
                    Sequence: result.Sequence,
                    Command: result.Command,
                    Succeeded: result.Succeeded,
                    ExitCode: result.ExitCode,
                    TimedOut: result.TimedOut,
                    StandardOutputSummary: result.StandardOutputSummary,
                    StandardErrorSummary: result.StandardErrorSummary))
                .ToArray(),
            ProgressionPolicy = progressionPolicy,
            OperationalDiagnostics = BuildOperationalDiagnostics(
                request,
                context,
                commandTimeout,
                now,
                restoreResult,
                buildResult,
                testResult,
                progressionPolicy)
        };

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

        var sessionValidation = new SessionValidationResult(
            ProjectGraph: request.ProjectGraph,
            TimeoutPolicy: new DotNetCommandTimeoutPolicy(commandTimeout, request.AstTimeout),
            Restore: restoreResult,
            Build: buildResult,
            Test: testResult,
            DiffHash: diffHash,
            Ast: astResult,
            ReportArtifactId: reportArtifact.ArtifactId,
            Succeeded: restoreResult.Succeeded && buildResult.Succeeded && testResult.Succeeded && (astResult?.Succeeded ?? true),
            ProgressionPolicy: progressionPolicy);

        return new DotNetValidationResult(report, reportArtifact, sessionValidation);
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

    private static async ValueTask<DotNetExecutableResolutionPolicy> ResolveDotNetExecutableAsync(
        DotNetExecutableResolutionPolicy policy,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var startInfo = new ProcessStartInfo(policy.ExecutablePath)
        {
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = StartProcessOrThrow(startInfo, policy.ExecutablePath);

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort kill; resolution failure is reported deterministically below.
            }

            await process.WaitForExitAsync(cancellationToken);
            throw new InvalidOperationException($"Unable to resolve dotnet executable '{policy.ExecutablePath}': version probe timed out after {timeout.TotalMilliseconds:0} ms.");
        }

        var stdOut = (await stdOutTask).Trim();
        var stdErr = (await stdErrTask).Trim();
        var version = string.IsNullOrWhiteSpace(stdOut) ? stdErr : stdOut;

        if (!Version.TryParse(version, out var parsedVersion))
            throw new InvalidOperationException($"Unable to resolve dotnet executable '{policy.ExecutablePath}': version probe returned '{version}'.");

        if (parsedVersion.Major < policy.RequiredMajorVersion)
            throw new InvalidOperationException($"Dotnet executable '{policy.ExecutablePath}' reports SDK major version {parsedVersion.Major}, but major version {policy.RequiredMajorVersion} is required.");

        _ = startedAtUtc;
        return policy;
    }

    private static Process StartProcessOrThrow(ProcessStartInfo startInfo, string executable)
    {
        try
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Unable to resolve dotnet executable '{executable}'.");
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException or InvalidOperationException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException($"Unable to resolve dotnet executable '{executable}': {exception.Message}", exception);
        }
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
    }

    private static ValidationCommandResult CreateSkippedCommandResult(
        string executable,
        IReadOnlyList<string> arguments,
        string reason)
    {
        var now = DateTimeOffset.UtcNow;
        return new ValidationCommandResult(
            Command: string.Join(" ", [executable, ..arguments]),
            ExitCode: -2,
            StandardOutput: string.Empty,
            StandardError: reason,
            TimedOut: false,
            StartedAtUtc: now,
            CompletedAtUtc: now);
    }

    private static IReadOnlyList<ValidationCommandSequenceEntry> BuildCommandSequence(
        ValidationCommandResult restoreResult,
        ValidationCommandResult buildResult,
        ValidationCommandResult testResult)
    {
        var ordered = new[] { restoreResult, buildResult, testResult };

        return ordered
            .Select((result, index) => new ValidationCommandSequenceEntry(
                Sequence: index + 1,
                Command: result.Command,
                ExitCode: result.ExitCode,
                TimedOut: result.TimedOut,
                Succeeded: result.Succeeded,
                StartedAtUtc: result.StartedAtUtc,
                CompletedAtUtc: result.CompletedAtUtc,
                StandardOutputSummary: SummarizeCommandEvidence(result.StandardOutput),
                StandardErrorSummary: SummarizeCommandEvidence(result.StandardError),
                StandardOutput: result.StandardOutput,
                StandardError: result.StandardError))
            .ToArray();
    }

    private static ValidationOperationalDiagnostics BuildOperationalDiagnostics(
        DotNetValidationRequest request,
        CapabilityContext context,
        TimeSpan commandTimeout,
        DateTimeOffset producedAtUtc,
        ValidationCommandResult restoreResult,
        ValidationCommandResult buildResult,
        ValidationCommandResult testResult,
        ValidationProgressionPolicy progressionPolicy)
    {
        var effectiveConfiguration = new ValidationEffectiveConfiguration(
            RepositoryPath: request.RepositoryPath,
            WorktreePath: context.WorktreePath,
            BuildProjectPath: request.BuildProjectPath,
            TestProjectPath: request.TestProjectPath,
            AstProjectPath: request.AstProjectPath,
            AstValidationEnabled: request.EnableAstValidation,
            DotNetExecutablePath: request.DotNetExecutablePath,
            RequiredSdkMajorVersion: request.RequiredSdkMajorVersion,
            CommandTimeout: commandTimeout,
            AstTimeout: request.AstTimeout,
            TargetBranch: request.TargetBranch,
            SessionBranch: request.SessionBranch);

        var commandCatalog = new[]
        {
            CreateCommandCatalogEntry("restore", restoreResult, enabled: true),
            CreateCommandCatalogEntry("build", buildResult, enabled: true),
            CreateCommandCatalogEntry("test", testResult, enabled: true),
            new ValidationCommandCatalogEntry(
                Name: "ast",
                Command: request.EnableAstValidation
                    ? string.Join(" ", [request.DotNetExecutablePath, "ast", request.AstProjectPath ?? string.Empty])
                    : "disabled",
                Enabled: request.EnableAstValidation,
                Executed: false,
                Succeeded: !request.EnableAstValidation,
                ExitCode: request.EnableAstValidation ? -3 : 0,
                TimedOut: false,
                Summary: request.EnableAstValidation
                    ? "AST analyzer runs after restore/build/test command orchestration."
                    : "AST analyzer disabled by configuration.")
        };

        var activeRuntimePolicies = new[]
        {
            new ValidationRuntimePolicyEntry(
                Policy: ValidationProgressionPolicy.PolicyName,
                Status: progressionPolicy.Outcome.ToString(),
                Reason: progressionPolicy.Reason),
            new ValidationRuntimePolicyEntry(
                Policy: "dotnet-sdk-major-version-guard",
                Status: "Active",
                Reason: $"Requires SDK major version >= {request.RequiredSdkMajorVersion} from '{request.DotNetExecutablePath}'."),
            new ValidationRuntimePolicyEntry(
                Policy: "command-timeout",
                Status: "Active",
                Reason: $"Each command enforces timeout of {commandTimeout.TotalMilliseconds:0} ms."),
            new ValidationRuntimePolicyEntry(
                Policy: "ast-validation",
                Status: request.EnableAstValidation ? "Active" : "Disabled",
                Reason: request.EnableAstValidation
                    ? $"AST validation enabled for '{request.AstProjectPath}' with timeout {(request.AstTimeout ?? commandTimeout).TotalMilliseconds:0} ms."
                    : "AST validation is disabled in the request.")
        };

        var status = new ValidationExecutionStatus(
            CorrelationId: context.SessionId.Value,
            Succeeded: restoreResult.Succeeded && buildResult.Succeeded && testResult.Succeeded,
            ProgressionOutcome: progressionPolicy.Outcome,
            ProgressionReason: progressionPolicy.Reason,
            ProducedAtUtc: producedAtUtc);

        return new ValidationOperationalDiagnostics(
            EffectiveConfiguration: effectiveConfiguration,
            CommandCatalog: commandCatalog,
            ActiveRuntimePolicies: activeRuntimePolicies,
            Status: status);
    }

    private static ValidationCommandCatalogEntry CreateCommandCatalogEntry(
        string name,
        ValidationCommandResult result,
        bool enabled)
    {
        var summary = result.Succeeded
            ? "Command completed successfully."
            : SummarizeCommandEvidence(result.StandardError);

        if (string.IsNullOrWhiteSpace(summary))
            summary = SummarizeCommandEvidence(result.StandardOutput);

        return new ValidationCommandCatalogEntry(
            Name: name,
            Command: result.Command,
            Enabled: enabled,
            Executed: result.ExitCode != -2,
            Succeeded: result.Succeeded,
            ExitCode: result.ExitCode,
            TimedOut: result.TimedOut,
            Summary: summary);
    }

    private static string SummarizeCommandEvidence(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var lines = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => line.Length > 0)
            .Take(3)
            .ToArray();

        return lines.Length == 0
            ? string.Empty
            : string.Join(" | ", lines);
    }
}
