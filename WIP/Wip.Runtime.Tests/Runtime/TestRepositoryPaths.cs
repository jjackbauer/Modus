namespace Wip.Runtime.Tests.Runtime;

internal static class TestRepositoryPaths
{
    public static string RepositoryRoot { get; } = Path.Combine(Path.GetTempPath(), "modus-wip-runtime-tests");

    static TestRepositoryPaths()
    {
        Directory.CreateDirectory(RepositoryRoot);
    }

    public static string Worktree(string name) => Path.Combine(RepositoryRoot, ".wip", "worktrees", name);
}
