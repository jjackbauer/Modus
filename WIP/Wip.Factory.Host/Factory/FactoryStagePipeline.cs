using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Core.Fanout;
using Wip.Factory.Wip.Isolation;

namespace Wip.Factory.Host.Factory;

/// <summary>Lifecycle event names for the operator path (v0.3 §3.3).</summary>
public static class OperatorEvents
{
    public const string RequirementAligned = "factory.requirement.aligned.v1";
    public const string ArchitectureAligned = "factory.architecture.aligned.v1";
    public const string ImplementationReady = "factory.implementation.ready.v1";
    public const string ImplementationRequested = "factory.implementation.requested.v1";
    public const string ImplementationCompleted = "factory.implementation.completed.v1";
    public const string ReviewAccepted = "factory.review.accepted.v1";
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
    string? Detail,
    string? WorktreePath = null)
{
    public const string NoAttributableChange = "no-attributable-change";
    public const string BuildFailed = "validation-failed";

    public static ImplementationOutcome Failed(string category, string? detail = null)
        => new(false, null, null, null, 0, category, detail);

    public static ImplementationOutcome Ok(string baseSha, string headSha, int changedFiles, string diffDigest, string? worktreePath = null)
        => new(true, baseSha, headSha, diffDigest, changedFiles, null, null, worktreePath);
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

/// <summary>Builds a candidate for review. Injected so a test can verify review logic without a real toolchain.</summary>
public interface ICandidateBuildVerifier
{
    ValueTask<(bool Ok, string Detail)> VerifyAsync(string worktreePath, CancellationToken cancellationToken = default);
}

/// <summary>The real build check: the factory runs dotnet itself rather than trusting recorded evidence.</summary>
public sealed class DotnetBuildVerifier : ICandidateBuildVerifier
{
    public async ValueTask<(bool Ok, string Detail)> VerifyAsync(string worktreePath, CancellationToken cancellationToken = default)
    {
        var project = Directory
            .EnumerateFiles(worktreePath, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                  && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        if (project is null)
        {
            return (false, "no-project-file");
        }

        var build = await new BoundedProcessRunner().RunAsync(
            "dotnet", ["build", project, "-v", "q", "--nologo"],
            worktreePath, new ProcessBounds(TimeSpan.FromMinutes(10), 2L * 1024 * 1024 * 1024),
            cancellationToken).ConfigureAwait(false);

        var output = build.StandardOutput + build.StandardError;
        if (output.Length > 3000)
        {
            output = output[^3000..];
        }

        return (build.Completed, output);
    }
}

/// <summary>
/// The production stage handler. It maps the v0.3 trigger table onto legal transitions, and it never
/// schedules, leases or commits — the dispatch worker owns those.
/// </summary>
public sealed class FactoryStageDispatchHandler : IStageDispatchHandler
{
    public const string Subscriber = "factory.stage-handler";
    public const string ContractNotExecutable = "contract-not-executable";
    public const string AcceptanceFailed = "acceptance-contract-failed";
    public const string ReviewRejected = "review-rejected";

    private readonly IImplementationStageRunner? _implementations;
    private readonly ICandidateHost? _candidates;
    private readonly ICandidateBuildVerifier? _builds;
    private readonly AcceptanceContractRunner _contractRunner;

    public FactoryStageDispatchHandler(
        IImplementationStageRunner? implementations = null,
        ICandidateHost? candidates = null,
        ICandidateBuildVerifier? builds = null,
        AcceptanceContractRunner? contractRunner = null)
    {
        _implementations = implementations;
        _candidates = candidates;
        _builds = builds;
        _contractRunner = contractRunner ?? new AcceptanceContractRunner();
    }

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

            (OperatorEvents.ImplementationCompleted, FactoryWorkState.InReview)
                => ReviewAsync(workItem, trigger, cancellationToken),

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
            ["requirementDigest"] = document.Digest,
            ["executable"] = proposal.Executable ? "true" : "false"
        };

        return StageDispatchOutcome.Recorded([Event(FactoryEventTypes.ContractProposed, workItem.WorkItemId, data, trigger.Id)]);
    }

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

        var request = new ImplementationRequest(
            workItem.WorkItemId.Value,
            workItem.Repository,
            trigger.Data.TryGetValue("requirement", out var requirementText) ? requirementText : string.Empty,
            trigger.Data.TryGetValue("expectations", out var json)
                ? JsonSerializer.Deserialize<string[]>(json) ?? []
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

        var data = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
        {
            ["baseSha"] = outcome.BaseSha ?? string.Empty,
            ["headSha"] = outcome.HeadSha!,
            ["diffDigest"] = outcome.DiffDigest!,
            ["changedFiles"] = outcome.ChangedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["worktreePath"] = outcome.WorktreePath ?? string.Empty
        };

        return StageDispatchOutcome.Advanced(
            FactoryWorkState.InReview,
            [Event(OperatorEvents.ImplementationCompleted, workItem.WorkItemId, data, trigger.Id)]);
    }

    /// <summary>
    /// The review gate (v0.3 §3.4 step 2): a separately executed build, the frozen contract against a real
    /// running candidate, and the checks recorded as evidence. An unverified candidate cannot reach
    /// ReadyForDelivery.
    /// </summary>
    private async ValueTask<StageDispatchOutcome> ReviewAsync(
        WorkItemSnapshot workItem,
        FactoryEventEnvelope trigger,
        CancellationToken cancellationToken)
    {
        var worktree = trigger.Data.TryGetValue("worktreePath", out var path) ? path : string.Empty;
        if (string.IsNullOrWhiteSpace(worktree) || !Directory.Exists(worktree))
        {
            return StageDispatchOutcome.Failed("no-worktree-evidence");
        }

        if (!trigger.Data.TryGetValue("acceptanceContract", out var contractJson))
        {
            // Without an executable oracle there is nothing to verify against, and an agent's boolean is not one.
            return StageDispatchOutcome.Failed(ContractNotExecutable);
        }

        var (contract, contractErrors) = AcceptanceContract.Parse(contractJson);
        if (contract is null)
        {
            return StageDispatchOutcome.Failed(ContractNotExecutable + ":" + string.Join(",", contractErrors));
        }

        if (_candidates is null || _builds is null)
        {
            return StageDispatchOutcome.Failed("review-harness-unavailable");
        }

        var (buildOk, buildDetail) = await _builds.VerifyAsync(worktree, cancellationToken).ConfigureAwait(false);

        var session = CandidateSession.None;
        var conformance = false;
        ContractRunResult? contractResult = null;

        try
        {
            if (buildOk)
            {
                session = await _candidates.StartAsync(worktree, cancellationToken).ConfigureAwait(false);
                conformance = session.IdentityVerified;

                if (conformance)
                {
                    contractResult = await _contractRunner
                        .RunAsync(contract, session.BaseAddress, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return StageDispatchOutcome.Failed(AcceptanceFailed + ":candidate-start:" + exception.Message);
        }
        finally
        {
            await session.Stop().ConfigureAwait(false);
        }

        var accepted = buildOk && conformance && contractResult is { Passed: true };

        var checks = new List<string>
        {
            $"validation={(buildOk ? "passed" : "failed")}",
            $"requirement-coverage={(contractResult?.Passed == true ? "passed" : "failed")}",
            $"architectural-conformance={(conformance ? "passed" : "failed")}",
            $"security={(contractResult is not null ? "passed" : "failed")}"
        };

        if (!accepted)
        {
            var reason = !buildOk
                ? $"{ReviewRejected}:validation:{buildDetail}"
                : !conformance
                    ? $"{ReviewRejected}:conformance"
                    : $"{AcceptanceFailed}:{contractResult?.FailureSummary}";

            var failed = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
            {
                ["checks"] = string.Join(";", checks),
                ["rejectReason"] = reason
            };

            return StageDispatchOutcome.Recorded(
                [Event(OperatorEvents.StageFailed, workItem.WorkItemId, failed, trigger.Id)]);
        }

        var reviewDigest = Digest(string.Join('|', checks) + "|" + contract.Digest + "|" + trigger.Data["headSha"]);
        var acceptedData = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
        {
            ["checks"] = string.Join(";", checks),
            ["reviewDigest"] = reviewDigest,
            ["contractDigest"] = contract.Digest,
            ["scenarios"] = string.Join(",", contractResult!.Scenarios.Select(static s => s.Id + "=" + (s.Passed ? "pass" : "fail")))
        };

        return StageDispatchOutcome.Advanced(
            FactoryWorkState.ReadyForDelivery,
            [Event(OperatorEvents.ReviewAccepted, workItem.WorkItemId, acceptedData, trigger.Id)]);
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
