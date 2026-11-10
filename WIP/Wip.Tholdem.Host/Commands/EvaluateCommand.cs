using Wip.Tholdem.Agents.Learning;

namespace Wip.Tholdem.Host;

/// <summary>
/// Runs one paired, held-out challenger-versus-incumbent evaluation, persists the CLI evaluation
/// document and reports the gate verdict. Evaluation never moves the champion pointer.
/// </summary>
internal static class EvaluateCommand
{
    private const int DefaultHandsPerMatch = 10;

    private const double DefaultMarginBbPer100 = 5.0;

    private const ulong DefaultBaseSeed = SessionSeedPolicy.MinimumSeed;

    public static int Execute(CliOptions options, TextWriter output)
    {
        var challengerId = CliPaths.SafeStrategyId(options.Require("challenger"));
        var seeds = options.RequireInt("seeds", minimum: 1);
        var handsPerMatch = options.OptionalInt("hands", DefaultHandsPerMatch, minimum: 1);
        var margin = options.OptionalDouble("margin", DefaultMarginBbPer100);
        var baseSeed = options.OptionalUlong("seed", DefaultBaseSeed);
        SessionSeedPolicy.RequireStrongOrExplicitOptIn(baseSeed, options.Flag("allow-weak-seed"));
        var dataDirectory = CliPaths.ResolveDataDirectory(options);
        var challenger = StrategyArtifactStore.Load(dataDirectory, challengerId);
        var incumbentId = options.Get("incumbent") is { Length: > 0 } configuredIncumbent
            ? CliPaths.SafeStrategyId(configuredIncumbent)
            : StrategyArtifactStore.ReadChampionStrategyId(dataDirectory);
        var incumbent = StrategyArtifactStore.Load(dataDirectory, incumbentId);
        var criteria = new EvaluationCriteria(margin, new EvaluationCriteria().MinSeeds);

        var report = MatchEvaluator.Evaluate(
            new StrategyAgent(challenger.Genome, challengerId),
            challengerId,
            new StrategyAgent(incumbent.Genome, incumbentId),
            incumbentId,
            baseSeed,
            seeds,
            handsPerMatch);

        var path = EvaluationFileStore.Write(dataDirectory, criteria, report);
        var decision = new StatisticalPromotionPolicy().Decide(report, criteria);
        var verified = PromotionVerifier.Verify(decision, report, criteria, out var reason);

        if (options.Flag("json"))
        {
            output.WriteLine(File.ReadAllText(path));
            return TholdemCli.ExitOk;
        }

        output.WriteLine($"evaluation: {path}");
        output.WriteLine(
            $"challenger: {challengerId}  incumbent: {incumbentId}  seeds: {report.Seeds}  "
            + $"hands per match: {report.HandsPerMatch}");
        output.WriteLine(
            $"mean bb/100: {CliJson.Format(report.MeanBbPer100)}  "
            + $"LCB: {CliJson.Format(report.LowerBoundBbPer100)}  "
            + $"margin: {CliJson.Format(criteria.MarginBbPer100)}");
        output.WriteLine($"decision: {decision.Reason}");
        output.WriteLine(verified ? $"verifier: verified ({reason})" : $"verifier: refused ({reason})");
        return TholdemCli.ExitOk;
    }
}
