using Samples.TodoApp;
using Xunit;

namespace Wip.Shell.E2E.Tests.Documentation;

public sealed class WipContributorDiagnosticsReadmeTests
{
    private const string ReadmePath = "src/WIP.Contributor-Architecture.README.md";

    private const string DiagnosticsChecklistItem =
        "Add plugin and workflow diagnostics documentation tied to `plugins`, `workflows`, `config`, and `debug-logs` shell commands with expected output semantics [depends on contributor architecture map]";

    [Fact]
    [Trait("ChecklistItem", DiagnosticsChecklistItem)]
    public async Task WipContributorDiagnosticsReadme_GivenPluginsCommand_ExpectedOutputContractDocumentedWithCapabilityInventoryRules()
    {
        var readme = ShellHostDocumentationTestDriver.ReadRepositoryFile(ReadmePath);
        Assert.Contains("## Plugin and Workflow Diagnostics Reference", readme, StringComparison.Ordinal);
        Assert.Contains("Loaded plugins:", readme, StringComparison.Ordinal);
        Assert.Contains("No plugins are currently loaded.", readme, StringComparison.Ordinal);
        Assert.Contains("capabilities:", readme, StringComparison.Ordinal);
        Assert.Contains("Plugin diagnostics:", readme, StringComparison.Ordinal);

        var result = await ShellHostDocumentationTestDriver.ExecuteScriptAsync(
            ["plugins", "exit"],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(
            result.StdOut.Contains("Loaded plugins:", StringComparison.Ordinal)
            || result.StdOut.Contains("No plugins are currently loaded.", StringComparison.Ordinal),
            $"Expected plugin diagnostics output. StdOut: {result.StdOut}");
    }

    [Fact]
    [Trait("ChecklistItem", DiagnosticsChecklistItem)]
    public async Task WipContributorDiagnosticsReadme_GivenWorkflowsAndDebugLogsCommands_ExpectedManifestAndDebugSnapshotSemanticsDocumented()
    {
        var readme = ShellHostDocumentationTestDriver.ReadRepositoryFile(ReadmePath);
        Assert.Contains("Registered workflows:", readme, StringComparison.Ordinal);
        Assert.Contains("Host run correlation:", readme, StringComparison.Ordinal);
        Assert.Contains("No Modus debug logs were emitted for the current host run.", readme, StringComparison.Ordinal);
        Assert.Contains("`config`", readme, StringComparison.Ordinal);

        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var result = await ShellHostDocumentationTestDriver.ExecuteScriptAsync(
            [
                "init",
                "config",
                "workflows",
                "debug-logs",
                "exit"
            ],
            CancellationToken.None,
            fixture.RepositoryPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("configSource:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Registered workflows:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("workflow.linear", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Host run correlation:", result.StdOut, StringComparison.Ordinal);
        Assert.True(
            result.StdOut.Contains("Modus debug logs:", StringComparison.Ordinal)
            || result.StdOut.Contains("No Modus debug logs were emitted for the current host run.", StringComparison.Ordinal),
            $"Expected debug log snapshot output. StdOut: {result.StdOut}");
    }
}
