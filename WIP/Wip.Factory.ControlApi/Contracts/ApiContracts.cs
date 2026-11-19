using System.Text.Json.Serialization;

namespace Wip.Factory.ControlApi.Contracts;

/// <summary>
/// The versioned adapter contract. These types are deliberately explicit rather than reusing factory records:
/// the wire schema is the control plane's public integration surface and must be reviewable on its own.
/// </summary>
public static class ApiSchema
{
    public const string Version = "v1";
    public const string ProtocolVersion = "1.0";
}

public sealed record ErrorBody(string Code, string Message, string Category, bool Retryable);

public sealed record ErrorEnvelope(ErrorBody Error);

public sealed record PageDto<T>(
    IReadOnlyList<T> Items,
    int Limit,
    int Offset,
    int Total,
    bool HasMore);

public sealed record CapabilitiesDto(
    bool WorkIntake,
    bool ContractApproval,
    bool ContractRejection,
    bool Dispatch,
    bool MergeApproval,
    bool ServeSupervision,
    bool Templates,
    bool AgentProfiles,
    bool Analytics,
    bool Audit,
    bool Cancellation,
    bool PauseResume,
    bool Scheduling,
    bool Retry);

public sealed record PolicyVersionsDto(
    string MandatoryPolicy,
    string Guidelines,
    string Profile,
    string Runner,
    string ReviewChecks,
    string ArchitecturePolicy,
    string SecurityPolicy);

public sealed record MetaDto(
    string ApiVersion,
    string AdapterVersion,
    string ProtocolVersion,
    string FactoryInstanceId,
    string ImplementationRunner,
    bool ExecutionEnabled,
    CapabilitiesDto Capabilities,
    PolicyVersionsDto Policy,
    DateTimeOffset ServerTimeUtc);

public sealed record HealthDto(
    bool Healthy,
    bool StoreReadable,
    string FactoryInstanceId,
    int WorkItemCount,
    int EventCount,
    long LatestEventSequence,
    bool DispatchRunning,
    string ImplementationRunner,
    int TornRecordCount,
    DateTimeOffset CheckedAtUtc,
    string? Detail);

public sealed record FactoryDto(
    string InstanceId,
    string Label,
    string ControlRoot,
    string TargetRepository,
    string TargetBranch,
    string SupportedClass,
    string ImplementationRunner,
    bool ExecutionEnabled,
    CapabilitiesDto Capabilities,
    PolicyVersionsDto Policy,
    DateTimeOffset ObservedAtUtc);

public sealed record PlanningDto(
    string? Priority,
    string? DeadlineUtc,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Links,
    string? Notes,
    string? UpdatedBy,
    string? UpdatedAtUtc,
    bool Authoritative);

public sealed record RequirementDto(
    string Title,
    string SupportedClass,
    string Digest,
    string Text,
    IReadOnlyList<string> Expectations,
    string? AcceptanceContractReference,
    string? TemplateReference,
    string? TemplateParametersJson);

public sealed record ContractScenarioDto(
    string Id,
    string Kind,
    string Path,
    string Expectation);

public sealed record ContractDto(
    string? ProposalDigest,
    string? RequirementDigest,
    bool Executable,
    IReadOnlyList<ContractScenarioDto> Scenarios,
    IReadOnlyList<string> UnresolvedQuestions,
    string? FrozenDigest,
    string? AcceptedContractDigest,
    string? ApprovedAtUtc,
    bool IsFrozen,
    bool AwaitingApproval,
    string? RejectedDigest,
    string? RejectedAtUtc,
    string? RejectionReason);

public sealed record EventDto(
    long Sequence,
    string Id,
    string Type,
    string Source,
    string Subject,
    DateTimeOffset TimeUtc,
    string? CorrelationId,
    string? CausationId,
    string? WorkItemId,
    string? Stage,
    string? AgentDefinitionId,
    string? SessionId,
    string? AttemptId,
    string? OperationId,
    string? Summary,
    IReadOnlyDictionary<string, string> Data);

public sealed record StageEvidenceDto(
    string Role,
    string? SessionId,
    string? AttemptId,
    string? ArtifactDigest,
    string? Summary);

