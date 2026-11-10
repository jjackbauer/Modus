using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>One frozen component: canonical source digest (when source is available) and behaviour digest.</summary>
public sealed record FrozenComponent(string Name, string? SourceHash, string BehaviorHash);

/// <summary>
/// The frozen-gate manifest. <see cref="ManifestHash"/> is the SHA-256 of the canonical manifest body;
/// the body covers the version, source availability and every component's source and behaviour
/// digests. Two manifests are compatible only when every available field matches the pinned value.
/// </summary>
public sealed record FrozenManifest(
    string Version,
    bool SourcePinned,
    IReadOnlyList<FrozenComponent> Components,
    string ManifestHash,
    string SourceRoot)
{
    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(1024);
        builder.Append("{\"version\":");
        CanonicalJsonWriter.Text(builder, Version);
        builder.Append(",\"sourcePinned\":");
        CanonicalJsonWriter.Boolean(builder, SourcePinned);
        builder.Append(",\"components\":[");
        for (var index = 0; index < Components.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var component = Components[index];
            builder.Append("{\"name\":");
            CanonicalJsonWriter.Text(builder, component.Name);
            builder.Append(",\"sourceHash\":");
            if (component.SourceHash is null)
            {
                builder.Append("null");
            }
            else
            {
                CanonicalJsonWriter.Text(builder, component.SourceHash);
            }

            builder.Append(",\"behaviorHash\":");
            CanonicalJsonWriter.Text(builder, component.BehaviorHash);
            builder.Append('}');
        }

        builder.Append("]}");
        return builder.ToString();
    }
}

/// <summary>
/// The frozen components the meta-loop must never evolve: the evaluation criteria, the promotion
/// policy, the promotion verifier, the paired match evaluator and the seed derivation. They are
/// pinned independently by canonical source text (when this repository's sources are reachable) and
/// by deterministic behaviour probes that exercise the live code. The meta-loop recomputes the
/// manifest before every iteration and refuses on any mismatch.
/// </summary>
/// <remarks>
/// The source half detects an edit in the working tree of this repository. The behaviour half also
/// works from a copied build output without sources: it exercises the compiled frozen code with fixed
/// inputs. Neither half is a signature. An attacker who edits the frozen source and also rewrites
/// <see cref="Pinned"/> in this non-frozen file, then rebuilds, is not detected; that residual is
/// stated in the threat model (A14).
/// </remarks>
public static class FrozenComponents
{
    public const string Version = "tholdem-rsi-frozen-v1";

    public const string MatchReason = "frozen-manifest-match";

    public const string FsMatchReasonPrefix = "frozen-manifest:";

    /// <summary>The committed expectation. Regenerate only with a deliberate, reviewed reason.</summary>
    public static FrozenManifest Pinned { get; } = CreatePinned();

    public static IReadOnlyList<string> ComponentNames { get; } =
    [
        "EvaluationCriteria",
        "PromotionPolicy",
        "PromotionVerifier",
        "MatchEvaluator",
        "SeedDerivation",
    ];

    /// <summary>Captures the live manifestation and hashes it. Never throws for a missing source tree.</summary>
    public static FrozenManifest Capture(string? repositoryRoot = null)
    {
        var root = repositoryRoot ?? FindRepositoryRoot();
        var sourcePinned = false;
        var components = new List<FrozenComponent>(ComponentNames.Count);
        foreach (var name in ComponentNames)
        {
            var sourceFile = SourceFile(name);
            string? sourceHash = null;
            if (root is not null && sourceFile is not null)
            {
                var path = Path.Combine(root, sourceFile.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                {
                    sourcePinned = true;
                    sourceHash = HashSource(File.ReadAllText(path));
                }
            }

            components.Add(new FrozenComponent(name, sourceHash, BehaviorHash(name)));
        }

        var draft = new FrozenManifest(Version, sourcePinned, components, string.Empty, root ?? string.Empty);
        return draft with { ManifestHash = StrategyArtifact.ComputeHash(draft.ToCanonicalJson()) };
    }

    /// <summary>
    /// Compares a captured manifest with <see cref="Pinned"/>. Source fields are compared only when
    /// the capture has them; behaviour fields are always compared.
    /// </summary>
    public static bool MatchesPinned(FrozenManifest captured, out string reason) =>
        Matches(captured, Pinned, out reason);

    /// <summary>Compares a captured manifest with any expected manifest (the loop passes the pinned one).</summary>
    public static bool Matches(FrozenManifest captured, FrozenManifest expected, out string reason)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(expected);

        if (!string.Equals(captured.Version, expected.Version, StringComparison.Ordinal))
        {
            reason = FsMatchReasonPrefix + "version-mismatch";
            return false;
        }

        if (captured.Components.Count != expected.Components.Count)
        {
            reason = FsMatchReasonPrefix + "component-count-mismatch";
            return false;
        }

        for (var index = 0; index < captured.Components.Count; index++)
        {
            var actual = captured.Components[index];
            var pinned = expected.Components[index];
            if (!string.Equals(actual.Name, pinned.Name, StringComparison.Ordinal))
            {
                reason = FsMatchReasonPrefix + "component-name-mismatch";
                return false;
            }

            if (!string.Equals(actual.BehaviorHash, pinned.BehaviorHash, StringComparison.Ordinal))
            {
                reason = FsMatchReasonPrefix + "behavior-mismatch:" + actual.Name;
                return false;
            }

            if (captured.SourcePinned
                && pinned.SourceHash is not null
                && !string.Equals(actual.SourceHash, pinned.SourceHash, StringComparison.Ordinal))
            {
                reason = FsMatchReasonPrefix + "source-mismatch:" + actual.Name;
                return false;
            }
        }

        reason = MatchReason;
        return true;
    }

