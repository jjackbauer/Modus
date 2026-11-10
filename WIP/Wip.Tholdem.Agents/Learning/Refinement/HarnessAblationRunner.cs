using System.Diagnostics;
using System.Text;

namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// The bounded six-configuration ablation runner. Every arm executes the same real
/// <see cref="TrainingLoop.Run"/> in its own scratch data directory and emits one canonical
/// <see cref="HarnessAblationReport"/>. It never writes a champion or proposer pointer outside a
/// per-configuration scratch directory.
/// </summary>
/// <remarks>
/// The runner validates every bound before creating a directory and checks the wall-clock budget
/// before each configuration, so a refused run starts no simulation and emits no report. The
/// canonical report contains no clock, path, GUID or culture-dependent value; the budget is only a
/// <see cref="Stopwatch"/> guard and its elapsed time is never persisted.
/// </remarks>
public static class HarnessAblationRunner
{
    /// <summary>The directory under the caller's data root that holds the scratch runs and report.</summary>
    public const string AblationDirectoryName = "ablation";

    /// <summary>The canonical report file name inside <see cref="AblationDirectoryName"/>.</summary>
    public const string ReportFileName = "report.json";

    /// <summary>The zero-effect band from the goal pre-registration: both means below this are inert.</summary>
    public const double ZeroEffectThresholdBbPer100 = 0.05;

    public const int MinimumIterations = 1;

    public const int MaximumIterations = 10;

    public const int MinimumTrainingHands = 1;

    public const int MaximumTrainingHands = 2_000;

    public const int MinimumEvaluationSeeds = 1;

    public const int MaximumEvaluationSeeds = 100;

    public const int MinimumEvaluationHandsPerMatch = 1;

    public const int MaximumEvaluationHandsPerMatch = 50;

    /// <summary>The fixed number of configurations; R-11 does not allow a caller to change it.</summary>
    public const int ConfigurationCount = 6;

    /// <summary>The wall-clock cap, used only as a budget and never persisted in the report.</summary>
    public static readonly TimeSpan DefaultWallClockCap = TimeSpan.FromMinutes(10);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Runs the six fixed configurations, each in a freshly recreated
    /// <c>&lt;DataDirectory&gt;/ablation/&lt;config&gt;/data</c> directory, and writes the one
    /// canonical <c>&lt;DataDirectory&gt;/ablation/report.json</c>.
    /// </summary>
    /// <exception cref="HarnessAblationException">
    /// An option is outside its bound, the data directory is blank, or the wall-clock budget is
    /// exhausted before the next configuration starts.
    /// </exception>
    public static HarnessAblationReport Run(HarnessAblationOptions options)
    {
        ValidateOptions(options);

        var ablationDirectory = Path.Combine(options.DataDirectory, AblationDirectoryName);
        var reportPath = Path.Combine(ablationDirectory, ReportFileName);
        var stopwatch = Stopwatch.StartNew();
        var results = new List<HarnessAblationConfigResult>(ConfigurationCount);

        foreach (var configuration in HarnessAblationConfigurations.Ordered)
        {
            ThrowIfBudgetExceeded(stopwatch, options.WallClockCap, results.Count);

            var configurationDirectory = Path.Combine(ablationDirectory, configuration.Name);
            var dataDirectory = Path.Combine(configurationDirectory, "data");
            if (Directory.Exists(configurationDirectory))
            {
                Directory.Delete(configurationDirectory, recursive: true);
            }

            Directory.CreateDirectory(dataDirectory);

            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: options.Seed,
                    Iterations: options.Iterations,
                    TrainingHands: options.TrainingHands,
                    EvaluationSeeds: options.EvaluationSeeds,
                    EvaluationHandsPerMatch: options.EvaluationHandsPerMatch,
                    DataDirectory: dataDirectory,
                    HarnessRefinement: configuration.HarnessRefinement,
                    SkillRefinement: configuration.SkillRefinement));

