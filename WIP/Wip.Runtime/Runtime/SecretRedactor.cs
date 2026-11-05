using System.Text.RegularExpressions;
using Wip.Abstractions.Privacy;

namespace Wip.Runtime.Runtime;

/// <summary>
/// Default secret redactor (WP-5.2). It recognises common credential shapes: provider API keys,
/// bearer tokens, key/value assignments for secret-like names, and PEM private-key blocks. The
/// patterns are intentionally conservative so ordinary prose and code survive unchanged.
/// </summary>
public sealed partial class SecretRedactor : ISecretRedactor
{
    public const string Replacement = "[REDACTED]";

    private readonly IReadOnlyList<Regex> _patterns;

    public SecretRedactor(IEnumerable<string>? additionalPatterns = null)
    {
        var patterns = new List<Regex>
        {
            ApiKeyPattern(),
            BearerPattern(),
            AssignmentPattern(),
            PrivateKeyPattern(),
        };

        if (additionalPatterns is not null)
        {
            foreach (var pattern in additionalPatterns)
            {
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    patterns.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
                }
            }
        }

        _patterns = patterns;
    }

    public static SecretRedactor Default { get; } = new();

    public RedactionResult Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new RedactionResult(value, 0);
        }

        var current = value;
        var count = 0;
        foreach (var pattern in _patterns)
        {
            current = pattern.Replace(current, match =>
            {
                count++;
                // Preserve the assignment name (for example "apiKey=") and redact only the value.
                var nameGroup = match.Groups["name"];
                return nameGroup.Success
                    ? $"{nameGroup.Value}{Replacement}"
                    : Replacement;
            });
        }

        return new RedactionResult(current, count);
    }

    [GeneratedRegex(@"\bsk-[A-Za-z0-9][A-Za-z0-9_-]{15,}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]{12,}=*", RegexOptions.Compiled | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"(?i)(?<name>\b(?:api[_-]?key|apikey|access[_-]?token|auth[_-]?token|secret|password|client[_-]?secret)\b\s*[:=]\s*""?)(?<value>[A-Za-z0-9\-._~+/]{8,})", RegexOptions.Compiled | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AssignmentPattern();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PrivateKeyPattern();
}