public sealed record StageDto(
    string Id,
    string Label,
    string State,
    string? EnteredAtUtc,
    string? ExitedAtUtc,
    long? DurationSeconds,
    string? Actor,
    int Attempts,
    string? Decision,
    string? FailureCategory,
    IReadOnlyList<string> InputArtifacts,
    IReadOnlyList<string> OutputArtifacts,
    IReadOnlyList<string> EventIds,
    IReadOnlyList<StageEvidenceDto> Evidence);

public sealed record AttemptDto(
    string AttemptId,
    string WorkItemId,
    int AttemptNumber,
    string SessionId,
    string Outcome,
    string? FailureCategory,
    string? FailureRole,
    string? Detail,
    string? WorktreePath,
    string? BaseSha,
    string? HeadSha,
    string? PreviousAttemptId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc);

public sealed record ReviewExecutionDto(
    string Kind,
    string CheckId,
    string ExecutedBy,
    string ExecutedVersion,
    string ReviewedSha,
    string PolicyVersion,
    string Outcome,
    bool Passed,
    string SubjectDigest,
    string ArtifactDigest,
    DateTimeOffset ExecutedAtUtc,
    string Detail);

public sealed record ReviewHistoryDto(
    string ReviewId,
    string? ReviewDigest,
    string? HeadSha,
    string? BaseSha,
    string? ContractDigest,
    string? EvidenceDigest,
    string Outcome,
    string? Reason,
    string? ChecksSummary,
    string? RecordedAtUtc,
    IReadOnlyList<ReviewExecutionDto> Executions);

public sealed record ReleaseVerificationDto(
    string WorkItemId,
    string MergeOperationId,
    string ApprovedHeadSha,
    string BaseSha,
    string MergedSha,
    string? MergedTreeSha,
    string ContractDigest,
    string BuildEvidenceDigest,
    string RegressionEvidenceDigest,
    string ContractEvidenceDigest,
    string ScenarioSummary,
    string VerifierVersion,
    string Outcome,
    bool Verified,
    string Detail,
    DateTimeOffset VerifiedAtUtc,
    string RecordDigest);

public sealed record ServingDto(
    string? WorkItemId,
    string? MergedSha,
    string? Status,
    int? ProcessId,
    int? RestartCount,
    string? Detail,
    DateTimeOffset? RecordedAtUtc,
    bool Active,
    string? Url,
    string? JobId,
    string? SupervisedUntilUtc);

public sealed record ReleaseDto(
    string WorkItemId,
    string? MergedSha,
    string? MergedTreeSha,
    string? ApprovedHeadSha,
    string? BaseSha,
    string? ContractDigest,
    string? PostMergeDetail,
    string? Scenarios,
    string? MutationMode,
    string? OperationId,
    string? VerifiedAtUtc,
    ReleaseVerificationDto? Verification,
    ServingDto Serving,
    bool ReleaseRecorded);

public sealed record NextActionDto(
    string Action,
    string Label,
    string Reason,
    string Consequence,
    bool Available,
    string RequiredRole,
    string? Endpoint);

public sealed record AttentionDto(
    string Id,
    string Severity,
    string Title,
    string WhatHappened,
    string WhyItMatters,
    string SafeAction,
    string Consequence,
    string? WorkItemId,
    string? Action,
    string? Endpoint);

public sealed record WorkItemSummaryDto(
    string WorkItemId,
    string State,
    int Version,
    string Title,
    string Repository,
    string RequirementDigest,
    string? ProposalDigest,
    string? FrozenContractDigest,
    string? ReviewDigest,
    string? LastFailure,
    int EventCount,
    string? CreatedAtUtc,
    string? UpdatedAtUtc,
    bool AwaitingContractApproval,
    bool AwaitingMergeApproval,
    string? DerivedIndicator,
    PlanningDto Planning);

