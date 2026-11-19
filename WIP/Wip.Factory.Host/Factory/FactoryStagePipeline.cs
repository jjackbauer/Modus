using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Core.Fanout;
using Wip.Factory.Wip.Governance;
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
    public const string MergeApproved = "factory.merge.approved.v1";
    public const string DeliveryRequested = "factory.delivery.requested.v1";
    public const string CandidateReady = "factory.release.candidate-ready.v1";
    public const string ReleaseCompleted = "factory.release.completed.v1";
    public const string ReleaseObserved = "factory.release.observed.v1";
    public const string ReworkRequested = "factory.rework.requested.v1";
    public const string StageFailed = "factory.stage.failed.v1";
}

/// <summary>What the implementation stage is asked to do.</summary>
public sealed record ImplementationRequest(
    string WorkItemId,
    string RepositoryPath,
    string RequirementText,
    IReadOnlyList<string> Expectations,
    string? ReworkContext = null);

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

/// <summary>
/// The facts a merge approval is bound to (v0.3 §5.2). Delivery validation re-reads them from durable evidence so a
/// stale approval cannot authorise a merge after any of them changed.
/// </summary>
public sealed record MergeAuthority(
    string ReviewDigest,
    string HeadSha,
    string BaseSha,
    string ContractDigest,
    string PolicyVersion,
    string GuidelinesVersion,
    string ProfileVersion,
    string RunnerVersion,
    string TargetBranch = "main",
    string RequirementDigest = "",
    string EvidenceDigest = "",
    string ValidationEvidenceDigest = "",
    string CoverageEvidenceDigest = "",
    string ArchitectureEvidenceDigest = "",
    string SecurityEvidenceDigest = "",
    string TemplateProvenanceDigest = "");

/// <summary>The policy, guidelines, profile and runner versions an approval is bound to (v0.3 §5.2).</summary>
public static class FactoryPolicy
{
    public const string PolicyVersion = "policy://factory/mandatory@1";
    public const string GuidelinesVersion = "guidelines/system@1";
    public const string ProfileVersion = "profile/implementations-analyst@1";
    public const string RunnerVersion = "factory-runner@1";
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

    /// <summary>Bounded repair attempts per work item (v0.3 §3.5; default two).</summary>
    public const int ReworkBudget = 2;

    private readonly IImplementationStageRunner? _implementations;
    private readonly ICandidateHost? _candidates;
    private readonly ICandidateBuildVerifier? _builds;
    private readonly AcceptanceContractRunner _contractRunner;
    private readonly IMergeEngine? _merges;
    private readonly string _verificationRoot;
    private readonly Func<CancellationToken, Task<string?>>? _targetHead;
    private readonly Func<string, CancellationToken, Task<MergeAuthority?>>? _currentEvidence;
    private readonly FactoryOperationLedger? _operations;
    private readonly IRegressionTestRunner _regressions;
    private readonly FactoryReviewCheckSet? _reviewChecks;
    private readonly ReviewEvidenceStore _reviewEvidence;
    private readonly IReleaseVerifier? _releaseVerifier;
    private readonly ReleaseVerificationStore _releaseVerifications;

