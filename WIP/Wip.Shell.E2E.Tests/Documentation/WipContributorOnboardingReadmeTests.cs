using System.Text.Json;
using Samples.TodoApp;
using Wip.Abstractions.Identifiers;
using Xunit;

namespace Wip.Shell.E2E.Tests.Documentation;

public sealed class WipContributorOnboardingReadmeTests
{
    private const string ReadmePath = "src/WIP.Contributor-Architecture.README.md";

    private const string OnboardingChecklistItem =
        "Add executable onboarding quickstart to root WIP contributor README covering `init`, `session start`, `attach`, `plan`, `run`, `validate`, `review`, `approve`, `merge`, and diagnostics commands with reproducible local commands [depends on contributor architecture map]";

    private static readonly (string Command, string Marker)[] BootstrapMarkers =
    [
        ("init", "Repository initialized:"),
        ("use workflow workflow.linear", "Workflow selected: workflow.linear"),
        ("session start \"Contributor onboarding\"", "Session started:"),
        ("plan", "Plan generated: workflow=workflow.linear state=Editing"),
        ("run", "Workflow executed: workflow.linear"),
        ("status", "Active session state:")
    ];

    private static readonly (string Command, string Marker)[] GovernanceMarkers =
    [
        ("session attach", "Session attached:"),
        ("diff", "worktree:"),
        ("validate", "Validation completed: session="),
        ("review", "Review generated: session="),
        ("approve", "Approval completed: session="),
        ("merge", "Merge completed: session=")
    ];

    [Fact]
    [Trait("ChecklistItem", OnboardingChecklistItem)]
    public async Task WipContributorOnboardingReadme_GivenDocumentedCommandSequence_ExpectedShellTranscriptMatchesDocumentedMarkers()
    {
        var readme = ShellHostDocumentationTestDriver.ReadRepositoryFile(ReadmePath);
        Assert.Contains("## Contributor Onboarding Quickstart", readme, StringComparison.Ordinal);

        foreach (var (_, marker) in BootstrapMarkers.Concat(GovernanceMarkers))
        {
            Assert.Contains(marker, readme, StringComparison.Ordinal);
        }

        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var bootstrapRun = await ShellHostDocumentationTestDriver.ExecuteScriptAsync(
            [
                "init",
                "use workflow workflow.linear",
                "session start \"Contributor onboarding\"",
                "plan",
                "run",
                "status",
                "exit"
            ],
            CancellationToken.None,
            fixture.RepositoryPath);

        Assert.Equal(0, bootstrapRun.ExitCode);
        foreach (var (_, marker) in BootstrapMarkers)
        {
            Assert.Contains(marker, bootstrapRun.StdOut, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("ChecklistItem", OnboardingChecklistItem)]
    public async Task WipContributorOnboardingReadme_GivenFreshRepository_ExpectedInitThroughPlanRunValidatePathReproducible()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();

        var bootstrapRun = await ShellHostDocumentationTestDriver.ExecuteScriptAsync(
            [
                "init",
                "use workflow workflow.linear",
                "session start \"Contributor onboarding validate path\"",
                "plan",
                "run",
                "exit"
            ],
            CancellationToken.None,
            fixture.RepositoryPath);

        Assert.Equal(0, bootstrapRun.ExitCode);
        var sessionId = ShellHostDocumentationTestDriver.ExtractSessionId(bootstrapRun.StdOut);

        await AppendAndCommitWorktreeChangeAsync(fixture, sessionId, "onboarding-validate-change\n");

        var governanceRun = await ShellHostDocumentationTestDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId.Value}",
                "diff",
                "validate",
                "review",
                "approve",
                "yes",
                "merge",
                "exit"
            ],
            CancellationToken.None,
            fixture.RepositoryPath);

        Assert.Equal(0, governanceRun.ExitCode);
        Assert.Contains("Session attached:", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Validation completed: session=", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Review generated: session=", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Approval completed: session=", governanceRun.StdOut, StringComparison.Ordinal);
        Assert.Contains("Merge completed: session=", governanceRun.StdOut, StringComparison.Ordinal);
    }

    private static async Task AppendAndCommitWorktreeChangeAsync(
        TempGitRepositoryFixture fixture,
        SessionId sessionId,
        string appendedContent)
    {
        var statePath = Path.Combine(fixture.RepositoryPath, ".wip", "sessions", sessionId.Value, "session-state.json");
        var payload = await File.ReadAllTextAsync(statePath, CancellationToken.None);
        using var state = JsonDocument.Parse(payload);
        var worktreePath = state.RootElement.GetProperty("WorktreePath").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain a worktree path.");

        var targetPath = Path.Combine(worktreePath, "README.md");
        await File.AppendAllTextAsync(targetPath, appendedContent, CancellationToken.None);
        await fixture.RunGitAsync("-C", worktreePath, "add", "README.md");
        await fixture.RunGitAsync("-C", worktreePath, "commit", "-m", "onboarding validate change");
    }
}
