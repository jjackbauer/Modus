using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Wip.Shell.E2E.Compliance;

public sealed record E2EMatrixEntry(string ScenarioId, string Scenario, string TestName);

public sealed record E2EMatrixTestProjectMapping(string TestName, string ProjectFileRelativePath);

public sealed record E2EMatrixRowExecutionResult(
    string ScenarioId,
    string TestName,
    string TestProjectPath,
    string TestFilter,
    int ExitCode,
    string Output);

public sealed record E2EMatrixRowFailureViolation(
    string ScenarioId,
    string TestName,
    string TestProjectPath,
    string TestFilter,
    int ExitCode,
    string Output);

public sealed record E2EMatrixComplianceGateResult(
    bool IsCompliant,
    IReadOnlyList<E2EMatrixRowFailureViolation> RowFailures,
    IReadOnlyList<string> Messages)
{
    public static E2EMatrixComplianceGateResult Compliant()
        => new(true, [], []);

    public static E2EMatrixComplianceGateResult Failed(
        IReadOnlyList<E2EMatrixRowFailureViolation> rowFailures,
        IReadOnlyList<string> messages)
        => new(rowFailures.Count == 0 && messages.Count == 0, rowFailures, messages);
}

public interface IE2EMatrixTestExecutor
{
    Task<E2EMatrixRowExecutionResult> ExecuteRowAsync(
        string repositoryRoot,
        E2EMatrixEntry entry,
        string projectFileRelativePath,
        bool useNoBuild,
        CancellationToken cancellationToken = default);
}

public static class E2EMatrixComplianceRegistry
{
    public const string MatrixHeading = "### E2E-001..E2E-035 Executable Transcript Matrix";
    public const int ExpectedRowCount = 35;
    public const string SkipE2EMatrixRowExecutionEnvironmentVariable = "WIP_SKIP_E2E_MATRIX_ROW_EXECUTION";

    public static bool ShouldSkipE2EMatrixRowExecution()
    {
        var value = Environment.GetEnvironmentVariable(SkipE2EMatrixRowExecutionEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static void ConfigureNestedTestProcessEnvironment(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        startInfo.Environment[SkipE2EMatrixRowExecutionEnvironmentVariable] = "1";
        startInfo.Environment[BehaviorProofComplianceRegistry.SkipMappedTestExecutionEnvironmentVariable] = "1";
    }

    private static readonly E2EMatrixTestProjectSpec[] TestProjectSpecs =
    [
        new("WIP/Wip.Shell.E2E.Tests", "WIP/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj", Priority: 0),
        new("WIP/Wip.Builder.Tests", "WIP/Wip.Builder.Tests/Wip.Builder.Tests.csproj", Priority: 1),
        new("wip/Wip.Shell.E2E.Tests", "wip/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj", Priority: 0),
        new("wip/Wip.Builder.Tests", "wip/Wip.Builder.Tests/Wip.Builder.Tests.csproj", Priority: 1),
        new("tests/Wip.Shell.E2E.Tests", "tests/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj", Priority: 0),
        new("tests/Wip.Builder.Tests", "tests/Wip.Builder.Tests/Wip.Builder.Tests.csproj", Priority: 1),
    ];

    public static WipRequirementsDocumentRegistration NextStepsDocument
        => BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.NextSteps);

    public static IReadOnlyList<E2EMatrixEntry> ParseMatrixEntries(string requirementsMarkdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementsMarkdown);

        var headingIndex = requirementsMarkdown.IndexOf(MatrixHeading, StringComparison.Ordinal);
        if (headingIndex < 0)
        {
            throw new InvalidOperationException($"Missing heading '{MatrixHeading}' in requirements document.");
        }

        var lines = requirementsMarkdown[headingIndex..]
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        var entries = new List<E2EMatrixEntry>();
        foreach (var line in lines)
        {
            if (!line.StartsWith("| E2E-", StringComparison.Ordinal))
            {
                continue;
            }

            var columns = line.Split('|', StringSplitOptions.TrimEntries);
            if (columns.Length < 4)
            {
                continue;
            }

            var scenarioId = columns[1];
            var scenario = columns[2];
            var testName = columns[3].Trim('`');
            entries.Add(new E2EMatrixEntry(scenarioId, scenario, testName));
        }

        return entries;
    }

