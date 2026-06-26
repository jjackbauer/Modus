namespace Wip.ShellHost.Hosting;

public static class DotNetWorkspaceBootstrap
{
    public static DotNetWorkspaceBootstrapResult Validate(WipShellHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<string>();
        var repositoryRoot = options.EffectiveConfig.RepositoryPath;
        var workspaceRoot = options.EffectiveConfig.WorkspaceRoot;

        diagnostics.Add($"repositoryRoot={repositoryRoot}");
        diagnostics.Add($"workspaceRoot={workspaceRoot}");

        if (string.IsNullOrWhiteSpace(repositoryRoot))
            return Fail(11, "repository-root-missing", diagnostics);

        repositoryRoot = Path.GetFullPath(repositoryRoot);
        diagnostics.Add($"repositoryRoot.normalized={repositoryRoot}");

        if (!Directory.Exists(repositoryRoot))
            return Fail(11, "repository-root-not-found", diagnostics);

        var gitPath = Path.Combine(repositoryRoot, ".git");
        if (!Directory.Exists(gitPath))
        {
            diagnostics.Add("validation=skipped");
            diagnostics.Add("skip.reason=repository-git-metadata-missing");
            return new DotNetWorkspaceBootstrapResult(true, 0, diagnostics);
        }

        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return Fail(12, "workspace-root-missing", diagnostics);

        workspaceRoot = Path.GetFullPath(workspaceRoot);
        diagnostics.Add($"workspaceRoot.normalized={workspaceRoot}");

        if (!Directory.Exists(workspaceRoot))
            return Fail(12, "workspace-root-not-found", diagnostics);

        if (!IsPathWithinRoot(workspaceRoot, repositoryRoot))
            return Fail(13, "workspace-root-outside-repository", diagnostics);

        var solutionCount = CountFiles(workspaceRoot, ".sln", ".slnx");
        var projectCount = CountFiles(workspaceRoot, ".csproj");
        if (!string.Equals(workspaceRoot, repositoryRoot, StringComparison.OrdinalIgnoreCase)
            && solutionCount == 0
            && projectCount == 0)
        {
            diagnostics.Add("discovery.scope=fallback-repository-root");
            solutionCount = CountFiles(repositoryRoot, ".sln", ".slnx");
            projectCount = CountFiles(repositoryRoot, ".csproj");
        }

        diagnostics.Add($"discovery.solutionCount={solutionCount}");
        diagnostics.Add($"discovery.projectCount={projectCount}");

        if (solutionCount == 0 && projectCount == 0)
            return Fail(14, "workspace-dotnet-artifacts-missing", diagnostics);

        diagnostics.Add("validation=success");
        return new DotNetWorkspaceBootstrapResult(true, 0, diagnostics);
    }

    private static DotNetWorkspaceBootstrapResult Fail(int exitCode, string reason, List<string> diagnostics)
    {
        diagnostics.Add($"validation=failure");
        diagnostics.Add($"failure.reason={reason}");
        diagnostics.Add($"failure.exitCode={exitCode}");
        return new DotNetWorkspaceBootstrapResult(false, exitCode, diagnostics);
    }

    private static int CountFiles(string rootPath, params string[] extensions)
    {
        if (extensions.Length == 0)
            return 0;

        var normalized = new HashSet<string>(extensions.Select(static value => value.ToLowerInvariant()), StringComparer.Ordinal);
        return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(static path => !IsExcludedPath(path))
            .Count(path => normalized.Contains(Path.GetExtension(path).ToLowerInvariant()));
    }

    private static bool IsExcludedPath(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains($"{separator}.git{separator}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{separator}.wip{separator}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record DotNetWorkspaceBootstrapResult(
    bool Succeeded,
    int ExitCode,
    IReadOnlyList<string> Diagnostics);