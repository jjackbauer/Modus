using Xunit;

namespace Wip.Modus.Tests.Documentation;

public sealed class ContributorReadmeRequirementsTests
{
    private const string RequirementsPath = ".github/requirements/WIP.Contributor-Readmes.md";
    private const string ArchitectureReadmePath = "src/WIP.Contributor-Architecture.README.md";

    private const string ReconcileChecklistItem =
        "Reconcile WIP.Contributor-Readmes.md coverage matrix Pending rows with checklist reality (complete docs or uncheck items until done) [depends on onboarding and diagnostics documentation]";

    [Fact]
    [Trait("ChecklistItem", ReconcileChecklistItem)]
    public void ContributorReadmeRequirements_GivenCoverageMatrix_ExpectedNoPendingRowWithCheckedChecklistItem()
    {
        var requirements = ReadRepositoryFile(RequirementsPath);
        var rows = ParseCoverageMatrixRows(requirements);

        Assert.NotEmpty(rows);

        foreach (var row in rows)
        {
            if (!string.Equals(row.Status, "Pending", StringComparison.Ordinal))
            {
                continue;
            }

            var matchingChecklistItem = FindMatchingCheckedChecklistItem(requirements, row.Capability);
            Assert.Null(matchingChecklistItem);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ReconcileChecklistItem)]
    public void ContributorReadmeRequirements_GivenPendingOnboardingRow_ExpectedChecklistItemRemainsUncheckedUntilQuickstartProofExists()
    {
        var requirements = ReadRepositoryFile(RequirementsPath);
        var architectureReadme = ReadRepositoryFile(ArchitectureReadmePath);
        var onboardingRow = ParseCoverageMatrixRows(requirements)
            .Single(row => row.Capability.Contains("Executable onboarding flow", StringComparison.Ordinal));

        Assert.Equal("Done", onboardingRow.Status);
        Assert.Contains("## Contributor Onboarding Quickstart", architectureReadme, StringComparison.Ordinal);
        Assert.Contains("Repository initialized:", architectureReadme, StringComparison.Ordinal);
        Assert.Contains("Merge completed: session=", architectureReadme, StringComparison.Ordinal);
    }

    private static string? FindMatchingCheckedChecklistItem(string requirementsMarkdown, string capability)
    {
        if (capability.Contains("Executable onboarding flow", StringComparison.Ordinal)
            && requirementsMarkdown.Contains("- [x] Document Shell and ShellHost command surface", StringComparison.Ordinal))
        {
            return "Document Shell and ShellHost command surface";
        }

        if (capability.Contains("Policy and safety behavior docs", StringComparison.Ordinal)
            && requirementsMarkdown.Contains("- [x] Document LocalSafe policy rules", StringComparison.Ordinal))
        {
            return "Document LocalSafe policy rules";
        }

        if (capability.Contains("Plugin/workflow diagnostics docs", StringComparison.Ordinal)
            && requirementsMarkdown.Contains("- [x] Document Shell and ShellHost command surface", StringComparison.Ordinal))
        {
            return "Document Shell and ShellHost command surface";
        }

        return null;
    }

    private static IReadOnlyList<CoverageMatrixRow> ParseCoverageMatrixRows(string markdown)
    {
        var rows = new List<CoverageMatrixRow>();
        var inMatrix = false;

        foreach (var line in markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("### Coverage Matrix", StringComparison.Ordinal))
            {
                inMatrix = true;
                continue;
            }

            if (!inMatrix)
            {
                continue;
            }

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                break;
            }

            if (!trimmed.StartsWith("|", StringComparison.Ordinal) || !trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = trimmed.Trim('|').Split('|').Select(static cell => cell.Trim()).ToArray();
            if (cells.Length < 4)
            {
                continue;
            }

            if (string.Equals(cells[0], "Capability", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (cells[0].StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            rows.Add(new CoverageMatrixRow(cells[0], cells[3]));
        }

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

    private sealed record CoverageMatrixRow(string Capability, string Status);
}