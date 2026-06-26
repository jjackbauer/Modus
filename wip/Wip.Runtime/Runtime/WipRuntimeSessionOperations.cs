using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Sessions;
using Wip.Agent.Basic;

namespace Wip.Runtime.Runtime;

public sealed record SessionPlanResult(
    SessionSnapshot Session,
    PlanOnlyAgentResult Plan);

public sealed record SessionDiffRequest(
    string DiffHash,
    IReadOnlyList<string> ChangedFiles,
    string Patch,
    DateTimeOffset? ProducedAtUtc = null);

public sealed record SessionDiffResult(
    SessionSnapshot Session,
    string DiffHash,
    IReadOnlyList<string> ChangedFiles,
    ArtifactDescriptor DiffArtifact);

public sealed record SessionCheckpointRequest(
    string Name,
    string DiffHash,
    IReadOnlyList<string> ChangedFiles,
    string Patch,
    DateTimeOffset? ProducedAtUtc = null);

public sealed record SessionCheckpointResult(
    SessionSnapshot Session,
    ArtifactDescriptor CheckpointArtifact);

public sealed record SessionValidationCommandResult(
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

public sealed record SessionRuntimeDotNetValidationRequest(
    string BuildProjectPath,
    string TestProjectPath,
    string DotNetExecutablePath = "dotnet",
    TimeSpan? CommandTimeout = null);

public sealed record SessionValidationRequest(
    bool BuildSucceeded,
    bool TestSucceeded,
    string? DiffHash,
    string Summary,
    DateTimeOffset? ProducedAtUtc = null,
    IReadOnlyList<SessionValidationCommandResult>? CommandResults = null,
    SessionRuntimeDotNetValidationRequest? RuntimeValidation = null)
{
    public bool Succeeded => BuildSucceeded && TestSucceeded;
}

public sealed record SessionValidationResult(
    SessionSnapshot Session,
    ArtifactDescriptor ValidationArtifact,
    bool Succeeded,
    string? DiffHash);

public sealed record SessionReviewResult(
    SessionSnapshot Session,
    ReviewResult Review);

public sealed record SessionApprovalTokenResult(
    SessionSnapshot Session,
    ApprovalToken ApprovalToken,
    ArtifactDescriptor ApprovalArtifact);

public sealed record SessionMergeRequest(
    string TargetBranch,
    string TargetCommit,
    string DiffHash,
    string Summary,
    DateTimeOffset? ProducedAtUtc = null);

public sealed record SessionMergeResult(
    SessionSnapshot Session,
    ArtifactDescriptor MergeArtifact);

public enum SessionArchivePolicyMode
{
    MarkOnly = 0,
    Cleanup = 1
}

public sealed record SessionArchiveRequest(
    string Reason,
    SessionArchivePolicyMode PolicyMode = SessionArchivePolicyMode.MarkOnly,
    DateTimeOffset? ProducedAtUtc = null);

public sealed record SessionAbortRequest(
    string Reason,
    DateTimeOffset? ProducedAtUtc = null);

public sealed record SessionArtifactResult(
    SessionSnapshot Session,
    ArtifactDescriptor Artifact);