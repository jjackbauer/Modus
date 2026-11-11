using System.Text;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>How a match treats the starting stacks across hands.</summary>
public enum SimulationMode
{
    CashIndependentHands = 0,
    TournamentPersistentStacks = 1,
}

/// <summary>A refused game configuration, carrying the stable code that names the refusal.</summary>
public sealed class GameConfigurationException : ArgumentException
{
    public GameConfigurationException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}

/// <summary>
/// The versioned, four-seat-by-default game contract. Everything the simulator needs is declared
/// here and serialized in a fixed key order, so two equal configurations always hash alike and no
/// setting is left ambient. Settings that the v1 engine cannot honour are refused by
/// <see cref="Validate"/> instead of being silently reinterpreted.
/// </summary>
public sealed record GameConfiguration
{
    public const string Schema = "tholdem.game.v1";
    public const string CashMetricId = "tholdem.cash.bb100.v1";
    public const string ButtonRulePerHand = "tholdem.button.per-hand-one-seat.v1";
    public const string SidePotRuleLayered = "tholdem.sidepot.layered.v1";
    public const int DefaultActionCapPerHand = 1000;

    /// <summary>Number of seats, 2..6.</summary>
    public int Seats { get; init; } = 4;

    public long SmallBlind { get; init; } = 5;

    public long BigBlind { get; init; } = 10;

    /// <summary>Per-seat starting stack in chips; 100 big blinds by default.</summary>
    public long StartingStack { get; init; } = 1000;

    /// <summary>
    /// Optional per-seat starting stacks. When null every seat starts with
    /// <see cref="StartingStack"/>; when supplied it holds exactly one value per seat.
    /// </summary>
    public IReadOnlyList<long>? StartingStacks { get; init; }

    public long Ante { get; init; }

    public string ButtonRule { get; init; } = ButtonRulePerHand;

    public int ActionCapPerHand { get; init; } = DefaultActionCapPerHand;

    public string SidePotRule { get; init; } = SidePotRuleLayered;

    public SimulationMode Mode { get; init; } = SimulationMode.CashIndependentHands;

    /// <summary>The cash metric this configuration is evaluated with.</summary>
    public string MetricId => CashMetricId;

    /// <summary>
    /// Refuses every setting the v1 engine cannot honour, with the design's stable refusal codes.
    /// </summary>
    public void Validate()
    {
        if (Seats is < 2 or > 6)
        {
            throw new GameConfigurationException(
                "seats-out-of-range",
                $"A table must have between 2 and 6 seats; got {Seats}.");
        }

        if (SmallBlind <= 0 || BigBlind <= 0)
        {
            throw new GameConfigurationException(
                "blind-not-positive",
                $"Both blinds must be positive; got small blind {SmallBlind} and big blind {BigBlind}.");
        }

        if (SmallBlind > BigBlind)
        {
            throw new GameConfigurationException(
                "small-blind-above-big-blind",
                $"The small blind ({SmallBlind}) cannot exceed the big blind ({BigBlind}).");
        }

        if (StartingStack < BigBlind)
        {
            throw new GameConfigurationException(
                "stack-below-big-blind",
                $"The starting stack ({StartingStack}) must cover the big blind ({BigBlind}).");
        }

        if (StartingStacks is not null)
        {
            if (StartingStacks.Count != Seats)
            {
                throw new GameConfigurationException(
                    "starting-stacks-count",
                    $"Expected one starting stack per seat ({Seats}); got {StartingStacks.Count}.");
            }

            for (var seat = 0; seat < StartingStacks.Count; seat++)
            {
                if (StartingStacks[seat] < BigBlind)
                {
                    throw new GameConfigurationException(
                        "stack-below-big-blind",
                        $"The starting stack at seat {seat} ({StartingStacks[seat]}) must cover "
                        + $"the big blind ({BigBlind}).");
                }
            }
        }

        if (Ante != 0)
        {
            throw new GameConfigurationException(
                "ante-not-supported",
                $"The v1 engine has no ante; got {Ante}.");
        }

        if (!string.Equals(ButtonRule, ButtonRulePerHand, StringComparison.Ordinal))
        {
            throw new GameConfigurationException(
                "button-rule-unsupported",
                $"Unsupported button rule '{ButtonRule}'; v1 supports only '{ButtonRulePerHand}'.");
        }

        if (!string.Equals(SidePotRule, SidePotRuleLayered, StringComparison.Ordinal))
        {
            throw new GameConfigurationException(
                "side-pot-rule-unsupported",
                $"Unsupported side-pot rule '{SidePotRule}'; v1 supports only '{SidePotRuleLayered}'.");
        }

        if (ActionCapPerHand < 1)
        {
            throw new GameConfigurationException(
                "action-cap-invalid",
                $"The per-hand action cap must be at least one; got {ActionCapPerHand}.");
        }

        if (Mode != SimulationMode.CashIndependentHands)
        {
            throw new GameConfigurationException(
                "mode-tournament-not-implemented",
                "The v1 simulator resets stacks every hand, so a persistent-stack tournament "
                + "configuration is refused rather than silently treated as cash.");
        }
    }

