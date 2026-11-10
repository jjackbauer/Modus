using System.Text.RegularExpressions;

namespace Wip.Tholdem.ModelHarness;

/// <summary>
/// Redacts secret-shaped spans before any transcript text or hash is produced. The three shapes are
/// an <c>sk-</c> token of at least eight body characters, a <c>Bearer </c> token of at least eight
/// non-space characters, and a <c>DEEPSEEK_API_KEY=</c> value. Each whole span becomes
/// <c>[redacted]</c>, so a stored transcript never contains a key and its hash stays checkable
/// against its stored text.
/// </summary>
public static class LlmRedaction
{
    /// <summary>The literal replacement written in place of every secret-shaped span.</summary>
    public const string Replacement = "[redacted]";

    private static readonly Regex SkToken = new(
        "sk-[A-Za-z0-9_-]{8,}",
        RegexOptions.CultureInvariant);

    private static readonly Regex BearerToken = new(
        "Bearer [^\\s]{8,}",
        RegexOptions.CultureInvariant);

    private static readonly Regex EnvironmentKey = new(
        "DEEPSEEK_API_KEY=\\S+",
        RegexOptions.CultureInvariant);

    /// <summary>Returns the text with every secret-shaped span replaced by <c>[redacted]</c>.</summary>
    public static string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var redacted = SkToken.Replace(value, Replacement);
        redacted = BearerToken.Replace(redacted, Replacement);
        redacted = EnvironmentKey.Replace(redacted, Replacement);
        return redacted;
    }
}
