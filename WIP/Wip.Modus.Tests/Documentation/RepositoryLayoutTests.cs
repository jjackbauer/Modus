using System.Text.RegularExpressions;
using Xunit;

namespace Wip.Modus.Tests.Documentation;

public sealed class RepositoryLayoutTests
{
    private const string LayoutChecklistItem =
        "Close Wip.Src-WIP-Project-Layout-Refactoring.md checklist by marking completed relocation items [x] and adding verification item for stale src/Wip.* path references in execution-affecting code and docs [depends on documentation alignment]";

    private static readonly string[] ExecutionAffectingRelativeRoots =
    [
        "WIP"
    ];

    private static readonly Regex StaleSrcWipPathPattern = new(
        @"src/Wip\.[^""'\s`]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Fact]
    [Trait("ChecklistItem", LayoutChecklistItem)]
    public void RepositoryLayout_GivenWipProjects_ExpectedAllResolveUnderWipFolderWithoutSrcWipAssumptions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var wipRoot = Path.Combine(repositoryRoot, "WIP");
        Assert.True(Directory.Exists(wipRoot));

        var projectNames = Directory.GetDirectories(wipRoot, "Wip.*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(projectNames);

        foreach (var projectName in projectNames)
        {
            var csprojPath = Path.Combine(wipRoot, projectName, $"{projectName}.csproj");
            Assert.True(File.Exists(csprojPath), $"Expected project file '{csprojPath}'.");
        }

        var staleSrcProjects = Directory.Exists(Path.Combine(repositoryRoot, "src"))
            ? Directory.GetDirectories(Path.Combine(repositoryRoot, "src"), "Wip.*", SearchOption.TopDirectoryOnly)
            : [];

        Assert.Empty(staleSrcProjects);
    }

    [Fact]
    [Trait("ChecklistItem", LayoutChecklistItem)]
    public void RepositoryLayout_GivenExecutionAffectingDocsAndScripts_ExpectedNoStaleSrcWipPathReferences()
    {
        var repositoryRoot = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var relativeRoot in ExecutionAffectingRelativeRoots)
        {
            var absoluteRoot = Path.Combine(repositoryRoot, relativeRoot);
            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(absoluteRoot, "*.*", SearchOption.AllDirectories))
            {
                if (ShouldSkipExecutionAffectingScan(filePath))
                {
                    continue;
                }

                if (!IsExecutionAffectingFile(filePath))
                {
                    continue;
                }

                var content = File.ReadAllText(filePath);
                foreach (Match match in StaleSrcWipPathPattern.Matches(content))
                {
                    violations.Add($"{Path.GetRelativePath(repositoryRoot, filePath)}:{match.Value}");
                }
            }
        }

        Assert.Empty(violations);
    }

    private static bool ShouldSkipExecutionAffectingScan(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("/transition-proofs/", StringComparison.Ordinal)
            || normalized.Contains("/proof-artifacts/", StringComparison.Ordinal)
            || normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal);
    }

    private static bool IsExecutionAffectingFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        return fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase);
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
}