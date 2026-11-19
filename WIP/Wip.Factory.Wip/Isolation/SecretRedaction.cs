using System.Text.RegularExpressions;

namespace Wip.Factory.Wip.Isolation;

/// <summary>
/// Raised when child-process output cannot be redacted. The caller must refuse to return the output rather than
/// persist or prompt with an unredacted secret (v0.4 FAC-P1-06.5).
/// </summary>
public sealed class SecretRedactionException : InvalidOperationException
{
    public const string Category = "redaction-failed";

    public SecretRedactionException(string detail)
        : base(Category + ":" + detail) => Detail = detail;

    public string Detail { get; }
}

/// <summary>Redacts secret material from child-process output before it reaches a model observation or evidence.</summary>
public interface ISecretRedactor
{
    string Redact(string text, out int redactions);
}

/// <summary>
/// Versioned redaction policy: configured secret values (constructor or credential-shaped environment variables)
/// plus common token shapes. It reports how many substitutions were made and fails closed instead of returning
/// text it could not scan.
/// </summary>
public sealed class SecretRedactor : ISecretRedactor
{
    public const string PolicyVersion = "factory/redaction@1";
    public const string RedactedMarker = "[redacted]";

    private static readonly Regex[] Patterns =
    [
        new(@"(?i)\b(api[_-]?key|access[_-]?token|auth[_-]?token|token|secret|password)\s*[:=]\s*[^\s""']{8,}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"\bsk-[A-Za-z0-9_\-]{16,}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"(?i)\bBearer\s+[A-Za-z0-9\-\._~\+/]{16,}", RegexOptions.Compiled | RegexOptions.CultureInvariant)
    ];

    private readonly string[] _secrets;

    public SecretRedactor(IEnumerable<string>? secrets = null)
    {
        _secrets = (secrets ?? LoadEnvironmentSecrets())
            .Where(static secret => !string.IsNullOrWhiteSpace(secret) && secret.Length >= 8)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static secret => secret.Length)
            .ToArray();
    }

    public string Redact(string text, out int redactions)
    {
        if (text is null)
        {
            throw new SecretRedactionException("null-output");
        }

        redactions = 0;
        try
        {
            var result = text;

            foreach (var secret in _secrets)
            {
                if (result.Contains(secret, StringComparison.Ordinal))
                {
                    result = result.Replace(secret, RedactedMarker, StringComparison.Ordinal);
                    redactions++;
                }
            }

            var patternRedactions = 0;
            foreach (var pattern in Patterns)
            {
                result = pattern.Replace(result, match =>
                {
                    patternRedactions++;
                    if (match.Value.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Bearer " + RedactedMarker;
                    }

                    return match.Groups[1].Success
                        ? match.Groups[1].Value + "=" + RedactedMarker
                        : RedactedMarker;
                });
            }

            redactions += patternRedactions;
            return result;
        }
        catch (SecretRedactionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is RegexMatchTimeoutException or ArgumentException or InvalidOperationException)
        {
            throw new SecretRedactionException(exception.GetType().Name);
        }
    }

    private static IEnumerable<string> LoadEnvironmentSecrets()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = entry.Key?.ToString() ?? string.Empty;
            var value = entry.Value?.ToString() ?? string.Empty;

            if (value.Length >= 8
                && (name.Contains("API_KEY", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)))
            {
                yield return value;
            }
        }
    }
}
