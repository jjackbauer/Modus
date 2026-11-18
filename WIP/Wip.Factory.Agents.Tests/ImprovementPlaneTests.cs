using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Factory.Adaptation;
using Wip.Factory.Adaptation.Improvement;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Contracts.Profiles;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class ImprovementPlaneTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wip-factory-improvement-tests", Guid.NewGuid().ToString("N"));

    private static readonly CapabilityCatalog Catalog = new(
    [
        new CapabilityCatalogEntry(new CapabilityId("tool.repo-read"), new CapabilityVersion("1.0.0"))
    ]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private LocalAgentProfileRegistry Registry() => new(_root, Catalog, FactoryRolePack.Definitions);

    private static readonly HardGateResults AllGatesPass = new(true, true, true, true, true, true);

    [Fact]
    public void ReflectionPolicy_GivenAnomalyWithinCooldown_SuppressesDuplicateReflection()
    {
        var policy = new AgentReflectionPolicy(minimumSamples: 3, cooldown: TimeSpan.FromHours(6));
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

        // Below the minimum sample size nothing runs, however dramatic the metric.
        Assert.Equal(
            AgentReflectionPolicy.InsufficientSamples,
            policy.Decide(new ReflectionSignal("failure-rate-increased", now, SampleSize: 2), now).Reason);

        var first = policy.Decide(new ReflectionSignal("failure-rate-increased", now, SampleSize: 8), now);
        Assert.True(first.ShouldReflect);

        // Inside the cooldown a second anomaly does not open another reflection.
        var tooSoon = policy.Decide(
            new ReflectionSignal("downstream-regression", now.AddHours(1), SampleSize: 9),
            now.AddHours(1));
        Assert.False(tooSoon.ShouldReflect);
        Assert.Equal(AgentReflectionPolicy.CooldownActive, tooSoon.Reason);

        // Past both the cooldown and the per-window budget a genuinely new observation can.
        var later = now.AddHours(25);
        var afterWindow = policy.Decide(new ReflectionSignal("downstream-regression", later, SampleSize: 9), later);
        Assert.True(afterWindow.ShouldReflect, afterWindow.Reason);
    }

    [Fact]
    public void Candidate_GivenHypothesis_CreatesNewVersionWithoutTouchingActivePointer()
    {
        var registry = Registry();
        var ledger = new HypothesisLedger();
        var baseline = FactoryRolePack.Profiles[0];

        var published = registry.PublishAsync(baseline).AsTask().GetAwaiter().GetResult();
        Assert.True(published.Published, published.Reason);
        Assert.True(registry.PromoteAsync(baseline.DefinitionId, baseline.Version).AsTask().GetAwaiter().GetResult());

        // A hypothesis must name its evidence and is immutable once published.
        var hypothesis = ledger.Publish(
            "H-1",
            "Repeated test failures come from an over-broad context selection.",
            ["trace:session-1", "trace:session-2"],
            DateTimeOffset.UtcNow);
        Assert.StartsWith("sha256:", hypothesis.ContentDigest, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => ledger.Publish(
            "H-1",
            "a quietly rewritten hypothesis",
            ["trace:session-3"],
            DateTimeOffset.UtcNow));

        // Creating the candidate version does not move the pointer.
        var candidate = baseline with
        {
            Version = new AgentProfileVersion(2),
            ParentVersion = new AgentProfileVersion(1),
            ChangeReason = "H-1: narrow context selection"
        };

        var candidatePublish = registry.PublishAsync(candidate).AsTask().GetAwaiter().GetResult();
        Assert.True(candidatePublish.Published, candidatePublish.Reason);

        var stillActive = registry.ResolveActiveAsync(baseline.DefinitionId).AsTask().GetAwaiter().GetResult();
        Assert.Equal(1, stillActive!.Version.Value);
    }

    [Fact]
    public void Evaluator_GivenHardGateRegression_RejectsFasterCandidate()
    {
        var evaluator = new AgentEvolutionEvaluator(minimumSamples: 3);
        var definition = new AgentDefinitionId("factory.requirements-analyst");

        // Faster, but security regressed: the trade is refused.
        var regressing = new AgentEvolutionExperiment(
            "exp-1",
            definition,
            new AgentProfileVersion(1),
            new AgentProfileVersion(2),
            SampleSize: 10,
            IncumbentLatencyMs: 900,
            CandidateLatencyMs: 400,
            AllGatesPass,
            AllGatesPass with { Security = false },
            ReworkDelta: 0);

        var rejected = evaluator.Evaluate(regressing);
        Assert.Equal(AgentEvolutionEvaluator.HardGateRegression, rejected.Outcome);
        Assert.Contains("security", rejected.Reason, StringComparison.Ordinal);

        // Faster but more rework: also refused, even with every gate green.
        var reworky = regressing with { CandidateGates = AllGatesPass, ReworkDelta = 0.4 };
        Assert.Equal(AgentEvolutionEvaluator.ReworkRegression, evaluator.Evaluate(reworky).Outcome);

        // Too small a sample cannot justify promotion.
        var thin = regressing with { CandidateGates = AllGatesPass, ReworkDelta = 0, SampleSize = 2 };
        Assert.Equal(AgentEvolutionEvaluator.InsufficientEvidence, evaluator.Evaluate(thin).Outcome);
        Assert.True(evaluator.Evaluate(thin).MissingDownstreamData);

        // Faster with gates intact and no rework regression promotes, and reports its interval.
        var good = regressing with { CandidateGates = AllGatesPass, ReworkDelta = -0.1 };
        var promoted = evaluator.Evaluate(good);
        Assert.Equal(AgentEvolutionEvaluator.Promoted, promoted.Outcome);
        Assert.Equal(10, promoted.SampleSize);
        Assert.True(promoted.ConfidenceIntervalUpper > promoted.ConfidenceIntervalLower);
        Assert.False(promoted.MissingDownstreamData);
    }

    [Fact]
    public async Task Promotion_GivenPromotedCandidateThenRollback_RestoresPointerAndRetainsHistory()
    {
        var registry = Registry();
        var baseline = FactoryRolePack.Profiles[0];
        await registry.PublishAsync(baseline);
        await registry.PromoteAsync(baseline.DefinitionId, baseline.Version);

        var candidate = baseline with
        {
            Version = new AgentProfileVersion(2),
            ParentVersion = new AgentProfileVersion(1),
            ChangeReason = "measured improvement"
        };
        Assert.True((await registry.PublishAsync(candidate)).Published);

        Assert.True(await registry.PromoteAsync(baseline.DefinitionId, new AgentProfileVersion(2)));
        Assert.Equal(2, (await registry.ResolveActiveAsync(baseline.DefinitionId))!.Version.Value);

        Assert.True(await registry.RollBackAsync(baseline.DefinitionId));
        Assert.Equal(1, (await registry.ResolveActiveAsync(baseline.DefinitionId))!.Version.Value);

        // History is retained: the rolled-back version is still readable.
        var rolledBack = await registry.GetAsync(baseline.AgentProfileId, new AgentProfileVersion(2));
        Assert.NotNull(rolledBack);
        Assert.Equal("measured improvement", rolledBack!.Draft.ChangeReason);
    }

    [Fact]
    public async Task PermissionEscalation_GivenCandidateRequestingWiderCeiling_IsDeniedByIndependentEvaluator()
    {
        var registry = Registry();
        var baseline = FactoryRolePack.Profiles[0];
        await registry.PublishAsync(baseline);
        await registry.PromoteAsync(baseline.DefinitionId, baseline.Version);

        var definition = FactoryRolePack.Definitions[0];
        Assert.DoesNotContain(PermissionCatalog.Process, definition.PermissionCeiling);

        // The candidate tries to widen its own authority.
        var escalating = baseline with
        {
            Version = new AgentProfileVersion(2),
            ParentVersion = new AgentProfileVersion(1),
            ChangeReason = "need process access",
            Permissions = new AgentProfilePermissions(
                [.. baseline.Permissions.Requested, PermissionCatalog.Process],
                baseline.Permissions.PolicyRef)
        };

        var attempt = await registry.PublishAsync(escalating);
        Assert.False(attempt.Published);
        Assert.Equal(ProfilePublishRejection.PermissionOutsideCeiling, attempt.Rejection);
        Assert.Contains(PermissionCatalog.Process, attempt.Reason, StringComparison.Ordinal);

        // The active pointer is untouched and the candidate version does not exist.
        Assert.Equal(1, (await registry.ResolveActiveAsync(baseline.DefinitionId))!.Version.Value);
        Assert.Null(await registry.GetAsync(baseline.AgentProfileId, new AgentProfileVersion(2)));
    }
}
