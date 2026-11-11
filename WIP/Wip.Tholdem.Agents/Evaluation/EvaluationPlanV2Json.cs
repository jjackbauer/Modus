using System.Globalization;
using System.Text.Json;

namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>
/// Strict persistence boundary for the pre-registered evaluation plan. <see cref="Write"/> emits the
/// plan's canonical JSON; <see cref="Parse"/> trusts nothing: it reads every declared member,
/// re-derives the four protected seed blocks from the embedded games and domains, rebuilds the plan
/// through <see cref="EvaluationPlanV2.Create"/>, and requires the result to reproduce the input
/// byte-for-byte. A failure throws <see cref="FormatException"/> whose message starts with one of
/// <see cref="NotJsonReason"/>, <see cref="ShapeReason"/>, <see cref="NotCanonicalReason"/> or
/// <see cref="SeedMismatchReason"/>.
/// </summary>
public static class EvaluationPlanV2Json
{
    public const string NotJsonReason = "plan-not-json";

    public const string ShapeReason = "plan-shape-invalid";

    public const string NotCanonicalReason = "plan-not-canonical";

    public const string SeedMismatchReason = "plan-seed-mismatch";

    private const string DomainSchema = "tholdem.seed-domain.v1";

    private static readonly SeedPurpose[] PurposeOrder =
    {
        SeedPurpose.Training, SeedPurpose.Selection, SeedPurpose.Holdout, SeedPurpose.Audit,
    };

    /// <summary>The plan's canonical JSON, the only form <see cref="Parse"/> accepts.</summary>
    public static string Write(EvaluationPlanV2 plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ToCanonicalJson();
    }

    /// <summary>
    /// Parses a persisted plan strictly. Whitespace, key reordering, extra members, non-canonical
    /// numbers/escapes and a stored seed block that does not match its derivation are refused. A
    /// semantic change that is itself re-serialized canonically parses, but its <see cref="EvaluationPlanV2.PlanHash"/>
    /// differs, so callers must compare the hash, not merely accept a successful parse.
    /// </summary>
    public static EvaluationPlanV2 Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw NotJson("the plan must be a JSON object.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException($"{NotJsonReason}: {exception.Message}", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Shape("the plan root must be a JSON object.");
            }

            var created = ReadPlan(root);
            if (!string.Equals(created.ToCanonicalJson(), json, StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"{NotCanonicalReason}: the plan is not in its canonical form.");
            }

