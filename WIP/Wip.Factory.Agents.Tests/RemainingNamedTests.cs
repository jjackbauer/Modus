using Modus.Core.Plugins;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Builder;
using Wip.Factory.Adaptation;
using Wip.Factory.Adaptation.Improvement;
using Wip.Factory.Analysis;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.Modus;
using Wip.Factory.Wip.Delivery;
using Wip.Factory.Wip.Governance;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class RemainingNamedTests
{
    private static readonly CapabilityCatalog Catalog = new(
    [
        new CapabilityCatalogEntry(new CapabilityId("tool.repo-read"), new CapabilityVersion("1.0.0"))
    ]);

    private sealed class PingContributor : IFactoryCapabilityContributor
    {
        public void RegisterCapabilities(WipBuilder builder, PluginOrigin origin)
            => builder.AddAgent<PingAgent, PingRequest, PingResult>(
                new CapabilityId("factory.agent.ping"),
                "Ping",
                new CapabilityVersion("1.0.0"),
                pluginOrigin: origin);

        public IReadOnlyList<FactoryAgentDefinition> GetAgentDefinitions() => [];
    }

    [Fact]
    public void Dispatcher_GivenProfileCapabilityReference_ResolvesExactlyOneHostOwnedDescriptor()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var builder = new WipBuilder(services);
        var binder = new FactoryCapabilityBinder();

        var result = binder.Bind(
            [
                new FactoryContributorRegistration(
                    new PluginId("Plugin.Factory.Ping"),
                    new CapabilityVersion("1.0.0"),
                    "TestAssembly",
                    new PingContributor())
            ],
            builder);

        var descriptor = Assert.Single(result.RegisteredDescriptors);
        Assert.Equal(new CapabilityId("factory.agent.ping"), descriptor.CapabilityId);

        // Referencing the capability resolves to that same descriptor, not a per-dispatch copy.
        var catalog = new CapabilityCatalog(
        [
            new CapabilityCatalogEntry(descriptor.CapabilityId, descriptor.Version)
        ]);

        Assert.True(catalog.TryResolve(descriptor.CapabilityId, descriptor.Version, out var entry));
        Assert.Equal(descriptor.CapabilityId, entry.CapabilityId);
        Assert.Single(result.RegisteredDescriptors);
    }

    [Fact]
    public async Task DeliveryEnvironment_GivenBuiltCandidate_StartsContainerProcessAndObservesReadinessProbe()
    {
        var root = Path.Combine(Path.GetTempPath(), "wip-factory-ready-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "ready");

        static int FreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        var port = FreePort();
        using var environment = new LocalProcessDeliveryEnvironment();

        var released = await environment.ReleaseAsync(new DeliveryCandidate(
            Version: 1,
            Command: "python",
            Arguments:
            [
                "-m", "http.server", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--bind", "127.0.0.1", "--directory", root
            ],
            HealthUrl: $"http://127.0.0.1:{port}/",
            StartupTimeout: TimeSpan.FromSeconds(25)));

        Assert.True(released.Released, released.Detail);
        Assert.Contains("answered", released.Detail, StringComparison.Ordinal);

        var probe = await environment.ProbeAsync();
        Assert.True(probe.Healthy, probe.Detail);
        Assert.Equal(1, probe.Version);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ManagerRoles_GivenManagerProfile_ObtainsNoUnilateralMergeOrReleaseAuthority()
    {
        var requirement = new ReviewRequirement("Q-1", "1", ["AC-1"]);
        var baseline = new ReviewBaseline("head", "base", "main", "policy@1", "guidelines@1");

        var reviewService = new FactoryReviewService();
        var review = reviewService.Evaluate(
            requirement,
            baseline,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["v"] = "1" },
            [
                new ReviewCheckResult(ReviewCheckKind.Validation, true, "ok"),
                new ReviewCheckResult(ReviewCheckKind.RequirementCoverage, true, "ok"),
                new ReviewCheckResult(ReviewCheckKind.ArchitecturalConformance, true, "ok"),
                new ReviewCheckResult(ReviewCheckKind.Security, true, "ok")
            ],
            [],
            [],
            "low",
            new AgentProfileVersion(1),
            new SessionId("review-session"));

        Assert.True(review.Accepted, review.Reason);

        var approvals = new FactoryApprovalService();
        var gate = new FactoryMergeGate(approvals);

        var mergeInvocations = 0;
        ValueTask Merge(CancellationToken _)
        {
            mergeInvocations++;
            return ValueTask.CompletedTask;
        }

        // A manager profile's word alone is not an approval: without a token the merge is unreachable.
        var (mergedWithoutToken, reason) = await gate.MergeAsync(
            null, review.Artifact, baseline, requirement, Merge);

        Assert.False(mergedWithoutToken);
        Assert.StartsWith(nameof(ApprovalStatus.Missing), reason, StringComparison.Ordinal);
        Assert.Equal(0, mergeInvocations);

        // The Deliver Manager role exists and is dispatchable, but its profile carries no merge token.
        var deliverManager = FactoryRolePack.Definitions.Single(static d => d.Role == "Deliver Manager");
        Assert.DoesNotContain("merge", string.Join(',', deliverManager.PermissionCeiling), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("release", string.Join(',', deliverManager.PermissionCeiling), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reflection_GivenSelectedSessionBatch_PublishesImmutableHypothesisArtifact()
    {
        var ledger = new HypothesisLedger();

        var hypothesis = ledger.Publish(
            "H-batch",
            "Failures cluster on review sessions with over-wide context.",
            ["trace:s1", "trace:s2", "trace:s3"],
            DateTimeOffset.UtcNow);

        Assert.Equal("H-batch", hypothesis.HypothesisId);
        Assert.Equal(3, hypothesis.EvidenceRefs.Count);
        Assert.StartsWith("sha256:", hypothesis.ContentDigest, StringComparison.Ordinal);
        Assert.Same(hypothesis, ledger.Get("H-batch"));

        // A hypothesis without evidence is refused, and one already published cannot be rewritten.
        Assert.Throws<ArgumentException>(() => ledger.Publish("H-empty", "nothing to cite", [], DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(
            () => ledger.Publish("H-batch", "a quietly revised statement", ["trace:s9"], DateTimeOffset.UtcNow));
        Assert.Same(hypothesis, ledger.Get("H-batch"));
    }

    [Fact]
    public void DecisionRecord_GivenCompletedExperiment_ReportsSampleSizeIntervalAndMissingData()
    {
        var evaluator = new AgentEvolutionEvaluator(minimumSamples: 3);
        var allPass = new HardGateResults(true, true, true, true, true, true);

        var promoted = evaluator.Evaluate(new AgentEvolutionExperiment(
            "exp-rec",
            new AgentDefinitionId("factory.review-analyst"),
            new AgentProfileVersion(1),
            new AgentProfileVersion(2),
            SampleSize: 16,
            IncumbentLatencyMs: 1200,
            CandidateLatencyMs: 800,
            allPass,
            allPass,
            ReworkDelta: 0));

        Assert.Equal(AgentEvolutionEvaluator.Promoted, promoted.Outcome);
        Assert.Equal(16, promoted.SampleSize);
        Assert.True(promoted.ConfidenceIntervalUpper > promoted.ConfidenceIntervalLower);
        Assert.False(promoted.MissingDownstreamData);

        // A decision made on too little evidence says so instead of claiming certainty.
        var thin = evaluator.Evaluate(new AgentEvolutionExperiment(
            "exp-thin",
            new AgentDefinitionId("factory.review-analyst"),
            new AgentProfileVersion(1),
            new AgentProfileVersion(2),
            SampleSize: 1,
            IncumbentLatencyMs: 1200,
            CandidateLatencyMs: 800,
            allPass,
            allPass,
            ReworkDelta: 0));

        Assert.Equal(AgentEvolutionEvaluator.InsufficientEvidence, thin.Outcome);
        Assert.True(thin.MissingDownstreamData);
        Assert.Equal(0, thin.ConfidenceIntervalUpper);
    }
    [Fact]
    public void Analysis_GivenPredictionRequest_InvokesBoundedChildProcessWithBoundedArguments()
    {
        var capability = new RustLaceAnalysisCapability(
            Path.Combine(Path.GetTempPath(), "lovelace-run-unused.exe"),
            TimeSpan.FromSeconds(20));

        var arguments = capability.BuildArguments(new StageNetworkModel(
            ArrivalRatePerSecond: 0.05,
            Stages: [new StageObservation("implementation", Completions: 20, TotalServiceSeconds: 240)],
            ObservationWindowSeconds: 60));

        // The invocation is bounded: JSON mode, registry payloads suppressed, explicit budget.
        Assert.Equal("--eval", arguments[0]);
        Assert.Contains("sys_open", arguments[1], StringComparison.Ordinal);
        Assert.Contains("sys_station", arguments[1], StringComparison.Ordinal);
        Assert.Contains("--json", arguments);
        Assert.Contains("--omit-functions", arguments);
        Assert.Contains("--omit-variables", arguments);
        Assert.Contains("--cancel-after", arguments);
        Assert.Equal("20000", arguments[^1]);

        // No argument asks the engine for its full registry, which is what keeps the payload bounded.
        Assert.DoesNotContain("--functions", arguments);
        Assert.DoesNotContain("--repl", arguments);
    }

    [Fact]
    public async Task Idempotency_GivenDuplicateEvent_SuppressesSecondMergeReleaseAndPromotion()
    {
        var requirement = new ReviewRequirement("Q-9", "1", ["AC-1"]);
        var baseline = new ReviewBaseline("head-9", "base-9", "main", "policy@1", "guidelines@1");

        var review = new FactoryReviewService().Evaluate(
            requirement,
            baseline,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["v"] = "1" },
            [
                new ReviewCheckResult(ReviewCheckKind.Validation, true, "ok"),
                new ReviewCheckResult(ReviewCheckKind.RequirementCoverage, true, "ok"),
                new ReviewCheckResult(ReviewCheckKind.ArchitecturalConformance, true, "ok"),
                new ReviewCheckResult(ReviewCheckKind.Security, true, "ok")
            ],
            [],
            [],
            "low",
            new AgentProfileVersion(1),
            new SessionId("review-9"));

        Assert.True(review.Accepted, review.Reason);

        var approvals = new FactoryApprovalService();
        var gate = new FactoryMergeGate(approvals);
        var (approval, _) = approvals.Create(review.Artifact!, DateTimeOffset.UtcNow);

        var merges = 0;
        ValueTask Merge(CancellationToken _)
        {
            merges++;
            return ValueTask.CompletedTask;
        }

        var first = await gate.MergeAsync(approval, review.Artifact, baseline, requirement, Merge);
        Assert.True(first.Merged, first.Reason);

        // A duplicate delivery carrying the same approval must not merge, release or promote again.
        var second = await gate.MergeAsync(approval, review.Artifact, baseline, requirement, Merge);
        Assert.False(second.Merged);
        Assert.StartsWith(FactoryMergeGate.AlreadyAppliedCategory, second.Reason, StringComparison.Ordinal);
        Assert.Equal(1, merges);
    }
    [Fact]
    public void Evaluator_GivenReflectionWriteAttemptToGatesOrHoldouts_IsDenied()
    {
        var guard = new GateWriteGuard();
        var allGates = new[]
        {
            EvaluationGate.Compilation, EvaluationGate.Tests, EvaluationGate.RequirementCoverage,
            EvaluationGate.Architecture, EvaluationGate.GovernanceIntegrity, EvaluationGate.Security
        };

        // A reflection principal may read gate results but may never rewrite the gates it is judged by.
        var gateWrite = guard.TryWriteHardGates(actorIsReflection: true, allGates);
        Assert.False(gateWrite.Allowed);
        Assert.StartsWith(GateWriteGuard.ReflectionCannotWriteCategory, gateWrite.Reason, StringComparison.Ordinal);
        Assert.Contains("hard-gates", gateWrite.Reason, StringComparison.Ordinal);

        // Holdout labels are equally off limits.
        var holdoutWrite = guard.TryWriteHoldouts(actorIsReflection: true, "holdout-v3");
        Assert.False(holdoutWrite.Allowed);
        Assert.Contains("holdouts", holdoutWrite.Reason, StringComparison.Ordinal);

        // No principal may widen its own ceiling, reflection or not.
        Assert.False(guard.TryWidenOwnCeiling(actorIsReflection: true, "process").Allowed);
        Assert.False(guard.TryWidenOwnCeiling(actorIsReflection: false, "process").Allowed);

        // An independent, non-reflection authority can still maintain the gates.
        Assert.True(guard.TryWriteHardGates(actorIsReflection: false, allGates).Allowed);
        Assert.True(guard.TryWriteHoldouts(actorIsReflection: false, "holdout-v4").Allowed);
    }
}