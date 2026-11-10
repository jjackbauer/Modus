using Wip.Runtime.Runtime;

namespace Wip.Tholdem.ModelHarness;

/// <summary>
/// The live DeepSeek configuration resolved from the process environment after the operator secret
/// file has been loaded. The object deliberately stores no key: <see cref="CreateChatModel"/> passes
/// the environment source to the provider adapter, which reads the variable only when it sends.
/// A missing flag, a missing key or an invalid base URL refuses before any HTTP client is used.
/// </summary>
public sealed class LiveModelConfiguration
{
    /// <summary>The flag that must be 1 or true before the live path can be resolved.</summary>
    public const string FlagVariable = "THOLDEM_LIVE_MODEL";

    /// <summary>The key variable that must be non-empty; its value is never stored here.</summary>
    public const string ApiKeyVariable = "DEEPSEEK_API_KEY";

    /// <summary>The optional base URL override.</summary>
    public const string BaseUrlVariable = "DEEPSEEK_BASE_URL";

    /// <summary>The endpoint used when <see cref="BaseUrlVariable"/> is absent.</summary>
    public const string DefaultBaseUrl = "https://api.deepseek.com";

    private const string ApiKeyEnvironmentSource = "env:" + ApiKeyVariable;

    private LiveModelConfiguration(Uri baseUrl)
    {
        BaseUrl = baseUrl;
    }

    /// <summary>The validated absolute HTTP or HTTPS endpoint.</summary>
    public Uri BaseUrl { get; }

    /// <summary>
    /// Resolves the live configuration from <paramref name="environmentLookup"/> (the process
    /// environment by default). The flag and key are checked in that order and every refusal is a
    /// stable <see cref="LlmRefinementRefusalException"/> that names the variable, not its value.
    /// </summary>
    public static LiveModelConfiguration Resolve(
        Func<string, string?>? environmentLookup = null,
        string defaultModelId = "deepseek-chat")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModelId);

        var lookup = environmentLookup ?? Environment.GetEnvironmentVariable;
        if (!IsLiveEnabled(lookup(FlagVariable)))
        {
            throw new LlmRefinementRefusalException(
                LlmRefinementRefusalException.LiveModelDisabledCode,
                FlagVariable + " must be set to 1 or true to enable the live refinement path.");
        }

        if (string.IsNullOrEmpty(lookup(ApiKeyVariable)))
        {
            throw new LlmRefinementRefusalException(
                LlmRefinementRefusalException.LiveModelDisabledCode,
                ApiKeyVariable + " must be set to a non-empty value to enable the live refinement path.");
        }

        var configured = lookup(BaseUrlVariable);
        var baseUrlText = string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.Trim();
        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl)
            || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new LlmRefinementRefusalException(
                "invalid-base-url",
                BaseUrlVariable + " must be an absolute HTTP or HTTPS URI.");
        }

        return new LiveModelConfiguration(baseUrl);
    }

    /// <summary>
    /// Builds the DeepSeek adapter over <paramref name="httpClient"/>. The provider receives
    /// <c>env:DEEPSEEK_API_KEY</c>, so the key is read from the environment at send time and is never
    /// copied into this configuration, the options object or a message.
    /// </summary>
    public DeepSeekChatModel CreateChatModel(
        HttpClient httpClient,
        TimeSpan timeout,
        int maxTransientRetries = 2)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var options = new DeepSeekModelProviderOptions(
            BaseUrl,
            ApiKeyEnvironmentSource,
            timeout,
            maxTransientRetries,
            temperature: null);
        return new DeepSeekChatModel(httpClient, options, DeepSeekThinkingMode.Disabled);
    }

    private static bool IsLiveEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The operator secret-file reader. The file is a simple KEY=VALUE list; the loader exposes only the
/// two variables the live path needs and returns names, never values. Refusal messages name the line
/// number and never contain a value.
/// </summary>
public static class LlmSecretFile
{
    private static readonly string[] AllowlistedNames =
    [
        LiveModelConfiguration.ApiKeyVariable,
        LiveModelConfiguration.BaseUrlVariable,
    ];

    /// <summary>
    /// Parses the KEY=VALUE file. Blank lines and lines whose first non-space character is '# ' are
    /// skipped; surrounding whitespace and one pair of matching single or double quotes are removed
    /// from each value. A missing '=', an empty name or a duplicate name is refused with a message
    /// that names the line number.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var lines = File.ReadAllLines(path);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index].Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                throw new FormatException(
                    "Secret file line " + lineNumber + " has no '='.");
            }

            var name = line[..separator].Trim();
            if (name.Length == 0)
            {
                throw new FormatException(
                    "Secret file line " + lineNumber + " has an empty name.");
            }

            var value = Unquote(line[(separator + 1)..].Trim());
            if (!entries.TryAdd(name, value))
            {
                throw new FormatException(
                    "Secret file line " + lineNumber + " repeats the name '" + name + "'.");
            }
        }

        return entries;
    }

    /// <summary>
    /// Reads <paramref name="path"/> and sets only the allowlisted variables through
    /// <paramref name="setEnvironment"/> (the process environment by default). Returns the names that
    /// were set, in allowlist order, and never a value.
    /// </summary>
    public static IReadOnlyList<string> LoadIntoEnvironment(
        string path,
        Action<string, string>? setEnvironment = null)
    {
        var entries = Read(path);
        var setter = setEnvironment ?? Environment.SetEnvironmentVariable;
        var names = new List<string>(AllowlistedNames.Length);
        foreach (var name in AllowlistedNames)
        {
            if (entries.TryGetValue(name, out var value))
            {
                setter(name, value);
                names.Add(name);
            }
        }

        return names;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            var first = value[0];
            var last = value[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                return value[1..^1];
            }
        }

        return value;
    }
}
