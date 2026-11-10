using System.Globalization;
using System.Text;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// An immutable proposer-policy artifact: data only, content-addressed, versioned and parent-linked.
/// Its identity is the same construction as <see cref="StrategyArtifact"/>: the hash covers the
/// canonical payload (version, parent, iteration, genome) and the identifier embeds its first eight
/// characters, so the identifier never participates in its own hash.
/// </summary>
public sealed record MetaArtifact(
    string Id,
    int Version,
    string? ParentId,
    int CreatedAtIteration,
    ProposerGenome Proposer)
{
    public string Hash => StrategyArtifact.ComputeHash(ToHashInputJson());

    public static MetaArtifact Create(ProposerGenome proposerGenome, int iteration, MetaArtifact? parent)
    {
        ArgumentNullException.ThrowIfNull(proposerGenome);

        var clamped = proposerGenome.Clamp();
        var version = parent is null ? 1 : parent.Version + 1;
        var hash = StrategyArtifact.ComputeHash(Payload(version, parent?.Id, iteration, clamped));
        var id = "tholdem-proposer-v" + version.ToString(CultureInfo.InvariantCulture) + "-" + hash[..8];
        return new MetaArtifact(id, version, parent?.Id, iteration, clamped);
    }

    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(512);
        builder.Append("{\"id\":");
        CanonicalJsonWriter.Text(builder, Id);
        builder.Append(",");
        AppendPayload(builder, Version, ParentId, CreatedAtIteration, Proposer);
        builder.Append('}');
        return builder.ToString();
    }

    public string ToHashInputJson() => Payload(Version, ParentId, CreatedAtIteration, Proposer);

    /// <summary>Reads an artifact and checks that its identifier still matches its content hash.</summary>
    public static bool TryParse(string json, out MetaArtifact? artifact)
    {
        artifact = null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return false;
            }

            var id = root.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var version = root.GetProperty("version").GetInt32();
            var parentId = root.TryGetProperty("parentId", out var parent)
                && parent.ValueKind == System.Text.Json.JsonValueKind.String
                    ? parent.GetString()
                    : null;
            var createdAtIteration = root.GetProperty("createdAtIteration").GetInt32();
            var genome = root.GetProperty("proposerGenome");
            var policyName = genome.GetProperty("parameterSelectionPolicy").GetString();
            var policy = policyName switch
            {
                "sessionDriven" => ParameterSelectionPolicy.SessionDriven,
                "cyclic" => ParameterSelectionPolicy.Cyclic,
                "aggressiveFirst" => ParameterSelectionPolicy.AggressiveFirst,
                _ => throw new FormatException("Unknown parameter selection policy."),
            };
            var proposer = new ProposerGenome(
                genome.GetProperty("mutationStep").GetDouble(),
                policy,
                genome.GetProperty("evaluationBudgetShare").GetDouble(),
                genome.GetProperty("promptTemplate").GetString() ?? StrategyGenome.DefaultPromptText,
                ReadRefinementStrategy(genome)).Clamp();
            var candidate = new MetaArtifact(id!, version, parentId, createdAtIteration, proposer);
            var suffix = "-" + candidate.Hash[..8];
            if (version < 1 || !candidate.Id.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            artifact = candidate;
            return true;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or KeyNotFoundException
            or InvalidOperationException or FormatException or OverflowException)
        {
            return false;
        }
    }

    public static string ParameterSelectionPolicyName(ParameterSelectionPolicy policy) => policy switch
    {
        ParameterSelectionPolicy.SessionDriven => "sessionDriven",
        ParameterSelectionPolicy.Cyclic => "cyclic",
        ParameterSelectionPolicy.AggressiveFirst => "aggressiveFirst",
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown parameter selection policy."),
    };

    /// <summary>The canonical wire name of the optional refinement strategy.</summary>
    public static string RefinementStrategyName(RefinementStrategy strategy) => strategy switch
    {
        RefinementStrategy.SessionDriven => "session-driven",
        RefinementStrategy.SignatureDriven => "signature-driven",
        _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown refinement strategy."),
    };

    /// <summary>
    /// Reads the optional refinement-strategy member. An absent member is the default so every
    /// pre-R-12 artifact stays readable and byte-identical; an unknown or non-string value is
    /// refused with a <see cref="FormatException"/> that callers map to their stable refusal.
    /// </summary>
    internal static RefinementStrategy ReadRefinementStrategy(System.Text.Json.JsonElement genome)
    {
        if (!genome.TryGetProperty("refinementStrategy", out var element))
        {
            return RefinementStrategy.SessionDriven;
        }

        if (element.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new FormatException("The refinement strategy must be a string.");
        }

        return element.GetString() switch
        {
            "session-driven" => RefinementStrategy.SessionDriven,
            "signature-driven" => RefinementStrategy.SignatureDriven,
            _ => throw new FormatException("Unknown refinement strategy."),
        };
    }

    private static string Payload(
        int version,
        string? parentId,
        int iteration,
        ProposerGenome proposerGenome)
    {
        var builder = new StringBuilder(512);
        builder.Append('{');
        AppendPayload(builder, version, parentId, iteration, proposerGenome);
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendPayload(
        StringBuilder builder,
        int version,
        string? parentId,
        int iteration,
        ProposerGenome proposerGenome)
    {
        builder.Append("\"version\":");
        CanonicalJsonWriter.Number(builder, version);
        builder.Append(",\"parentId\":");
        if (parentId is null)
        {
            builder.Append("null");
        }
        else
        {
            CanonicalJsonWriter.Text(builder, parentId);
        }

        builder.Append(",\"createdAtIteration\":");
        CanonicalJsonWriter.Number(builder, iteration);
        builder.Append(",\"proposerGenome\":{\"mutationStep\":");
        CanonicalJsonWriter.Number(builder, proposerGenome.MutationStep);
        builder.Append(",\"parameterSelectionPolicy\":");
        CanonicalJsonWriter.Text(builder, ParameterSelectionPolicyName(proposerGenome.ParameterSelectionPolicy));
        builder.Append(",\"evaluationBudgetShare\":");
        CanonicalJsonWriter.Number(builder, proposerGenome.EvaluationBudgetShare);
        builder.Append(",\"promptTemplate\":");
        CanonicalJsonWriter.Text(builder, proposerGenome.PromptTemplate);
        AppendRefinementStrategy(builder, proposerGenome.RefinementStrategy);
        builder.Append('}');
    }

    /// <summary>
    /// Emits the optional refinement-strategy member only when it is not the default. A default
    /// genome therefore keeps exactly the pre-R-12 bytes and content hash.
    /// </summary>
    internal static void AppendRefinementStrategy(StringBuilder builder, RefinementStrategy strategy)
    {
        if (strategy == RefinementStrategy.SessionDriven)
        {
            return;
        }

        builder.Append(",\"refinementStrategy\":");
        CanonicalJsonWriter.Text(builder, RefinementStrategyName(strategy));
    }
}

