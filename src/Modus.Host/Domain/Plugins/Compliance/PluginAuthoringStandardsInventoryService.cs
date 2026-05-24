using System.Xml.Linq;

namespace Modus.Host.Plugins.Compliance;

public sealed record PluginAuthoringStandardsViolation(
    string ProjectPath,
    string ProjectId,
    string Category,
    string Rule,
    string Evidence,
    string RuntimeRisk);

public sealed record PluginAuthoringStandardsInventory(
    int TotalProjectsScanned,
    IReadOnlyList<PluginAuthoringStandardsViolation> Violations)
{
    public IReadOnlyList<string> NonCompliantProjectIds => Violations
        .Select(static violation => violation.ProjectId)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static projectId => projectId, StringComparer.Ordinal)
        .ToArray();
}

public sealed class PluginAuthoringStandardsInventoryService
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public PluginAuthoringStandardsInventory DiscoverNonCompliantProjects(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new ArgumentException("A repository root path is required.", nameof(repositoryRoot));
        }

        var root = Path.GetFullPath(repositoryRoot);
        var scopeRoots = new[]
        {
            Path.Combine(root, "plugins"),
            Path.Combine(root, "src", "SamplePlugins"),
        };

        var projectPaths = scopeRoots
            .Where(Directory.Exists)
            .SelectMany(static directory => Directory.EnumerateFiles(directory, "Plugin*.csproj", SearchOption.AllDirectories))
            .OrderBy(static path => path, PathComparer)
            .ToArray();

        var violations = new List<PluginAuthoringStandardsViolation>();
        foreach (var projectPath in projectPaths)
        {
            var projectViolations = EvaluateProject(root, projectPath);
            violations.AddRange(projectViolations);
        }

        return new PluginAuthoringStandardsInventory(projectPaths.Length, violations);
    }

    private static IReadOnlyList<PluginAuthoringStandardsViolation> EvaluateProject(string repositoryRoot, string projectPath)
    {
        var violations = new List<PluginAuthoringStandardsViolation>();
        var projectId = Path.GetFileNameWithoutExtension(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        var isTopLevelPluginsProject = PathComparer.Equals(projectDirectory, Path.Combine(repositoryRoot, "plugins"));

        XDocument projectXml;
        try
        {
            projectXml = XDocument.Load(projectPath);
        }
        catch (Exception ex)
        {
            violations.Add(new PluginAuthoringStandardsViolation(
                ProjectPath: projectPath,
                ProjectId: projectId,
                Category: "project-metadata",
                Rule: "parseable-csproj",
                Evidence: ex.Message,
                RuntimeRisk: "project-load-failure"));
            return violations;
        }

        EvaluateMetadata(repositoryRoot, projectPath, projectId, projectXml, violations);
        EvaluateSourceLayout(repositoryRoot, projectPath, projectId, projectXml, isTopLevelPluginsProject, violations);
        EvaluateRuntimeContractShape(projectPath, projectId, projectXml, violations);

        return violations;
    }

    private static void EvaluateMetadata(
        string repositoryRoot,
        string projectPath,
        string projectId,
        XDocument projectXml,
        ICollection<PluginAuthoringStandardsViolation> violations)
    {
        AssertPropertyEquals(projectPath, projectId, projectXml, "TargetFramework", "net10.0", "project-metadata", "target-framework-net10", "runtime-load-failure", violations);
        AssertPropertyEquals(projectPath, projectId, projectXml, "Nullable", "enable", "project-metadata", "nullable-enabled", "typed-contract-nullability-drift", violations);
        AssertPropertyEquals(projectPath, projectId, projectXml, "ImplicitUsings", "enable", "project-metadata", "implicit-usings-enabled", "build-time-contract-drift", violations);

        var modusCoreReference = ResolveModusCoreReference(repositoryRoot, projectPath, projectXml);
        if (!modusCoreReference.IsValid)
        {
            violations.Add(new PluginAuthoringStandardsViolation(
                ProjectPath: projectPath,
                ProjectId: projectId,
                Category: "project-metadata",
                Rule: "valid-modus-core-reference",
                Evidence: modusCoreReference.Evidence,
                RuntimeRisk: "project-load-failure"));
        }
    }

    private static void EvaluateSourceLayout(
        string repositoryRoot,
        string projectPath,
        string projectId,
        XDocument projectXml,
        bool isTopLevelPluginsProject,
        ICollection<PluginAuthoringStandardsViolation> violations)
    {
        var compileItems = projectXml
            .Descendants()
            .Where(static x => string.Equals(x.Name.LocalName, "Compile", StringComparison.OrdinalIgnoreCase))
            .Select(static x => (string?)x.Attribute("Include"))
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!)
            .ToArray();

        var enableDefaultCompileItems = ReadProperty(projectXml, "EnableDefaultCompileItems");

        if (isTopLevelPluginsProject)
        {
            if (!string.Equals(enableDefaultCompileItems, "false", StringComparison.OrdinalIgnoreCase) || compileItems.Length == 0)
            {
                violations.Add(new PluginAuthoringStandardsViolation(
                    ProjectPath: projectPath,
                    ProjectId: projectId,
                    Category: "source-layout",
                    Rule: "one-plugin-per-assembly-compile-isolation",
                    Evidence: $"EnableDefaultCompileItems={enableDefaultCompileItems ?? "<missing>"}; explicit Compile entries={compileItems.Length}",
                    RuntimeRisk: "cross-project-source-bleed"));
            }
        }

        var projectDirectory = Path.GetDirectoryName(projectPath) ?? repositoryRoot;
        var compiledFileCount = EstimateCompiledSourceCount(projectDirectory, compileItems, enableDefaultCompileItems);
        if (compiledFileCount == 0)
        {
            violations.Add(new PluginAuthoringStandardsViolation(
                ProjectPath: projectPath,
                ProjectId: projectId,
                Category: "source-layout",
                Rule: "compiled-source-present",
                Evidence: "No compiled .cs files detected for project.",
                RuntimeRisk: "runtime-dispatch-unreachable"));
        }
    }

    private static void EvaluateRuntimeContractShape(
        string projectPath,
        string projectId,
        XDocument projectXml,
        ICollection<PluginAuthoringStandardsViolation> violations)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        var compileItems = projectXml
            .Descendants()
            .Where(static x => string.Equals(x.Name.LocalName, "Compile", StringComparison.OrdinalIgnoreCase))
            .Select(static x => (string?)x.Attribute("Include"))
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!)
            .ToArray();
        var enableDefaultCompileItems = ReadProperty(projectXml, "EnableDefaultCompileItems");

        var sourceFiles = ResolveCompiledSourceFiles(projectDirectory, compileItems, enableDefaultCompileItems)
            .ToArray();

        var hasTypedSyncResponder = false;
        var pluginClassCount = 0;
        foreach (var sourcePath in sourceFiles)
        {
            var content = File.ReadAllText(sourcePath);
            if (content.Contains("ISyncResponder<SyncRequest", StringComparison.Ordinal)
                || content.Contains("ISyncResponder< Modus.Core.Messaging.SyncRequest", StringComparison.Ordinal))
            {
                hasTypedSyncResponder = true;
            }

            pluginClassCount += CountPluginClassDeclarations(content);
        }

        var operations = ParseDelimitedList(ReadProperty(projectXml, "ModusOperations"));
        if (operations.Length > 0 && !hasTypedSyncResponder)
        {
            violations.Add(new PluginAuthoringStandardsViolation(
                ProjectPath: projectPath,
                ProjectId: projectId,
                Category: "runtime-contract-shape",
                Rule: "typed-sync-responder-contract",
                Evidence: "ModusOperations declared but no typed ISyncResponder<SyncRequest, SyncResponse<TPayload>> implementation discovered.",
                RuntimeRisk: "dispatch-failure"));
        }

        var runtimePluginType = ReadProperty(projectXml, "ModusRuntimePluginType");
        if (pluginClassCount != 1 && string.IsNullOrWhiteSpace(runtimePluginType))
        {
            violations.Add(new PluginAuthoringStandardsViolation(
                ProjectPath: projectPath,
                ProjectId: projectId,
                Category: "runtime-contract-shape",
                Rule: "deterministic-runtime-plugin-type",
                Evidence: $"Detected {pluginClassCount} plugin class declarations and no ModusRuntimePluginType metadata.",
                RuntimeRisk: "dispatch-failure"));
        }
    }

    private static (bool IsValid, string Evidence) ResolveModusCoreReference(string repositoryRoot, string projectPath, XDocument projectXml)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? repositoryRoot;
        var expectedCorePath = Path.GetFullPath(Path.Combine(repositoryRoot, "src", "Modus.Core", "Modus.Core.csproj"));

        var projectReferences = projectXml
            .Descendants()
            .Where(static x => string.Equals(x.Name.LocalName, "ProjectReference", StringComparison.OrdinalIgnoreCase))
            .Select(static x => (string?)x.Attribute("Include"))
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!)))
            .ToArray();

        if (projectReferences.Any(path => PathComparer.Equals(path, expectedCorePath)))
        {
            return (true, "ProjectReference -> src/Modus.Core/Modus.Core.csproj");
        }

        var packageReferences = projectXml
            .Descendants()
            .Where(static x => string.Equals(x.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase))
            .Select(static x => (string?)x.Attribute("Include"))
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include => include!)
            .ToArray();

        if (packageReferences.Any(static include => string.Equals(include, "Modus.Core", StringComparison.OrdinalIgnoreCase)))
        {
            return (true, "PackageReference -> Modus.Core");
        }

        var referenceEvidence = projectReferences.Length == 0
            ? "No Modus.Core project or package reference found."
            : $"ProjectReference entries do not resolve to src/Modus.Core/Modus.Core.csproj: {string.Join(", ", projectReferences)}";

        return (false, referenceEvidence);
    }

    private static void AssertPropertyEquals(
        string projectPath,
        string projectId,
        XDocument projectXml,
        string propertyName,
        string expected,
        string category,
        string rule,
        string runtimeRisk,
        ICollection<PluginAuthoringStandardsViolation> violations)
    {
        var actual = ReadProperty(projectXml, propertyName);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add(new PluginAuthoringStandardsViolation(
                ProjectPath: projectPath,
                ProjectId: projectId,
                Category: category,
                Rule: rule,
                Evidence: $"{propertyName}={actual ?? "<missing>"}; expected={expected}",
                RuntimeRisk: runtimeRisk));
        }
    }

    private static string? ReadProperty(XDocument projectXml, string propertyName)
    {
        return projectXml
            .Descendants()
            .FirstOrDefault(x => string.Equals(x.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();
    }

    private static int EstimateCompiledSourceCount(string projectDirectory, IReadOnlyList<string> compileIncludes, string? enableDefaultCompileItems)
    {
        return ResolveCompiledSourceFiles(projectDirectory, compileIncludes, enableDefaultCompileItems).Count();
    }

    private static IEnumerable<string> ResolveCompiledSourceFiles(string projectDirectory, IReadOnlyList<string> compileIncludes, string? enableDefaultCompileItems)
    {
        if (string.Equals(enableDefaultCompileItems, "false", StringComparison.OrdinalIgnoreCase) && compileIncludes.Count > 0)
        {
            foreach (var include in compileIncludes)
            {
                var includePath = Path.GetFullPath(Path.Combine(projectDirectory, include));
                if (File.Exists(includePath))
                {
                    yield return includePath;
                }
            }

            yield break;
        }

        if (!Directory.Exists(projectDirectory))
        {
            yield break;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(sourcePath);
            if (string.Equals(fileName, "AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, "GlobalUsings.cs", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return sourcePath;
        }
    }

    private static string[] ParseDelimitedList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static int CountPluginClassDeclarations(string source)
    {
        var count = 0;
        const string marker = " class ";
        var start = 0;

        while (true)
        {
            var index = source.IndexOf(marker, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            var nameStart = index + marker.Length;
            while (nameStart < source.Length && char.IsWhiteSpace(source[nameStart]))
            {
                nameStart++;
            }

            var nameEnd = nameStart;
            while (nameEnd < source.Length && (char.IsLetterOrDigit(source[nameEnd]) || source[nameEnd] == '_'))
            {
                nameEnd++;
            }

            if (nameEnd > nameStart)
            {
                var className = source.Substring(nameStart, nameEnd - nameStart);
                if (className.EndsWith("Plugin", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            start = nameEnd;
        }
    }
}