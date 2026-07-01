namespace Wip.Bones.ModelProviders.DeepSeek;

public enum BonesDeepSeekApiKeySourceKind
{
    EnvironmentVariable,
}

public readonly record struct BonesDeepSeekModelIdentifier
{
    private static readonly HashSet<string> SupportedValues = new(StringComparer.Ordinal)
    {
        "deepseek-chat",
        "deepseek-reasoner",
    };

    public string Value { get; }

    public BonesDeepSeekModelIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim();
        if (!SupportedValues.Contains(normalized))
        {
            throw new InvalidOperationException(
                $"DeepSeek provider configuration is invalid: Model '{normalized}' is not supported. Supported models: deepseek-chat, deepseek-reasoner.");
        }

        Value = normalized;
    }

    public override string ToString() => Value;
}

public sealed record BonesDeepSeekProviderOptions
{
    public BonesDeepSeekProviderOptions(
        Uri baseUrl,
        BonesDeepSeekModelIdentifier model,
        TimeSpan timeout,
        BonesDeepSeekApiKeySourceKind apiKeySourceKind,
        string apiKeySourceReference)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        if (!baseUrl.IsAbsoluteUri)
        {
            throw new InvalidOperationException(
                "DeepSeek provider configuration is invalid: BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        if (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "DeepSeek provider configuration is invalid: BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "DeepSeek provider configuration is invalid: Timeout must be greater than zero.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(apiKeySourceReference);

        BaseUrl = baseUrl;
        Model = model;
        Timeout = timeout;
        ApiKeySourceKind = apiKeySourceKind;
        ApiKeySourceReference = apiKeySourceReference.Trim();
    }

    public Uri BaseUrl { get; }

    public BonesDeepSeekModelIdentifier Model { get; }

    public TimeSpan Timeout { get; }

    public BonesDeepSeekApiKeySourceKind ApiKeySourceKind { get; }

    public string ApiKeySourceReference { get; }

    public string ApiKeySource => $"env:{ApiKeySourceReference}";

    public static BonesDeepSeekProviderOptions Create(
        string baseUrl,
        string model,
        TimeSpan timeout,
        string apiKeySource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var parsedBaseUrl)
            || (parsedBaseUrl.Scheme != Uri.UriSchemeHttps && parsedBaseUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "DeepSeek provider configuration is invalid: BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        var modelIdentifier = new BonesDeepSeekModelIdentifier(model);

        if (!TryParseEnvironmentKeySource(apiKeySource, out var keyVariableName))
        {
            throw new InvalidOperationException(
                "DeepSeek provider configuration is invalid: ApiKeySource must use 'env:<VARIABLE_NAME>' format.");
        }

        return new BonesDeepSeekProviderOptions(
            baseUrl: parsedBaseUrl,
            model: modelIdentifier,
            timeout: timeout,
            apiKeySourceKind: BonesDeepSeekApiKeySourceKind.EnvironmentVariable,
            apiKeySourceReference: keyVariableName);
    }

    private static bool TryParseEnvironmentKeySource(string value, out string variableName)
    {
        variableName = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!value.Trim().StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            return false;

        var parsed = value.Trim()[4..].Trim();
        if (string.IsNullOrWhiteSpace(parsed))
            return false;

        variableName = parsed;
        return true;
    }
}
