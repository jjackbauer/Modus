using System.Globalization;
using System.Text;

namespace Wip.Tholdem.Agents.Simulation;

/// <summary>
/// Whitespace-free, culture-free JSON primitives with a fixed escaping and number format, so two
/// runs with the same inputs produce identical bytes.
/// </summary>
internal static class CanonicalJsonWriter
{
    public static void Text(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < ' ')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    public static void Number(StringBuilder builder, long value) =>
        builder.Append(value.ToString(CultureInfo.InvariantCulture));

    public static void Number(StringBuilder builder, int value) =>
        builder.Append(value.ToString(CultureInfo.InvariantCulture));

    public static void Number(StringBuilder builder, ulong value) =>
        builder.Append(value.ToString(CultureInfo.InvariantCulture));

    public static void Boolean(StringBuilder builder, bool value) =>
        builder.Append(value ? "true" : "false");

    public static void Number(StringBuilder builder, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException("A metric must be a finite number to be serialized canonically.");
        }

        if (value == 0.0)
        {
            builder.Append('0');
            return;
        }

        builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
    }
}