            results.Add(Summarize(configuration, run, dataDirectory));
        }

        var report = new HarnessAblationReport(options.Seed, options.Iterations, results);
        Directory.CreateDirectory(ablationDirectory);
        File.WriteAllText(reportPath, report.ToCanonicalJson(), Utf8);
        return report;
    }

    private static void ValidateOptions(HarnessAblationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.DataDirectory))
        {
            throw new HarnessAblationException(
                HarnessAblationException.InvalidOptionCode,
                "HarnessAblationOptions.DataDirectory must not be blank.");
        }

        RequireInRange(
            nameof(HarnessAblationOptions.Iterations),
            options.Iterations,
            MinimumIterations,
            MaximumIterations);
        RequireInRange(
            nameof(HarnessAblationOptions.TrainingHands),
            options.TrainingHands,
            MinimumTrainingHands,
            MaximumTrainingHands);
        RequireInRange(
            nameof(HarnessAblationOptions.EvaluationSeeds),
            options.EvaluationSeeds,
            MinimumEvaluationSeeds,
            MaximumEvaluationSeeds);
        RequireInRange(
            nameof(HarnessAblationOptions.EvaluationHandsPerMatch),
            options.EvaluationHandsPerMatch,
            MinimumEvaluationHandsPerMatch,
            MaximumEvaluationHandsPerMatch);

        if (options.WallClockCap < TimeSpan.Zero || options.WallClockCap > DefaultWallClockCap)
        {
            throw new HarnessAblationException(
                HarnessAblationException.InvalidOptionCode,
                "HarnessAblationOptions.WallClockCap must be between zero and the ten-minute cap.");
        }
    }

    private static void RequireInRange(string option, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new HarnessAblationException(
                HarnessAblationException.InvalidOptionCode,
                $"HarnessAblationOptions.{option} must be between {minimum} and {maximum}; observed {value}.");
        }
    }

    private static void ThrowIfBudgetExceeded(Stopwatch stopwatch, TimeSpan cap, int completedConfigurations)
    {
        if (stopwatch.Elapsed >= cap)
        {
            throw new HarnessAblationException(
                HarnessAblationException.WallClockExceededCode,
                $"The ablation wall-clock budget was exhausted after {completedConfigurations} of "
                + $"{ConfigurationCount} configurations.");
        }
    }

    private static HarnessAblationConfigResult Summarize(
        HarnessAblationConfiguration configuration,
        TrainingRun run,
        string dataDirectory)
    {
        var reports = run.Iterations
            .Where(static iteration => iteration.Report is not null)
            .Select(static iteration => iteration.Report!)
            .ToArray();
        var verifiedPromotions = run.Iterations.Count(
            static iteration => iteration.Verified && iteration.Decision.Promoted);
        var zeroEffectIterations = reports.Count(
            static report => Math.Abs(report.MeanBbPer100) < ZeroEffectThresholdBbPer100
                && Math.Abs(report.LowerBoundBbPer100) < ZeroEffectThresholdBbPer100);
        var inertProposals = run.Iterations.Count(static iteration => iteration.Report is null);
        double? meanBbPer100 = reports.Length == 0
            ? null
            : reports.Average(static report => report.MeanBbPer100);

        var memoryEntries = 0;
        string? memoryContentHash = null;
        var memoryStore = new HarnessMemoryStore(dataDirectory);
        if (File.Exists(memoryStore.MemoryPath))
        {
            var snapshot = memoryStore.Load();
            memoryEntries = snapshot.Entries.Count;
            memoryContentHash = snapshot.ContentHash;
        }

        return new HarnessAblationConfigResult(
            configuration.Name,
            run.FinalChampionId,
            run.Iterations.Count,
            verifiedPromotions,
            meanBbPer100,
            zeroEffectIterations,
            inertProposals,
            memoryEntries,
            memoryContentHash,
            configuration.Note);
    }
}
