using System.Text.Json;
using Wip.Factory.Adaptation;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.Contracts.Telemetry;
using Wip.Factory.ControlApi.Contracts;
using Wip.Factory.ControlApi.Infrastructure;
using Wip.Factory.Host.Factory;

namespace Wip.Factory.ControlApi.ReadModels;

public sealed partial class FactoryReadModel
{
    public async Task<IReadOnlyList<AgentSummaryDto>> GetAgentsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var traces = await _service.Traces.RecentAsync(2000, cancellationToken).ConfigureAwait(false);
        var attempts = LoadAllAttempts(snapshot);
        var summaries = new List<AgentSummaryDto>();

        foreach (var definition in FactoryRolePack.Definitions)
        {
            var profiles = await _service.Profiles.ListAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false);
            var active = await _service.Profiles.ResolveActiveAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false);
            var mine = traces
                .Where(trace => string.Equals(trace.AgentDefinitionId.Value, definition.AgentDefinitionId.Value, StringComparison.Ordinal))
                .ToArray();
            var myAttempts = string.Equals(definition.AgentDefinitionId.Value, "factory.implementations-analyst", StringComparison.Ordinal)
                ? attempts
                : [];
            var success = mine.Count(static trace => trace.Outcome == "completed")
                          + myAttempts.Count(static attempt => attempt.Outcome == ImplementationAttemptRecord.SucceededOutcome);
            var failure = mine.Count(static trace => trace.Outcome == "failed")
                          + myAttempts.Count(static attempt => attempt.Outcome != ImplementationAttemptRecord.SucceededOutcome);
            var tokensMeasured = mine.Any(static trace => IsMeasuredModel(trace.ModelId.Value));
            var inputTokens = mine.Sum(static trace => trace.InputTokens);
            var outputTokens = mine.Sum(static trace => trace.OutputTokens);
            var lastActivity = mine.Select(static trace => trace.RecordedAtUtc)
                .Concat(myAttempts.Select(static attempt => attempt.FinishedAtUtc))
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max();

            summaries.Add(new AgentSummaryDto(
                definition.AgentDefinitionId.Value,
                definition.Role,
                definition.Objective,
                definition.Responsibilities,
                definition.PermissionCeiling,
                definition.RequiredCapabilities,
                active?.AgentProfileId.Value,
                active?.Version.Value,
                active?.ContentDigest,
                profiles.Count,
                mine.Length,
                myAttempts.Count,
                success,
                failure,
                tokensMeasured ? inputTokens : 0,
                tokensMeasured ? outputTokens : 0,
                tokensMeasured,
                ImplementedBy(definition, myAttempts.Count),
                lastActivity == DateTimeOffset.MinValue ? null : lastActivity.ToString("O")));
        }

        return summaries;
    }

    public async Task<AgentDetailDto> GetAgentAsync(string definitionId, CancellationToken cancellationToken)
    {
        var definition = FactoryRolePack.Definitions.FirstOrDefault(candidate =>
            string.Equals(candidate.AgentDefinitionId.Value, definitionId, StringComparison.Ordinal));
        if (definition is null)
        {
            throw ApiException.NotFound("agent-definition-not-found", "No registered factory role has that definition id.");
        }

        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var traces = await _service.Traces.RecentAsync(2000, cancellationToken).ConfigureAwait(false);
        var mine = traces
            .Where(trace => string.Equals(trace.AgentDefinitionId.Value, definitionId, StringComparison.Ordinal))
            .OrderByDescending(static trace => trace.RecordedAtUtc)
            .ToArray();
        var attempts = string.Equals(definitionId, "factory.implementations-analyst", StringComparison.Ordinal)
            ? LoadAllAttempts(snapshot).OrderByDescending(static attempt => attempt.FinishedAtUtc).ToArray()
            : [];
        var profiles = await _service.Profiles.ListAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false);
        var active = await _service.Profiles.ResolveActiveAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false);

        var sessions = mine.Select(ToSessionDto).ToArray();
        var attemptDtos = attempts.Select(ToAttemptDto).ToArray();
        var improvement = snapshot.Events
            .Select((envelope, index) => (Sequence: (long)index, Event: envelope))
            .Where(pair => pair.Event.Type is "factory.agent.reflection-requested.v1"
                           or "factory.agent.candidate-evaluated.v1"
                           or "factory.agent.profile-promoted.v1"
                           or "factory.agent.profile-rolled-back.v1"
                           or "factory.capability.admission-requested.v1"
                           or "factory.capability.promoted.v1"
                           or "factory.capability.rolled-back.v1")
            .Where(pair => !pair.Event.Data.TryGetValue("agentDefinitionId", out var recorded)
                           || string.Equals(recorded, definitionId, StringComparison.Ordinal))
            .Select(pair => ToEventDto(pair.Sequence, pair.Event, snapshot))
            .OrderByDescending(static eventDto => eventDto.TimeUtc)
            .ToArray();

        var tokensMeasured = mine.Any(static trace => IsMeasuredModel(trace.ModelId.Value));
        var success = sessions.Count(static session => session.Outcome == "completed")
                      + attemptDtos.Count(static attempt => attempt.Outcome == ImplementationAttemptRecord.SucceededOutcome);
        var failure = sessions.Length + attemptDtos.Length - success;

        var summary = new AgentSummaryDto(
            definition.AgentDefinitionId.Value,
            definition.Role,
            definition.Objective,
            definition.Responsibilities,
            definition.PermissionCeiling,
            definition.RequiredCapabilities,
            active?.AgentProfileId.Value,
            active?.Version.Value,
            active?.ContentDigest,
            profiles.Count,
            sessions.Length,
            attemptDtos.Length,
            success,
            failure,
            tokensMeasured ? mine.Sum(static trace => trace.InputTokens) : 0,
            tokensMeasured ? mine.Sum(static trace => trace.OutputTokens) : 0,
            tokensMeasured,
            ImplementedBy(definition, attemptDtos.Length),
            LastActivity(mine, attemptDtos));

        var metrics = new List<MetricDto>
        {
            Metric("agent.sessions", "Agent sessions", "count", sessions.Length, "Durable session traces and implementation attempt journals."),
            Metric("agent.success", "Successful sessions", "count", success, "Traces/attempts whose recorded outcome is completed or succeeded."),
            Metric("agent.failure", "Failed sessions", "count", failure, "Traces/attempts whose recorded outcome is failed, cancelled or non-succeeded."),
            tokensMeasured
                ? Metric("agent.input_tokens", "Input tokens", "tokens", mine.Sum(static trace => trace.InputTokens), "Sum of measured provider usage in durable traces.")
                : NotMeasured("agent.input_tokens", "Input tokens", "tokens", "No provider usage was recorded for this role. An unmeasured value is not zero."),
            tokensMeasured
                ? Metric("agent.output_tokens", "Output tokens", "tokens", mine.Sum(static trace => trace.OutputTokens), "Sum of measured provider output usage in durable traces.")
                : NotMeasured("agent.output_tokens", "Output tokens", "tokens", "No provider usage was recorded for this role. An unmeasured value is not zero."),
            NotMeasured("agent.cost", "Model cost", "currency", "Wip.Factory has no billing feed; token usage alone does not determine price."),
            NotMeasured("agent.tool_efficiency", "Tool-use efficiency", "ratio", "No complexity-normalised tool outcome denominator exists in the durable evidence."),
            Metric("agent.profile_versions", "Published profile versions", "count", profiles.Count, "Immutable versions returned by the factory profile registry.")
        };

        return new AgentDetailDto(
            summary,
            profiles.Select(profile => ToProfileDto(profile, active)).ToArray(),
            sessions,
            attemptDtos,
            improvement,
            metrics,
            new[]
            {
                "The permission ceiling belongs to the agent definition and cannot be widened by a profile.",
                "A profile may only request capabilities admitted by the host catalog.",
                "The active pointer affects future sessions; a running session keeps the pinned version it started with.",
                "This control plane does not expose a tool-permission editor."
            });
    }

    public IReadOnlyList<FactoryAgentDefinition> Definitions => FactoryRolePack.Definitions;

    public async Task<IReadOnlyList<ProfileDto>> GetProfilesForDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken)
    {
        var definition = FindDefinition(definitionId)
                         ?? throw ApiException.NotFound("agent-definition-not-found", "No registered factory role has that definition id.");
        var active = await _service.Profiles.ResolveActiveAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false);
        var profiles = await _service.Profiles.ListAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false);
        return profiles.Select(profile => ToProfileDto(profile, active)).ToArray();
    }

    public FactoryAgentDefinition? FindDefinition(string definitionId)
        => FactoryRolePack.Definitions.FirstOrDefault(candidate =>
            string.Equals(candidate.AgentDefinitionId.Value, definitionId, StringComparison.Ordinal));

    private async Task<IReadOnlyList<AgentProfile>> GetProfilesAsync(string definitionId, CancellationToken cancellationToken)
    {
        var definition = FindDefinition(definitionId)
                         ?? throw ApiException.NotFound("agent-definition-not-found", "No registered factory role has that definition id.");
        return await _service.Profiles.ListAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false);
    }

    private ProfileDto ToProfileDto(AgentProfile profile, AgentProfile? active) => new(
        profile.AgentProfileId.Value,
        profile.DefinitionId.Value,
        profile.Version.Value,
        profile.Draft.ParentVersion?.Value,
        profile.Draft.ChangeReason,
        profile.Draft.Role,
        profile.Draft.Objective,
        profile.Draft.Model.ProviderCapability,
        profile.Draft.Model.PreferredModel,
        profile.Draft.Skills.Select(static reference => reference.CapabilityId.Value + "@" + reference.Version.Value).ToArray(),
        profile.Draft.Tools.Select(static reference => reference.CapabilityId.Value + "@" + reference.Version.Value).ToArray(),
        profile.Draft.ExecutionPolicy.MaxSteps,
        profile.Draft.ExecutionPolicy.TimeoutSeconds,
        profile.Draft.ExecutionPolicy.MaxTotalTokens,
        profile.Draft.ExecutionPolicy.MaxConsecutiveToolFailures,
        profile.Draft.Permissions.Requested,
        profile.Draft.Permissions.PolicyRef,
        profile.Draft.Evaluation.HardGates.Select(static gate => gate.ToString()).ToArray(),
        profile.Draft.Evaluation.SuiteRef.Value,
        profile.Draft.Evaluation.CandidateRollout,
        profile.ContentDigest,
        profile.PublishedAtUtc,
        active is not null
        && string.Equals(active.AgentProfileId.Value, profile.AgentProfileId.Value, StringComparison.Ordinal)
        && active.Version.Value == profile.Version.Value);

    private static SessionDto ToSessionDto(AgentSessionTrace trace) => new(
        trace.SessionId.Value,
        trace.WorkItemId.Value,
        trace.AgentDefinitionId.Value,
        trace.AgentProfileVersion.Value,
        trace.ModelId.Value,
        trace.InputTokens,
        trace.OutputTokens,
        trace.Outcome,
        trace.FailureCategory,
        trace.ToolNames,
        trace.Summary,
        trace.RecordedAtUtc,
        IsMeasuredModel(trace.ModelId.Value) ? "provider-usage" : "not-measured",
        IsMeasuredModel(trace.ModelId.Value),
        (trace.ToolInvocations ?? [])
            .Select(static invocation => new ToolInvocationDto(
                invocation.ToolName,
                invocation.ArgumentsDigest,
                invocation.ArgumentsExcerpt,
                invocation.Failed,
                invocation.ResultExcerpt))
            .ToArray());

    public async Task<IReadOnlyList<SessionDto>> GetWorkSessionsAsync(
        string workItemId,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Statuses.Any(status => string.Equals(status.WorkItemId, workItemId, StringComparison.Ordinal)))
        {
            throw ApiException.NotFound("work-item-not-found", "No work item with that identifier exists.");
        }

        var traces = await _service.Traces
            .QueryAsync(new WorkItemId(workItemId), cancellationToken)
            .ConfigureAwait(false);
        return traces
            .OrderBy(static trace => trace.RecordedAtUtc)
            .Select(ToSessionDto)
            .ToArray();
    }

    private static bool IsMeasuredModel(string model)
        => !string.IsNullOrWhiteSpace(model)
           && !string.Equals(model, "unknown", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(model, "deterministic-demo", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<ImplementationAttemptRecord> LoadAllAttempts(Snapshot snapshot)
    {
        var attempts = new List<ImplementationAttemptRecord>();
        foreach (var status in snapshot.Statuses)
        {
            attempts.AddRange(LoadAttempts(status.WorkItemId));
        }

        return attempts;
    }

    private static string[] ImplementedBy(FactoryAgentDefinition definition, int attempts) =>
        definition.AgentDefinitionId.Value switch
        {
            "factory.implementations-analyst" => attempts > 0
                ? ["factory implementation runner (attempt journal)"]
                : ["factory implementation runner (no attempts recorded)"],
            "factory.review-analyst" => ["factory-review-checks@3 (deterministic checks)"],
            "factory.requirements-analyst" or "factory.tactical-architecture-analyst" =>
                ["deterministic factory stage handler"],
            "factory.deliver-analyst" => ["dotnet-run candidate host + acceptance contract runner"],
            "factory.deliver-manager" or "factory.release-manager" => ["factory release verifier + operator authorization"],
            "factory.operations-analyst" => ["factory serve supervisor and status journal"],
            _ => ["declared role; no execution path in the current operator lifecycle"]
        };

    private static string? LastActivity(IReadOnlyList<AgentSessionTrace> traces, IReadOnlyList<AttemptDto> attempts)
    {
        var latest = traces.Select(static trace => trace.RecordedAtUtc)
            .Concat(attempts.Select(static attempt => attempt.FinishedAtUtc))
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        return latest == DateTimeOffset.MinValue ? null : latest.ToString("O");
    }
}
