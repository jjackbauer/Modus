using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Simulation;

/// <summary>
/// Reproducible batch simulator for two to six seats: a fixed seed, the same agents and the same
/// options always produce byte-identical hand histories, a byte-identical manifest and a
/// byte-identical report. The two-agent overload is a thin delegation to the general
/// <see cref="Run(IReadOnlyList{IPokerAgent}, SimulationOptions, CancellationToken)"/> overload and
/// keeps the recorded heads-up behaviour byte-for-byte.
/// </summary>
public static class MatchSimulator
{
    public const string SessionSchema = "tholdem.session.v1";

    public const string ReportSchema = "tholdem.report.v1";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Runs a two-seat match. This is the pre-multiway public surface: every existing call site,
    /// test and the CLI oracle observe exactly the same session id, files and manifest hash as
    /// before the multiway change.
    /// </summary>
    public static SimulationReport Run(
        IPokerAgent seat0,
        IPokerAgent seat1,
        SimulationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seat0);
        ArgumentNullException.ThrowIfNull(seat1);
        ArgumentNullException.ThrowIfNull(options);

        return Run(new[] { seat0, seat1 }, options, cancellationToken);
    }

    /// <summary>
    /// Runs a two-to-six-seat match. Seat <c>s</c> is always <c>agents[s]</c>; hand <c>n</c> uses
    /// <c>SeedDerivation.Derive(options.Seed, n, 0, 0, 0)</c> and
    /// <c>buttonSeat = AlternateButton ? (n - 1) % seats : 0</c>, exactly as the two-seat simulator
    /// always did. Agent names derive the session id over all seats.
    /// </summary>
    public static SimulationReport Run(
        IReadOnlyList<IPokerAgent> agents,
        SimulationOptions options,
        CancellationToken cancellationToken = default)
        => RunDetailed(agents, options, cancellationToken).Report;

    /// <summary>
    /// Runs a two-to-six-seat match and returns the report together with the per-hand outcomes in
    /// hand order and the per-hand seeds actually dealt (one per hand). The report is exactly what
    /// <see cref="Run(IReadOnlyList{IPokerAgent}, SimulationOptions, CancellationToken)"/> returns.
    /// </summary>
    public static MatchSimulation RunDetailed(
        IReadOnlyList<IPokerAgent> agents,
        SimulationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(options);

        var seats = agents.Count;
        if (seats is < HoldemHandEngine.MinimumSeats or > HoldemHandEngine.MaximumSeats)
        {
            throw new ArgumentOutOfRangeException(
                nameof(agents),
                seats,
                $"A table must have between {HoldemHandEngine.MinimumSeats} and "
                + $"{HoldemHandEngine.MaximumSeats} agents.");
        }

        var names = new string[seats];
        for (var seat = 0; seat < seats; seat++)
        {
            var agent = agents[seat]
                ?? throw new ArgumentException($"The agent at seat {seat} is null.", nameof(agents));
            names[seat] = NormalizeName(
                agent.Name,
                "seat-" + seat.ToString(CultureInfo.InvariantCulture));
        }

        var outcomes = new List<HandOutcome>(options.Hands);
        var handSeeds = new List<ulong>(options.Hands);
        var handHashes = new List<string>(options.Hands);
        var historyDirectory = options.HistoryDirectory;
        if (historyDirectory is not null)
        {
            Directory.CreateDirectory(historyDirectory);
        }

        var startingStacks = options.StartingStacks?.ToArray();

        for (var hand = 1; hand <= options.Hands; hand++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handSeed = SeedDerivation.Derive(options.Seed, hand, 0, 0, 0UL);
            handSeeds.Add(handSeed);
            var buttonSeat = options.AlternateButton ? (hand - 1) % seats : 0;
            var state = HoldemHandEngine.Start(
                handSeed,
                seats,
                hand,
                options.StartingStack,
                options.SmallBlind,
                options.BigBlind,
                buttonSeat,
                startingStacks);

            // The engine validates the supplied per-seat list (exact count, every value covers the
            // big blind). Its normalized copy is the authoritative source for metrics and the
            // manifest, so an equal-stack session still uses the scalar starting stack.
            startingStacks ??= state.StartingStacks.ToArray();

            var actionCounts = new int[seats];
            var aggressiveActions = new int[seats];
            var calls = new int[seats];
            var voluntaryPreflop = new bool[seats];
            var handActionCount = 0;

            while (!state.IsComplete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var actingSeat = state.ActingSeat;
                var observation = PlayerObservation.ForSeat(state, actingSeat);
                var context = new AgentTurnContext(
                    hand,
                    actingSeat,
                    actionCounts[actingSeat]);

                PokerAction action;
                try
                {
                    action = agents[actingSeat]
                        .ActAsync(observation, context, cancellationToken)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new IllegalAgentActionException(
                        names[actingSeat],
                        hand,
                        default,
                        $"the agent threw {exception.GetType().Name}: {exception.Message}",
                        exception);
                }

                actionCounts[actingSeat]++;

                TableState next;
                try
                {
                    next = HoldemHandEngine.Apply(state, action);
                }
                catch (IllegalActionException exception)
                {
                    throw new IllegalAgentActionException(
                        names[actingSeat],
                        hand,
                        action,
                        exception.Message,
                        exception);
                }

                if (action.Kind is ActionKind.Bet or ActionKind.Raise or ActionKind.AllIn)
                {
                    aggressiveActions[actingSeat]++;
                }

                if (action.Kind == ActionKind.Call)
                {
                    calls[actingSeat]++;
                }

                // Voluntary preflop participation is measured against the street the action was
                // taken on: a call, bet, raise or all-in before the flop counts; a check or a
                // posted blind does not.
                if (state.Street == Street.Preflop
                    && action.Kind is ActionKind.Call
                        or ActionKind.Bet
                        or ActionKind.Raise
                        or ActionKind.AllIn)
                {
                    voluntaryPreflop[actingSeat] = true;
                }

                state = next;
                handActionCount++;
                if (handActionCount > options.ActionCapPerHand)
                {
                    throw new HandActionCapExceededException(hand, options.ActionCapPerHand);
                }
            }

            var record = HandRecord.From(state);
            var json = record.ToCanonicalJson();
            var bytes = Utf8.GetBytes(json);
            handHashes.Add(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

            if (historyDirectory is not null)
            {
                var fileName = "hand-" + hand.ToString("D5", CultureInfo.InvariantCulture) + ".json";
                File.WriteAllBytes(Path.Combine(historyDirectory, fileName), bytes);
            }

            var showdown = state.ShowdownReached;
            var showdownParticipants = new bool[seats];
            for (var seat = 0; seat < seats; seat++)
            {
                // A seat that folded before the showdown did not participate in it. For two seats a
                // showdown implies both seats are still live, so this is the recorded [true, true].
                showdownParticipants[seat] = showdown && !state.Folded[seat];
            }

            outcomes.Add(new HandOutcome(
                hand,
                state.Stacks.ToArray(),
                state.Winners.ToArray(),
                showdown,
                aggressiveActions,
                calls,
                showdownParticipants)
            {
                VoluntaryPreflop = voluntaryPreflop,
            });
        }

        // options.Hands is at least one, so the first Start above always produced the validated
        // per-seat array (or threw); it cannot still be null here.
        var effectiveStartingStacks = startingStacks!;
        var sessionId = ComputeSessionId(options.Seed, options.Hands, names);
        var manifest = BuildManifest(
            sessionId,
            options,
            names,
            handHashes,
            effectiveStartingStacks);
        var manifestBytes = Utf8.GetBytes(manifest);
        var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();

        if (historyDirectory is not null)
        {
            File.WriteAllBytes(Path.Combine(historyDirectory, "session.json"), manifestBytes);
        }

        var metrics = new AgentMetrics[seats];
        for (var seat = 0; seat < seats; seat++)
        {
            metrics[seat] = MetricsCalculator.Compute(
                names[seat],
                seat,
                outcomes,
                effectiveStartingStacks[seat],
                options.BigBlind);
        }

        var reportJson = BuildReport(
            sessionId,
            options,
            names,
            manifestHash,
            metrics,
            effectiveStartingStacks[0]);

        var vpip = new VpipSummary[seats];
        for (var seat = 0; seat < seats; seat++)
        {
            var voluntaryHands = 0;
            foreach (var outcome in outcomes)
            {
                if (outcome.VoluntaryPreflop.Count > seat && outcome.VoluntaryPreflop[seat])
                {
                    voluntaryHands++;
                }
            }

            vpip[seat] = new VpipSummary(options.Hands, voluntaryHands);
        }

        var report = new SimulationReport(
            sessionId,
            options.Seed,
            options.Hands,
            manifestHash,
            reportJson,
            metrics)
        {
            Vpip = vpip,
        };

        return new MatchSimulation(report, outcomes, handSeeds);
    }

    private static string NormalizeName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name;

    private static string ComputeSessionId(ulong seed, int hands, IReadOnlyList<string> names)
    {
        var material = new StringBuilder(160);
        material.Append(seed.ToString(CultureInfo.InvariantCulture));
        material.Append('\n');
        material.Append(hands.ToString(CultureInfo.InvariantCulture));
        for (var seat = 0; seat < names.Count; seat++)
        {
            material.Append('\n');
            material.Append(names[seat]);
        }

        var digest = SHA256.HashData(Utf8.GetBytes(material.ToString()));
        return "tholdem-" + Convert.ToHexString(digest).ToLowerInvariant()[..16];
    }

    /// <summary>
    /// Writes the canonical manifest. A two-seat session with equal starting stacks is written
    /// exactly as before the multiway change (no <c>seats</c>/<c>startingStacks</c> members, so every
    /// recorded manifest hash still verifies). Every other session adds <c>"seats"</c> and the
    /// per-seat <c>"startingStacks"</c> that its v2 hand records already carry.
    /// </summary>
    private static string BuildManifest(
        string sessionId,
        SimulationOptions options,
        IReadOnlyList<string> names,
        IReadOnlyList<string> handHashes,
        IReadOnlyList<long> startingStacks)
    {
        var seats = names.Count;
        var multiwayLayout = seats != 2 || !AllEqual(startingStacks);
        var builder = new StringBuilder(256 + (handHashes.Count * 66) + (seats * 8));
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, SessionSchema);
        builder.Append(",\"sessionId\":");
        CanonicalJsonWriter.Text(builder, sessionId);
        builder.Append(",\"seed\":");
        CanonicalJsonWriter.Number(builder, options.Seed);
        builder.Append(",\"hands\":");
        CanonicalJsonWriter.Number(builder, options.Hands);
        if (multiwayLayout)
        {
            builder.Append(",\"seats\":");
            CanonicalJsonWriter.Number(builder, seats);
        }

        builder.Append(",\"startingStack\":");
        CanonicalJsonWriter.Number(builder, startingStacks[0]);
        if (multiwayLayout)
        {
            builder.Append(",\"startingStacks\":[");
            for (var seat = 0; seat < startingStacks.Count; seat++)
            {
                if (seat > 0)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.Number(builder, startingStacks[seat]);
            }

            builder.Append(']');
        }

        builder.Append(",\"smallBlind\":");
        CanonicalJsonWriter.Number(builder, options.SmallBlind);
        builder.Append(",\"bigBlind\":");
        CanonicalJsonWriter.Number(builder, options.BigBlind);
        builder.Append(",\"agents\":[");
        for (var index = 0; index < names.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.Text(builder, names[index]);
        }

        builder.Append("],\"handHashes\":[");
        for (var index = 0; index < handHashes.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.Text(builder, handHashes[index]);
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static string BuildReport(
        string sessionId,
        SimulationOptions options,
        IReadOnlyList<string> names,
        string manifestHash,
        IReadOnlyList<AgentMetrics> metrics,
        long startingStack)
    {
        var builder = new StringBuilder(512 + (metrics.Count * 320));
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, ReportSchema);
        builder.Append(",\"sessionId\":");
        CanonicalJsonWriter.Text(builder, sessionId);
        builder.Append(",\"seed\":");
        CanonicalJsonWriter.Number(builder, options.Seed);
        builder.Append(",\"hands\":");
        CanonicalJsonWriter.Number(builder, options.Hands);
        builder.Append(",\"startingStack\":");
        CanonicalJsonWriter.Number(builder, startingStack);
        builder.Append(",\"smallBlind\":");
        CanonicalJsonWriter.Number(builder, options.SmallBlind);
        builder.Append(",\"bigBlind\":");
        CanonicalJsonWriter.Number(builder, options.BigBlind);
        builder.Append(",\"alternateButton\":");
        CanonicalJsonWriter.Boolean(builder, options.AlternateButton);
        builder.Append(",\"agents\":[");
        for (var index = 0; index < names.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.Text(builder, names[index]);
        }

        builder.Append("],\"manifestHash\":");
        CanonicalJsonWriter.Text(builder, manifestHash);
        builder.Append(",\"metrics\":[");
        for (var index = 0; index < metrics.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendMetric(builder, metrics[index], index);
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static void AppendMetric(StringBuilder builder, AgentMetrics metrics, int seat)
    {
        builder.Append("{\"agent\":");
        CanonicalJsonWriter.Text(builder, metrics.AgentName);
        builder.Append(",\"seat\":");
        CanonicalJsonWriter.Number(builder, seat);
        builder.Append(",\"hands\":");
        CanonicalJsonWriter.Number(builder, metrics.Hands);
        builder.Append(",\"wins\":");
        CanonicalJsonWriter.Number(builder, metrics.Wins);
        builder.Append(",\"winRate\":");
        CanonicalJsonWriter.Number(builder, metrics.WinRate);
        builder.Append(",\"winRateCi\":{\"low\":");
        CanonicalJsonWriter.Number(builder, metrics.WinRateCi.Low);
        builder.Append(",\"high\":");
        CanonicalJsonWriter.Number(builder, metrics.WinRateCi.High);
        builder.Append("},\"netChips\":");
        CanonicalJsonWriter.Number(builder, metrics.NetChips);
        builder.Append(",\"bigBlindsPer100\":");
        CanonicalJsonWriter.Number(builder, metrics.BigBlindsPer100);
        builder.Append(",\"bigBlindsPer100Ci\":{\"low\":");
        CanonicalJsonWriter.Number(builder, metrics.BigBlindsPer100Ci.Low);
        builder.Append(",\"high\":");
        CanonicalJsonWriter.Number(builder, metrics.BigBlindsPer100Ci.High);
        builder.Append("},\"aggressionFactor\":");
        CanonicalJsonWriter.Number(builder, metrics.AggressionFactor);
        builder.Append(",\"showdownRate\":");
        CanonicalJsonWriter.Number(builder, metrics.ShowdownRate);
        builder.Append('}');
    }

    private static bool AllEqual(IReadOnlyList<long> values)
    {
        if (values.Count == 0)
        {
            return false;
        }

        var first = values[0];
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index] != first)
            {
                return false;
            }
        }

        return true;
    }
}
