using Wip.Tholdem.Agents.Learning;

namespace Wip.Tholdem.Host;

/// <summary>
/// Reads and writes the data-only equity-policy artifacts under <c>&lt;data&gt;/equity-policies</c>.
/// The file name is the content-addressed policy id and the bytes are the canonical artifact, so a
/// persisted policy is reproducible from its id alone.
/// </summary>
internal static class EquityPolicyStore
{
    private const string DirectoryName = "equity-policies";

    private const string IdPrefix = "tholdem-equity-policy-v1-";

    public static EquityPolicy Load(string dataDirectory, string policyId)
    {
        var safeId = CliPaths.SafeStrategyId(policyId);
        var path = Path.Combine(dataDirectory, DirectoryName, safeId + ".json");
        if (!File.Exists(path))
        {
            throw new CliArtifactException($"equity policy artifact '{path}' does not exist.");
        }

        try
        {
            return EquityPolicyJson.Parse(File.ReadAllText(path, CliJson.Utf8));
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
        {
            throw new CliArtifactException(
                $"equity policy artifact '{path}' is not a valid equity policy: {exception.Message}",
                exception);
        }
    }

    /// <summary>Writes the canonical artifact under its content-addressed id and returns the path.</summary>
    public static string Write(string dataDirectory, EquityPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var json = EquityPolicyJson.Write(policy);
        var id = IdPrefix + EquityPolicyJson.Hash(policy)[..8];
        var directory = Path.Combine(dataDirectory, DirectoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, CliPaths.SafeStrategyId(id) + ".json");
        File.WriteAllText(path, json, CliJson.Utf8);
        return path;
    }
}
