using System.Text;
using Wip.Tholdem.Agents.Learning;

namespace Wip.Tholdem.Host;

/// <summary>
/// Runs the statistically gated learning loop and reports one line per iteration plus the final
/// champion. The loop persists strategies, evaluation reports, the ledger and the champion pointer.
/// </summary>
internal static class TrainCommand
{
    private const int DefaultTrainingHands = 120;

    private const int DefaultEvaluationSeeds = 24;

    private const int DefaultEvaluationHandsPerMatch = 10;

    public static int Execute(CliOptions options, TextWriter output)
    {
        var seed = options.RequireUlong("seed");
        var iterations = options.RequireInt("iterations", minimum: 0);
        SessionSeedPolicy.RequireStrongOrExplicitOptIn(seed, options.Flag("allow-weak-seed"));
        var dataDirectory = CliPaths.ResolveDataDirectory(options);

        TrainingRun run;
        try
        {
            run = TrainingLoop.Run(
                new TrainingOptions(
                    seed,
                    iterations,
                    TrainingHands: DefaultTrainingHands,
                    EvaluationSeeds: DefaultEvaluationSeeds,
                    EvaluationHandsPerMatch: DefaultEvaluationHandsPerMatch,
                    DataDirectory: dataDirectory));
        }
        catch (InvalidOperationException exception)
        {
            throw new CliArtifactException(exception.Message, exception);
        }

        if (options.Flag("json"))
        {
            output.WriteLine(ToCanonicalJson(run));
            return TholdemCli.ExitOk;
        }

        foreach (var iteration in run.Iterations)
        {
            output.WriteLine(
                $"iteration={iteration.Iteration} champion={iteration.ChampionId} "
                + $"challenger={iteration.ChallengerId} promoted={CliJson.Format(iteration.Decision.Promoted)} "
                + $"verified={CliJson.Format(iteration.Verified)} outcome={iteration.Outcome} "
                + $"meanBbPer100={CliJson.Format(iteration.Report.MeanBbPer100)} "
                + $"lcb={CliJson.Format(iteration.Report.LowerBoundBbPer100)} "
                + $"seeds={iteration.Report.Seeds}");
        }

        output.WriteLine($"final champion: {run.FinalChampionId}");
        return TholdemCli.ExitOk;
    }

    private static string ToCanonicalJson(TrainingRun run)
    {
        var builder = new StringBuilder(512);
        builder.Append("{\"schema\":\"tholdem.cli-training.v1\",\"seed\":");
        builder.Append(CliJson.Number(run.Options.Seed));
        builder.Append(",\"iterations\":[");
        for (var index = 0; index < run.Iterations.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var iteration = run.Iterations[index];
            builder.Append("{\"iteration\":");
            builder.Append(CliJson.Number(iteration.Iteration));
            builder.Append(",\"championId\":");
            builder.Append(CliJson.Text(iteration.ChampionId));
            builder.Append(",\"challengerId\":");
            builder.Append(CliJson.Text(iteration.ChallengerId));
            builder.Append(",\"promoted\":");
            builder.Append(CliJson.Format(iteration.Decision.Promoted));
            builder.Append(",\"verified\":");
            builder.Append(CliJson.Format(iteration.Verified));
            builder.Append(",\"outcome\":");
            builder.Append(CliJson.Text(iteration.Outcome));
            builder.Append(",\"report\":");
            builder.Append(iteration.Report.ToCanonicalJson());
            builder.Append('}');
        }

        builder.Append("],\"finalChampionId\":");
        builder.Append(CliJson.Text(run.FinalChampionId));
        builder.Append('}');
        return builder.ToString();
    }
}
