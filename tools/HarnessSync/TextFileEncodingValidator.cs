namespace HarnessSync;

public static class TextFileEncodingValidator
{
    private static readonly string[] TextExtensions =
    [
        ".cs", ".md", ".mdc", ".json", ".csproj", ".props", ".targets", ".slnx",
        ".html", ".css", ".js", ".ts", ".tsx", ".xml", ".editorconfig", ".gitignore",
        ".yml", ".yaml", ".config", ".razor", ".cshtml", ".txt", ".sh", ".ps1"
    ];

    private static readonly string[] ExcludedDirectoryNames =
    [
        "bin", "obj", ".git", "node_modules", ".tmp", "bones-data", "artifacts", "transition-proofs", "evidence", "archive"
    ];

    public static IReadOnlyList<string> FindUtf16TextFiles(string repositoryRoot)
    {
        var issues = new List<string>();
        foreach (var filePath in EnumerateCandidateTextFiles(repositoryRoot))
        {
            if (LooksLikeUtf16(filePath))
            {
                issues.Add(Path.GetRelativePath(repositoryRoot, filePath).Replace('\\', '/'));
            }
        }

        return issues.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool LooksLikeUtf16(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 2)
        {
            return false;
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return true;
        }

        if (bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return true;
        }

        if (bytes.Length < 4)
        {
            return false;
        }

        return bytes[1] == 0 && bytes[3] == 0;
    }

    private static IEnumerable<string> EnumerateCandidateTextFiles(string repositoryRoot)
    {
        foreach (var filePath in Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories))
        {
            if (IsExcluded(filePath))
            {
                continue;
            }

            var extension = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(extension))
            {
                continue;
            }

            if (!TextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                && !filePath.EndsWith(".prompt.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return filePath;
        }
    }

    private static bool IsExcluded(string filePath)
    {
        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(static part => ExcludedDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase));
    }
}