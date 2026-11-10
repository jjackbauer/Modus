using System.Globalization;
using System.Text;

namespace Wip.Tholdem.Host;

/// <summary>
/// Whitespace-free, culture-free JSON primitives for the CLI's own files. Numbers use the shortest
/// round-trippable invariant form, so the same inputs always produce identical bytes.
/// </summary>
internal static class CliJson
{
    public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static string Text(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
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
        return builder.ToString();
    }

    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException("A CLI metric must be a finite number to be serialized.");
        }

        return value == 0.0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture);
    }

    public static string Format(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException("A CLI metric must be a finite number to be formatted.");
        }

        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static string Format(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Format(bool value) => value ? "true" : "false";
}
