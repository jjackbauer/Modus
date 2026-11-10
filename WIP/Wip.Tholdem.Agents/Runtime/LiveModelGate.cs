namespace Wip.Tholdem.Agents.Runtime;

/// <summary>
/// The live-model gate. It never constructs a model and never reads the key value beyond an emptiness
/// check: the returned reason names the missing environment variable and can never contain the key.
/// </summary>
public static class LiveModelGate
{
    public const string FlagVariable = "THOLDEM_LIVE_MODEL";

    public const string ApiKeyVariable = "DEEPSEEK_API_KEY";

    public static bool IsEnabled(out string? reason)
    {
        var flag = Environment.GetEnvironmentVariable(FlagVariable);
        if (!IsEnabledValue(flag))
        {
            reason = $"{FlagVariable} is not set to 1 or true; the live model path is disabled.";
            return false;
        }

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrEmpty(apiKey))
        {
            reason = $"{ApiKeyVariable} is not set; the live model path is disabled.";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool IsEnabledValue(string? value)
        => string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
