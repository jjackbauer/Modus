using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning.Skills;

/// <summary>
/// The canonical reader and writer for the optional <c>skills</c> artifact member. The writer emits a
/// fixed key order with invariant numbers; the reader accepts exactly that shape and refuses any
/// other with one stable code.
/// </summary>
internal static class StrategySkillSetJson
{
    /// <summary>The stable prefix of every skills parse refusal.</summary>
    public const string InvalidCode = "strategy-skills-invalid";

    private static readonly string[] SkillNames = ["preflop", "postflop", "sizing", "bluff"];

    private static readonly string[] ThresholdNames = ["raiseThreshold", "callThreshold"];

    private static readonly string[] SizingNames = ["multiplier"];

    private static readonly string[] BluffNames = ["frequency"];

    public static void Write(StringBuilder builder, StrategySkillSet skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        builder.Append("{\"preflop\":{\"raiseThreshold\":");
        CanonicalJsonWriter.Number(builder, skills.Preflop.RaiseThreshold);
        builder.Append(",\"callThreshold\":");
        CanonicalJsonWriter.Number(builder, skills.Preflop.CallThreshold);
        builder.Append("},\"postflop\":{\"raiseThreshold\":");
        CanonicalJsonWriter.Number(builder, skills.Postflop.RaiseThreshold);
        builder.Append(",\"callThreshold\":");
        CanonicalJsonWriter.Number(builder, skills.Postflop.CallThreshold);
        builder.Append("},\"sizing\":{\"multiplier\":");
        CanonicalJsonWriter.Number(builder, skills.Sizing.Multiplier);
        builder.Append("},\"bluff\":{\"frequency\":");
        CanonicalJsonWriter.Number(builder, skills.Bluff.Frequency);
        builder.Append("}}");
    }

    public static StrategySkillSet Read(JsonElement element)
    {
        RequireObject(element, "skills", SkillNames);

        var preflop = ReadThresholds(Child(element, "preflop"), "preflop");
        var postflop = ReadThresholds(Child(element, "postflop"), "postflop");
        var sizing = ReadSizing(Child(element, "sizing"));
        var bluff = ReadBluff(Child(element, "bluff"));

        return new StrategySkillSet(
            new PreflopSkillParameters(preflop.Raise, preflop.Call),
            new PostflopSkillParameters(postflop.Raise, postflop.Call),
            new SizingSkillParameters(sizing),
            new BluffSkillParameters(bluff));
    }

    private static (double Raise, double Call) ReadThresholds(JsonElement element, string name)
    {
        RequireObject(element, name, ThresholdNames);
        return (
            ReadNumber(Child(element, "raiseThreshold"), name + ".raiseThreshold"),
            ReadNumber(Child(element, "callThreshold"), name + ".callThreshold"));
    }

    private static double ReadSizing(JsonElement element)
    {
        RequireObject(element, "sizing", SizingNames);
        return ReadNumber(Child(element, "multiplier"), "sizing.multiplier");
    }

    private static double ReadBluff(JsonElement element)
    {
        RequireObject(element, "bluff", BluffNames);
        return ReadNumber(Child(element, "frequency"), "bluff.frequency");
    }

    private static double ReadNumber(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value))
        {
            throw Invalid(path + " must be a number");
        }

        return value;
    }

    private static JsonElement Child(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var child))
        {
            throw Invalid("missing '" + name + "'");
        }

        return child;
    }

    private static void RequireObject(JsonElement element, string path, IReadOnlyList<string> names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(path + " must be an object");
        }

        var count = 0;
        foreach (var property in element.EnumerateObject())
        {
            count++;
            if (!Contains(names, property.Name))
            {
                throw Invalid(path + " contains an unknown member '" + property.Name + "'");
            }
        }

        if (count != names.Count)
        {
            throw Invalid(path + " must contain exactly " + names.Count + " members");
        }
    }

    private static bool Contains(IReadOnlyList<string> names, string name)
    {
        for (var index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static FormatException Invalid(string detail) =>
        new(InvalidCode + ": " + detail + ".");
}