            return created;
        }
    }

    private static EvaluationPlanV2 ReadPlan(JsonElement root)
    {
        RequireSchema(root, "schema", EvaluationPlanV2.Schema, "plan schema");
        RequireSchema(
            root,
            "evaluatorVersion",
            EvaluationPlanV2.EvaluatorVersion,
            "evaluator version");

        var game = ReadGame(RequireObject(root, "game", "plan game"));
        var opponentPoolId = ReadString(root, "opponentPoolId", "plan opponent pool id");
        var compositions = ReadStringArray(root, "compositions", "plan compositions");
        var domains = ReadDomains(root);
        var handsPerSeatMatch = ReadInt32(root, "handsPerSeatMatch", "plan hands per seat match");
        var minimumPracticalEffectBbPer100 = ReadDouble(
            root,
            "minimumPracticalEffectBbPer100",
            "plan minimum practical effect");
        var confidenceLevel = ReadDouble(root, "confidenceLevel", "plan confidence level");
        var plannedCandidates = ReadInt32(root, "plannedCandidates", "plan planned candidates");
        var anchorRegressionToleranceBbPer100 = ReadDouble(
            root,
            "anchorRegressionToleranceBbPer100",
            "plan anchor regression tolerance");

        try
        {
            return EvaluationPlanV2.Create(
                game,
                opponentPoolId,
                compositions,
                domains,
                handsPerSeatMatch,
                minimumPracticalEffectBbPer100,
                confidenceLevel,
                plannedCandidates,
                anchorRegressionToleranceBbPer100);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw Shape(exception.Message);
        }
    }

    private static GameConfiguration ReadGame(JsonElement game)
    {
        RequireSchema(game, "schema", GameConfiguration.Schema, "game schema");
        var startingStacks = ReadNullableInt64Array(game, "startingStacks", "game starting stacks");
        var modeText = ReadString(game, "mode", "game mode");
        var mode = modeText switch
        {
            "CashIndependentHands" => SimulationMode.CashIndependentHands,
            "TournamentPersistentStacks" => SimulationMode.TournamentPersistentStacks,
            _ => throw Shape($"game mode '{modeText}' is not a declared simulation mode."),
        };

        var metricId = ReadString(game, "metricId", "game metric id");
        if (!string.Equals(metricId, GameConfiguration.CashMetricId, StringComparison.Ordinal))
        {
            throw Shape($"game metric id '{metricId}' is not the declared cash metric.");
        }

        return new GameConfiguration
        {
            Seats = ReadInt32(game, "seats", "game seats"),
            SmallBlind = ReadInt64(game, "smallBlind", "game small blind"),
            BigBlind = ReadInt64(game, "bigBlind", "game big blind"),
            StartingStack = ReadInt64(game, "startingStack", "game starting stack"),
            StartingStacks = startingStacks,
            Ante = ReadInt64(game, "ante", "game ante"),
            ButtonRule = ReadString(game, "buttonRule", "game button rule"),
            ActionCapPerHand = ReadInt32(game, "actionCapPerHand", "game action cap per hand"),
            SidePotRule = ReadString(game, "sidePotRule", "game side pot rule"),
            Mode = mode,
        };
    }

    private static SeedDomain[] ReadDomains(JsonElement root)
    {
        var element = RequireArray(root, "domains", "plan domains");
        var domains = new List<SeedDomain>();
        var byPurpose = new Dictionary<SeedPurpose, SeedDomain>();
        foreach (var domainElement in element.EnumerateArray())
        {
            if (domainElement.ValueKind != JsonValueKind.Object)
            {
                throw Shape("every plan seed domain must be a JSON object.");
            }

            var domain = ReadDomain(domainElement);
            if (!byPurpose.TryAdd(domain.Purpose, domain))
            {
                throw Shape(
                    $"the plan declares the seed purpose '{domain.Purpose}' more than once.");
            }

            domains.Add(domain);
        }

        foreach (var purpose in PurposeOrder)
        {
            if (!byPurpose.ContainsKey(purpose))
            {
                throw Shape($"the plan is missing the required '{purpose}' seed domain.");
            }
        }

        return domains.ToArray();
    }

    private static SeedDomain ReadDomain(JsonElement element)
    {
        RequireSchema(element, "schema", DomainSchema, "seed domain schema");
        var domainId = ReadString(element, "domainId", "seed domain id");
        var purposeText = ReadString(element, "purpose", "seed domain purpose");
        var purpose = purposeText switch
        {
            "Training" => SeedPurpose.Training,
            "Selection" => SeedPurpose.Selection,
            "Holdout" => SeedPurpose.Holdout,
            "Audit" => SeedPurpose.Audit,
            _ => throw Shape($"seed domain purpose '{purposeText}' is not declared."),
        };
        var baseSeed = ReadUInt64(element, "baseSeed", "seed domain base seed");
        var count = ReadInt32(element, "count", "seed domain count");
        var storedSeeds = ReadUInt64Array(element, "seeds", "seed domain seeds");

        SeedDomain domain;
        try
        {
            domain = new SeedDomain(domainId, purpose, baseSeed, count);
        }
        catch (ArgumentException exception)
        {
            throw Shape(exception.Message);
        }

        var derivedSeeds = domain.Seeds;
        if (storedSeeds.Length != derivedSeeds.Count)
        {
            throw new FormatException(
                $"{SeedMismatchReason}: domain '{domainId}' stores {storedSeeds.Length} seeds, "
                + $"but the derivation yields {derivedSeeds.Count}.");
        }

        for (var index = 0; index < storedSeeds.Length; index++)
        {
            if (storedSeeds[index] != derivedSeeds[index])
            {
                throw new FormatException(
                    $"{SeedMismatchReason}: domain '{domainId}' stored seed {index} is "
                    + $"{storedSeeds[index].ToString(CultureInfo.InvariantCulture)}, but the "
                    + $"derivation yields {derivedSeeds[index].ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        return domain;
    }

    private static void RequireSchema(
        JsonElement parent,
        string member,
        string expected,
        string context)
    {
        var actual = ReadString(parent, member, context);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw Shape($"{context} '{actual}' is not supported; expected '{expected}'.");
        }
    }

    private static JsonElement RequireObject(JsonElement parent, string member, string context)
    {
        var element = Require(parent, member, context);
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Shape($"{context} must be a JSON object.");
        }

        return element;
    }

    private static JsonElement RequireArray(JsonElement parent, string member, string context)
    {
        var element = Require(parent, member, context);
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Shape($"{context} must be a JSON array.");
        }

        return element;
    }

    private static JsonElement Require(JsonElement parent, string member, string context)
    {
        if (!parent.TryGetProperty(member, out var element))
        {
            throw Shape($"{context} requires the member '{member}'.");
        }

        return element;
    }

    private static string ReadString(JsonElement parent, string member, string context)
    {
        var element = Require(parent, member, context);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Shape($"{context} must be a JSON string.");
        }

        return element.GetString()!;
    }

    private static int ReadInt32(JsonElement parent, string member, string context)
    {
        var element = Require(parent, member, context);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw Shape($"{context} must be a JSON integer.");
        }

        return value;
    }

    private static long ReadInt64(JsonElement parent, string member, string context)
    {
        var element = Require(parent, member, context);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value))
        {
            throw Shape($"{context} must be a JSON integer.");
        }

        return value;
    }

    private static ulong ReadUInt64(JsonElement parent, string member, string context)
    {
        var element = Require(parent, member, context);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetUInt64(out var value))
        {
            throw Shape($"{context} must be a JSON unsigned integer.");
        }

        return value;
    }

    private static double ReadDouble(JsonElement parent, string member, string context)
    {
        var element = Require(parent, member, context);
        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out var value)
            || !double.IsFinite(value))
        {
            throw Shape($"{context} must be a finite JSON number.");
        }

        return value;
    }

    private static string[] ReadStringArray(JsonElement parent, string member, string context)
    {
        var element = RequireArray(parent, member, context);
        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Shape($"{context} must contain only JSON strings.");
            }

            values.Add(item.GetString()!);
        }

        return values.ToArray();
    }

    private static ulong[] ReadUInt64Array(JsonElement parent, string member, string context)
    {
        var element = RequireArray(parent, member, context);
        var values = new List<ulong>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetUInt64(out var value))
            {
                throw Shape($"{context} must contain only JSON unsigned integers.");
            }

            values.Add(value);
        }

        return values.ToArray();
    }

    private static long[]? ReadNullableInt64Array(
        JsonElement parent,
        string member,
        string context)
    {
        var element = Require(parent, member, context);
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Shape($"{context} must be null or a JSON array of integers.");
        }

        var values = new List<long>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out var value))
            {
                throw Shape($"{context} must contain only JSON integers.");
            }

            values.Add(value);
        }

        return values.ToArray();
    }

    private static FormatException NotJson(string detail) => new($"{NotJsonReason}: {detail}");

    private static FormatException Shape(string detail) => new($"{ShapeReason}: {detail}");
}
