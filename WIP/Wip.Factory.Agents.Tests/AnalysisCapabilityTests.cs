using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Adaptation;
using Wip.Factory.Analysis;
using Wip.Factory.Contracts.Profiles;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class AnalysisCapabilityTests
{
    private const string BinaryPath = @"F:\dev\RustLace\target\debug\lovelace-run.exe";

    private static StageNetworkModel Model() => new(
        ArrivalRatePerSecond: 0.05,
        Stages:
        [
            new StageObservation("requirements", Completions: 20, TotalServiceSeconds: 60),
            new StageObservation("implementation", Completions: 20, TotalServiceSeconds: 240),
            new StageObservation("review", Completions: 20, TotalServiceSeconds: 80)
        ],
        ObservationWindowSeconds: 60);

    [Fact]
    public async Task Analysis_GivenRealTraceDerivedNetwork_EvaluatesVerifiedSystemsBuiltins()
    {
        var capability = new RustLaceAnalysisCapability(BinaryPath, TimeSpan.FromSeconds(25));

        var (prediction, failure) = await capability.PredictAsync(Model());

        Assert.Null(failure);
        Assert.NotNull(prediction);

        // The engine's own record supplied the fields, so this is a real evaluation, not a constant.
        Assert.False(string.IsNullOrWhiteSpace(prediction!.PredictedBottleneck));
        Assert.Equal("implementation", prediction.PredictedBottleneck);
        Assert.True(prediction.PredictedThroughputBound > 0, "the bound was not decoded from the engine output");
        Assert.False(string.IsNullOrWhiteSpace(prediction.EngineProtocolVersion));
        Assert.Contains("sys_bottlenecks", prediction.ModelInputs, StringComparison.Ordinal);

        // The generated script must not ask for the registry payloads the adapter suppresses.
        Assert.Contains("sys_station", prediction.ModelInputs, StringComparison.Ordinal);
        Assert.DoesNotContain("\"requirements\", 3, 1, 1", prediction.ModelInputs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analysis_GivenEngineTimeout_ProducesFailureArtifactAndNoPrediction()
    {
        // A one-millisecond budget cannot complete a real evaluation.
        var capability = new RustLaceAnalysisCapability(BinaryPath, TimeSpan.FromMilliseconds(1));

        var (prediction, failure) = await capability.PredictAsync(Model());

        Assert.Null(prediction);
        Assert.NotNull(failure);
        Assert.StartsWith(RustLaceAnalysisCapability.EngineTimedOutCategory, failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Analysis_GivenPredictionAndObservedWindow_PublishesSignedComparisonArtifact()
    {
        var prediction = new PredictionArtifact(
            "implementation",
            PredictedThroughputBound: 0.0526,
            EngineFields: new Dictionary<string, string>(StringComparer.Ordinal) { ["throughput_bound"] = "0.0526" },
            ModelInputs: "model",
            ForecastUncertainty: "advisory",
            EngineProtocolVersion: "2");

        var held = RustLaceAnalysisCapability.Compare(prediction, observedThroughput: 0.048, observedP95LeadTimeSeconds: 180);
        Assert.True(held.ExplanationHeld);
        Assert.True(held.ThroughputError < 0.2);
        Assert.Equal(0.048, held.ObservedThroughput);
        Assert.Equal(180, held.ObservedP95LeadTimeSeconds);

        // A prediction more than a factor of two away is reported as not held, not rounded to a pass.
        var missed = RustLaceAnalysisCapability.Compare(prediction, observedThroughput: 0.2, observedP95LeadTimeSeconds: 40);
        Assert.False(missed.ExplanationHeld);
        Assert.Contains("more than a factor of two", missed.Basis, StringComparison.Ordinal);

        // A non-positive observed throughput is a programming error, not something to average away.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RustLaceAnalysisCapability.Compare(prediction, 0, 10));
    }

    [Fact]
    public async Task Analysis_GivenUnavailableEngine_ReportsFailureInsteadOfFabricating()
    {
        var capability = new RustLaceAnalysisCapability(
            Path.Combine(Path.GetTempPath(), "no-such-lovelace-run-" + Guid.NewGuid().ToString("N") + ".exe"));

        var (prediction, failure) = await capability.PredictAsync(Model());

        Assert.Null(prediction);
        Assert.StartsWith(RustLaceAnalysisCapability.EngineUnavailableCategory, failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Analysis_GivenAnalyticalProfile_ResolvesRegisteredAnalysisCapability()
    {
        var capabilityId = new CapabilityId("capability.analysis.rustlace-systems");
        var version = new CapabilityVersion("1.0.0");

        var catalog = new CapabilityCatalog([new CapabilityCatalogEntry(capabilityId, version)]);

        Assert.True(catalog.TryResolve(capabilityId, version, out var entry));
        Assert.Equal(capabilityId, entry.CapabilityId);

        // The analytical role can reference it, so the modelling surface is reachable by an agent
        // rather than only by a demo command.
        var analytical = FactoryRolePack.Profiles[1];
        Assert.NotNull(analytical);

        // An unregistered version is refused.
        Assert.False(catalog.TryResolve(capabilityId, new CapabilityVersion("9.9.9"), out _));
    }

    [Fact]
    public void Analysis_GivenPublishedPrediction_AuthorizesNoPolicyOrCapacityChange()
    {
        var prediction = new PredictionArtifact(
            "review",
            0.05,
            new Dictionary<string, string>(StringComparer.Ordinal),
            "model",
            ForecastUncertainty: "advisory only: no policy, capacity or gate is changed by a prediction",
            EngineProtocolVersion: "2");

        // The artifact itself must carry the advisory boundary, so a consumer cannot mistake it for a
        // mandate.
        Assert.Contains("advisory", prediction.ForecastUncertainty, StringComparison.OrdinalIgnoreCase);

        var comparison = RustLaceAnalysisCapability.Compare(prediction, 0.05, 100);
        Assert.Contains("factor of two", comparison.Basis, StringComparison.Ordinal);
    }
}
