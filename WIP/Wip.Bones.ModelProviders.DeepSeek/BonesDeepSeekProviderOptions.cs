using Wip.Runtime.Runtime;

namespace Wip.Bones.ModelProviders.DeepSeek;

public enum BonesDeepSeekApiKeySourceKind
{
    EnvironmentVariable,
}

public readonly record struct BonesDeepSeekModelIdentifier
{
    public const string Flash = "deepseek-flash";

    public const string V4Pro = "deepseek-v4-pro";

    public const string V4FlashLegacy = "deepseek-v4-flash";

    public const string V4FlashVisionExpLegacy = "deepseek-v4-flash-vision-exp";

    private static readonly string[] SupportedModelValues =
    [
        Flash,
        V4Pro,
        V4FlashLegacy,
        V4FlashVisionExpLegacy,
    ];

    private static readonly HashSet<string> SupportedValues = new(SupportedModelValues, StringComparer.Ordinal);

    public static IReadOnlyList<string> SupportedModels => SupportedModelValues;

    public static bool IsSupported(string? value)
        => !string.IsNullOrWhiteSpace(value) && SupportedValues.Contains(value.Trim());

    public string Value { get; }

    public BonesDeepSeekModelIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim();
        if (!SupportedValues.Contains(normalized))
        {
            throw new InvalidOperationException(
                $"DeepSeek provider configuration is invalid: Model '{normalized}' is not supported. Supported models: {string.Join(", ", SupportedModelValues)}.");
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
        string apiKeySourceReference,
        double? temperature = null,
        DeepSeekThinkingMode thinking = DeepSeekThinkingMode.Disabled)
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

        if (temperature.HasValue && (temperature.Value < 0.0 || temperature.Value > 2.0))
        {
            throw new InvalidOperationException(
                $"DeepSeek provider configuration is invalid: Temperature '{temperature.Value}' must be between 0 and 2.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(apiKeySourceReference);

        BaseUrl = baseUrl;
        Model = model;
        Timeout = timeout;
        ApiKeySourceKind = apiKeySourceKind;
        ApiKeySourceReference = apiKeySourceReference.Trim();
        Temperature = temperature;
        Thinking = thinking;
    }

    public Uri BaseUrl { get; }

    public BonesDeepSeekModelIdentifier Model { get; }

    public TimeSpan Timeout { get; }

    public BonesDeepSeekApiKeySourceKind ApiKeySourceKind { get; }

    public string ApiKeySourceReference { get; }

    public string ApiKeySource => $"env:{ApiKeySourceReference}";

    public double? Temperature { get; }

    public DeepSeekThinkingMode Thinking { get; }

    public static BonesDeepSeekProviderOptions Create(
        string baseUrl,
        string model,
        TimeSpan timeout,
        string apiKeySource,
        double? temperature = null,
        DeepSeekThinkingMode thinking = DeepSeekThinkingMode.Disabled)
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
            apiKeySourceReference: keyVariableName,
            temperature: temperature,
            thinking: thinking);
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