    public FactoryStageDispatchHandler(
        IImplementationStageRunner? implementations = null,
        ICandidateHost? candidates = null,
        ICandidateBuildVerifier? builds = null,
        AcceptanceContractRunner? contractRunner = null,
        IMergeEngine? merges = null,
        string? verificationRoot = null,
        Func<CancellationToken, Task<string?>>? targetHead = null,
        Func<string, CancellationToken, Task<MergeAuthority?>>? currentEvidence = null,
        FactoryOperationLedger? operations = null,
        IRegressionTestRunner? regressions = null,
        FactoryReviewCheckSet? reviewChecks = null,
        string? reviewEvidencePath = null,
        IReleaseVerifier? releaseVerifier = null,
        string? releaseVerificationPath = null)
    {
        _implementations = implementations;
        _candidates = candidates;
        _builds = builds;
        _contractRunner = contractRunner ?? new AcceptanceContractRunner();
        _merges = merges;
        _verificationRoot = verificationRoot ?? Path.Combine(Path.GetTempPath(), "wip-factory-verify");
        _targetHead = targetHead;
        _currentEvidence = currentEvidence;
        _operations = operations;
        _regressions = regressions ?? new DotnetRegressionTestRunner();
        _reviewChecks = reviewChecks;
        _reviewEvidence = new ReviewEvidenceStore(
            reviewEvidencePath ?? Path.Combine(_verificationRoot, "review-evidence.log"));
        _releaseVerifier = releaseVerifier;
        _releaseVerifications = new ReleaseVerificationStore(
            releaseVerificationPath ?? Path.Combine(_verificationRoot, "release-verifications.log"));
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

            (OperatorEvents.MergeApproved, FactoryWorkState.ReadyForDelivery)
                => ValueTask.FromResult(StartDeliveryValidation(workItem, trigger)),

            (OperatorEvents.DeliveryRequested, FactoryWorkState.DeliveryValidation)
                => ValidateDelivery(workItem, trigger),

            (OperatorEvents.CandidateReady, FactoryWorkState.ReleaseCandidate)
                => ReleaseAsync(workItem, trigger, cancellationToken),

            (OperatorEvents.ReleaseObserved, FactoryWorkState.Released)
                => ValueTask.FromResult(RecordObservation(workItem, trigger)),

            (OperatorEvents.ReworkRequested, FactoryWorkState.NeedsRework)
                => ValueTask.FromResult(StartRework(workItem, trigger)),

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
                : [],
            ReworkContext: trigger.Data.TryGetValue("reworkContext", out var rework) ? rework : null);

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

        var reviewedSha = trigger.Data.TryGetValue("headSha", out var reviewedHead) ? reviewedHead : string.Empty;
        if (string.IsNullOrWhiteSpace(reviewedSha))
        {
            return StageDispatchOutcome.Failed("review-evidence-missing:reviewed-sha");
        }

        var (contract, contractErrors) = AcceptanceContract.Parse(contractJson);
        if (contract is null)
        {
            return StageDispatchOutcome.Failed(ContractNotExecutable + ":" + string.Join(",", contractErrors));
        }

        if (trigger.Data.TryGetValue("acceptanceContractDigest", out var frozenContractDigest)
            && !string.IsNullOrWhiteSpace(frozenContractDigest)
            && !string.Equals(frozenContractDigest, contract.Digest, StringComparison.Ordinal))
        {
            return StageDispatchOutcome.Failed("contract-tampered");
        }

        if (_candidates is null || _builds is null)
        {
            return StageDispatchOutcome.Failed("review-harness-unavailable");
        }

        var baseSha = trigger.Data.TryGetValue("baseSha", out var baseValue) ? baseValue : string.Empty;
        var checks = _reviewChecks ?? FactoryReviewCheckSet.Default(_builds, _regressions, _candidates, _contractRunner);

        IReadOnlyList<ReviewCheckExecution> executions;
        try
        {
            executions = await checks.RunAsync(
                new ReviewCheckContext(workItem.WorkItemId.Value, worktree, reviewedSha, baseSha, contract),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return StageDispatchOutcome.Failed("review-check-runner-failed:" + exception.Message);
        }

        // Evidence is durable before the review decision is published (FAC-P0-02.1, FAC-P0-01.6).
        _reviewEvidence.Append(workItem.WorkItemId.Value, executions);

        var requirementDigest = trigger.Data.TryGetValue("requirementDigest", out var requirementValue)
            ? requirementValue
            : string.Empty;

        var requirement = new ReviewRequirement(
            workItem.WorkItemId.Value,
            requirementDigest,
            contract.Scenarios.Select(static scenario => scenario.Id).ToArray());

        var baseline = new ReviewBaseline(
            reviewedSha, baseSha, "main", FactoryPolicy.PolicyVersion, FactoryPolicy.GuidelinesVersion);

        var review = new FactoryReviewService().EvaluateWithEvidence(
            requirement,
            baseline,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["review-check-runner"] = ReviewPolicy.CheckRunnerVersion,
                ["validation"] = ReviewPolicy.ValidationPolicy,
                ["coverage"] = ReviewPolicy.CoveragePolicy,
                ["architecture"] = ReviewPolicy.ArchitecturePolicy,
                ["security"] = ReviewPolicy.SecurityPolicyVersion
            },
            executions,
            findings: [],
            exclusions: [],
            riskClassification: "alpha-standard",
            reviewerProfileVersion: new AgentProfileVersion(1),
            reviewerSessionId: new SessionId("review-" + workItem.WorkItemId.Value.ToLowerInvariant()),
            requiredHeadSha: reviewedSha,
            requiredContractDigest: contract.Digest);