    /// <summary>Canonical source normalisation: no BOM, LF line endings, no trailing blanks, one final LF.</summary>
    public static string NormalizeSource(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var builder = new StringBuilder(normalized.Length + 1);
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            builder.Append(lines[index].TrimEnd(' ', '\t'));
        }

        return builder.ToString().TrimEnd('\n') + "\n";
    }

    private static string? SourceFile(string component) => component switch
    {
        "EvaluationCriteria" => "WIP/Wip.Tholdem.Agents/Learning/EvaluationCriteria.cs",
        "PromotionPolicy" => "WIP/Wip.Tholdem.Agents/Learning/PromotionPolicy.cs",
        "PromotionVerifier" => "WIP/Wip.Tholdem.Agents/Learning/PromotionVerifier.cs",
        "MatchEvaluator" => "WIP/Wip.Tholdem.Agents/Learning/MatchEvaluator.cs",
        "SeedDerivation" => "WIP/Wip.Tholdem.Agents/SeedDerivation.cs",
        _ => null,
    };

    private static string HashSource(string text) => StrategyArtifact.ComputeHash(NormalizeSource(text));

    private static string BehaviorHash(string component) =>
        StrategyArtifact.ComputeHash(BehaviorProbe(component));

    private static string BehaviorProbe(string component) => component switch
    {
        "EvaluationCriteria" => CriteriaProbe(),
        "PromotionPolicy" => PolicyProbe(),
        "PromotionVerifier" => VerifierProbe(),
        "MatchEvaluator" => EvaluatorProbe(),
        "SeedDerivation" => SeedProbe(),
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, "Unknown frozen component."),
    };

    private static string CriteriaProbe()
    {
        var criteria = new EvaluationCriteria();
        var builder = new StringBuilder(96);
        builder.Append("margin=");
        CanonicalJsonWriter.Number(builder, criteria.MarginBbPer100);
        builder.Append(";minSeeds=");
        CanonicalJsonWriter.Number(builder, criteria.MinSeeds);
        builder.Append(";z=");
        CanonicalJsonWriter.Number(builder, criteria.Z);
        return builder.ToString();
    }

    private static string PolicyProbe()
    {
        var criteria = new EvaluationCriteria();
        var policy = new StatisticalPromotionPolicy();
        var builder = new StringBuilder(256);
        AppendDecision(builder, policy.Decide(ProbeReport(6.0, 24), criteria));
        AppendDecision(builder, policy.Decide(ProbeReport(4.0, 24), criteria));
        AppendDecision(builder, policy.Decide(ProbeReport(6.0, 23), criteria));
        return builder.ToString();
    }

    private static string VerifierProbe()
    {
        var criteria = new EvaluationCriteria();
        var builder = new StringBuilder(256);
        AppendVerification(
            builder,
            PromotionVerifier.Verify(new PromotionDecision(true, "probe", 6.0, 5.0, 24), ProbeReport(6.0, 24), criteria, out var reason),
            reason);
        AppendVerification(
            builder,
            PromotionVerifier.Verify(new PromotionDecision(true, "probe", 4.0, 5.0, 24), ProbeReport(4.0, 24), criteria, out reason),
            reason);
        AppendVerification(
            builder,
            PromotionVerifier.Verify(new PromotionDecision(true, "probe", 6.0, 5.0, 23), ProbeReport(6.0, 23), criteria, out reason),
            reason);
        AppendVerification(
            builder,
            PromotionVerifier.Verify(new PromotionDecision(false, "probe", 6.0, 5.0, 24), ProbeReport(6.0, 24), criteria, out reason),
            reason);
        return builder.ToString();
    }

    private static string EvaluatorProbe()
    {
        var report = MatchEvaluator.Evaluate(
            new ProbeCallingAgent("probe-caller"),
            "probe-caller",
            new ProbeFoldingAgent("probe-folder"),
            "probe-folder",
            123_456UL,
            seeds: 3,
            handsPerMatch: 4);
        return report.ToCanonicalJson();
    }

    private static string SeedProbe()
    {
        var builder = new StringBuilder(128);
        builder.Append("derive=");
        CanonicalJsonWriter.Number(builder, SeedDerivation.Derive(42UL, 1, 0, 0, 1));
        builder.Append(";derive2=");
        CanonicalJsonWriter.Number(builder, SeedDerivation.Derive(424242UL, 1000, 1, 2, 3));
        builder.Append(";agent=");
        CanonicalJsonWriter.Number(
            builder,
            SeedDerivation.DeriveAgentSeed("probe-observation", 3, 1, 2, 4));
        return builder.ToString();
    }

    private static EvaluationReport ProbeReport(double lowerBound, int seeds) =>
        new(
            "probe-challenger",
            "probe-incumbent",
            seeds,
            4,
            10,
            lowerBound + 2.0,
            new ConfidenceInterval(lowerBound, lowerBound + 4.0),
            lowerBound,
            ChallengerWins: 2,
            IncumbentWins: 1,
            Ties: 0,
            ChallengerNetChips: 120);

    private static void AppendDecision(StringBuilder builder, PromotionDecision decision)
    {
        builder.Append(decision.Promoted ? "promoted" : "rejected");
        builder.Append('|');
        builder.Append(decision.Reason);
        builder.Append('|');
        CanonicalJsonWriter.Number(builder, decision.LowerBoundBbPer100);
        builder.Append('|');
        CanonicalJsonWriter.Number(builder, decision.MarginBbPer100);
        builder.Append('|');
        CanonicalJsonWriter.Number(builder, decision.Seeds);
        builder.Append(';');
    }

    private static void AppendVerification(StringBuilder builder, bool verified, string reason)
    {
        builder.Append(verified ? "verified" : "refused");
        builder.Append('|');
        builder.Append(reason);
        builder.Append(';');
    }

    private static FrozenManifest CreatePinned()
    {
        // Filled by the committed frozen-probe output. The versioned constant is deliberately kept in
        // this non-frozen file so a reviewed change to a frozen component is an explicit manifest bump.
        string[] names = ["EvaluationCriteria", "PromotionPolicy", "PromotionVerifier", "MatchEvaluator", "SeedDerivation"];
        var components = new List<FrozenComponent>(names.Length);
        foreach (var name in names)
        {
            components.Add(new FrozenComponent(name, PinnedSourceHash(name), PinnedBehaviorHash(name)));
        }

        var draft = new FrozenManifest(Version, true, components, string.Empty, string.Empty);
        return draft with { ManifestHash = StrategyArtifact.ComputeHash(draft.ToCanonicalJson()) };
    }

    private static string? PinnedSourceHash(string component) => component switch
    {
        "EvaluationCriteria" => "9ff873f0c7727d79957815b29556c7028b37c11e980b65bed8689ef4ee7f71ee",
        "PromotionPolicy" => "2bb7a80083500b34b43280a5adc67342f3b57c201c75b909a6dfa0a8cf788cbe",
        "PromotionVerifier" => "72252cdd9bdbf838a3ddc0a5e5abb60298ca3d356ee4bb455fd521802e0029f0",
        "MatchEvaluator" => "56c85e2de11fc5f4b30acd3423c65b6ffcea0cbaa2e7fc9a2cbe87ec8b8c251d",
        "SeedDerivation" => "ec4e7845746a930bc115387e25176ad2bf6a417261550939571630a1a944b123",
        _ => null,
    };

    private static string PinnedBehaviorHash(string component) => component switch
    {
        "EvaluationCriteria" => "b21b19ed619f668b03ee50e501e4a773c208984f7008a8328601b22fc6cca474",
        "PromotionPolicy" => "affaec7ae8027ad1ed76b488cfdc8e2fb7982ef1a5cf1e264ba3c794c66d14bc",
        "PromotionVerifier" => "6f7e6c3eaa4b1d29862298d007e8176b294080deea9521ec6b1823af298485e7",
        "MatchEvaluator" => "9194e047617c292bd9336020f87f3324204255d54108f441f929ae694f27a903",
        "SeedDerivation" => "bf19f7b3b56c786bfbc50326089f391f777771ad44b3d89013261e40889aace1",
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, "Unknown frozen component."),
    };

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wip.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private sealed class ProbeCallingAgent : IPokerAgent
    {
        public ProbeCallingAgent(string name) => Name = name;

        public string Name { get; }

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LegalActionSelection.Has(observation, ActionKind.Check))
            {
                return ValueTask.FromResult(PokerAction.Check());
            }

            if (LegalActionSelection.Has(observation, ActionKind.Call))
            {
                return ValueTask.FromResult(PokerAction.Call());
            }

            return ValueTask.FromResult(LegalActionSelection.FirstLegal(observation));
        }
    }

    private sealed class ProbeFoldingAgent : IPokerAgent
    {
        public ProbeFoldingAgent(string name) => Name = name;

        public string Name { get; }

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LegalActionSelection.Has(observation, ActionKind.Check))
            {
                return ValueTask.FromResult(PokerAction.Check());
            }

            if (LegalActionSelection.Has(observation, ActionKind.Fold))
            {
                return ValueTask.FromResult(PokerAction.Fold());
            }

            return ValueTask.FromResult(LegalActionSelection.FirstLegal(observation));
        }
    }
}
