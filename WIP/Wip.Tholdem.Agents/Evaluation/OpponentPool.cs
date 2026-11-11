using System.Text;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>The five frozen opponent behaviours the evaluation population is built from.</summary>
public enum OpponentArchetype
{
    TightPassive,
    TightAggressive,
    LooseAggressive,
    CallingStation,
    AdversarialRaiser,
}

/// <summary>One immutable, versioned opponent genome in a pool.</summary>
public sealed record OpponentSpec(
    string OpponentId,
    int Version,
    OpponentArchetype Archetype,
    string DisplayName,
    StrategyGenome Genome);

/// <summary>A stable menu entry naming the opponents (in seat order) for one composition.</summary>
public sealed record OpponentComposition(string CompositionId, IReadOnlyList<string> OpponentIds);

/// <summary>
/// A frozen, content-addressed opponent population. Every pool is immutable, its canonical JSON has
/// a fixed key order and its hash is derived from that JSON, so equal pools always hash alike and no
/// clock, culture or ambient randomness participates in the identity. The holdout pool uses disjoint
/// opponent ids so it can never be confused with the selection pool.
/// </summary>
public sealed class FrozenOpponentPool
{
    public const string Schema = "tholdem.opponent-pool.v1";

    public const string SelectionPoolId = "tholdem.opponents.v1";

    public const string HoldoutPoolId = "tholdem.opponents.holdout.v1";

    public const string SelectionVersion = "1.0.0";

    public const string HoldoutVersion = "1.0.0-holdout";

    public const int OpponentVersion = 1;

    private readonly Dictionary<string, OpponentSpec> _opponentsById;
    private readonly Dictionary<string, OpponentComposition> _compositionsById;

    private FrozenOpponentPool(
        string poolId,
        string version,
        OpponentSpec[] opponents,
        OpponentComposition[] compositions)
    {
        PoolId = poolId;
        Version = version;
        Opponents = Array.AsReadOnly(opponents);
        Compositions = Array.AsReadOnly(compositions);
        _opponentsById = opponents.ToDictionary(spec => spec.OpponentId, StringComparer.Ordinal);
        _compositionsById = compositions.ToDictionary(composition => composition.CompositionId, StringComparer.Ordinal);
        PoolHash = StrategyArtifact.ComputeHash(ToCanonicalJson());
    }

    /// <summary>The selection pool used for training and promotion decisions.</summary>
    public static FrozenOpponentPool SelectionV1 { get; } = BuildSelectionV1();

    /// <summary>The disjoint holdout pool used to check that a strategy generalizes.</summary>
    public static FrozenOpponentPool HoldoutV1 { get; } = BuildHoldoutV1();

    public string PoolId { get; }

    public string Version { get; }

    public IReadOnlyList<OpponentSpec> Opponents { get; }

    public IReadOnlyList<OpponentComposition> Compositions { get; }

    public string PoolHash { get; }

    public OpponentSpec Get(string opponentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(opponentId);
        return _opponentsById.TryGetValue(opponentId, out var spec)
            ? spec
            : throw new KeyNotFoundException($"No opponent '{opponentId}' exists in pool '{PoolId}'.");
    }

    public OpponentComposition GetComposition(string compositionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(compositionId);
        return _compositionsById.TryGetValue(compositionId, out var composition)
            ? composition
            : throw new KeyNotFoundException($"No composition '{compositionId}' exists in pool '{PoolId}'.");
    }

    /// <summary>Builds a fresh deterministic agent; the agent name is the stable opponent id.</summary>
    public IPokerAgent Create(string opponentId)
    {
        var spec = Get(opponentId);
        return new StrategyAgent(spec.Genome, spec.OpponentId);
    }

