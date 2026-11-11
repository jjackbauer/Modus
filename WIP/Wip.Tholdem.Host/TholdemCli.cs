namespace Wip.Tholdem.Host;

/// <summary>
/// The testable command surface of the Tholdem CLI. <see cref="Run"/> never touches
/// <see cref="Console"/>; the process entry point supplies the real writers.
/// </summary>
public static class TholdemCli
{
    public const string Version = "0.1.0";

    public const int ExitOk = 0;

    public const int ExitUsage = 2;

    public const int ExitRefused = 3;

    public const int ExitArtifact = 4;

    public const int ExitInvariant = 5;

    private static readonly IReadOnlySet<string> PlayFlags = Flags("json", "allow-weak-seed");

    private static readonly IReadOnlySet<string> PlayValued = Valued(
        "seed",
        "hands",
        "seats",
        "agents",
        "starting-stacks",
        "agent0",
        "agent1",
        "data-dir");

    private static readonly IReadOnlySet<string> TrainFlags = Flags("json", "allow-weak-seed");

    private static readonly IReadOnlySet<string> TrainValued = Valued("iterations", "seed", "data-dir");

    private static readonly IReadOnlySet<string> Train4Flags = Flags("json", "allow-weak-seed", "diagnosis");

    private static readonly IReadOnlySet<string> Train4Valued = Valued(
        "seed",
        "iterations",
        "selection-blocks",
        "selection-hands",
        "holdout-blocks",
        "holdout-hands",
        "margin",
        "diagnosis-hands",
        "data-dir");

    private static readonly IReadOnlySet<string> EvaluateFlags = Flags("json", "allow-weak-seed");

    private static readonly IReadOnlySet<string> EvaluateValued =
        Valued("challenger", "seeds", "hands", "incumbent", "margin", "seed", "data-dir");

    private static readonly IReadOnlySet<string> Evaluate4Flags = Flags("json");

    private static readonly IReadOnlySet<string> Evaluate4Valued = Valued(
        "plan",
        "challenger",
        "incumbent",
        "challenger-kind",
        "incumbent-kind",
        "anchor-plan",
        "out",
        "data-dir");

    private static readonly IReadOnlySet<string> PromoteFlags = Flags("yes");

    private static readonly IReadOnlySet<string> PromoteValued = Valued("challenger", "incumbent", "data-dir");

    private static readonly IReadOnlySet<string> ReplayFlags = Flags();

    private static readonly IReadOnlySet<string> ReplayValued = Valued("data-dir", "expected-hash");

    private static readonly IReadOnlySet<string> RetentionFlags = Flags("status", "purge", "json", "yes");

