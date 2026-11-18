using System.Text.Json;

namespace Wip.Factory.Host.Configuration;

/// <summary>
/// The single validated reader for `.wip/config.json` (F-L.5). Shell and host must not each carry a
/// divergent parser, so both compose this type; an unknown key is rejected rather than silently
/// ignored.
/// </summary>
public sealed record FactoryConfig(
    string RepositoryPath,
    string WorkspaceRoot,
    string WipRoot,
    string SessionsPath,
    string WorktreesPath,
    string PluginsPath,
    string DefaultWorkflowId,
    string PolicyId);

/// <summary>Outcome of reading configuration.</summary>
public sealed record FactoryConfigResult(bool Loaded, FactoryConfig? Config, IReadOnlyList<string> Errors)
{
    public static FactoryConfigResult Ok(FactoryConfig config) => new(true, config, []);

    public static FactoryConfigResult Failed(params string[] errors) => new(false, null, errors);
}

/// <summary>Reads and validates repository configuration.</summary>
public static class FactoryConfigReader
{
    /// <summary>The keys this alpha understands. Anything else is a configuration error.</summary>
    public static IReadOnlySet<string> KnownKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "RepositoryPath",
        "WorkspaceRoot",
        "WipRoot",
        "SessionsPath",
        "WorktreesPath",
        "PluginsPath",
        "DefaultWorkflowId",
        "PolicyId"
    };

    public const string UnknownKeyCategory = "unknown-config-key";

    public static FactoryConfigResult Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return FactoryConfigResult.Failed("invalid-json:" + exception.Message);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return FactoryConfigResult.Failed("root-must-be-an-object");
            }

            var unknown = document.RootElement
                .EnumerateObject()
                .Select(static property => property.Name)
                .Where(name => !KnownKeys.Contains(name))
                .ToArray();

            if (unknown.Length > 0)
            {
                return FactoryConfigResult.Failed(
                    UnknownKeyCategory + ":" + string.Join(",", unknown));
            }

            var values = document.RootElement
                .EnumerateObject()
                .ToDictionary(static property => property.Name, static property => property.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            var errors = new List<string>();
            string Required(string key)
            {
                if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    errors.Add("missing-config-key:" + key);
                    return string.Empty;
                }

                return value;
            }

            var config = new FactoryConfig(
                Required("RepositoryPath"),
                Required("WorkspaceRoot"),
                Required("WipRoot"),
                Required("SessionsPath"),
                Required("WorktreesPath"),
                Required("PluginsPath"),
                Required("DefaultWorkflowId"),
                Required("PolicyId"));

            return errors.Count == 0 ? FactoryConfigResult.Ok(config) : FactoryConfigResult.Failed([.. errors]);
        }
    }

    /// <summary>
    /// The effective configuration is a pure function of the file, so a shell surface and a host surface
    /// reading the same file cannot disagree.
    /// </summary>
    public static string RenderEffective(FactoryConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return string.Join(
            "\n",
            $"repository={config.RepositoryPath}",
            $"workspaceRoot={config.WorkspaceRoot}",
            $"wipRoot={config.WipRoot}",
            $"sessions={config.SessionsPath}",
            $"worktrees={config.WorktreesPath}",
            $"plugins={config.PluginsPath}",
            $"workflow={config.DefaultWorkflowId}",
            $"policy={config.PolicyId}");
    }
}