    /// <summary>Validates and returns a copy whose starting stacks are a defensive array.</summary>
    public GameConfiguration Normalized()
    {
        Validate();
        return this with { StartingStacks = StartingStacks?.ToArray() };
    }

    /// <summary>Maps every declared setting onto the simulator's options.</summary>
    public SimulationOptions ToSimulationOptions(ulong seed, int hands, string? historyDirectory = null)
    {
        var normalized = Normalized();
        return new SimulationOptions(
            seed,
            hands,
            normalized.StartingStack,
            normalized.SmallBlind,
            normalized.BigBlind,
            AlternateButton: true,
            HistoryDirectory: historyDirectory,
            StartingStacks: normalized.StartingStacks,
            actionCapPerHand: normalized.ActionCapPerHand);
    }

    /// <summary>The fixed-key-order, culture-free JSON the configuration hash covers.</summary>
    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(256);
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, Schema);
        builder.Append(",\"seats\":");
        CanonicalJsonWriter.Number(builder, Seats);
        builder.Append(",\"smallBlind\":");
        CanonicalJsonWriter.Number(builder, SmallBlind);
        builder.Append(",\"bigBlind\":");
        CanonicalJsonWriter.Number(builder, BigBlind);
        builder.Append(",\"startingStack\":");
        CanonicalJsonWriter.Number(builder, StartingStack);
        builder.Append(",\"startingStacks\":");
        if (StartingStacks is null)
        {
            builder.Append("null");
        }
        else
        {
            builder.Append('[');
            for (var seat = 0; seat < StartingStacks.Count; seat++)
            {
                if (seat > 0)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.Number(builder, StartingStacks[seat]);
            }

            builder.Append(']');
        }

        builder.Append(",\"ante\":");
        CanonicalJsonWriter.Number(builder, Ante);
        builder.Append(",\"buttonRule\":");
        CanonicalJsonWriter.Text(builder, ButtonRule);
        builder.Append(",\"actionCapPerHand\":");
        CanonicalJsonWriter.Number(builder, ActionCapPerHand);
        builder.Append(",\"sidePotRule\":");
        CanonicalJsonWriter.Text(builder, SidePotRule);
        builder.Append(",\"mode\":");
        CanonicalJsonWriter.Text(builder, Mode.ToString());
        builder.Append(",\"metricId\":");
        CanonicalJsonWriter.Text(builder, MetricId);
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>The content address of the canonical configuration.</summary>
    public string ConfigHash => StrategyArtifact.ComputeHash(ToCanonicalJson());

    /// <summary>The design default: four seats, 1000 chips, 5/10 blinds, cash independent hands.</summary>
    public static GameConfiguration FourSeatCash(
        long startingStack = 1000,
        long smallBlind = 5,
        long bigBlind = 10)
    {
        var configuration = new GameConfiguration
        {
            Seats = 4,
            StartingStack = startingStack,
            SmallBlind = smallBlind,
            BigBlind = bigBlind,
        };
        configuration.Validate();
        return configuration;
    }
}