/// <summary>
/// The deterministic meta-proposer: it moves exactly one proposer-policy field per meta-iteration
/// and guarantees the result is behaviourally different from its incumbent. It generates data only;
/// it never writes, compiles or executes code.
/// </summary>
public sealed class DeterministicMetaProposer : IMetaProposer
{
    public const double StepIncrement = 0.005;

    public const double ShareIncrement = 0.05;

    public ProposerGenome Propose(ProposerGenome incumbent, int metaIteration)
    {
        ArgumentNullException.ThrowIfNull(incumbent);

        var candidates = new Func<ProposerGenome, ProposerGenome>[]
        {
            genome => genome with
            {
                MutationStep = genome.MutationStep >= ProposerGenome.MaximumMutationStep
                    ? genome.MutationStep - StepIncrement
                    : genome.MutationStep + StepIncrement,
                PromptTemplate = Annotate(genome.PromptTemplate, metaIteration),
            },
            genome => genome with { ParameterSelectionPolicy = NextPolicy(genome.ParameterSelectionPolicy) },
            genome => genome with
            {
                EvaluationBudgetShare = genome.EvaluationBudgetShare <= ProposerGenome.MinimumEvaluationBudgetShare
                    ? genome.EvaluationBudgetShare + ShareIncrement
                    : genome.EvaluationBudgetShare - ShareIncrement,
            },
            genome => genome with
            {
                MutationStep = genome.MutationStep <= ProposerGenome.MinimumMutationStep
                    ? genome.MutationStep + StepIncrement
                    : genome.MutationStep - StepIncrement,
            },
            genome => genome with
            {
                RefinementStrategy = genome.RefinementStrategy == RefinementStrategy.SessionDriven
                    ? RefinementStrategy.SignatureDriven
                    : RefinementStrategy.SessionDriven,
            },
        };

        for (var offset = 0; offset < candidates.Length; offset++)
        {
            var candidate = candidates[(metaIteration + offset) % candidates.Length](incumbent).Clamp();
            if (!candidate.BehaviorEquals(incumbent))
            {
                return candidate;
            }
        }

        // The policy-cycle candidate always differs (three distinct values exist), so this is a
        // defensive failure rather than an expected path.
        throw new InvalidOperationException("The meta-proposer could not produce a behaviourally different candidate.");
    }

    private static ParameterSelectionPolicy NextPolicy(ParameterSelectionPolicy policy) => policy switch
    {
        ParameterSelectionPolicy.SessionDriven => ParameterSelectionPolicy.Cyclic,
        ParameterSelectionPolicy.Cyclic => ParameterSelectionPolicy.AggressiveFirst,
        _ => ParameterSelectionPolicy.SessionDriven,
    };

    private static string Annotate(string template, int iteration) =>
        ProposerGenome.SanitizePrompt(
            template + " [proposer-policy " + iteration.ToString(CultureInfo.InvariantCulture) + "]");
}
