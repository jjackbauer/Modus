using System.Text.RegularExpressions;
using Xunit;

namespace Wip.Modus.Tests.Documentation;

public sealed class WipContributorArchitectureReadmeTests
{
    private const string PathFixChecklistItem =
        "Fix WipContributorArchitectureReadmeTests to resolve WIP/Wip.* project paths and discover all Wip.* projects under WIP/ (not src/) [foundation for documentation alignment]";

    private const string MapUpdateChecklistItem =
        "Update src/WIP.Contributor-Architecture.README.md project map to include every WIP/Wip.* project with correct ownership, runtime role, and extension seams [depends on readme path test fix]";
    private const string ReadmePath = "src/WIP.Contributor-Architecture.README.md";
    private const string WipProjectsRoot = "WIP";

    [Fact]
    [Trait("ChecklistItem", PathFixChecklistItem)]
    [Trait("ChecklistItem", MapUpdateChecklistItem)]
    public void WipContributorArchitectureReadme_GivenRepositoryStructure_MapsEveryWipProjectToRuntimeRole()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectNames = DiscoverWipProjectNames(repositoryRoot);
        var readme = ReadRepositoryFile(ReadmePath);
        var rows = ParseProjectMapRows(readme);
        var rowsByProject = rows.ToDictionary(static row => row.Project, StringComparer.Ordinal);

        Assert.NotEmpty(projectNames);

        foreach (var projectName in projectNames)
        {
            var projectPath = ResolveWipProjectPath(repositoryRoot, projectName);
            Assert.True(File.Exists(projectPath), $"Discovered WIP project '{projectName}', but '{projectPath}' does not exist.");

            Assert.True(rowsByProject.TryGetValue(projectName, out var row), $"README project map is missing '{projectName}'.");
            Assert.False(string.IsNullOrWhiteSpace(row.Ownership), $"README ownership is empty for '{projectName}'.");
            Assert.False(string.IsNullOrWhiteSpace(row.RuntimeRole), $"README runtime role is empty for '{projectName}'.");
            Assert.False(string.IsNullOrWhiteSpace(row.ExtensionSeams), $"README extension seams are empty for '{projectName}'.");
        }

        var staleSrcWipProjects = Directory.Exists(Path.Combine(repositoryRoot, "src"))
            ? Directory.GetDirectories(Path.Combine(repositoryRoot, "src"), "Wip.*", SearchOption.TopDirectoryOnly)
            : [];

        Assert.Empty(staleSrcWipProjects);
    }

    [Fact]
    [Trait("ChecklistItem", PathFixChecklistItem)]
    [Trait("ChecklistItem", MapUpdateChecklistItem)]
    public void WipContributorArchitectureReadme_GivenArchitectureMap_ContributorsCanTraceSessionCommandToRuntimeComponents()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = ReadRepositoryFile(ReadmePath);

        Assert.Contains("transition planning", readme, StringComparison.Ordinal);

        var tracePaths = ExtractWipRepositoryPaths(readme);
        Assert.NotEmpty(tracePaths);

        foreach (var relativePath in tracePaths)
        {
            var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(fullPath), $"README references '{relativePath}', but '{fullPath}' does not exist.");
        }
    }

    [Fact]
    [Trait("ChecklistItem", PathFixChecklistItem)]
    [Trait("ChecklistItem", MapUpdateChecklistItem)]
    public void WipContributorArchitectureReadme_GivenOutdatedMap_RuntimeVerificationTestFailsUntilReadmeUpdated()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = ReadRepositoryFile(ReadmePath);
        var rows = ParseProjectMapRows(readme);

        foreach (var row in rows)
        {
            var projectPath = ResolveWipProjectPath(repositoryRoot, row.Project);
            Assert.True(File.Exists(projectPath), $"README maps '{row.Project}', but '{projectPath}' does not exist.");
        }
    }

    private static string[] DiscoverWipProjectNames(string repositoryRoot)
    {
        var wipRoot = Path.Combine(repositoryRoot, WipProjectsRoot);
        return Directory.GetDirectories(wipRoot, "Wip.*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name) && !name.EndsWith(".Tests", StringComparison.Ordinal))
            .Cast<string>()
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveWipProjectPath(string repositoryRoot, string projectName) =>
        Path.Combine(repositoryRoot, WipProjectsRoot, projectName, $"{projectName}.csproj");

    private static string[] ExtractWipRepositoryPaths(string markdown)
    {
        var matches = Regex.Matches(markdown, @"WIP/Wip\.[^`\s]+", RegexOptions.CultureInvariant);
        return matches
            .Select(static match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ProjectMapRow> ParseProjectMapRows(string markdown)
    {
        var rows = new List<ProjectMapRow>();
        var lines = markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal) || !trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = trimmed.Trim('|').Split('|').Select(static cell => cell.Trim()).ToArray();
            if (cells.Length < 4)
            {
                continue;
            }

            if (string.Equals(cells[0], "Project", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (cells[0].StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            if (!cells[0].StartsWith("Wip.", StringComparison.Ordinal))
            {
                continue;
            }

            rows.Add(new ProjectMapRow(cells[0], cells[1], cells[2], cells[3]));
        }

        Assert.NotEmpty(rows);
        return rows;
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var filePath = Path.Combine(repositoryRoot, relativePath);
        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"Could not locate repository file '{relativePath}'.");
        }

        return File.ReadAllText(filePath);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Modus.slnx.");
    }

    private sealed record ProjectMapRow(string Project, string Ownership, string RuntimeRole, string ExtensionSeams);
}
