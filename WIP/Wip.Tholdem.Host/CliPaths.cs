namespace Wip.Tholdem.Host;

/// <summary>Resolves the data directory shared by every artifact-producing command.</summary>
internal static class CliPaths
{
    public const string DefaultDataDirectory = "artifacts/tholdem";

    public static string ResolveDataDirectory(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Get("data-dir");
        var path = string.IsNullOrWhiteSpace(configured) ? DefaultDataDirectory : configured;
        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Strategy identifiers become file names. Reject anything that could escape the strategies
    /// directory before any path is combined.
    /// </summary>
    public static string SafeStrategyId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliArtifactException("a strategy id is required.");
        }

        if (value is "." or ".."
            || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar)
            || value.Contains(':')
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new CliArtifactException($"strategy id '{value}' is not a safe artifact name.");
        }

        return value;
    }
}