    public static IReadOnlyList<E2EMatrixTestProjectMapping> DiscoverTestProjectMappings(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        const string methodPattern = "public\\s+(?:async\\s+)?(?:Task(?:<[^>]+>)?|void)\\s+(?<name>[A-Za-z0-9_]+)\\s*\\(";
        var mappingsByTestName = new Dictionary<string, (int Priority, string ProjectFile)>(StringComparer.Ordinal);

        foreach (var projectSpec in TestProjectSpecs)
        {
            var projectDirectory = Path.Combine(repositoryRoot, NormalizePath(projectSpec.ProjectDirectory));
            if (!Directory.Exists(projectDirectory))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                         .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                             && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            {
                var contents = File.ReadAllText(filePath);
                var matches = Regex.Matches(contents, methodPattern, RegexOptions.Multiline);
                foreach (Match match in matches)
                {
                    var searchStart = Math.Max(0, match.Index - 600);
                    var prefix = contents[searchStart..match.Index];
                    if (!prefix.Contains("[Fact", StringComparison.Ordinal)
                        && !prefix.Contains("[Theory", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var testName = match.Groups["name"].Value;
                    if (!mappingsByTestName.TryGetValue(testName, out var existing)
                        || projectSpec.Priority < existing.Priority)
                    {
                        mappingsByTestName[testName] = (projectSpec.Priority, projectSpec.ProjectFile);
                    }
                }
            }
        }

        return mappingsByTestName
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(static entry => new E2EMatrixTestProjectMapping(entry.Key, entry.Value.ProjectFile))
            .ToArray();
    }

    public static string ResolveProjectFileForTest(
        string testName,
        IReadOnlyList<E2EMatrixTestProjectMapping> mappings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testName);
        ArgumentNullException.ThrowIfNull(mappings);

        var mapping = mappings.SingleOrDefault(
            candidate => string.Equals(candidate.TestName, testName, StringComparison.Ordinal));

        if (mapping is null)
        {
            throw new InvalidOperationException($"No executable xUnit test project mapping found for '{testName}'.");
        }

        return mapping.ProjectFileRelativePath;
    }

    public static async Task<IReadOnlyList<E2EMatrixRowExecutionResult>> ExecuteAllRowsAsync(
        string repositoryRoot,
        IReadOnlyList<E2EMatrixEntry> entries,
        IE2EMatrixTestExecutor? executor = null,
        IReadOnlyList<E2EMatrixTestProjectMapping>? mappings = null,
        bool useNoBuild = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(entries);

        executor ??= new DotNetE2EMatrixTestExecutor();
        mappings ??= DiscoverTestProjectMappings(repositoryRoot);

        var results = new List<E2EMatrixRowExecutionResult>(entries.Count);
        foreach (var entry in entries)
        {
            var projectFile = ResolveProjectFileForTest(entry.TestName, mappings);
            results.Add(await executor.ExecuteRowAsync(
                repositoryRoot,
                entry,
                projectFile,
                useNoBuild,
                cancellationToken));
        }

        return results;
    }

    public static E2EMatrixRowFailureViolation CreateRowFailureViolation(E2EMatrixRowExecutionResult executionResult)
    {
        ArgumentNullException.ThrowIfNull(executionResult);

        return new E2EMatrixRowFailureViolation(
            executionResult.ScenarioId,
            executionResult.TestName,
            executionResult.TestProjectPath,
            executionResult.TestFilter,
            executionResult.ExitCode,
            executionResult.Output);
    }

    public static E2EMatrixComplianceGateResult EvaluateRowExecutions(
        IReadOnlyList<E2EMatrixRowExecutionResult> executionResults)
    {
        ArgumentNullException.ThrowIfNull(executionResults);

        var failures = executionResults
            .Where(static result => result.ExitCode != 0)
            .Select(CreateRowFailureViolation)
            .ToArray();

        if (failures.Length == 0)
        {
            return E2EMatrixComplianceGateResult.Compliant();
        }

        var messages = failures
            .Select(static failure =>
                $"E2E matrix row '{failure.ScenarioId}' mapped test '{failure.TestName}' failed: " +
                $"project='{failure.TestProjectPath}' filter='{failure.TestFilter}' exitCode={failure.ExitCode}.{Environment.NewLine}{failure.Output}")
            .ToArray();

        return E2EMatrixComplianceGateResult.Failed(failures, messages);
    }

    private static string NormalizePath(string relativePath)
        => relativePath.Replace('/', Path.DirectorySeparatorChar);

    private sealed record E2EMatrixTestProjectSpec(string ProjectDirectory, string ProjectFile, int Priority);
}

public sealed class DotNetE2EMatrixTestExecutor : IE2EMatrixTestExecutor
{
    public async Task<E2EMatrixRowExecutionResult> ExecuteRowAsync(
        string repositoryRoot,
        E2EMatrixEntry entry,
        string projectFileRelativePath,
        bool useNoBuild,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFileRelativePath);

        var projectPath = Path.Combine(repositoryRoot, projectFileRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var filter = BehaviorProofComplianceRegistry.BuildMappedTestFilter([entry.TestName]);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        E2EMatrixComplianceRegistry.ConfigureNestedTestProcessEnvironment(startInfo);
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(projectPath);
        if (useNoBuild)
        {
            startInfo.ArgumentList.Add("--no-build");
        }

        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add(filter);
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("minimal");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start dotnet test for '{projectPath}'.");

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = (await standardOutput) + (await standardError);
        return new E2EMatrixRowExecutionResult(
            entry.ScenarioId,
            entry.TestName,
            projectPath,
            filter,
            process.ExitCode,
            output);
    }
}