    private static readonly IReadOnlySet<string> RetentionValued = Valued("older-than-days", "data-dir");

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            return Dispatch(args, output);
        }
        catch (CliUsageException exception)
        {
            error.WriteLine("tholdem: " + exception.Message);
            error.WriteLine();
            WriteUsage(error);
            return ExitUsage;
        }
        catch (CliArtifactException exception)
        {
            error.WriteLine("tholdem: " + exception.Message);
            return ExitArtifact;
        }
        catch (CliInvariantException exception)
        {
            error.WriteLine("tholdem: " + exception.Message);
            return ExitInvariant;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine("tholdem: " + exception.Message);
            return ExitArtifact;
        }
    }

    private static int Dispatch(string[] args, TextWriter output)
    {
        if (args.Length == 0)
        {
            throw new CliUsageException("a command is required.");
        }

        var command = args[0];
        if (command is "--help" or "-h" or "help")
        {
            if (args.Length != 1)
            {
                throw new CliUsageException($"'{command}' does not take arguments.");
            }

            WriteUsage(output);
            return ExitOk;
        }

        if (command is "--version" or "version")
        {
            if (args.Length != 1)
            {
                throw new CliUsageException($"'{command}' does not take arguments.");
            }

            output.WriteLine(Version);
            return ExitOk;
        }

        return command switch
        {
            "play" => PlayCommand.Execute(CliOptions.Parse(args, 1, PlayFlags, PlayValued), output),
            "train" => TrainCommand.Execute(CliOptions.Parse(args, 1, TrainFlags, TrainValued), output),
            "train4" => Train4Command.Execute(CliOptions.Parse(args, 1, Train4Flags, Train4Valued), output),
            "evaluate" => EvaluateCommand.Execute(CliOptions.Parse(args, 1, EvaluateFlags, EvaluateValued), output),
            "evaluate4" => Evaluate4Command.Execute(
                CliOptions.Parse(args, 1, Evaluate4Flags, Evaluate4Valued),
                output),
            "promote" => PromoteCommand.Execute(CliOptions.Parse(args, 1, PromoteFlags, PromoteValued), output),
            "replay" => ReplayCommand.Execute(CliOptions.Parse(args, 1, ReplayFlags, ReplayValued), output),
            "retention" => RetentionCommand.Execute(
                CliOptions.Parse(args, 1, RetentionFlags, RetentionValued),
                output),
            _ => throw new CliUsageException($"unknown command '{command}'."),
        };
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine($"tholdem {Version}");
        writer.WriteLine();
        writer.WriteLine("usage:");
        writer.WriteLine("  tholdem play --seed <n> [--hands <n>=200] [--seats <2..6>=2] [--allow-weak-seed]");
        writer.WriteLine("               [--agents <name>[,<name>...]] [--starting-stacks <n>[,<n>...]]");
        writer.WriteLine("               [--agent0 <random-legal|tight-aggressive|pot-odds-equity>]");
        writer.WriteLine("               [--agent1 <random-legal|tight-aggressive|pot-odds-equity>]");
        writer.WriteLine("               [--json] [--data-dir <path>]");
        writer.WriteLine("  tholdem train --iterations <n> --seed <n> [--allow-weak-seed]");
        writer.WriteLine("               [--json] [--data-dir <path>]");
        writer.WriteLine("  tholdem train4 --iterations <n> --seed <n> [--allow-weak-seed]");
        writer.WriteLine("               [--selection-blocks <n>=8] [--selection-hands <n>=4]");
        writer.WriteLine("               [--holdout-blocks <n>=16] [--holdout-hands <n>=4]");
        writer.WriteLine("               [--margin <bb/100>=5] [--diagnosis] [--diagnosis-hands <n>=20]");
        writer.WriteLine("               [--json] [--data-dir <path>]");
        writer.WriteLine("  tholdem evaluate --challenger <id> --seeds <n> [--hands <n>=10]");
        writer.WriteLine("               [--incumbent <id>] [--margin <bb/100>=5]");
        writer.WriteLine("               [--seed <n>=1099511627776] [--allow-weak-seed]");
        writer.WriteLine("               [--json] [--data-dir <path>]");
        writer.WriteLine("  tholdem evaluate4 --plan <path> --challenger <id> --incumbent <id>");
        writer.WriteLine("               [--challenger-kind <strategy|equity-policy>]");
        writer.WriteLine("               [--incumbent-kind <strategy|equity-policy>]");
        writer.WriteLine("               [--anchor-plan <path>] [--out <dir>] [--json] [--data-dir <path>]");
        writer.WriteLine("  tholdem promote --challenger <id> [--incumbent <id>] [--yes] [--data-dir <path>]");
        writer.WriteLine("  tholdem replay <hand.json> [--expected-hash <hex>] [--data-dir <path>]");
        writer.WriteLine("  tholdem retention --status [--json] [--data-dir <path>]");
        writer.WriteLine("  tholdem retention --purge --older-than-days <n> [--yes] [--data-dir <path>]");
        writer.WriteLine("  tholdem --help | --version");
        writer.WriteLine();
        writer.WriteLine(
            "  --data-dir <path>  artifact directory (default: <current directory>"
            + Path.DirectorySeparatorChar
            + "artifacts"
            + Path.DirectorySeparatorChar
            + "tholdem)");
        writer.WriteLine(
            "  --allow-weak-seed  permit a seed below "
            + SessionSeedPolicy.MinimumSeed
            + " for a reproducible demo or test");
        writer.WriteLine(
            "  --agents           one comma-separated name per seat; takes precedence over"
            + " --agent0/--agent1");
        writer.WriteLine(
            "  --starting-stacks  one comma-separated stack per seat, each at least the big blind (10)");
        writer.WriteLine(
            "  --challenger-kind  evaluate4 challenger agent kind (default: strategy)");
        writer.WriteLine(
            "  --incumbent-kind   evaluate4 incumbent agent kind (default: strategy)");
        writer.WriteLine();
        writer.WriteLine("seeds:");
        writer.WriteLine(
            "  Sessions are dealt deterministically from --seed, so a guessable seed lets an"
            + " observer");
        writer.WriteLine(
            "  enumerate candidates and reconstruct the deck. play, train, train4 and evaluate require"
            + " seed >= "
            + SessionSeedPolicy.MinimumSeed);
        writer.WriteLine(
            "  unless --allow-weak-seed is given; weak seeds are for reproducible demos and"
            + " tests only.");
        writer.WriteLine();
        writer.WriteLine("promote never auto-promotes: it requires explicit confirmation (--yes) and a");
        writer.WriteLine("verified lower confidence bound above the configured margin.");
        writer.WriteLine();
        writer.WriteLine(
            "exit codes: 0 ok, 2 usage error, 3 refused, 4 missing/invalid artifact, "
            + "5 replay invariant failure.");
    }

    private static IReadOnlySet<string> Flags(params string[] names) =>
        new HashSet<string>(names, StringComparer.Ordinal);

    private static IReadOnlySet<string> Valued(params string[] names) =>
        new HashSet<string>(names, StringComparer.Ordinal);
}