        var summary = string.Join(
            ";",
            executions
                .OrderBy(static execution => execution.Kind)
                .Select(static execution => ReviewKindName(execution.Kind) + "=" + execution.Outcome));

        var reworkCount = trigger.Data.TryGetValue("reworkCount", out var reworkText)
            && int.TryParse(reworkText, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        if (!review.Accepted || review.Artifact is null)
        {
            // The operator-facing reason names the refusal category and the concrete failing observation, so a
            // rework context carries the counterexample rather than only "review failed".
            var failures = executions
                .Where(static execution => !execution.Passed)
                .OrderBy(static execution => execution.Kind)
                .Select(static execution => ReviewKindName(execution.Kind) + ":" + execution.Detail);

            var reason = $"{ReviewRejected}:{review.Reason}:{string.Join("|", failures)}";
            var failed = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
            {
                ["checks"] = summary,
                ["rejectReason"] = reason,
                ["reworkCount"] = (reworkCount + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["reworkContext"] = reason,
                ["evidenceDigest"] = ReviewEvidenceStore.AggregateDigest(executions)
            };

            if (reworkCount < ReworkBudget)
            {
                return StageDispatchOutcome.Advanced(
                    FactoryWorkState.NeedsRework,
                    [Event(OperatorEvents.ReworkRequested, workItem.WorkItemId, failed, trigger.Id)]);
            }

            // The budget is spent: take the legal NeedsRework -> Failed edge rather than retrying forever.
            return StageDispatchOutcome.Advanced(
                FactoryWorkState.Failed,
                [Event(OperatorEvents.StageFailed, workItem.WorkItemId, failed, trigger.Id)]);
        }

        var acceptedData = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
        {
            ["checks"] = summary,
            ["reviewDigest"] = review.Artifact.ContentDigest,
            ["evidenceDigest"] = ReviewEvidenceStore.AggregateDigest(executions),
            ["evidence"] = JsonSerializer.Serialize(executions),
            ["reviewedSha"] = reviewedSha,
            ["reviewPolicyVersion"] = ReviewPolicy.CheckRunnerVersion,
            ["contractDigest"] = contract.Digest,
            ["acceptanceContractDigest"] = contract.Digest,
            ["policyVersion"] = FactoryPolicy.PolicyVersion,
            ["guidelinesVersion"] = FactoryPolicy.GuidelinesVersion,
            ["profileVersion"] = FactoryPolicy.ProfileVersion,
            ["runnerVersion"] = FactoryPolicy.RunnerVersion
        };

        // Scenario evidence is carried by the requirement-coverage execution detail; keep the stable summary too.
        acceptedData["scenarios"] = ScenariosFromEvidence(executions);

        return StageDispatchOutcome.Advanced(
            FactoryWorkState.ReadyForDelivery,
            [Event(OperatorEvents.ReviewAccepted, workItem.WorkItemId, acceptedData, trigger.Id)]);
    }

    private static string ReviewKindName(ReviewCheckKind kind) => kind switch
    {
        ReviewCheckKind.Validation => "validation",
        ReviewCheckKind.RequirementCoverage => "requirement-coverage",
        ReviewCheckKind.ArchitecturalConformance => "architectural-conformance",
        ReviewCheckKind.Security => "security",
        _ => kind.ToString()
    };

    private static string ScenariosFromEvidence(IReadOnlyList<ReviewCheckExecution> executions)
    {
        var coverage = executions.FirstOrDefault(static execution =>
            execution.Kind == ReviewCheckKind.RequirementCoverage);

        if (coverage is null)
        {
            return string.Empty;
        }

        const string marker = "scenarios=";
        var index = coverage.Detail.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return string.Empty;
        }

        var rest = coverage.Detail[(index + marker.Length)..];
        var end = rest.IndexOf(';', StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }

    /// <summary>An approved candidate enters delivery validation; nothing is merged yet (v0.3 §3.4 step 5).</summary>
    private static StageDispatchOutcome StartDeliveryValidation(WorkItemSnapshot workItem, FactoryEventEnvelope trigger)
    {
        if (!trigger.Data.TryGetValue("reviewDigest", out var reviewDigest) || string.IsNullOrWhiteSpace(reviewDigest))
        {
            return StageDispatchOutcome.Failed("missing-merge-approval");
        }

        var data = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
        {
            ["approvalId"] = trigger.Id.Value,
            ["approvedAtUtc"] = trigger.TimeUtc.ToString("O")
        };

        return StageDispatchOutcome.Advanced(
            FactoryWorkState.DeliveryValidation,
            [Event(OperatorEvents.DeliveryRequested, workItem.WorkItemId, data, trigger.Id)]);
    }

    /// <summary>
    /// Pre-merge drift guard: the approval must still describe the repository's actual baseline. A moved target
    /// branch invalidates the approval instead of being silently rebased (v0.3 §3.4 conflict/drift).
    /// </summary>
    private async ValueTask<StageDispatchOutcome> ValidateDelivery(WorkItemSnapshot workItem, FactoryEventEnvelope trigger)
    {
        var boundBase = trigger.Data.TryGetValue("baseSha", out var baseSha) ? baseSha : string.Empty;
        var boundHead = trigger.Data.TryGetValue("headSha", out var headSha) ? headSha : string.Empty;

        if (string.IsNullOrWhiteSpace(boundHead))
        {
            return StageDispatchOutcome.Failed("approval-incomplete");
        }

        // The approval must still describe the evidence that exists now. Any changed bound fact makes it stale.
        if (_currentEvidence is not null)
        {
            var current = await _currentEvidence(workItem.WorkItemId.Value, CancellationToken.None).ConfigureAwait(false);
            if (current is null)
            {
                return StageDispatchOutcome.Failed("approval-stale:no-current-evidence");
            }

            var mismatches = new List<string>();
            static bool Differs(IReadOnlyDictionary<string, string> data, string key, string actual)
                => data.TryGetValue(key, out var recorded)
                   && !string.IsNullOrWhiteSpace(recorded)
                   && !string.Equals(recorded, actual, StringComparison.Ordinal);

            if (Differs(trigger.Data, "reviewDigest", current.ReviewDigest)) { mismatches.Add("reviewDigest"); }
            if (Differs(trigger.Data, "headSha", current.HeadSha)) { mismatches.Add("headSha"); }
            if (Differs(trigger.Data, "baseSha", current.BaseSha)) { mismatches.Add("baseSha"); }
            if (Differs(trigger.Data, "targetBranch", current.TargetBranch)) { mismatches.Add("targetBranch"); }
            if (Differs(trigger.Data, "requirementDigest", current.RequirementDigest)) { mismatches.Add("requirementDigest"); }
            if (Differs(trigger.Data, "contractDigest", current.ContractDigest)) { mismatches.Add("contractDigest"); }
            if (Differs(trigger.Data, "evidenceDigest", current.EvidenceDigest)) { mismatches.Add("evidenceDigest"); }
            if (Differs(trigger.Data, "validationEvidenceDigest", current.ValidationEvidenceDigest)) { mismatches.Add("validationEvidenceDigest"); }
            if (Differs(trigger.Data, "coverageEvidenceDigest", current.CoverageEvidenceDigest)) { mismatches.Add("coverageEvidenceDigest"); }
            if (Differs(trigger.Data, "architectureEvidenceDigest", current.ArchitectureEvidenceDigest)) { mismatches.Add("architectureEvidenceDigest"); }
            if (Differs(trigger.Data, "securityEvidenceDigest", current.SecurityEvidenceDigest)) { mismatches.Add("securityEvidenceDigest"); }
            if (Differs(trigger.Data, "templateProvenanceDigest", current.TemplateProvenanceDigest)) { mismatches.Add("templateProvenanceDigest"); }
            if (Differs(trigger.Data, "policyVersion", current.PolicyVersion)) { mismatches.Add("policyVersion"); }
            if (Differs(trigger.Data, "guidelinesVersion", current.GuidelinesVersion)) { mismatches.Add("guidelinesVersion"); }
            if (Differs(trigger.Data, "profileVersion", current.ProfileVersion)) { mismatches.Add("profileVersion"); }
            if (Differs(trigger.Data, "runnerVersion", current.RunnerVersion)) { mismatches.Add("runnerVersion"); }

            if (mismatches.Count > 0)
            {
                var stale = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
                {
                    ["reason"] = "approval-stale",
                    ["changedFields"] = string.Join(",", mismatches)
                };

                // D-08 A: a pre-merge failure takes the legal DeliveryValidation -> NeedsRework edge.
                return ReworkOrFail(workItem, stale, trigger, "target-drift");
            }
        }

        if (_targetHead is not null)
        {
            var current = await _targetHead(CancellationToken.None).ConfigureAwait(false);
            if (current is not null && !string.IsNullOrWhiteSpace(boundBase)
                && !string.Equals(current.Trim(), boundBase.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                var stale = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
                {
                    ["reason"] = "target-drift",
                    ["expectedBase"] = boundBase,
                    ["observedBase"] = current.Trim()
                };

                // D-08 A: a pre-merge failure takes the legal DeliveryValidation -> NeedsRework edge.
                return ReworkOrFail(workItem, stale, trigger, "target-drift");
            }
        }

        return StageDispatchOutcome.Advanced(
            FactoryWorkState.ReleaseCandidate,
            [Event(OperatorEvents.CandidateReady, workItem.WorkItemId, trigger.Data, trigger.Id)]);
    }

    /// <summary>
    /// Performs the authorized merge, then verifies the <b>merged revision</b> rather than the worktree (v0.3 §3.4
    /// step 7). Merge-applied and post-merge-verified are distinct durable facts: replaying an applied merge resumes
    /// verification instead of emitting a release (v0.4 FAC-P0-01).
    /// </summary>
    private async ValueTask<StageDispatchOutcome> ReleaseAsync(
        WorkItemSnapshot workItem,
        FactoryEventEnvelope trigger,
        CancellationToken cancellationToken)
    {
        if (_merges is null)
        {
            return StageDispatchOutcome.Failed("merge-engine-unavailable");
        }

        var headSha = trigger.Data.TryGetValue("headSha", out var head) ? head : string.Empty;
        if (string.IsNullOrWhiteSpace(headSha))
        {
            return StageDispatchOutcome.Failed("approval-incomplete");
        }

        var baseSha = trigger.Data.TryGetValue("baseSha", out var baseValue) ? baseValue : string.Empty;

        // The oracle is validated before any external effect: an unexecutable contract must not merge.
        if (!trigger.Data.TryGetValue("acceptanceContract", out var contractJson))
        {
            return StageDispatchOutcome.Failed(ContractNotExecutable);
        }

        var (contract, contractErrors) = AcceptanceContract.Parse(contractJson);
        if (contract is null)
        {
            return StageDispatchOutcome.Failed(ContractNotExecutable + ":" + string.Join(",", contractErrors));
        }

        if (trigger.Data.TryGetValue("acceptanceContractDigest", out var frozenContractDigest)
            && !string.IsNullOrWhiteSpace(frozenContractDigest)
            && !string.Equals(frozenContractDigest, contract.Digest, StringComparison.Ordinal))
        {
            return StageDispatchOutcome.Failed("contract-tampered");
        }

        if (_builds is null || _candidates is null)
        {
            return StageDispatchOutcome.Failed("release-verifier-unavailable");
        }

        // Durable operation record: the merge is one identified effect, so a retry reconciles instead of repeating.
        var operationId = _operations is null
            ? null
            : FactoryOperationLedger.DeriveOperationId(
                workItem.WorkItemId.Value, trigger.Id.Value, "release", trigger.Id.Value, 1, "git-merge", workItem.Repository, headSha);

        var applied = operationId is null ? null : _operations!.Latest(operationId);
        var mergeWasApplied = applied?.State == OperationState.Applied;
        string mergedSha;

        if (mergeWasApplied)
        {
            mergedSha = applied!.ExternalIdentity ?? string.Empty;
            if (string.IsNullOrWhiteSpace(mergedSha))
            {
                return StageDispatchOutcome.Failed("merge-reconciliation-failed:no-merged-sha");
            }

            // The ledger proves the effect was attempted; Git proves the revision is actually present. Both are needed.
            if (!await _merges.ContainsAsync(workItem.Repository, mergedSha, cancellationToken).ConfigureAwait(false))
            {
                return StageDispatchOutcome.Failed("merge-reconciliation-failed:git-does-not-contain-merged-sha");
            }
        }
        else
        {
            _operations?.Begin(operationId!, workItem.WorkItemId.Value, trigger.Id.Value, "release", trigger.Id.Value, 1,
                "git-merge", workItem.Repository, headSha);

            var merge = await _merges.MergeAsync(workItem.Repository, headSha, cancellationToken).ConfigureAwait(false);
            if (!merge.Ok || string.IsNullOrWhiteSpace(merge.MergedSha))
            {
                if (operationId is not null)
                {
                    _operations!.MarkFailed(operationId, merge.Reason ?? "merge-failed");
                }

                return StageDispatchOutcome.Failed(merge.Reason ?? "merge-failed");
            }

            mergedSha = merge.MergedSha!;
            if (operationId is not null)
            {
                _operations!.MarkApplied(operationId, mergedSha, "git:rev-parse");
            }
        }

        // Re-execute anything that is not a verified record for exactly this merged revision and contract.
        var verified = _releaseVerifications.Find(
            workItem.WorkItemId.Value, mergedSha, contract.Digest, headSha, baseSha);

        if (verified is not { Verified: true })
        {
            var verifier = _releaseVerifier ?? new PostMergeReleaseVerifier(
                _merges, _builds, _regressions, _candidates, _contractRunner, _verificationRoot);

            ReleaseVerificationResult verification;
            try
            {
                verification = await verifier.VerifyAsync(
                    new ReleaseVerificationRequest(
                        workItem.WorkItemId.Value,
                        workItem.Repository,
                        mergedSha,
                        headSha,
                        baseSha,
                        operationId ?? string.Empty,
                        contract),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return StageDispatchOutcome.Failed("post-merge-verification-failed:" + exception.Message);
            }

            verified = new ReleaseVerificationRecord(
                workItem.WorkItemId.Value,
                operationId ?? string.Empty,
                headSha,
                baseSha,
                mergedSha,
                verification.MergedTreeSha,
                contract.Digest,
                verification.BuildEvidenceDigest,
                verification.RegressionEvidenceDigest,
                verification.ContractEvidenceDigest,
                verification.ScenarioSummary,
                verification.VerifierVersion,
                verification.Ok ? ReleaseVerificationRecord.VerifiedOutcome : ReleaseVerificationRecord.FailedOutcome,
                verification.Detail,
                DateTimeOffset.UtcNow);

            // The verification fact is durable before the state/event commit that publishes Released.
            _releaseVerifications.Append(verified);

            if (operationId is not null)
            {
                if (verification.Ok)
                {
                    _operations!.MarkVerified(operationId, verified.RecordDigest, verification.Detail);
                }
                else
                {
                    _operations!.MarkVerificationFailed(operationId, verification.Detail);
                }
            }
        }

        if (!verified.Verified)
        {
            // A merged revision that fails is terminal: D-08 A's rework edge is pre-merge only.
            return StageDispatchOutcome.Failed("post-merge-verification-failed:" + verified.Detail);
        }

        var evidence = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
        {
            ["mergedSha"] = mergedSha,
            ["mergedTreeSha"] = verified.MergedTreeSha ?? string.Empty,
            ["postMergeDetail"] = verified.Detail,
            ["postMergeScenarios"] = verified.ScenarioSummary,
            ["buildEvidenceDigest"] = verified.BuildEvidenceDigest,
            ["regressionEvidenceDigest"] = verified.RegressionEvidenceDigest,
            ["contractEvidenceDigest"] = verified.ContractEvidenceDigest,
            ["verificationRecordDigest"] = verified.RecordDigest,
            ["verifierVersion"] = verified.VerifierVersion,
            ["verifiedAtUtc"] = verified.VerifiedAtUtc.ToString("O"),
            ["reconciled"] = mergeWasApplied ? "merge-already-applied" : "fresh-merge",
            ["operationId"] = operationId ?? string.Empty
        };

        return StageDispatchOutcome.Advanced(
            FactoryWorkState.Released,
            [Event(OperatorEvents.ReleaseCompleted, workItem.WorkItemId, evidence, trigger.Id)]);
    }

    /// <summary>
    /// A supervised release was observed serving the intended revision and passing the contract. This is the only
    /// way a work item becomes OperationallyValidated (v0.3 §0.3), so a merge alone never claims liveness.
    /// </summary>
    private static StageDispatchOutcome RecordObservation(WorkItemSnapshot workItem, FactoryEventEnvelope trigger)
    {
        if (!trigger.Data.TryGetValue("observedSha", out var observed) || string.IsNullOrWhiteSpace(observed))
        {
            return StageDispatchOutcome.Failed("serve-identity-unproven");
        }

        if (!trigger.Data.TryGetValue("expectedSha", out var expected)
            || !string.Equals(expected, observed, StringComparison.OrdinalIgnoreCase))
        {
            return StageDispatchOutcome.Failed("serve-identity-mismatch");
        }

        var data = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal)
        {
            ["validatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };

        return StageDispatchOutcome.Advanced(
            FactoryWorkState.OperationallyValidated,
            [Event("factory.operations.validated.v1", workItem.WorkItemId, data, trigger.Id)]);
    }

    /// <summary>Hands the rejection back to the implementer as a new attempt with the failure as context.</summary>
    private static StageDispatchOutcome StartRework(WorkItemSnapshot workItem, FactoryEventEnvelope trigger)
    {
        var data = new Dictionary<string, string>(trigger.Data, StringComparer.Ordinal);

        return StageDispatchOutcome.Advanced(
            FactoryWorkState.InImplementation,
            [Event(OperatorEvents.ImplementationRequested, workItem.WorkItemId, data, trigger.Id)]);
    }

    /// <summary>
    /// Pre-merge failure handling (v0.3 §3.5, D-08 A): spend a rework attempt on the legal
    /// DeliveryValidation -> NeedsRework edge, and only fall to Failed once the budget is gone.
    /// </summary>
    private static StageDispatchOutcome ReworkOrFail(
        WorkItemSnapshot workItem,
        Dictionary<string, string> data,
        FactoryEventEnvelope trigger,
        string reason)
    {
        var used = data.TryGetValue("reworkCount", out var text)
            && int.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

        data["reworkCount"] = (used + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        data["reworkContext"] = reason;

        return used < ReworkBudget
            ? StageDispatchOutcome.Advanced(FactoryWorkState.NeedsRework,
                [Event(OperatorEvents.ReworkRequested, workItem.WorkItemId, data, trigger.Id)])
            : StageDispatchOutcome.Advanced(FactoryWorkState.Failed,
                [Event(OperatorEvents.StageFailed, workItem.WorkItemId, data, trigger.Id)]);
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
