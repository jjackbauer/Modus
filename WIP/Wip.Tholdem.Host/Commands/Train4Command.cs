using System.Text;
using Wip.Tholdem.Agents.Evaluation;

namespace Wip.Tholdem.Host;

/// <summary>
/// The additive <c>train4</c> command: it runs the isolated four-seat training/promotion loop with
/// the strong-seed policy of <c>train</c>, reports one line per iteration plus the final isolated
/// champion, and never writes or reads the v1 champion pointer. Every persisted artifact stays under
/// <c>&lt;data&gt;/four-seat/</c>.
/// </summary>
internal static class Train4Command
{
    private const int DefaultSelectionBlocks = 8;

    private const int DefaultSelectionHandsPerMatch = 4;

    private const int DefaultHoldoutBlocks = 16;

    private const int DefaultHoldoutHandsPerMatch = 4;

    private const double DefaultMarginBbPer100 = 5.0;

    private const int DefaultDiagnosisHands = 20;

    public static int Execute(CliOptions options, TextWriter output)
    {
        var seed = options.RequireUlong("seed");
        var iterations = options.RequireInt("iterations", minimum: 0);
        SessionSeedPolicy.RequireStrongOrExplicitOptIn(seed, options.Flag("allow-weak-seed"));
        var selectionBlocks = options.OptionalInt(
            "selection-blocks",
            DefaultSelectionBlocks,
            minimum: 1);
        var selectionHands = options.OptionalInt(
            "selection-hands",
            DefaultSelectionHandsPerMatch,
            minimum: 1);
        var holdoutBlocks = options.OptionalInt(
            "holdout-blocks",
            DefaultHoldoutBlocks,
            minimum: 1);
        var holdoutHands = options.OptionalInt(
            "holdout-hands",
            DefaultHoldoutHandsPerMatch,
            minimum: 1);
        var margin = options.OptionalDouble("margin", DefaultMarginBbPer100);
        if (margin < 0.0)
        {
            throw new CliUsageException(
                "option '--margin' must be a finite, non-negative bb/100 value; observed "
                + CliJson.Number(margin)
                + ".");
        }

        var diagnosis = options.Flag("diagnosis");
        var diagnosisHands = options.OptionalInt(
            "diagnosis-hands",
            DefaultDiagnosisHands,
            minimum: 1);

        var dataDirectory = CliPaths.ResolveDataDirectory(options);

        FourSeatTrainingRun run;
        try
        {
            run = FourSeatTraining.Run(
                new FourSeatTrainingOptions(
                    seed,
                    iterations,
                    dataDirectory,
                    SelectionBlocks: selectionBlocks,
                    SelectionHandsPerMatch: selectionHands,
                    HoldoutBlocks: holdoutBlocks,
                    HoldoutHandsPerMatch: holdoutHands,
                    MarginBbPer100: margin,
                    DiagnosisDriven: diagnosis,
                    DiagnosisHands: diagnosisHands));
        }
        catch (InvalidOperationException exception)
        {
            throw new CliArtifactException(exception.Message, exception);
        }

        if (options.Flag("json"))
        {
            output.WriteLine(
                ToCanonicalJson(
                    run,
                    seed,
                    selectionBlocks,
                    selectionHands,
                    holdoutBlocks,
                    holdoutHands,
                    margin));
            return TholdemCli.ExitOk;
        }

        foreach (var iteration in run.Iterations)
        {
            output.WriteLine(
                $"iteration={iteration.Iteration} champion={iteration.ChampionId} "
                + $"challenger={iteration.ChallengerId} outcome={iteration.Outcome} "
                + $"verdict={iteration.Verdict} reason={iteration.Reason} "
                + $"meanBbPer100={CliJson.Format(iteration.SelectionMeanBbPer100)}");
        }

        output.WriteLine($"final champion: {run.FinalChampionId}");
        return TholdemCli.ExitOk;
    }

    private static string ToCanonicalJson(
        FourSeatTrainingRun run,
        ulong seed,
        int selectionBlocks,
        int selectionHands,
        int holdoutBlocks,
        int holdoutHands,
        double margin)
    {
        var builder = new StringBuilder(512);
        builder.Append("{\"schema\":\"tholdem.cli-train4.v1\",\"seed\":");
        builder.Append(CliJson.Number(seed));
        builder.Append(",\"selectionBlocks\":");
        builder.Append(CliJson.Number(selectionBlocks));
        builder.Append(",\"selectionHandsPerMatch\":");
        builder.Append(CliJson.Number(selectionHands));
        builder.Append(",\"holdoutBlocks\":");
        builder.Append(CliJson.Number(holdoutBlocks));
        builder.Append(",\"holdoutHandsPerMatch\":");
        builder.Append(CliJson.Number(holdoutHands));
        builder.Append(",\"marginBbPer100\":");
        builder.Append(CliJson.Number(margin));
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
            builder.Append(",\"outcome\":");
            builder.Append(CliJson.Text(iteration.Outcome));
            builder.Append(",\"selectionMeanBbPer100\":");
            builder.Append(CliJson.Number(iteration.SelectionMeanBbPer100));
            builder.Append(",\"verdict\":");
            builder.Append(CliJson.Text(iteration.Verdict));
            builder.Append(",\"reason\":");
            builder.Append(CliJson.Text(iteration.Reason));
            builder.Append('}');
        }

        builder.Append("],\"finalChampionId\":");
        builder.Append(CliJson.Text(run.FinalChampionId));
        builder.Append(",\"runHash\":");
        builder.Append(CliJson.Text(run.RunHash));
        builder.Append('}');
        return builder.ToString();
    }
}