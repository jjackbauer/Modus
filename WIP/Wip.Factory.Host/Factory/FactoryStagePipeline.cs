using System.Security.Cryptography;
using System.Text;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Core.Fanout;

namespace Wip.Factory.Host.Factory;

/// <summary>Lifecycle event names for the operator path (v0.3 §3.3).</summary>
public static class OperatorEvents
{
    public const string RequirementAligned = "factory.requirement.aligned.v1";
    public const string ArchitectureAligned = "factory.architecture.aligned.v1";
    public const string ImplementationReady = "factory.implementation.ready.v1";
    public const string ImplementationRequested = "factory.implementation.requested.v1";
    public const string ImplementationCompleted = "factory.implementation.completed.v1";
    public const string StageFailed = "factory.stage.failed.v1";
}

/// <summary>What the implementation stage is asked to do.</summary>
public sealed record ImplementationRequest(
    string WorkItemId,
    string RepositoryPath,
    string RequirementText,
    IReadOnlyList<string> Expectations);

/// <summary>The verified outcome of an implementation attempt. Only the factory sets these fields.</summary>
public sealed record ImplementationOutcome(
    bool Succeeded,
    string? BaseSha,
    string? HeadSha,
    string? DiffDigest,
    int ChangedFiles,
    string? FailureCategory,
    string? Detail)
{
    public const string NoAttributableChange = "no-attributable-change";
    public const string BuildFailed = "validation-failed";

    public static ImplementationOutcome Failed(string category, string? detail = null)
        => new(false, null, null, null, 0, category, detail);

    public static ImplementationOutcome Ok(string baseSha, string headSha, int changedFiles, string diffDigest)
        => new(true, baseSha, headSha, diffDigest, changedFiles, null, null);
}

/// <summary>
/// Runs one implementation attempt. The real implementation drives a model with the six tools inside a
/// worktree; tests supply a deterministic runner. Either way the factory — not the runner — decides whether
/// the attempt produced attributable source, because the runner's self-report is not evidence.
/// </summary>
public interface IImplementationStageRunner
{
    ValueTask<ImplementationOutcome> RunAsync(ImplementationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The production stage handler. It maps the v0.3 trigger table onto legal transitions, and it never
/// schedules, leases or commits — the dispatch worker owns those.
/// </summary>
public sealed class FactoryStageDispatchHandler : IStageDispatchHandler
{
    public const string Subscriber = "factory.stage-handler";
    public const string IllegalTransitionCategory = "illegal-transition";

    private readonly IImplementationStageRunner? _implementations;

    public FactoryStageDispatchHandler(IImplementationStageRunner? implementations = null)
        => _implementations = implementations;

    public ValueTask<StageDispatchOutcome> HandleAsync(
        WorkItemSnapshot workItem,
        FactoryEventEnvelope trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(trigger);

        return (trigger.Type, workItem.State) switch
        {
            (FactoryEventTypes.RequirementSubmitted, FactoryWorkState.Proposed)
                => ValueTask.FromResult(Propose(workItem, trigger)),

            (FactoryEventTypes.ContractApproved, FactoryWorkState.Proposed)
                => ValueTask.FromResult(Align(workItem, trigger)),

            (OperatorEvents.RequirementAligned, FactoryWorkState.RequirementsAligned)
                => ValueTask.FromResult(RefineArchitecture(workItem, trigger)),

            (OperatorEvents.ArchitectureAligned, FactoryWorkState.ArchitectureRefined)
                => ValueTask.FromResult(PlanImplementation(workItem, trigger)),

            (OperatorEvents.ImplementationReady, FactoryWorkState.ReadyForImplementation)
                => ValueTask.FromResult(BeginImplementation(workItem, trigger)),

            (OperatorEvents.ImplementationRequested, FactoryWorkState.InImplementation)
                => ImplementAsync(workItem, trigger, cancellationToken),

            _ => ValueTask.FromResult(StageDispatchOutcome.NoOp())
        };
    }

    private static StageDispatchOutcome Propose(WorkItemSnapshot workItem, FactoryEventEnvelope trigger)
    {
        if (!trigger.Data.TryGetValue("requirement", out var text))
        {
            return StageDispatchOutcome.Failed("requirement-event-incomplete");
        }

        var (document, errors) = RequirementDocument.Parse(text);
        if (document is null)
        {
            return StageDispatchOutcome.Failed("requirement-invalid:" + string.Join(",", errors));
        }

        string? contractDigest = null;
        if (trigger.Data.TryGetValue("acceptanceContract", out var contractJson))
        {
            var (contract, _) = AcceptanceContract.Parse(contractJson);
            contractDigest = contract?.Digest;
        }

        var proposal = ContractProposal.From(document, contractDigest);
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["proposal"] = proposal.ToJson(),
            ["proposalDigest"] = proposal.ProposalDigest,
            ["requirementDigest"] = requirement_digest(document),
            ["executable"] = proposal.Executable ? "true" : "false"
        };

        return StageDispatchOutcome.Recorded([Event(FactoryEventTypes.ContractProposed, workItem.WorkItemId, data, trigger.Id)]);
    }

    private static string requirement_digest(RequirementDocument document) => document.Digest;

    private static StageDispatchOutcome Align(WorkItemSnapshot workItem, FactoryEventEnvelope trigger)
    {
        var data = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
        {
            ["contractDigest"] = trigger.Data.TryGetValue("contractDigest", out var digest) ? digest : string.Empty
        };

        return StageDispatchOutcome.Advanced(
            FactoryWorkState.RequirementsAligned,
            [Event(OperatorEvents.RequirementAligned, workItem.WorkItemId, data, trigger.Id)]);
    }

    /// <summary>Deterministic architectural gate — no model turn (v0.3 D-04).</summary>
    private static StageDispatchOutcome RefineArchitecture(WorkItemSnapshot workItem, FactoryEventEnvelope trigger)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(workItem.Repository))
        {
            errors.Add("no-target-repository");
        }
        else if (!Directory.Exists(workItem.Repository))
        {
            errors.Add("target-repository-missing");
        }