    /// <summary>The persisted form: fixed key order and no timestamp anywhere.</summary>
    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(1024);
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, Schema);
        builder.Append(",\"poolId\":");
        CanonicalJsonWriter.Text(builder, PoolId);
        builder.Append(",\"version\":");
        CanonicalJsonWriter.Text(builder, Version);
        builder.Append(",\"opponents\":[");
        for (var index = 0; index < Opponents.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var spec = Opponents[index];
            builder.Append("{\"id\":");
            CanonicalJsonWriter.Text(builder, spec.OpponentId);
            builder.Append(",\"version\":");
            CanonicalJsonWriter.Number(builder, spec.Version);
            builder.Append(",\"archetype\":");
            CanonicalJsonWriter.Text(builder, ArchetypeText(spec.Archetype));
            builder.Append(",\"displayName\":");
            CanonicalJsonWriter.Text(builder, spec.DisplayName);
            builder.Append(",\"genome\":{\"raiseThreshold\":");
            CanonicalJsonWriter.Number(builder, spec.Genome.RaiseThreshold);
            builder.Append(",\"callThreshold\":");
            CanonicalJsonWriter.Number(builder, spec.Genome.CallThreshold);
            builder.Append(",\"bluffFrequency\":");
            CanonicalJsonWriter.Number(builder, spec.Genome.BluffFrequency);
            builder.Append(",\"sizingMultiplier\":");
            CanonicalJsonWriter.Number(builder, spec.Genome.SizingMultiplier);
            builder.Append("}}");
        }

        builder.Append("],\"compositions\":[");
        for (var index = 0; index < Compositions.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var composition = Compositions[index];
            builder.Append("{\"id\":");
            CanonicalJsonWriter.Text(builder, composition.CompositionId);
            builder.Append(",\"opponents\":[");
            for (var opponentIndex = 0; opponentIndex < composition.OpponentIds.Count; opponentIndex++)
            {
                if (opponentIndex > 0)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.Text(builder, composition.OpponentIds[opponentIndex]);
            }

            builder.Append("]}");
        }

        builder.Append("]}");
        return builder.ToString();
    }

    /// <summary>The stable JSON spelling of an archetype (kebab-case, culture-free).</summary>
    public static string ArchetypeText(OpponentArchetype archetype) => archetype switch
    {
        OpponentArchetype.TightPassive => "tight-passive",
        OpponentArchetype.TightAggressive => "tight-aggressive",
        OpponentArchetype.LooseAggressive => "loose-aggressive",
        OpponentArchetype.CallingStation => "calling-station",
        OpponentArchetype.AdversarialRaiser => "adversarial-raiser",
        _ => throw new ArgumentOutOfRangeException(nameof(archetype), archetype, "Unknown archetype."),
    };

    private static FrozenOpponentPool BuildSelectionV1()
    {
        var opponents = new[]
        {
            Spec("tight-passive", OpponentArchetype.TightPassive, "Tight Passive", 0.88, 0.55, 0.00, 1.0),
            Spec("tight-aggressive", OpponentArchetype.TightAggressive, "Tight Aggressive", 0.72, 0.45, 0.12, 1.0),
            Spec("loose-aggressive", OpponentArchetype.LooseAggressive, "Loose Aggressive", 0.45, 0.30, 0.40, 1.6),
            Spec("calling-station", OpponentArchetype.CallingStation, "Calling Station", 0.97, 0.05, 0.00, 1.0),
            Spec("adversarial-raiser", OpponentArchetype.AdversarialRaiser, "Adversarial Raiser", 0.30, 0.40, 0.30, 2.4),
        };
        var compositions = new[]
        {
            Composition("primary-mixed", "tight-aggressive", "loose-aggressive", "calling-station"),
            Composition("three-tag", "tight-aggressive", "tight-aggressive", "tight-aggressive"),
            Composition("three-station", "calling-station", "calling-station", "calling-station"),
            Composition("pressure", "loose-aggressive", "adversarial-raiser", "tight-aggressive"),
        };
        return new FrozenOpponentPool(SelectionPoolId, SelectionVersion, opponents, compositions);
    }

    private static FrozenOpponentPool BuildHoldoutV1()
    {
        var opponents = new[]
        {
            Spec("holdout-tp", OpponentArchetype.TightPassive, "Holdout Tight Passive", 0.84, 0.58, 0.02, 1.1),
            Spec("holdout-tag", OpponentArchetype.TightAggressive, "Holdout Tight Aggressive", 0.76, 0.48, 0.09, 0.9),
            Spec("holdout-lag", OpponentArchetype.LooseAggressive, "Holdout Loose Aggressive", 0.49, 0.33, 0.36, 1.5),
            Spec("holdout-station", OpponentArchetype.CallingStation, "Holdout Calling Station", 0.95, 0.08, 0.01, 1.05),
            Spec("holdout-adv", OpponentArchetype.AdversarialRaiser, "Holdout Adversarial Raiser", 0.34, 0.43, 0.27, 2.2),
        };
        var compositions = new[]
        {
            Composition("primary-mixed", "holdout-tag", "holdout-lag", "holdout-station"),
            Composition("three-tag", "holdout-tag", "holdout-tag", "holdout-tag"),
            Composition("three-station", "holdout-station", "holdout-station", "holdout-station"),
            Composition("pressure", "holdout-lag", "holdout-adv", "holdout-tag"),
        };
        return new FrozenOpponentPool(HoldoutPoolId, HoldoutVersion, opponents, compositions);
    }

    private static OpponentSpec Spec(
        string opponentId,
        OpponentArchetype archetype,
        string displayName,
        double raiseThreshold,
        double callThreshold,
        double bluffFrequency,
        double sizingMultiplier)
        => new(
            opponentId,
            OpponentVersion,
            archetype,
            displayName,
            new StrategyGenome(raiseThreshold, callThreshold, bluffFrequency, sizingMultiplier));

    private static OpponentComposition Composition(string compositionId, params string[] opponentIds)
        => new(compositionId, Array.AsReadOnly(opponentIds));
}
