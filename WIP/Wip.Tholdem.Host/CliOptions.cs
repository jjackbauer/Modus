using System.Globalization;

namespace Wip.Tholdem.Host;

/// <summary>
/// A small deterministic option parser: long options only, <c>--name value</c> or
/// <c>--name=value</c>, plus positionals. Unknown or duplicated options are usage errors.
/// </summary>
internal sealed class CliOptions
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);
    private readonly List<string> _positionals = [];

    public IReadOnlyList<string> Positionals => _positionals;

    public static CliOptions Parse(
        string[] arguments,
        int startIndex,
        IReadOnlySet<string> flags,
        IReadOnlySet<string> valued)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var options = new CliOptions();

        for (var index = startIndex; index < arguments.Length; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                options._positionals.Add(token);
                continue;
            }

            var name = token[2..];
            string? inlineValue = null;
            var equalsIndex = name.IndexOf('=');
            if (equalsIndex >= 0)
            {
                inlineValue = name[(equalsIndex + 1)..];
                name = name[..equalsIndex];
            }

            if (name.Length == 0)
            {
                throw new CliUsageException($"the option '{token}' does not name an option.");
            }

            if (flags.Contains(name))
            {
                if (inlineValue is not null)
                {
                    throw new CliUsageException($"option '--{name}' does not take a value.");
                }

                if (!options._values.TryAdd(name, null))
                {
                    throw new CliUsageException($"option '--{name}' was supplied more than once.");
                }

                continue;
            }

            if (!valued.Contains(name))
            {
                throw new CliUsageException($"unknown option '--{name}'.");
            }

            string value;
            if (inlineValue is not null)
            {
                value = inlineValue;
            }
            else
            {
                if (index + 1 >= arguments.Length)
                {
                    throw new CliUsageException($"option '--{name}' requires a value.");
                }

                value = arguments[++index];
            }

            if (!options._values.TryAdd(name, value))
            {
                throw new CliUsageException($"option '--{name}' was supplied more than once.");
            }
        }

        return options;
    }

    public bool Flag(string name) => _values.ContainsKey(name);

    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public string Require(string name) =>
        Get(name) ?? throw new CliUsageException($"option '--{name}' is required.");

    public string Optional(string name, string defaultValue)
    {
        var value = Get(name);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    public int RequireInt(string name, int minimum)
    {
        var text = Require(name);
        return ParseInt(name, text, minimum);
    }

    public int OptionalInt(string name, int defaultValue, int minimum)
    {
        var text = Get(name);
        return string.IsNullOrWhiteSpace(text) ? defaultValue : ParseInt(name, text, minimum);
    }

    public ulong RequireUlong(string name)
    {
        var text = Require(name);
        return ParseUlong(name, text);
    }

    public ulong OptionalUlong(string name, ulong defaultValue)
    {
        var text = Get(name);
        return string.IsNullOrWhiteSpace(text) ? defaultValue : ParseUlong(name, text);
    }

    public double OptionalDouble(string name, double defaultValue)
    {
        var text = Get(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || double.IsNaN(value)
            || double.IsInfinity(value))
        {
            throw new CliUsageException($"option '--{name}' must be a finite number; observed '{text}'.");
        }

        return value;
    }

    private static int ParseInt(string name, string text, int minimum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new CliUsageException($"option '--{name}' must be an integer; observed '{text}'.");
        }

        if (value < minimum)
        {
            throw new CliUsageException($"option '--{name}' must be at least {minimum}; observed {value}.");
        }

        return value;
    }

    private static ulong ParseUlong(string name, string text)
    {
        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            throw new CliUsageException($"option '--{name}' must be a non-negative integer; observed '{text}'.");
        }

        return value;
    }
}
