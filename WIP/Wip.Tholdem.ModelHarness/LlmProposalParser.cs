using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Skills;

namespace Wip.Tholdem.ModelHarness;

/// <summary>One requested-to-clamped correction recorded while parsing a proposal.</summary>
public sealed record LlmProposalAdjustment(string Field, double Requested, double Clamped);

/// <summary>
/// The parsed, bounded proposal. <see cref="Adjustments"/> records every value the parser clamped;
/// <see cref="Reason"/> is bounded free text and is never used as a memory key.
/// </summary>
/// <remarks>
/// The record supplies value equality over <see cref="Adjustments"/> so two parses of the same text
/// compare equal. Unknown top-level JSON members are ignored deterministically by
/// <see cref="LlmProposalParser"/>; malformed required members are refused with a stable code.
/// </remarks>
public sealed record LlmProposal(
    StrategyGenome Genome,
    string? MemoryKind,
    string? MemoryKey,
    string? Reason,
    IReadOnlyList<LlmProposalAdjustment> Adjustments)
{
    public bool Equals(LlmProposal? other) =>
        other is not null
        && Genome == other.Genome
        && string.Equals(MemoryKind, other.MemoryKind, StringComparison.Ordinal)
        && string.Equals(MemoryKey, other.MemoryKey, StringComparison.Ordinal)
        && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
        && Adjustments.SequenceEqual(other.Adjustments);

    public override int GetHashCode() =>
        HashCode.Combine(Genome, MemoryKind, MemoryKey, Reason, Adjustments.Count);
}

/// <summary>A proposal refusal with a stable <see cref="Code"/>.</summary>
public sealed class LlmProposalParseException : FormatException
{
    public const string InvalidJsonCode = "invalid-json";

    public const string InvalidShapeCode = "invalid-shape";

    public const string InvalidRequiredNumberCode = "invalid-required-number";

    public const string InvalidSkillsCode = "invalid-skills";

    public const string InvalidMemoryCode = "invalid-memory";

    public const string InvalidReasonCode = "invalid-reason";

    public LlmProposalParseException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    /// <summary>The stable refusal code a caller can match without parsing the message.</summary>
    public string Code { get; }
}

/// <summary>
/// Parses exactly one model response object into a bounded <see cref="LlmProposal"/>. The accepted
/// envelope is the object surrounded by whitespace and at most one markdown code fence; a leading
/// array, trailing prose or a second object is refused. The four required numbers are never
/// defaulted: a missing, non-number or non-finite value refuses with
/// <see cref="LlmProposalParseException.InvalidRequiredNumberCode"/>. Values outside their domains
/// are clamped and every correction is recorded. Unknown top-level members are ignored
/// deterministically; an unknown <c>skills</c> or <c>memory</c> shape is refused.
/// </summary>
public static class LlmProposalParser
{
    private const int MaximumMemoryKeyLength = 128;

    private const int MaximumReasonLength = 280;

    private const int MaximumNoteLength = 280;

    private static readonly string[] SkillNames = ["preflop", "postflop", "sizing", "bluff"];

    private static readonly string[] ThresholdNames = ["raiseThreshold", "callThreshold"];

    private static readonly string[] SizingNames = ["multiplier"];

    private static readonly string[] BluffNames = ["frequency"];

    private static readonly string[] MemoryNames = ["kind", "key", "note"];

