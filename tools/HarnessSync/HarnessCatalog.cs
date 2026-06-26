namespace HarnessSync;

public static class HarnessCatalog
{
    private static readonly string[] SourceDirectories =
    [
        "harness/skills",
        "harness/workflows",
        "harness/rules"
    ];

    public static IReadOnlyList<HarnessBody> Discover(string repositoryRoot)
    {
        var bodies = new List<HarnessBody>();

        foreach (var sourceDirectory in SourceDirectories)
        {
            var directoryPath = Path.Combine(repositoryRoot, sourceDirectory);
            if (!Directory.Exists(directoryPath))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.md", SearchOption.TopDirectoryOnly))
            {
                if (!HarnessMetadataParser.TryParse(filePath, out var body, out var error))
                {
                    if (error == "schema")
                    {
                        continue;
                    }

                    throw new InvalidOperationException(error ?? $"Failed to parse {filePath}");
                }

                if (body is not null)
                {
                    bodies.Add(body);
                }
            }
        }

        return bodies
            .OrderBy(b => b.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}