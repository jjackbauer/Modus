using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class DocsContributionWorkflowTests
{
    [Fact]
    [Trait("ChecklistItem", "Define contributor workflow for docs updates, sample maintenance, and architecture artifact synchronization")]
    public void DocsContributionWorkflow_GivenContributorEdits_ExpectedRequiredUpdatePathsAndReviewGatesAreDefined()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("## Contribution Workflow for Docs and Examples", rootReadme, StringComparison.Ordinal);
        Assert.Contains("When a change affects docs, examples, or architecture artifacts, contributors must update all linked surfaces in one PR.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("### Required update paths", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Core contracts and extension API docs: `src/Modus.Core/README.md`", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Host runtime behavior and lifecycle docs: `src/Modus.Host/README.md`", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Top-level navigation and quickstart: `README.md`", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Documentation requirements worktree: `.github/requirements/Modus.Core-Modus.Host.Docs.md`", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Transition-proof artifacts for checklist transitions: `.github/artifacts/`", rootReadme, StringComparison.Ordinal);
        Assert.Contains("### Review gates", rootReadme, StringComparison.Ordinal);
        Assert.Contains("1. Every docs PR includes at least one executable validation command and its expected success signal.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("2. Any changed command or snippet is verified against the current repository layout before merge.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("3. Checklist item transitions in requirements docs include a linked transition-proof artifact.", rootReadme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Define contributor workflow for docs updates, sample maintenance, and architecture artifact synchronization")]
    public void DocsContributionWorkflow_GivenArchitectureArtifacts_ExpectedSyncRulesPreventDriftBetweenDocsAndCode()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("### Architecture artifact synchronization rules", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Keep architecture diagrams, requirements checklists, and referenced runtime stages synchronized in the same PR.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- If runtime stage names or ordering change, update all affected docs and add/adjust integration tests that assert deterministic order.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Keep sample plugin metadata (`<ModusOperations>`, capabilities, and lifetimes) aligned with runnable plugin projects under `plugins/`.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Record checklist [ ] -> [x] transitions with before/after snapshots and SHA256 hashes in `.github/artifacts/`.", rootReadme, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            var filePath = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(solutionPath) && File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root containing Modus.slnx and {relativePath}.");
    }
}