public sealed record WorkItemDetailDto(
    WorkItemSummaryDto Summary,
    string FactoryInstanceId,
    RequirementDto? Requirement,
    PlanningDto Planning,
    ContractDto Contract,
    IReadOnlyList<EventDto> Events,
    IReadOnlyList<StageDto> Stages,
    IReadOnlyList<AttemptDto> Attempts,
    IReadOnlyList<ReviewHistoryDto> Reviews,
    IReadOnlyList<ReleaseDto> Releases,
    ReleaseDto? Release,
    ServingDto Serving,
    IReadOnlyList<AttentionDto> Attention,
    IReadOnlyList<NextActionDto> NextActions,
    IReadOnlyList<string> LegalStateTargets,
    string? ExecutionHealth,
    string? ActiveSessionId,
    bool DispatchActive,
    int EventTotal,
    long? LatestEventSequence,
    DateTimeOffset GeneratedAtUtc);

public sealed record LineageNodeDto(
    string Id,
    string Kind,
    string Label,
    string? Detail,
    string? WorkItemId,
    string? ParentId);

public sealed record GraphEdgeDto(string From, string To);

public sealed record LineageDto(
    string WorkItemId,
    IReadOnlyList<LineageNodeDto> Nodes,
    IReadOnlyList<GraphEdgeDto> Edges);

public sealed record PreviewRequestDto(
    string Requirement,
    string? Title,
    string? AcceptanceContractJson);

public sealed record PreviewDto(
    bool Valid,
    IReadOnlyList<string> Errors,
    string? Title,
    string? SupportedClass,
    IReadOnlyList<string> Expectations,
    string? RequirementDigest,
    string? ContractDigest,
    string? NormalizedRequirement,
    IReadOnlyList<ContractScenarioDto> ContractScenarios);

public sealed record PlanningInputDto(
    string? Priority,
    string? DeadlineUtc,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<string>? Links,
    string? Notes);

public sealed record SubmitRequestDto(
    string Requirement,
    string? Title,
    string? AcceptanceContractJson,
    PlanningInputDto? Planning,
    bool? ExplicitlyConfirmed);

public sealed record SubmitResponseDto(
    bool Accepted,
    string WorkItemId,
    string State,
    string RequirementDigest,
    bool Duplicate,
    PlanningDto Planning,
    string? NextAction);

public sealed record ContractApproveRequestDto(string ProposalDigest, bool Confirmed);

public sealed record ContractRejectRequestDto(string ProposalDigest, string? Reason);

public sealed record MergeApprovalRequestDto(string ReviewDigest, string Confirmation);

public sealed record PlanningUpdateRequestDto(
    string? Priority,
    string? DeadlineUtc,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<string>? Links,
    string? Notes,
    int? ExpectedVersion);

public sealed record DispatchRequestDto(int? MaxPasses);

public sealed record DispatchJobDto(
    string JobId,
    string State,
    string? Stage,
    string? WorkItemId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int Claimed,
    int Dispatched,
    int Blocked,
    int Failed,
    int Passes,
    string? Error,
    string? RequestedBy,
    bool ExclusiveLockHeld);

public sealed record DispatchStatusDto(
    bool Running,
    DispatchJobDto? Current,
    IReadOnlyList<DispatchJobDto> Recent);

public sealed record ServeRequestDto(int DurationSeconds);

public sealed record ServeStatusDto(ServeJobDto? Job, ServingDto Serving);

public sealed record ServeJobDto(
    string JobId,
    string WorkItemId,
    string State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int? ProcessId,
    string? Url,
    int Restarts,
    string? Error,
    string? MergedSha,
    string? RequestedBy);

public sealed record MutationResultDto(
    bool Ok,
    string Action,
    string Subject,
    string? Detail,
    int? PreviousVersion,
    int? ResultingVersion,
    DispatchJobDto? Job,
    ServeJobDto? ServeJob,
    DateTimeOffset RecordedAtUtc);

public sealed record DiffFileDto(string Path, string Status, string? PreviousPath);

public sealed record DiffDto(
    string WorkItemId,
    string BaseSha,
    string HeadSha,
    IReadOnlyList<DiffFileDto> Files,
    string Patch,
    bool Truncated,
    int MaxBytes);

public sealed record FileContentDto(
    string Path,
    string Sha,
    string Content,
    bool Truncated,
    int MaxBytes,
    string? MediaType);

