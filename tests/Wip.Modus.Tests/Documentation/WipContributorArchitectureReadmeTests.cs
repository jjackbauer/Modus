using Xunit;

namespace Wip.Modus.Tests.Documentation;

public sealed class WipContributorArchitectureReadmeTests
{
    private const string ChecklistItem = "Publish root WIP contributor architecture README that maps each Wip.* project to its ownership, runtime role, and extension seams [prerequisite for many others]";
    private const string ReadmePath = "src/WIP.Contributor-Architecture.README.md";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void WipContributorArchitectureReadme_GivenRepositoryStructure_MapsEveryWipProjectToRuntimeRole()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = ReadRepositoryFile(ReadmePath);
        var rows = ParseProjectMapRows(readme);

        var projectNames = Directory.GetDirectories(Path.Combine(repositoryRoot, "src"), "Wip.*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(projectNames);

        foreach (var projectName in projectNames)
        {
            var row = Assert.Single(rows, row => string.Equals(row.Project, projectName, StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(row.Ownership));
            Assert.False(string.IsNullOrWhiteSpace(row.RuntimeRole));
            Assert.False(string.IsNullOrWhiteSpace(row.ExtensionSeams));
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void WipContributorArchitectureReadme_GivenArchitectureMap_ContributorsCanTraceSessionCommandToRuntimeComponents()
    {
        var readme = ReadRepositoryFile(ReadmePath);

        Assert.Contains("transition planning", readme, StringComparison.Ordinal);
        Assert.Contains("Wip.Shell/Interactive/WipShellCommandLoop.cs", readme, StringComparison.Ordinal);
        Assert.Contains("Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs", readme, StringComparison.Ordinal);
        Assert.Contains("Wip.Policy.LocalSafe/LocalSafePolicy.cs", readme, StringComparison.Ordinal);
        Assert.Contains("Wip.Modus/Hosting/ModusWipBridge.cs", readme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void WipContributorArchitectureReadme_GivenOutdatedMap_RuntimeVerificationTestFailsUntilReadmeUpdated()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = ReadRepositoryFile(ReadmePath);
        var rows = ParseProjectMapRows(readme);

        foreach (var row in rows)
        {
            var projectPath = Path.Combine(repositoryRoot, "src", row.Project, $"{row.Project}.csproj");
            Assert.True(File.Exists(projectPath), $"README maps '{row.Project}', but '{projectPath}' does not exist.");
        }
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
