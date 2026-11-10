using System.Globalization;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Host;

/// <summary>
/// Runs one reproducible batch match into <c>&lt;data&gt;/sessions/&lt;sessionId&gt;/</c> and prints a
/// metrics table (or the canonical report JSON). A match has two to six seats: <c>--seats</c>
/// chooses the table size, <c>--agents</c> names one agent per seat (taking precedence over
/// <c>--agent0</c>/<c>--agent1</c>) and <c>--starting-stacks</c> optionally sizes each seat.
/// </summary>
internal static class PlayCommand
{
    public const string DefaultSeat0Agent = "tight-aggressive";

    public const string DefaultSeat1Agent = "random-legal";

    public const string DefaultAdditionalAgent = "random-legal";

    public const int MinimumSeats = 2;

    public const int MaximumSeats = 6;

    public const long DefaultBigBlind = 10;

    private const int DefaultHands = 200;

    private const string AgentChoices = "random-legal, tight-aggressive or pot-odds-equity";

    public static int Execute(CliOptions options, TextWriter output)
    {
        var seed = options.RequireUlong("seed");
        var hands = options.OptionalInt("hands", DefaultHands, minimum: 1);
        var seats = ReadSeats(options);
        var agents = ReadAgents(options, seats);
        var startingStacks = ReadStartingStacks(options, seats);
        SessionSeedPolicy.RequireStrongOrExplicitOptIn(seed, options.Flag("allow-weak-seed"));
        var dataDirectory = CliPaths.ResolveDataDirectory(options);
        var sessionsRoot = Path.Combine(dataDirectory, "sessions");
        var staging = Path.Combine(sessionsRoot, ".inprogress");
        Directory.CreateDirectory(sessionsRoot);

        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        try
        {
            var report = MatchSimulator.Run(
                agents,
                new SimulationOptions(
                    seed,
                    hands,
                    BigBlind: DefaultBigBlind,
                    HistoryDirectory: staging,
                    StartingStacks: startingStacks));

            var destination = Path.Combine(sessionsRoot, report.SessionId);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            Directory.Move(staging, destination);

            if (options.Flag("json"))
            {
                output.WriteLine(report.CanonicalJson);
                return TholdemCli.ExitOk;
            }

            WriteTable(output, report);
            return TholdemCli.ExitOk;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static int ReadSeats(CliOptions options)
    {
        var text = options.Get("seats");
        if (string.IsNullOrWhiteSpace(text))
        {
            return 2;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seats)
            || seats < MinimumSeats
            || seats > MaximumSeats)
        {
            throw new CliUsageException(
                $"option '--seats' must be between {MinimumSeats} and {MaximumSeats}; observed {text}.");
        }

        return seats;
    }

    private static IPokerAgent[] ReadAgents(CliOptions options, int seats)
    {
        var agentsText = options.Get("agents");
        if (!string.IsNullOrWhiteSpace(agentsText))
        {
            var names = agentsText.Split(',', StringSplitOptions.TrimEntries);
            if (names.Length != seats)
            {
                throw new CliUsageException(
                    $"option '--agents' must name exactly {seats} agents (one per seat); "
                    + $"observed {names.Length}.");
            }

            var agents = new IPokerAgent[seats];
            for (var seat = 0; seat < seats; seat++)
            {
                agents[seat] = CreateAgent(names[seat], "agents");
            }

            return agents;
        }

        var seat0 = options.Optional("agent0", DefaultSeat0Agent);
        var seat1 = options.Optional("agent1", DefaultSeat1Agent);
        var fallback = new IPokerAgent[seats];
        fallback[0] = CreateAgent(seat0, "agent0");
        fallback[1] = CreateAgent(seat1, "agent1");
        for (var seat = 2; seat < seats; seat++)
        {
            fallback[seat] = CreateAgent(DefaultAdditionalAgent, "agents");
        }

        return fallback;
    }

    private static long[]? ReadStartingStacks(CliOptions options, int seats)
    {
        var text = options.Get("starting-stacks");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != seats)
        {
            throw new CliUsageException(
                $"option '--starting-stacks' must list exactly {seats} stacks (one per seat); "
                + $"observed {parts.Length}.");
        }

        var stacks = new long[seats];
        for (var seat = 0; seat < seats; seat++)
        {
            if (!long.TryParse(parts[seat], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                throw new CliUsageException(
                    $"option '--starting-stacks' must be an integer at seat {seat}; "
                    + $"observed '{parts[seat]}'.");
            }

            if (value < DefaultBigBlind)
            {
                throw new CliUsageException(
                    $"option '--starting-stacks' must be at least {DefaultBigBlind} (the big blind); "
                    + $"observed {value} at seat {seat}.");
            }

            stacks[seat] = value;
        }

        return stacks;
    }

    private static IPokerAgent CreateAgent(string name, string optionName) => name switch
    {
        "random-legal" => new RandomLegalAgent(),
        "tight-aggressive" => new TightAggressiveAgent(),
        "pot-odds-equity" => new PotOddsEquityAgent(),
        _ => throw new CliUsageException(
            $"option '--{optionName}' must use {AgentChoices}; observed '{name}'."),
    };

    private static void WriteTable(TextWriter output, SimulationReport report)
    {
        output.WriteLine($"session: {report.SessionId}");
        output.WriteLine(
            $"seed: {report.Seed}  hands: {report.Hands}  manifest sha256: {report.ManifestHash}");
        output.WriteLine(
            $"{"agent",-24}{"hands",6}{"bb/100",10}{"95% CI (bb/100)",22}"
            + $"{"win rate",10}{"95% CI (win rate)",22}{"aggression",12}{"showdown",10}");

        foreach (var metrics in report.Agents)
        {
            var interval = metrics.BigBlindsPer100Ci;
            var winRateInterval = metrics.WinRateCi;
            output.WriteLine(
                $"{metrics.AgentName,-24}{metrics.Hands,6}{CliJson.Format(metrics.BigBlindsPer100),10}"
                + $"{CliJson.Format(interval.Low) + " .. " + CliJson.Format(interval.High),22}"
                + $"{CliJson.Format(metrics.WinRate),10}"
                + $"{CliJson.Format(winRateInterval.Low) + " .. " + CliJson.Format(winRateInterval.High),22}"
                + $"{CliJson.Format(metrics.AggressionFactor),12}{CliJson.Format(metrics.ShowdownRate),10}");
        }
    }
}
