using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Adaptation;
using Wip.Factory.Analysis;
using Wip.Factory.Contracts.Profiles;

namespace Wip.Factory.Host.Demo;

/// <summary>
/// D7: a RustLace prediction compared against observed traces. The engine runs as a bounded child
/// process, the prediction is advisory only, and the comparison records whether the explanation held.
/// </summary>
public sealed class AnalysisComparisonDemo
{
    private const string DefaultBinaryPath = @"F:\dev\RustLace\target\debug\lovelace-run.exe";

    public async Task<int> RunAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var binaryPath = Environment.GetEnvironmentVariable("LOVELACE_RUN_PATH");
        if (string.IsNullOrWhiteSpace(binaryPath))
        {
            binaryPath = DefaultBinaryPath;
        }

        // Observed stage demand from the recorded traces of one window.
        var model = new StageNetworkModel(
            ArrivalRatePerSecond: 0.5,
            Stages:
            [
                new StageObservation("requirements", Completions: 30, TotalServiceSeconds: 60),
                new StageObservation("implementation", Completions: 30, TotalServiceSeconds: 540),
                new StageObservation("review", Completions: 30, TotalServiceSeconds: 120),
                new StageObservation("delivery", Completions: 30, TotalServiceSeconds: 45)
            ],
            ObservationWindowSeconds: 60);

        var capability = new RustLaceAnalysisCapability(binaryPath, TimeSpan.FromSeconds(20));
        var (prediction, failure) = await capability.PredictAsync(model, cancellationToken);

        if (prediction is null)
        {
            await WriteAsync(outputDirectory, "01-prediction-failed.json", new { Failure = failure }, cancellationToken);
            Console.Error.WriteLine($"analysis failed: {failure}");
            return 1;
        }

        await WriteAsync(outputDirectory, "01-prediction.json", prediction, cancellationToken);

        // Observed throughput for the same window, and the p95 lead time it produced.
        const double observedThroughput = 0.42;
        const double observedP95LeadTimeSeconds = 182.0;

        var comparison = RustLaceAnalysisCapability.Compare(prediction, observedThroughput, observedP95LeadTimeSeconds);
        await WriteAsync(outputDirectory, "02-comparison.json", comparison, cancellationToken);

        // The capability must be reachable from the catalog so an analytical profile can reference it (F-N.8).
        var catalog = new CapabilityCatalog(
        [
            new CapabilityCatalogEntry(
                new CapabilityId("capability.analysis.rustlace-systems"),
                new CapabilityVersion("1.0.0"))
        ]);

        var analyticalProfile = FactoryRolePack.Profiles[1];
        var reachable = catalog.TryResolve(
            new CapabilityId("capability.analysis.rustlace-systems"),
            new CapabilityVersion("1.0.0"),
            out _);

        if (!reachable)
        {
            Console.Error.WriteLine("the analysis capability is not resolvable from the catalog");
            return 1;
        }

        await WriteAsync(
            outputDirectory,
            "03-capability-resolution.json",
            new
            {
                capabilityId = "capability.analysis.rustlace-systems",
                version = "1.0.0",
                resolvable = reachable,
                referencedByRole = analyticalProfile.Role,
                advisoryOnly = true
            },
            cancellationToken);

        Console.WriteLine($"engine       : {binaryPath}");
        Console.WriteLine($"model        : {prediction.ModelInputs}");
        Console.WriteLine($"bottleneck   : {prediction.PredictedBottleneck}");
        Console.WriteLine($"bound        : predicted={prediction.PredictedThroughputBound} observed={observedThroughput}");
        Console.WriteLine($"comparison   : error={comparison.ThroughputError} explanationHeld={comparison.ExplanationHeld}");
        Console.WriteLine($"uncertainty  : {prediction.ForecastUncertainty}");
        Console.WriteLine($"advisory     : no policy, capacity or gate was changed by this prediction");
        Console.WriteLine($"evidence     : {Path.GetFullPath(outputDirectory)}");

        return 0;
    }

    private static async Task WriteAsync<T>(string directory, string fileName, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, fileName), json, cancellationToken);
    }
}