    public static LlmProposal Parse(string response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = ExtractObject(response);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new LlmProposalParseException(
                LlmProposalParseException.InvalidJsonCode,
                "The proposal is not one JSON object: " + exception.Message,
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Shape("The proposal must be exactly one JSON object.");
            }

            var adjustments = new List<LlmProposalAdjustment>();
            var raiseThreshold = RequiredNumber(root, "raiseThreshold");
            var callThreshold = RequiredNumber(root, "callThreshold");
            var bluffFrequency = RequiredNumber(root, "bluffFrequency");
            var sizingMultiplier = RequiredNumber(root, "sizingMultiplier");

            var genome = new StrategyGenome(
                Clamp(raiseThreshold, 0.0, 1.0, "raiseThreshold", adjustments),
                Clamp(callThreshold, 0.0, 1.0, "callThreshold", adjustments),
                Clamp(bluffFrequency, 0.0, 1.0, "bluffFrequency", adjustments),
                Clamp(
                    sizingMultiplier,
                    StrategyGenome.MinimumSizingMultiplier,
                    StrategyGenome.MaximumSizingMultiplier,
                    "sizingMultiplier",
                    adjustments));

            var skills = ReadSkills(root, adjustments);
            if (skills is not null)
            {
                genome = genome with { Skills = skills };
            }

            var (memoryKind, memoryKey) = ReadMemory(root);
            var reason = ReadReason(root);
            return new LlmProposal(genome, memoryKind, memoryKey, reason, adjustments);
        }
    }

    private static string ExtractObject(string response)
    {
        var text = response.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var openingLineEnd = text.IndexOf('\n');
            if (openingLineEnd < 0)
            {
                throw Shape("A code fence must contain a JSON object.");
            }

            var body = text[(openingLineEnd + 1)..].TrimEnd();
            if (!body.EndsWith("```", StringComparison.Ordinal))
            {
                throw Shape("A code fence must be closed.");
            }

            body = body[..^3];
            if (body.Contains("```", StringComparison.Ordinal))
            {
                throw Shape("At most one markdown code fence is allowed.");
            }

            text = body.Trim();
        }

        if (text.Length == 0 || text[0] != '{')
        {
            throw Shape("The response must be exactly one JSON object.");
        }

        return text;
    }

    private static StrategySkillSet? ReadSkills(
        JsonElement root,
        List<LlmProposalAdjustment> adjustments)
    {
        if (!root.TryGetProperty("skills", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireExactObject(element, "skills", SkillNames);
        var preflop = ReadThresholds(Child(element, "preflop"), "skills.preflop", adjustments);
        var postflop = ReadThresholds(Child(element, "postflop"), "skills.postflop", adjustments);
        var sizing = ReadSingleNumber(
            Child(element, "sizing"),
            "sizing",
            "multiplier",
            "skills.sizing.multiplier",
            0.25,
            3.0,
            adjustments);
        var bluff = ReadSingleNumber(
            Child(element, "bluff"),
            "bluff",
            "frequency",
            "skills.bluff.frequency",
            0.0,
            1.0,
            adjustments);

        return new StrategySkillSet(
            new PreflopSkillParameters(preflop.Raise, preflop.Call),
            new PostflopSkillParameters(postflop.Raise, postflop.Call),
            new SizingSkillParameters(sizing),
            new BluffSkillParameters(bluff)).Clamp();
    }

    private static (double Raise, double Call) ReadThresholds(
        JsonElement element,
        string path,
        List<LlmProposalAdjustment> adjustments)
    {
        RequireExactObject(element, path, ThresholdNames);
        var raise = Clamp(
            ReadFiniteNumber(element, path + ".raiseThreshold", "raiseThreshold"),
            0.0,
            1.0,
            path + ".raiseThreshold",
            adjustments);
        var call = Clamp(
            ReadFiniteNumber(element, path + ".callThreshold", "callThreshold"),
            0.0,
            1.0,
            path + ".callThreshold",
            adjustments);
        return (raise, call);
    }

    private static double ReadSingleNumber(
        JsonElement element,
        string path,
        string member,
        string adjustmentField,
        double minimum,
        double maximum,
        List<LlmProposalAdjustment> adjustments)
    {
        var names = member == "multiplier" ? SizingNames : BluffNames;
        RequireExactObject(element, path, names);
        return Clamp(
            ReadFiniteNumber(element, path + "." + member, member),
            minimum,
            maximum,
            adjustmentField,
            adjustments);
    }

    private static (string? Kind, string? Key) ReadMemory(JsonElement root)
    {
        if (!root.TryGetProperty("memory", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return (null, null);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Memory("'memory' must be an object or null");
        }

        EnsureNoUnknownMembers(
            element,
            "memory",
            MemoryNames,
            LlmProposalParseException.InvalidMemoryCode);
        if (!element.TryGetProperty("kind", out var kindElement)
            || kindElement.ValueKind != JsonValueKind.String)
        {
            throw Memory("'memory.kind' must be a string");
        }

        var kind = kindElement.GetString()!;
        if (kind is not ("pattern" or "failed-approach" or "dead-end"))
        {
            throw Memory("'memory.kind' must be pattern, failed-approach or dead-end");
        }

        if (!element.TryGetProperty("key", out var keyElement) || keyElement.ValueKind != JsonValueKind.String)
        {
            throw Memory("'memory.key' must be a string");
        }

        var key = keyElement.GetString()!;
        if (key.Length == 0 || key.Length > MaximumMemoryKeyLength)
        {
            throw Memory("'memory.key' must contain 1 to " + MaximumMemoryKeyLength + " characters");
        }

        var sanitizedKey = SanitizeKey(key);
        if (sanitizedKey.Length == 0)
        {
            throw Memory("'memory.key' has no usable characters");
        }

        if (element.TryGetProperty("note", out var noteElement)
            && noteElement.ValueKind != JsonValueKind.Null)
        {
            if (noteElement.ValueKind != JsonValueKind.String)
            {
                throw Memory("'memory.note' must be a string or null");
            }

            if ((noteElement.GetString()?.Length ?? 0) > MaximumNoteLength)
            {
                throw Memory("'memory.note' must be at most " + MaximumNoteLength + " characters");
            }
        }

        return (kind, sanitizedKey);
    }

    private static string? ReadReason(JsonElement root)
    {
        if (!root.TryGetProperty("reason", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new LlmProposalParseException(
                LlmProposalParseException.InvalidReasonCode,
                "'reason' must be a string or null.");
        }

        var reason = element.GetString()!;
        if (reason.Length > MaximumReasonLength)
        {
            throw new LlmProposalParseException(
                LlmProposalParseException.InvalidReasonCode,
                "'reason' must be at most " + MaximumReasonLength + " characters.");
        }

        return reason;
    }

    private static double RequiredNumber(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            throw InvalidNumber("'" + name + "' is required");
        }

        if (element.ValueKind != JsonValueKind.Number)
        {
            throw InvalidNumber("'" + name + "' must be a number");
        }

        if (!element.TryGetDouble(out var value) || !double.IsFinite(value))
        {
            throw InvalidNumber("'" + name + "' must be a finite number");
        }

        return value;
    }

    private static double ReadFiniteNumber(JsonElement parent, string path, string member)
    {
        if (!parent.TryGetProperty(member, out var element))
        {
            throw new LlmProposalParseException(
                LlmProposalParseException.InvalidSkillsCode,
                "'" + path + "' is required");
        }

        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out var value)
            || !double.IsFinite(value))
        {
            throw new LlmProposalParseException(
                LlmProposalParseException.InvalidSkillsCode,
                "'" + path + "' must be a finite number");
        }

        return value;
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum,
        string field,
        List<LlmProposalAdjustment> adjustments)
    {
        var clamped = Math.Clamp(value, minimum, maximum);
        if (clamped != value)
        {
            adjustments.Add(new LlmProposalAdjustment(field, value, clamped));
        }

        return clamped;
    }

    private static JsonElement Child(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            throw new LlmProposalParseException(
                LlmProposalParseException.InvalidSkillsCode,
                "'" + name + "' must be an object");
        }

        return element;
    }

    private static void RequireExactObject(
        JsonElement element,
        string path,
        IReadOnlyList<string> names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new LlmProposalParseException(
                LlmProposalParseException.InvalidSkillsCode,
                "'" + path + "' must be an object");
        }

        EnsureNoUnknownMembers(element, path, names, LlmProposalParseException.InvalidSkillsCode);
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out _))
            {
                throw new LlmProposalParseException(
                    LlmProposalParseException.InvalidSkillsCode,
                    "'" + path + "' is missing '" + name + "'");
            }
        }
    }

    private static void EnsureNoUnknownMembers(
        JsonElement element,
        string path,
        IReadOnlyList<string> names,
        string code)
    {
        foreach (var property in element.EnumerateObject())
        {
            var known = false;
            foreach (var name in names)
            {
                if (string.Equals(property.Name, name, StringComparison.Ordinal))
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                throw new LlmProposalParseException(
                    code,
                    "'" + path + "' contains an unknown member '" + property.Name + "'");
            }
        }
    }

    private static string SanitizeKey(string key)
    {
        var builder = new StringBuilder(key.Length);
        foreach (var character in key)
        {
            var lower = char.ToLowerInvariant(character);
            builder.Append(lower is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? lower : '-');
        }

        return builder.ToString();
    }

    private static LlmProposalParseException Shape(string message) =>
        new(LlmProposalParseException.InvalidShapeCode, message);

    private static LlmProposalParseException InvalidNumber(string detail) =>
        new(LlmProposalParseException.InvalidRequiredNumberCode, detail);

    private static LlmProposalParseException Memory(string detail) =>
        new(LlmProposalParseException.InvalidMemoryCode, detail);
}
