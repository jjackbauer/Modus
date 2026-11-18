using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Builder;
using Wip.Factory.Adaptation;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.Contracts.Triggers;
using Wip.Factory.Wip.Fanout;

namespace Wip.Factory.Wip.Roles;

/// <summary>The declared artifact a role must publish when dispatched (source §8, F-G.11).</summary>
public sealed record RoleArtifact(
    AgentDefinitionId AgentDefinitionId,
    string Role,
    string StageName,
    string ArtifactKind,
    string ProducedBySession,
    string ProfileVersion,
    bool ModelDriven,
    string TriggerKind,
    string TriggerDetail);

/// <summary>Outcome of dispatching one role.</summary>
public sealed record RoleDispatchRecord(
    AgentDefinitionId AgentDefinitionId,
    string Role,
    bool Dispatched,
    string? FailureCategory,
    RoleArtifact? Artifact);

/// <summary>
/// Dispatches every role in the pack from its own stage trigger (F-G.1–F-G.9, D8). It routes through
/// <see cref="FactoryAgentDispatcher"/>, so each role is resolved from its pinned profile, authorized
/// and executed through the WIP execution port — not through a bespoke path per role.
/// </summary>
public sealed class NineRoleDispatchRunner
{
    private static readonly (string Stage, string ArtifactKind)[] RoleStages =
    [
        ("requirements", "aligned-requirement"),
        ("tactical-architecture", "refined-requirement"),
        ("implementation", "implementation-evidence"),
        ("review", "review-verdict"),
        ("delivery-decision", "merge-decision"),
        ("delivery", "delivery-validation"),
        ("release", "release-authorization"),
        ("strategic-architecture", "guidelines-revision"),
        ("operations", "operational-requirement")
    ];

    private readonly IAgentProfileRegistry _profiles;
    private readonly IReadOnlyList<FactoryAgentDefinition> _definitions;
    private readonly FactoryAgentDispatcher _dispatcher;

    public NineRoleDispatchRunner(
        IAgentProfileRegistry profiles,
        IReadOnlyList<FactoryAgentDefinition> definitions,
        FactoryAgentDispatcher dispatcher)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    /// The default trigger for each role in source §8 order: each stage is activated by the previous
    /// stage's committed fact. Roles with a distinct trigger (scheduled, telemetry) override these.
    /// </summary>
    private static readonly RoleTriggerKind[] DefaultTriggerKinds =
    [
        RoleTriggerKind.RequirementSubmitted,
        RoleTriggerKind.AlignedRequirement,
        RoleTriggerKind.ReadyWork,
        RoleTriggerKind.ProposedImplementation,
        RoleTriggerKind.AcceptedReview,
        RoleTriggerKind.MergeCandidate,
        RoleTriggerKind.ValidatedCandidate,
        RoleTriggerKind.ScheduleOccurrence,
        RoleTriggerKind.TelemetrySignal
    ];

    public async ValueTask<IReadOnlyList<RoleDispatchRecord>> RunAsync(
        SessionId sessionId,
        WorkItemId workItemId,
        bool modelDriven,
        IReadOnlyList<RoleTrigger>? triggers = null,
        CancellationToken cancellationToken = default)
    {
        if (_definitions.Count != RoleStages.Length)
        {
            throw new InvalidOperationException(
                $"The pack declares {_definitions.Count} roles but {RoleStages.Length} stage triggers are mapped.");
        }

        var records = new List<RoleDispatchRecord>(_definitions.Count);

        for (var index = 0; index < _definitions.Count; index++)
        {
            var definition = _definitions[index];
            var (stage, artifactKind) = RoleStages[index];

            var result = await _dispatcher.ExecuteAsync(
                new FactoryWorkDispatch(
                    sessionId,
                    workItemId,
                    new StageName(stage),
                    definition.AgentDefinitionId,
                    new AgentProfileVersion(1),
                    $"{definition.Role}: {definition.Objective}"),
                cancellationToken).ConfigureAwait(false);

            var trigger = triggers is not null && index < triggers.Count
                ? triggers[index]
                : RoleTrigger.Of(DefaultTriggerKinds[index], $"stage:{stage}");

            var artifact = result.Completed
                ? new RoleArtifact(
                    definition.AgentDefinitionId,
                    definition.Role,
                    stage,
                    artifactKind,
                    sessionId.Value,
                    result.AgentProfileVersion.ToString(),
                    modelDriven,
                    trigger.Kind.ToString(),
                    trigger.Detail)
                : null;

            records.Add(new RoleDispatchRecord(
                definition.AgentDefinitionId,
                definition.Role,
                result.Completed,
                result.FailureCategory,
                artifact));
        }

        return records;
    }

    /// <summary>Writes one artifact file per dispatched role, so the run leaves evidence on disk.</summary>
    public static async Task WriteArtifactsAsync(
        string outputDirectory,
        IReadOnlyList<RoleDispatchRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(records);

        Directory.CreateDirectory(outputDirectory);
        var options = new JsonSerializerOptions { WriteIndented = true };

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var path = Path.Combine(
                outputDirectory,
                $"{index + 1:00}-{record.AgentDefinitionId.Value}.json");

            var json = JsonSerializer.Serialize(record, options);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
    }
}