public sealed record ProfileDto(
    string ProfileId,
    string DefinitionId,
    int Version,
    int? ParentVersion,
    string ChangeReason,
    string Role,
    string Objective,
    string ModelProvider,
    string PreferredModel,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Tools,
    int MaxSteps,
    int TimeoutSeconds,
    int MaxTotalTokens,
    int MaxConsecutiveToolFailures,
    IReadOnlyList<string> RequestedPermissions,
    string PolicyRef,
    IReadOnlyList<string> HardGates,
    string EvaluationPolicy,
    string CandidateRollout,
    string ContentDigest,
    DateTimeOffset PublishedAtUtc,
    bool Active);

public sealed record ToolInvocationDto(
    string ToolName,
    string ArgumentsDigest,
    string ArgumentsExcerpt,
    bool Failed,
    string ResultExcerpt);

public sealed record SessionDto(
    string SessionId,
    string WorkItemId,
    string AgentDefinitionId,
    int ProfileVersion,
    string ModelId,
    int InputTokens,
    int OutputTokens,
    string Outcome,
    string? FailureCategory,
    IReadOnlyList<string> ToolNames,
    string Summary,
    DateTimeOffset RecordedAtUtc,
    string MeasurementSource,
    bool TokensMeasured,
    IReadOnlyList<ToolInvocationDto> ToolInvocations);

public sealed record AgentSummaryDto(
    string DefinitionId,
    string Role,
    string Objective,
    IReadOnlyList<string> Responsibilities,
    IReadOnlyList<string> PermissionCeiling,
    IReadOnlyList<string> RequiredCapabilities,
    string? ActiveProfileId,
    int? ActiveProfileVersion,
    string? ActiveProfileDigest,
    int PublishedProfileCount,
    int SessionCount,
    int AttemptCount,
    int SuccessCount,
    int FailureCount,
    int InputTokens,
    int OutputTokens,
    bool TokensMeasured,
    IReadOnlyList<string> ImplementedBy,
    string? LastActivityUtc);

public sealed record AgentDetailDto(
    AgentSummaryDto Summary,
    IReadOnlyList<ProfileDto> Profiles,
    IReadOnlyList<SessionDto> Sessions,
    IReadOnlyList<AttemptDto> Attempts,
    IReadOnlyList<EventDto> ImprovementEvents,
    IReadOnlyList<MetricDto> Metrics,
    IReadOnlyList<string> Constraints);

public sealed record ApprovalItemDto(
    string Id,
    string Kind,
    string WorkItemId,
    string Title,
    string Summary,
    string Digest,
    string? SourceRevision,
    IReadOnlyList<string> BindingConditions,
    IReadOnlyList<string> EffectOfApproval,
    string RequiredRole,
    string ApproveAction,
    string? RejectAction,
    string Endpoint,
    bool Stale,
    string? StaleReason,
    string? CreatedAtUtc);

public sealed record ApprovalInboxDto(
    IReadOnlyList<ApprovalItemDto> Items,
    int ContractCount,
    int MergeCount,
    int TemplateCount,
    int OtherCount);

public sealed record WorkReviewDto(
    string WorkItemId,
    string Title,
    string State,
    string Verdict,
    string? ReviewDigest,
    string? ReviewedSha,
    string? ContractDigest,
    string? RecordedAtUtc,
    IReadOnlyList<ReviewExecutionDto> Executions);

public sealed record ProfileDraftInputDto(
    int Version,
    string ChangeReason,
    string? PreferredModel,
    int? MaxSteps,
    int? TimeoutSeconds,
    int? MaxTotalTokens,
    int? MaxConsecutiveToolFailures,
    IReadOnlyList<string>? Tools,
    IReadOnlyList<string>? RequestedPermissions);

public sealed record ProfilePromoteRequestDto(int Version, bool Confirmed);

public sealed record TemplateDto(
    string TemplateId,
    int TemplateVersion,
    int SchemaVersion,
    string SupportedClass,
    string ManifestDigest,
    IReadOnlyList<string> Parameters,
    string ScenariosJson,
    bool Approved,
    bool Revoked,
    string? ApprovedAtUtc,
    string? ApprovedBy,
    string? Error);

public sealed record TemplateRegisterRequestDto(string ManifestJson);

public sealed record TemplateApproveRequestDto(string ManifestDigest, bool Confirmed);

