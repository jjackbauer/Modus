using System.Security.Cryptography;
using System.Text;
using Wip.Factory.Wip.Isolation;

namespace Wip.Factory.Host.Factory;

/// <summary>
/// Outcome of the mandatory pre-existing regression suite. <c>Ran=false</c> is the explicit
/// <c>not-applicable</c> case: the repository genuinely had no test projects, which is recorded rather than
/// silently treated as a pass (v0.4 FAC-P1-07).
/// </summary>
public sealed record RegressionTestResult(
    bool Ran,
    bool Passed,
    int Projects,
    string Detail,
    string ArtifactDigest)
{
    public static RegressionTestResult NotApplicable(string digest)
        => new(false, true, 0, "not-applicable:no-existing-tests", digest);
}

/// <summary>Runs the repository's own existing tests. Generated tests never substitute for this.</summary>
public interface IRegressionTestRunner
{
    ValueTask<RegressionTestResult> RunAsync(string worktreePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministically discovers existing test projects and executes them under OS bounds. The discovery rule is
/// explicit (name or test package reference); a project that cannot be discovered never counts as "no tests".
/// </summary>
public sealed class DotnetRegressionTestRunner : IRegressionTestRunner
{
    private readonly BoundedProcessRunner _runner;

    public DotnetRegressionTestRunner(BoundedProcessRunner? runner = null)
        => _runner = runner ?? new BoundedProcessRunner();

    public async ValueTask<RegressionTestResult> RunAsync(string worktreePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);

        var projects = Discover(worktreePath);
        if (projects.Count == 0)
        {
            return RegressionTestResult.NotApplicable(Digest("not-applicable|no-existing-tests|" + Normalise(worktreePath)));
        }

        var details = new List<string>();
        var passed = true;

        foreach (var project in projects)
        {
            var relative = Path.GetRelativePath(worktreePath, project);
            var result = await _runner.RunAsync(
                "dotnet",
                ["test", project, "--nologo", "-v", "q"],
                worktreePath,
                new ProcessBounds(TimeSpan.FromMinutes(15), 4L * 1024 * 1024 * 1024),
                cancellationToken).ConfigureAwait(false);

            var ok = result.Completed && result.IsolationApplied && !result.TerminatedByWallTime;
            passed &= ok;
            details.Add($"{relative}:exit={(result.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "killed")}:isolation={(result.IsolationApplied ? "applied" : "missing")}");

            if (!ok)
            {
                foreach (var line in (result.StandardOutput + result.StandardError).Split('\n'))
                {
                    if (line.Contains("error", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        details.Add(Trim(line));
                    }
                }
            }
        }

        var detailText = string.Join("; ", details);
        var digest = Digest("regressions|" + detailText + "|" + string.Join("|", projects.Select(Path.GetFileName)));
        return new RegressionTestResult(true, passed, projects.Count, Trim(detailText), digest);
    }

    /// <summary>Existing test projects: an explicit name convention or a real test-SDK/package reference.</summary>
    public static IReadOnlyList<string> Discover(string worktreePath)
    {
        var root = Path.GetFullPath(worktreePath);
        return Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !IsBuildOutput(path))
            .Where(path => IsTestProject(path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsTestProject(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var text = File.ReadAllText(path);
            return text.Contains("Microsoft.NET.Test.Sdk", StringComparison.Ordinal)
                   || text.Contains("xunit", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("nunit", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("MSTest", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
           || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Normalise(string path) => path.Replace('\\', '/');

    private static string Trim(string text) => text.Length <= 4000 ? text : text[^4000..];

    private static string Digest(string text)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