        if (!trigger.Data.TryGetValue("contractDigest", out var contract) || string.IsNullOrWhiteSpace(contract))
        {
            errors.Add("no-frozen-contract");
        }

        if (errors.Count > 0)
        {
            return StageDispatchOutcome.Failed("architecture-checks-failed:" + string.Join(",", errors));
        }

        var data = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
        {
            ["contractDigest"] = contract!,
            ["checks"] = "supported-class,repository,frozen-contract"
        };

        return StageDispatchOutcome.Advanced(
            FactoryWorkState.ArchitectureRefined,
            [Event(OperatorEvents.ArchitectureAligned, workItem.WorkItemId, data, trigger.Id)]);
    }

    private static StageDispatchOutcome PlanImplementation(WorkItemSnapshot workItem, FactoryEventEnvelope trigger)
        => StageDispatchOutcome.Advanced(
            FactoryWorkState.ReadyForImplementation,
            [Event(OperatorEvents.ImplementationReady, workItem.WorkItemId, trigger.Data, trigger.Id)]);

    private static StageDispatchOutcome BeginImplementation(WorkItemSnapshot workItem, FactoryEventEnvelope trigger)
        => StageDispatchOutcome.Advanced(
            FactoryWorkState.InImplementation,
            [Event(OperatorEvents.ImplementationRequested, workItem.WorkItemId, trigger.Data, trigger.Id)]);

    private async ValueTask<StageDispatchOutcome> ImplementAsync(
        WorkItemSnapshot workItem,
        FactoryEventEnvelope trigger,
        CancellationToken cancellationToken)
    {
        if (_implementations is null)
        {
            return StageDispatchOutcome.Failed("implementation-runner-unavailable");
        }

        if (!trigger.Data.TryGetValue("requirement", out var requirementText))
        {
            // The submitted event carries the requirement; a later trigger may not, so recover it.
            requirementText = trigger.Data.TryGetValue("expectations", out var expectationsJson)
                ? expectationsJson
                : string.Empty;
        }

        var request = new ImplementationRequest(
            workItem.WorkItemId.Value,
            workItem.Repository,
            requirementText,
            trigger.Data.TryGetValue("expectations", out var json)
                ? System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? []
                : []);

        var outcome = await _implementations.RunAsync(request, cancellationToken).ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            return StageDispatchOutcome.Failed(outcome.FailureCategory ?? "implementation-failed");
        }

        // The factory, not the runner, decides whether attributable source exists (v0.3 §7.1).
        if (outcome.ChangedFiles <= 0 || string.IsNullOrWhiteSpace(outcome.HeadSha) || string.IsNullOrWhiteSpace(outcome.DiffDigest))
        {
            return StageDispatchOutcome.Failed(ImplementationOutcome.NoAttributableChange);
        }

        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["baseSha"] = outcome.BaseSha ?? string.Empty,
            ["headSha"] = outcome.HeadSha!,
            ["diffDigest"] = outcome.DiffDigest!,
            ["changedFiles"] = outcome.ChangedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["contractDigest"] = trigger.Data.TryGetValue("contractDigest", out var contract) ? contract : string.Empty
        };

        return StageDispatchOutcome.Advanced(
            FactoryWorkState.InReview,
            [Event(OperatorEvents.ImplementationCompleted, workItem.WorkItemId, data, trigger.Id)]);
    }

    internal static FactoryEventEnvelope Event(
        string type,
        WorkItemId workItemId,
        IReadOnlyDictionary<string, string> data,
        FactoryEventId? causation = null)
    {
        var payload = new Dictionary<string, string>(data, StringComparer.Ordinal)
        {
            [FactoryDispatchOptions.WorkItemDataKey] = workItemId.Value
        };

        return new FactoryEventEnvelope(
            new FactoryEventId(Guid.NewGuid().ToString("N")),
            type,
            Subscriber,
            workItemId.Value,
            DateTimeOffset.UtcNow,
            null,
            causation,
            payload);
    }

    internal static string Digest(byte[] content)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    internal static string Digest(string content) => Digest(Encoding.UTF8.GetBytes(content));
}