public sealed record ReleaseListDto(
    IReadOnlyList<ReleaseListItemDto> Items,
    int Total);

public sealed record ReleaseListItemDto(
    string WorkItemId,
    string Title,
    string State,
    string? MergedSha,
    string? ContractDigest,
    string? VerifiedAtUtc,
    bool Verified,
    string ServingStatus,
    string? ServingUrl,
    string? MergedAtUtc);

public sealed record AnalyticsWindowDto(
    string FromUtc,
    string ToUtc,
    int WorkItemsCreated,
    int WorkItemsReleased,
    int WorkItemsFailed);

public sealed record MetricDefinitionDto(
    string Key,
    string Label,
    string Definition,
    string Unit,
    string? Notes);

public sealed record MetricDto(
    string Key,
    string Label,
    string Unit,
    string Status,
    double? Value,
    string Basis,
    string? Detail);

public sealed record SeriesPointDto(string Label, double Value);

public sealed record AnalyticsDto(
    AnalyticsWindowDto Window,
    IReadOnlyList<MetricDto> Metrics,
    IReadOnlyList<SeriesPointDto> ThroughputByDay,
    IReadOnlyList<SeriesPointDto> LeadTimeByState,
    IReadOnlyList<SeriesPointDto> FailureCategories,
    IReadOnlyList<SeriesPointDto> TokenUsageByModel,
    IReadOnlyList<SeriesPointDto> StageDurations,
    IReadOnlyList<MetricDefinitionDto> Definitions,
    IReadOnlyList<string> NotMeasured,
    DateTimeOffset GeneratedAtUtc);

public sealed record PipelineItemDto(
    string WorkItemId,
    string Title,
    string State,
    string Stage,
    string StageLabel,
    string EnteredStageAtUtc,
    long WaitingSeconds,
    string? LastFailure,
    bool NeedsAttention);

public sealed record PipelineStageDto(
    string Id,
    string Label,
    int QueueDepth,
    int InFlight,
    int WaitingForAuthorization,
    int Failed,
    double? AverageProcessingSeconds,
    double? AverageWaitingSeconds,
    int CompletedInWindow,
    IReadOnlyList<string> WorkItemIds);

public sealed record PipelineDto(
    string? AtUtc,
    bool IsReplay,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<PipelineStageDto> Stages,
    IReadOnlyList<PipelineItemDto> Items,
    IReadOnlyList<GraphEdgeDto> LegalEdges,
    string? PlaybackNote);

public sealed record SettingsDto(
    string FactoryInstanceId,
    string ControlRoot,
    string TargetRepository,
    string TargetBranch,
    string SupportedClass,
    string ImplementationRunner,
    bool ExecutionEnabled,
    bool ModelCredentialsConfigured,
    string ModelProvider,
    PolicyVersionsDto Policy,
    CapabilitiesDto Capabilities,
    IReadOnlyList<SettingDto> Settings);

public sealed record SettingDto(
    string Key,
    string Value,
    string Source,
    bool Mutable,
    string? Note);

public sealed record AuditEntryDto(
    string AuditId,
    string TimestampUtc,
    string Actor,
    string Role,
    string Action,
    string Subject,
    string Result,
    string? Detail,
    string? PreviousVersion,
    string? ResultingVersion,
    string? CorrelationId,
    string? OperationId,
    string? IdempotencyKey,
    string? RemoteAddress);

public sealed record EventStreamEnvelopeDto(
    long Sequence,
    string FactoryEventId,
    string FactoryInstanceId,
    string? WorkItemId,
    string? AgentDefinitionId,
    string? SessionId,
    string? AttemptId,
    string? OperationId,
    string? CorrelationId,
    string? CausationId,
    string Type,
    string? Stage,
    DateTimeOffset TimeUtc,
    string? Summary,
    IReadOnlyDictionary<string, string> Data);

public sealed record StreamResyncDto(string Reason, long LatestSequence, DateTimeOffset AtUtc);

public sealed record HeartbeatDto(DateTimeOffset AtUtc, long LatestSequence, bool Healthy);

public sealed record InstanceIdentityDto(string InstanceId, string Label, string TargetRepository);

[JsonSerializable(typeof(ErrorEnvelope))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
