using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HarnessSync;

public static class HarnessMetadataParser
{
    private static readonly Regex FrontmatterRegex = new(
        @"^---\s*\r?\n(.*?)\r?\n---\s*\r?\n?",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static bool TryParse(string filePath, out HarnessBody? body, out string? error)
    {
        body = null;
        error = null;

        var text = File.ReadAllText(filePath);
        var match = FrontmatterRegex.Match(text);
        if (!match.Success)
        {
            error = $"Missing YAML frontmatter: {filePath}";
            return false;
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        Dictionary<string, object?> metadata;
        try
        {
            metadata = deserializer.Deserialize<Dictionary<string, object?>>(match.Groups[1].Value)
                       ?? new Dictionary<string, object?>();
        }
        catch (Exception ex)
        {
            error = $"Invalid YAML frontmatter in {filePath}: {ex.Message}";
            return false;
        }

        var kind = GetString(metadata, "kind") ?? "skill";
        if (string.Equals(kind, "schema", StringComparison.OrdinalIgnoreCase))
        {
            error = "schema";
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(filePath);
        var relativePath = NormalizeRelative(filePath);

        body = new HarnessBody(
            Name: name,
            RelativePath: relativePath,
            Kind: kind,
            Mode: GetString(metadata, "mode"),
            Description: GetString(metadata, "description") ?? name,
            Subagents: GetStringList(metadata, "subagents"),
            Composes: GetStringList(metadata, "composes"),
            AssertMdc: GetString(metadata, "assert_mdc") ?? GetString(metadata, "assertMdc"));

        return true;
    }

    private static string NormalizeRelative(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        var harnessIndex = normalized.IndexOf("harness/", StringComparison.OrdinalIgnoreCase);
        return harnessIndex >= 0 ? normalized[harnessIndex..] : normalized;
    }

    private static string? GetString(Dictionary<string, object?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value.ToString();
    }

    private static IReadOnlyList<string> GetStringList(Dictionary<string, object?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null)
        {
            return Array.Empty<string>();
        }

        return value switch
        {
            IEnumerable<object?> objects => objects
                .Select(o => o?.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToArray(),
            string single => new[] { single },
            _ => Array.Empty<string>()
        };
    }
}