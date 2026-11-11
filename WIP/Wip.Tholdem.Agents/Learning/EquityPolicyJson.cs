using System.Globalization;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// The canonical data-only artifact for <see cref="EquityPolicy"/>: a fixed key order, invariant
/// numbers, a strict parse that reconstructs the policy and then re-serializes it, and a content
/// address over exactly the persisted bytes. There is no clock, identifier generator or culture in
/// the artifact.
/// </summary>
/// <remarks>
/// Schema <c>tholdem.equity-policy.v1</c> carries the five tunable policy members. The record's
/// <see cref="EquityPolicy.PreflopRaiseMargin"/> is deliberately not a v1 artifact member: it is a
/// frozen component, so a parsed policy always carries the record default. <see cref="Write"/>
/// refuses a non-default margin instead of silently persisting bytes whose evaluation would differ
/// from the supplied record; widening the artifact requires a new schema version.
/// </remarks>
public static class EquityPolicyJson
{
    public const string Schema = "tholdem.equity-policy.v1";

    /// <summary>The stable prefix of a refusal for input that is not JSON.</summary>
    public const string NotJsonCode = "equity-policy-not-json";

    /// <summary>The stable prefix of a refusal for a structurally invalid or out-of-domain policy.</summary>
    public const string ShapeInvalidCode = "equity-policy-shape-invalid";

    /// <summary>The stable prefix of a refusal for a valid policy that is not the canonical bytes.</summary>
    public const string NotCanonicalCode = "equity-policy-not-canonical";

    private const int MinimumEquitySamples = 50;

    private const int MaximumEquitySamples = 5000;

    private const double MinimumValueBetPotFraction = 0.01;

    private const double MaximumValueBetPotFraction = 2.0;

    private static readonly EquityPolicy Defaults = new();

    /// <summary>The fixed-key-order, whitespace-free canonical serialization.</summary>
    public static string Write(EquityPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Validate(policy);

        var builder = new StringBuilder(192);
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, Schema);
        builder.Append(",\"equitySamples\":");
        CanonicalJsonWriter.Number(builder, policy.EquitySamples);
        builder.Append(",\"valueEdge\":");
        CanonicalJsonWriter.Number(builder, policy.ValueEdge);
        builder.Append(",\"raiseEdge\":");
        CanonicalJsonWriter.Number(builder, policy.RaiseEdge);
        builder.Append(",\"bluffFrequency\":");
        CanonicalJsonWriter.Number(builder, policy.BluffFrequency);
        builder.Append(",\"valueBetPotFraction\":");
        CanonicalJsonWriter.Number(builder, policy.ValueBetPotFraction);
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// Strictly reads the canonical artifact. The policy is reconstructed and its canonical
    /// serialization must equal the input exactly; anything else is refused with one stable code.
    /// </summary>
    public static EquityPolicy Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException(NotJsonCode + ": " + exception.Message, exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("the policy must be a JSON object");
            }

            var schema = Required(root, "schema");
            if (schema.ValueKind != JsonValueKind.String
                || !string.Equals(schema.GetString(), Schema, StringComparison.Ordinal))
            {
                throw Invalid("'schema' must be '" + Schema + "'");
            }

            var policy = new EquityPolicy(
                ReadInt(root, "equitySamples"),
                ReadFiniteDouble(root, "valueEdge"),
                ReadFiniteDouble(root, "raiseEdge"),
                ReadFiniteDouble(root, "bluffFrequency"),
                ReadFiniteDouble(root, "valueBetPotFraction"));
            Validate(policy);

            var canonical = Write(policy);
            if (!string.Equals(canonical, json, StringComparison.Ordinal))
            {
                throw new FormatException(
                    NotCanonicalCode + ": the policy is not the canonical serialization; expected "
                    + canonical + ".");
            }

            return policy;
        }
    }

    /// <summary>The content address over the canonical artifact bytes.</summary>
    public static string Hash(EquityPolicy policy) => StrategyArtifact.ComputeHash(Write(policy));

    private static void Validate(EquityPolicy policy)
    {
        if (policy.EquitySamples is < MinimumEquitySamples or > MaximumEquitySamples)
        {
            throw Invalid(
                "'equitySamples' must be in [" + MinimumEquitySamples + ", "
                + MaximumEquitySamples + "]");
        }

        RequireUnit(policy.ValueEdge, "valueEdge");
        RequireUnit(policy.RaiseEdge, "raiseEdge");
        RequireUnit(policy.BluffFrequency, "bluffFrequency");

        if (!double.IsFinite(policy.ValueBetPotFraction)
            || policy.ValueBetPotFraction <= MinimumValueBetPotFraction
            || policy.ValueBetPotFraction > MaximumValueBetPotFraction)
        {
            throw Invalid(
                "'valueBetPotFraction' must be in (" + MinimumValueBetPotFraction.ToString(CultureInfo.InvariantCulture)
                + ", " + MaximumValueBetPotFraction.ToString(CultureInfo.InvariantCulture) + "]");
        }

        if (policy.PreflopRaiseMargin != Defaults.PreflopRaiseMargin)
        {
            throw Invalid(
                "'preflopRaiseMargin' is not a member of schema '" + Schema
                + "'; a parsed policy uses the default "
                + Defaults.PreflopRaiseMargin.ToString(CultureInfo.InvariantCulture)
                + " and a different margin requires a new schema version");
        }
    }

    private static void RequireUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
        {
            throw Invalid("'" + name + "' must be a finite number in [0, 1]");
        }
    }

    private static JsonElement Required(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            throw Invalid("missing member '" + name + "'");
        }

        return value;
    }

    private static int ReadInt(JsonElement root, string name)
    {
        var element = Required(root, name);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw Invalid("'" + name + "' must be an integer");
        }

        return value;
    }

    private static double ReadFiniteDouble(JsonElement root, string name)
    {
        var element = Required(root, name);
        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out var value)
            || !double.IsFinite(value))
        {
            throw Invalid("'" + name + "' must be a finite number");
        }

        return value;
    }

    private static FormatException Invalid(string detail) =>
        new(ShapeInvalidCode + ": " + detail + ".");
}
