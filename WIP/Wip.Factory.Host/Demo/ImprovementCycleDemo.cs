using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Factory.Adaptation;
using Wip.Factory.Adaptation.Improvement;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;

namespace Wip.Factory.Host.Demo;

/// <summary>
/// D6: one pass of the improvement plane. A reflection produces a hypothesis, a candidate profile is
/// published without moving the active pointer, an independent evaluation promotes it, a second
/// candidate that regresses a hard gate is rejected, and an attempted permission escalation is denied.
/// </summary>
public sealed class ImprovementCycleDemo
{
    private const int PromotionVersion = 2;
    private const int RejectedVersion = 3;
    private const int EscalationVersion = 4;

    private static readonly HardGateResults AllGatesPass = new(true, true, true, true, true, true);

    public async Task<int> RunAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var catalog = new CapabilityCatalog(
        [
            new CapabilityCatalogEntry(new CapabilityId("tool.repo-read"), new CapabilityVersion("1.0.0"))
        ]);

        var registry = new LocalAgentProfileRegistry(
            Path.Combine(outputDirectory, "registry"),
            catalog,
            FactoryRolePack.Definitions);

        var baseline = FactoryRolePack.Profiles[0];
        var published = await registry.PublishAsync(baseline, cancellationToken);
        if (!published.Published)
        {
            return Fail($"baseline publish failed: {published.Reason}");
        }

        await registry.PromoteAsync(baseline.DefinitionId, baseline.Version, cancellationToken);

        // 1. Reflection is authorised by the policy, not by a supervisor.
        var policy = new AgentReflectionPolicy(minimumSamples: 3, cooldown: TimeSpan.FromHours(6));
        var now = DateTimeOffset.UtcNow;
        var reflection = policy.Decide(
            new ReflectionSignal("failure-rate-increased", now, SampleSize: 12, MetricValue: 0.31),
            now);

        if (!reflection.ShouldReflect)
        {
            return Fail($"reflection refused: {reflection.Reason}");
        }

        await WriteAsync(outputDirectory, "01-reflection.json", reflection, cancellationToken);

        // 2. The hypothesis is immutable and must cite evidence.
        var ledger = new HypothesisLedger();
        var hypothesis = ledger.Publish(
            "H-1",
            "Repeated test failures come from an over-broad context selection.",
            ["trace:session-11", "trace:session-12"],
            now);

        await WriteAsync(outputDirectory, "02-hypothesis.json", hypothesis, cancellationToken);

        // 3. A candidate version is published; the active pointer must not move.
        var candidate = baseline with
        {
            Version = new AgentProfileVersion(PromotionVersion),
            ParentVersion = new AgentProfileVersion(baseline.Version.Value),
            ChangeReason = "H-1: narrow context selection"
        };

        var candidatePublish = await registry.PublishAsync(candidate, cancellationToken);
        if (!candidatePublish.Published)
        {
            return Fail($"candidate publish failed: {candidatePublish.Reason}");
        }

        var pointerAfterCandidate = (await registry.ResolveActiveAsync(baseline.DefinitionId, cancellationToken))!.Version.Value;
        if (pointerAfterCandidate != baseline.Version.Value)
        {
            return Fail($"active pointer moved on candidate creation: {pointerAfterCandidate}");
        }

        // 4. An independent evaluation promotes the clean candidate.
        var evaluator = new AgentEvolutionEvaluator(minimumSamples: 3);
        var experiment = new AgentEvolutionExperiment(
            "exp-1",
            baseline.DefinitionId,
            new AgentProfileVersion(baseline.Version.Value),
            new AgentProfileVersion(PromotionVersion),
            SampleSize: 12,
            IncumbentLatencyMs: 900,
            CandidateLatencyMs: 520,
            AllGatesPass,
            AllGatesPass,
            ReworkDelta: -0.05);

        var promotion = evaluator.Evaluate(experiment);
        await WriteAsync(outputDirectory, "03-decision-promotion.json", promotion, cancellationToken);

        if (promotion.Outcome != AgentEvolutionEvaluator.Promoted)
        {
            return Fail($"expected promotion, got {promotion.Outcome}");
        }

        await registry.PromoteAsync(baseline.DefinitionId, new AgentProfileVersion(PromotionVersion), cancellationToken);
        var pointerAfterPromotion = (await registry.ResolveActiveAsync(baseline.DefinitionId, cancellationToken))!.Version.Value;
        if (pointerAfterPromotion != PromotionVersion)
        {
            return Fail($"promotion did not take effect: pointer {pointerAfterPromotion}");
        }

        // 5. A second candidate is faster but regresses security: it must be rejected, and the pointer
        // must stay on the promoted version.
        var regressing = experiment with
        {
            ExperimentId = "exp-2",
            Candidate = new AgentProfileVersion(RejectedVersion),
            CandidateLatencyMs = 200,
            CandidateGates = AllGatesPass with { Security = false },
            ReworkDelta = 0
        };

        var rejection = evaluator.Evaluate(regressing);
        await WriteAsync(outputDirectory, "04-decision-rejection.json", rejection, cancellationToken);

        if (rejection.Outcome != AgentEvolutionEvaluator.HardGateRegression)
        {
            return Fail($"expected hard-gate rejection, got {rejection.Outcome}");
        }

        // 6. A candidate that tries to widen its own authority is denied by the ceiling.
        var escalating = baseline with
        {
            Version = new AgentProfileVersion(EscalationVersion),
            ParentVersion = new AgentProfileVersion(baseline.Version.Value),
            ChangeReason = "needs process access",
            Permissions = new AgentProfilePermissions(
                [.. baseline.Permissions.Requested, PermissionCatalog.Process],
                baseline.Permissions.PolicyRef)
        };

        var escalation = await registry.PublishAsync(escalating, cancellationToken);
        await WriteAsync(
            outputDirectory,
            "05-escalation-denied.json",
            new { Published = escalation.Published, Rejection = escalation.Rejection.ToString(), escalation.Reason },
            cancellationToken);

        if (escalation.Published)
        {
            return Fail("an escalation outside the definition ceiling was published");
        }

        var pointerAfterEscalation = (await registry.ResolveActiveAsync(baseline.DefinitionId, cancellationToken))!.Version.Value;
        if (pointerAfterEscalation != PromotionVersion)
        {
            return Fail($"active pointer changed: {pointerAfterEscalation}");
        }

        await WriteAsync(
            outputDirectory,
            "06-active-profile.json",
            new { activeVersion = pointerAfterEscalation, promotedExperiment = promotion.SampleSize },
            cancellationToken);

        Console.WriteLine($"reflection   : {reflection.Reason}");
        Console.WriteLine($"hypothesis   : {hypothesis.HypothesisId} digest={hypothesis.ContentDigest}");
        Console.WriteLine($"promotion    : outcome={promotion.Outcome} sample={promotion.SampleSize} ci=[{promotion.ConfidenceIntervalLower:F1},{promotion.ConfidenceIntervalUpper:F1}] missingData={promotion.MissingDownstreamData}");
        Console.WriteLine($"rejection    : outcome={rejection.Outcome} reason={rejection.Reason}");
        Console.WriteLine($"escalation   : published={escalation.Published} rejection={escalation.Rejection}");
        Console.WriteLine($"active       : v{pointerAfterEscalation}");
        Console.WriteLine($"evidence     : {Path.GetFullPath(outputDirectory)}");

        return 0;
    }

    private static int Fail(string reason)
    {
        Console.Error.WriteLine(reason);
        return 1;
    }

    private static async Task WriteAsync<T>(string directory, string fileName, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, fileName), json, cancellationToken);
    }
}
