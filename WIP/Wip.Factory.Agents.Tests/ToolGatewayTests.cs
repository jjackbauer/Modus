using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Agents.Core.Tools;
using Wip.Factory.Wip.Isolation;
using Xunit;

namespace Wip.Factory.Agents.Tests;

/// <summary>PA-08 supporting tests: the tool gateway refuses unsafe access and fails closed.</summary>
public sealed class ToolGatewayTests
{
    private static (string Root, CapabilityContext Context) Worktree()
    {
        var root = Path.Combine(Path.GetTempPath(), "factory-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (root, new CapabilityContext(new SessionId("session-tools"), root));
    }

    [Fact]
    public async Task ToolGateway_GivenAbsolutePath_IsRefused()
    {
        var (_, context) = Worktree();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new ReadFileTool().ExecuteAsync(new ReadFileRequest(@"C:\Windows\win.ini"), context, CancellationToken.None));

        Assert.Contains(WorkspacePaths.OutsideWorktreeCategory, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolGateway_GivenTraversalPath_IsRefused()
    {
        var (_, context) = Worktree();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new ReadFileTool().ExecuteAsync(new ReadFileRequest("../../etc/passwd"), context, CancellationToken.None));

        Assert.Contains(WorkspacePaths.OutsideWorktreeCategory, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolGateway_GivenApplyPatchConflict_RefusesAtomically()
    {
        var (root, context) = Worktree();
        var file = Path.Combine(root, "Program.cs");
        await File.WriteAllTextAsync(file, "class A\n{\n    int X = 1;\n}\n");

        var conflict = """
            @@ -1,2 +1,2 @@
             class B
            -{
            +{
            """;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new ApplyPatchTool().ExecuteAsync(new PatchHunkRequest("Program.cs", conflict), context, CancellationToken.None));

        Assert.Contains(ApplyPatchTool.ContextNotFoundCategory, error.Message, StringComparison.Ordinal);
        // Atomic: the file is untouched.
        Assert.Equal("class A\n{\n    int X = 1;\n}\n", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task ToolGateway_GivenMatchingPatch_AppliesAllHunks()
    {
        var (root, context) = Worktree();
        var file = Path.Combine(root, "Program.cs");
        await File.WriteAllTextAsync(file, "class A\n{\n    int X = 1;\n}\n");

        var patch = """
            @@ -3,1 +3,1 @@
            -    int X = 1;
            +    int X = 2;
            """;

        var result = await new ApplyPatchTool().ExecuteAsync(new PatchHunkRequest("Program.cs", patch), context, CancellationToken.None);

        Assert.Equal(1, result.HunkCount);
        Assert.Contains("int X = 2;", await File.ReadAllTextAsync(file), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolGateway_GivenSearchText_RespectsMatchCap()
    {
        var (root, context) = Worktree();
        for (var i = 0; i < 10; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(root, $"F{i}.cs"), "// needle\n");
        }

        var result = await new SearchTextTool().ExecuteAsync(
            new SearchTextRequest("needle", MaxMatches: 3), context, CancellationToken.None);

        Assert.Equal(3, result.Matches.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task ToolGateway_GivenReadFile_TruncatesAndReportsTruncation()
    {
        var (root, context) = Worktree();
        await File.WriteAllTextAsync(Path.Combine(root, "big.txt"), new string('x', 5000));

        var result = await new ReadFileTool().ExecuteAsync(new ReadFileRequest("big.txt", MaxCharacters: 100), context, CancellationToken.None);

        Assert.Equal(100, result.Content.Length);
        Assert.True(result.Truncated);
        Assert.Equal(5000, result.TotalCharacters);
    }

    [Fact]
    public async Task ToolGateway_GivenSubcommandOutsideAllowlist_IsRefused()
    {
        var (_, context) = Worktree();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new RunCommandTool().ExecuteAsync(new RunCommandRequest("nuget", ["push", "x.nupkg"]), context, CancellationToken.None));

        Assert.Contains(RunCommandTool.SubcommandNotAllowedCategory, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolGateway_GivenShellMetacharacters_TreatsThemAsLiterals()
    {
        var (root, context) = Worktree();
        var marker = Path.Combine(root, "escaped.txt");

        // If the argument were handed to a shell, this would create the marker file. It is an argument
        // vector, so MSBuild simply fails to find a project by that name.
        var result = await new RunCommandTool().ExecuteAsync(
            new RunCommandRequest("build", [$"; touch {marker}"]), context, CancellationToken.None);

        Assert.False(File.Exists(marker), "a shell metacharacter was interpreted");
        Assert.True(result.ExitCode is null or not 0, "the metacharacter argument should not succeed as a project");
        Assert.True(result.IsolationApplied);
    }
    [Fact]
    public async Task ToolGateway_GivenNoWorktree_FailsWithoutWriting()
    {
        var context = new CapabilityContext(new SessionId("session-none"), string.Empty);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new WriteWorkspaceFileTool().ExecuteAsync(new WriteFileRequest("a.txt", "x"), context, CancellationToken.None));

        Assert.Contains(WriteWorkspaceFileTool.NoWorktreeCategory, error.Message, StringComparison.Ordinal);
    }
}
