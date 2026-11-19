using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Wip.Factory.Wip.Governance;
using Wip.Factory.Wip.Isolation;

namespace Wip.Factory.Host.Factory;

/// <summary>The versioned review policy an approval is bound to (v0.4 FAC-P0-02).</summary>
public static class ReviewPolicy
{
    public const string CheckRunnerVersion = "factory-review-checks@3";
    public const string ValidationPolicy = "policy://factory/review/validation@2";
    public const string CoveragePolicy = "policy://factory/review/requirement-coverage@2";
    public const string ArchitecturePolicy = "policy://factory/review/architecture@3";
    public const string SecurityPolicyVersion = "security://factory/alpha-policy@1";
    public const string SupportedFramework = "net10.0";
    public const string SupportedClass = SupportedAppClass.AspNetCoreHttpNet10;
}

/// <summary>Everything one review check is allowed to observe. The checked-out revision is stated explicitly.</summary>
public sealed record ReviewCheckContext(
    string WorkItemId,
    string Worktree,
    string HeadSha,
    string BaseSha,
    AcceptanceContract Contract,
    bool ValidationPassed = false);

/// <summary>One independently executed review check.</summary>
public interface IReviewCheck
{
    ReviewCheckKind Kind { get; }

    ValueTask<ReviewCheckExecution> ExecuteAsync(ReviewCheckContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs the four mandatory checks in order. A check that throws produces an <c>error</c> execution, never a
/// pass; a set that omits a check is refused by <see cref="FactoryReviewService.EvaluateWithEvidence"/>.
/// </summary>
public sealed class FactoryReviewCheckSet
{
    private readonly IReadOnlyList<IReviewCheck> _checks;

    public FactoryReviewCheckSet(params IReviewCheck[] checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        _checks = checks;
    }

    public IReadOnlyList<ReviewCheckKind> Kinds => _checks.Select(static check => check.Kind).ToArray();

    public static FactoryReviewCheckSet Default(
        ICandidateBuildVerifier builds,
        IRegressionTestRunner regressions,
        ICandidateHost candidates,
        AcceptanceContractRunner contracts)
        => new(
            new ValidationReviewCheck(builds, regressions),
            new RequirementCoverageReviewCheck(candidates, contracts),
            new ArchitectureConformanceReviewCheck(),
            new SecurityPolicyReviewCheck());

    public async ValueTask<IReadOnlyList<ReviewCheckExecution>> RunAsync(
        ReviewCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<ReviewCheckExecution>(_checks.Count);
        var validationPassed = false;

        foreach (var check in _checks.OrderBy(static check => check.Kind))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var execution = await check
                    .ExecuteAsync(context with { ValidationPassed = validationPassed }, cancellationToken)
                    .ConfigureAwait(false);

                results.Add(execution);
                if (check.Kind == ReviewCheckKind.Validation && execution.Passed)
                {
                    validationPassed = true;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(ReviewEvidence.Failed(
                    check.Kind,
                    "check-runner",
                    ReviewPolicy.CheckRunnerVersion,
                    context.HeadSha,
                    ReviewPolicy.CheckRunnerVersion,
                    "error:" + exception.GetType().Name + ":" + exception.Message,
                    ReviewCheckExecution.ErrorOutcome,
                    ReviewEvidence.Digest("error|" + check.Kind + "|" + exception.GetType().Name + "|" + exception.Message)));
            }
        }

        return results;
    }
}

/// <summary>Helpers that turn observations into complete, digest-bearing evidence records.</summary>
public static class ReviewEvidence
{
    public static ReviewCheckExecution Failed(
        ReviewCheckKind kind,
        string checkId,
        string executedBy,
        string reviewedSha,
        string policyVersion,
        string detail,
        string outcome = ReviewCheckExecution.FailedOutcome,
        string? artifactDigest = null,
        string? subjectDigest = null)
        => new(
            kind,
            checkId,
            executedBy,
            ReviewPolicy.CheckRunnerVersion,
            reviewedSha,
            policyVersion,
            outcome,
            subjectDigest ?? Digest("subject|" + kind + "|" + reviewedSha),
            artifactDigest ?? Digest("artifact|" + kind + "|" + reviewedSha + "|" + detail),
            DateTimeOffset.UtcNow,
            detail);

    public static string Digest(string text)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>
/// Validation = a real separate build plus the repository's own mandatory regression tests. Generated tests cannot
/// satisfy it. When no pre-existing test project exists the result is explicitly <c>not-applicable</c>, not a silent
/// pass (v0.4 FAC-P0-02.2, FAC-P1-07).
/// </summary>
public sealed class ValidationReviewCheck : IReviewCheck
{
    private readonly ICandidateBuildVerifier _builds;
    private readonly IRegressionTestRunner _regressions;

    public ValidationReviewCheck(ICandidateBuildVerifier builds, IRegressionTestRunner regressions)
    {
        _builds = builds ?? throw new ArgumentNullException(nameof(builds));
        _regressions = regressions ?? throw new ArgumentNullException(nameof(regressions));
    }

    public ReviewCheckKind Kind => ReviewCheckKind.Validation;

    public async ValueTask<ReviewCheckExecution> ExecuteAsync(ReviewCheckContext context, CancellationToken cancellationToken = default)
    {
        var (buildOk, buildDetail) = await _builds.VerifyAsync(context.Worktree, cancellationToken).ConfigureAwait(false);
        var regression = await _regressions.RunAsync(context.Worktree, cancellationToken).ConfigureAwait(false);

        var passed = buildOk && regression.Passed;
        var regressionState = regression.Ran ? (regression.Passed ? "passed" : "failed") : "not-applicable";
        var detail = $"build={(buildOk ? "passed" : "failed")};regressions={regressionState};projects={regression.Projects}";

        if (!buildOk)
        {
            detail += ";build-detail=" + Short(buildDetail);
        }

        if (!regression.Passed)
        {
            detail += ";regression-detail=" + Short(regression.Detail);
        }

        var subject = ReviewEvidence.Digest(
            "validation|" + (buildOk ? "build-passed" : "build-failed") + "|" + regression.ArtifactDigest);

        return new ReviewCheckExecution(
            Kind,
            "validation.build+regressions",
            "dotnet-build+existing-regression-suite",
            ReviewPolicy.CheckRunnerVersion,
            context.HeadSha,
            ReviewPolicy.ValidationPolicy,
            passed ? ReviewCheckExecution.PassedOutcome : ReviewCheckExecution.FailedOutcome,
            subject,
            ReviewEvidence.Digest(subject + "|" + detail),
            DateTimeOffset.UtcNow,
            Short(detail));
    }

    private static string Short(string text) => text.Length <= 2000 ? text : text[^2000..];
}

/// <summary>
/// Requirement coverage = the frozen HTTP contract, executed against the candidate. Scenario IDs and results are
/// recorded explicitly; an unreachable or unverified candidate is a failed check (v0.4 FAC-P0-02.3).
/// </summary>
public sealed class RequirementCoverageReviewCheck : IReviewCheck
{
    private readonly ICandidateHost _candidates;
    private readonly AcceptanceContractRunner _contracts;

    public RequirementCoverageReviewCheck(ICandidateHost candidates, AcceptanceContractRunner contracts)
    {
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    }

    public ReviewCheckKind Kind => ReviewCheckKind.RequirementCoverage;

    public async ValueTask<ReviewCheckExecution> ExecuteAsync(ReviewCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!context.ValidationPassed)
        {
            return ReviewEvidence.Failed(
                Kind,
                "contract.http-scenarios",
                "acceptance-contract-runner",
                context.HeadSha,
                ReviewPolicy.CoveragePolicy,
                "validation-failed-before-coverage",
                subjectDigest: context.Contract.Digest);
        }

        var session = CandidateSession.None;
        try
        {
            session = await _candidates.StartAsync(context.Worktree, cancellationToken).ConfigureAwait(false);
            if (!session.IdentityVerified)
            {
                return ReviewEvidence.Failed(
                    Kind,
                    "contract.http-scenarios",
                    "acceptance-contract-runner",
                    context.HeadSha,
                    ReviewPolicy.CoveragePolicy,
                    "candidate-identity-unverified",
                    subjectDigest: context.Contract.Digest);
            }

            var result = await _contracts.RunAsync(context.Contract, session.BaseAddress, cancellationToken).ConfigureAwait(false);
            var scenarios = string.Join(",", result.Scenarios.Select(static scenario => scenario.Id + "=" + (scenario.Passed ? "pass" : "fail")));
            var detail = "scenarios=" + scenarios
                         + ";candidatePid=" + session.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                         + ";identity=" + (session.IdentityProof ?? "reported");

            if (!result.Passed)
            {
                detail += ";failures=" + result.FailureSummary;
            }

            return new ReviewCheckExecution(
                Kind,
                "contract.http-scenarios",
                "acceptance-contract-runner",
                ReviewPolicy.CheckRunnerVersion,
                context.HeadSha,
                ReviewPolicy.CoveragePolicy,
                result.Passed ? ReviewCheckExecution.PassedOutcome : ReviewCheckExecution.FailedOutcome,
                context.Contract.Digest,
                ReviewEvidence.Digest(context.Contract.Digest + "|" + detail),
                DateTimeOffset.UtcNow,
                detail);
        }
        finally
        {
            await session.Stop().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Architectural conformance = deterministic checks for the supported class. A reachable HTTP port is not an
/// architectural check (v0.4 FAC-P0-02.4).
/// </summary>
public sealed class ArchitectureConformanceReviewCheck : IReviewCheck
{
    public ReviewCheckKind Kind => ReviewCheckKind.ArchitecturalConformance;

    public ValueTask<ReviewCheckExecution> ExecuteAsync(ReviewCheckContext context, CancellationToken cancellationToken = default)
    {
        var inspection = ArchitectureInspection.Inspect(context.Worktree);
        var detail = string.Join(";", inspection.Checks.Select(static check => check.Name + "=" + (check.Passed ? "pass" : "fail:" + check.Detail)));
        var artifact = ReviewEvidence.Digest("architecture|" + context.HeadSha + "|" + detail);

        return ValueTask.FromResult(new ReviewCheckExecution(
            Kind,
            "architecture.supported-class",
            "factory-architecture-inspector",
            ReviewPolicy.CheckRunnerVersion,
            context.HeadSha,
            ReviewPolicy.ArchitecturePolicy,
            inspection.Passed ? ReviewCheckExecution.PassedOutcome : ReviewCheckExecution.FailedOutcome,
            inspection.SubjectDigest,
            artifact,
            DateTimeOffset.UtcNow,
            detail));
    }
}

public sealed record ArchitectureCheck(string Name, bool Passed, string Detail);

public sealed record ArchitectureInspectionResult(
    bool Passed,
    string SubjectDigest,
    IReadOnlyList<ArchitectureCheck> Checks);

/// <summary>
/// Static, deterministic conformance checks: one repository, ASP.NET Core on .NET 10, the prescribed entry point,
/// a project inside the worktree, and no reparse-point escape inside it.
/// </summary>
public static class ArchitectureInspection
{
    public static ArchitectureInspectionResult Inspect(string worktreePath)
    {
        var checks = new List<ArchitectureCheck>();
        var root = Path.GetFullPath(worktreePath);

        var projectPaths = Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !IsBuildOutput(path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        var projects = projectPaths.Select(path => ReadProject(root, path)).ToArray();

        checks.Add(new ArchitectureCheck(
            "single-repository",
            !Directory.EnumerateDirectories(root, ".git", SearchOption.AllDirectories)
                .Any(directory => !string.Equals(Path.GetFullPath(directory), Path.Combine(root, ".git"), StringComparison.OrdinalIgnoreCase)),
            projects.Length > 0 ? "no nested repository" : "no-project-file"));

        checks.Add(new ArchitectureCheck(
            "project-outside-worktree",
            projectPaths.All(path => IsInside(root, path)),
            projectPaths.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var layoutProblems = projects
            .Where(static project => project.Problem is not null)
            .Select(static project => project.RelativePath + "=" + project.Problem)
            .ToArray();

        checks.Add(new ArchitectureCheck(
            "project-layout-supported",
            projects.Length > 0 && layoutProblems.Length == 0,
            projects.Length == 0
                ? "no-project-file"
                : layoutProblems.Length == 0
                    ? "sdk-style projects classified structurally"
                    : string.Join(",", layoutProblems)));

        var frameworks = projects
            .SelectMany(static project => project.Frameworks)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var projectsWithoutFramework = projects
            .Where(static project => project.Frameworks.Count == 0)
            .Select(static project => project.RelativePath)
            .ToArray();

        checks.Add(new ArchitectureCheck(
            "framework-net10",
            projects.Length > 0
            && projectsWithoutFramework.Length == 0
            && frameworks.All(static framework => string.Equals(framework, ReviewPolicy.SupportedFramework, StringComparison.Ordinal)),
            frameworks.Length == 0
                ? "no-target-framework"
                : string.Join(",", frameworks)
                  + (projectsWithoutFramework.Length == 0 ? string.Empty : ";missing=" + string.Join(",", projectsWithoutFramework))));

        var webProjects = projects.Where(static project => project.IsWebApplication).ToArray();
        var testProjects = projects.Where(static project => project.IsTestProject).ToArray();
        var libraryProjects = projects.Where(static project => project.IsLibrary).ToArray();
        var unsupportedApplications = projects.Where(static project => project.IsUnsupportedApplication).ToArray();

        checks.Add(new ArchitectureCheck(
            "aspnetcore-web-sdk",
            projects.Length > 0 && webProjects.Length == 1,
            $"{webProjects.Length}/{projects.Length} web applications; {testProjects.Length} test projects; {libraryProjects.Length} libraries"));

        checks.Add(new ArchitectureCheck(
            "no-unsupported-application-project",
            unsupportedApplications.Length == 0,
            unsupportedApplications.Length == 0
                ? "one web application; test/library projects permitted"
                : string.Join(",", unsupportedApplications.Select(static project => project.RelativePath + ":" + project.ClassificationDetail))));

        var programFiles = Directory
            .EnumerateFiles(root, "Program.cs", SearchOption.AllDirectories)
            .Where(static path => !IsBuildOutput(path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        var webProject = webProjects.Length == 1 ? webProjects[0] : null;
        var entryPointOk = webProject is not null
                           && programFiles.Length == 1
                           && IsInside(webProject.DirectoryPath, programFiles[0])
                           && HasEntryPoint(programFiles[0]);

        checks.Add(new ArchitectureCheck(
            "entry-point",
            entryPointOk,
            programFiles.Length == 0
                ? "no-Program.cs"
                : programFiles.Length > 1
                    ? "multiple-Program.cs:" + string.Join(",", programFiles.Select(path => Path.GetRelativePath(root, path)))
                    : Path.GetRelativePath(root, programFiles[0])));

        var escapes = EnumerateReparsePoints(root).ToArray();
        checks.Add(new ArchitectureCheck(
            "no-reparse-escape",
            escapes.Length == 0,
            escapes.Length == 0 ? "none" : string.Join(",", escapes)));

        var passed = checks.All(static check => check.Passed);
        var subject = ReviewEvidence.Digest(
            "architecture@3|" + string.Join('\u001f', projects.Select(static project => project.ManifestLine)));
        return new ArchitectureInspectionResult(passed, subject, checks);
    }

    private static bool HasEntryPoint(string programFile)
    {
        try
        {
            var text = File.ReadAllText(programFile);
            return text.Contains("Main(", StringComparison.Ordinal)
                   || text.Contains("CreateBuilder", StringComparison.Ordinal)
                   || text.Contains("WebApplication", StringComparison.Ordinal)
                   || text.Contains("WebHost.CreateDefaultBuilder", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private enum ProjectSdkKind
    {
        NetSdk,
        WebSdk,
        RazorSdk,
        WorkerSdk,
        BlazorWebAssemblySdk,
        MstestSdk,
        Unsupported
    }

    private enum ProjectOutputType
    {
        Library,
        Exe,
        WinExe,
        Unsupported
    }

    /// <summary>
    /// One project classified from structural MSBuild facts only: SDK identity, declared output type, test-provider
    /// declarations, framework references and entry-point evidence. Filenames and free text never decide a class.
    /// </summary>
    private sealed record ArchitectureProject(
        string RelativePath,
        string DirectoryPath,
        ProjectSdkKind Sdk,
        ProjectOutputType OutputType,
        bool TestDeclared,
        bool WebDeclared,
        bool AspNetCoreReference,
        bool HasProgramFile,
        bool HasExplicitEntryPoint,
        IReadOnlyList<string> Frameworks,
        IReadOnlyList<string> TestMarkers,
        string? Problem)
    {
        public bool IsTestProject => TestDeclared && !WebDeclared;

        public bool IsWebApplication
            => !TestDeclared
               && (WebDeclared
                   || (AspNetCoreReference
                       && OutputType == ProjectOutputType.Exe
                       && (Sdk == ProjectSdkKind.NetSdk || Sdk == ProjectSdkKind.RazorSdk)));

        public bool IsLibrary
            => !TestDeclared && !IsWebApplication && OutputType == ProjectOutputType.Library;

        public bool IsUnsupportedApplication
            => !TestDeclared
               && !IsWebApplication
               && (OutputType is ProjectOutputType.Exe or ProjectOutputType.WinExe
                   || Sdk == ProjectSdkKind.WorkerSdk);

        public string ClassificationDetail
            => "sdk=" + Sdk + ";output=" + OutputType + ";test=" + TestDeclared + ";web=" + WebDeclared;

        public string ManifestLine
            => RelativePath + "|" + Sdk + "|" + OutputType + "|" + TestDeclared + "|" + WebDeclared + "|"
               + AspNetCoreReference + "|" + HasProgramFile + "|" + HasExplicitEntryPoint + "|"
               + string.Join(",", Frameworks) + "|" + string.Join(",", TestMarkers);
    }

    private static readonly HashSet<string> TestPackageMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.NET.Test.Sdk",
        "Microsoft.Testing.Platform",
        "Microsoft.Testing.Platform.MSBuild",
        "MSTest.TestAdapter",
        "MSTest.TestFramework",
        "NUnit",
        "NUnit3TestAdapter",
        "xunit",
        "xunit.v3",
        "xunit.runner.visualstudio",
        "TUnit"
    };

    private static ArchitectureProject ReadProject(string root, string projectPath)
    {
        var relativePath = Path.GetRelativePath(root, projectPath);
        var directoryPath = Path.GetDirectoryName(projectPath) ?? root;
        var problems = new List<string>();

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using var reader = XmlReader.Create(projectPath, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return InvalidProject(relativePath, directoryPath, "project-unreadable");
        }

        var project = document.Root;
        if (project is null || !string.Equals(project.Name.LocalName, "Project", StringComparison.Ordinal))
        {
            return InvalidProject(relativePath, directoryPath, "unsupported-project-root");
        }

        if (project.Elements().Any(static element => string.Equals(element.Name.LocalName, "Import", StringComparison.Ordinal)))
        {
            problems.Add("explicit-msbuild-import");
        }

        var inheritedFrameworks = ReadInheritedFrameworkDefaults(root, directoryPath, problems);
        var sdkAttribute = project.Attribute("Sdk")?.Value.Trim();
        var sdkElements = project.Elements()
            .Where(static element => string.Equals(element.Name.LocalName, "Sdk", StringComparison.Ordinal))
            .Select(static element => element.Attribute("Name")?.Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(sdkAttribute) && sdkElements.Length > 0)
        {
            problems.Add("ambiguous-sdk-declaration");
        }

        string[] sdkNames = !string.IsNullOrWhiteSpace(sdkAttribute)
            ? sdkAttribute!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : sdkElements!.Select(static value => value!).ToArray();

        if (sdkNames.Length == 0)
        {
            return InvalidProject(relativePath, directoryPath, "unsupported-non-sdk-project");
        }

        if (sdkNames.Length > 1)
        {
            problems.Add("ambiguous-sdk-list");
        }

        var sdk = ClassifySdk(sdkNames[0]);
        if (sdk == ProjectSdkKind.Unsupported)
        {
            problems.Add("unsupported-sdk:" + sdkNames[0]);
        }

        var properties = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var conditionalProperties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in project.Elements()
                     .Where(static element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.Ordinal)))
        {
            var groupConditioned = !string.IsNullOrWhiteSpace(group.Attribute("Condition")?.Value);
            foreach (var property in group.Elements())
            {
                var name = property.Name.LocalName;
                if (name is not ("OutputType" or "IsTestProject" or "TargetFramework" or "TargetFrameworks" or "StartupObject"))
                {
                    continue;
                }

                var value = property.Value.Trim();
                if (groupConditioned
                    || !string.IsNullOrWhiteSpace(property.Attribute("Condition")?.Value)
                    || value.Contains("$(", StringComparison.Ordinal))
                {
                    conditionalProperties.Add(name);
                    continue;
                }

                if (!properties.TryGetValue(name, out var values))
                {
                    values = [];
                    properties[name] = values;
                }

                values.Add(value);
            }
        }

        foreach (var conditional in conditionalProperties.OrderBy(static value => value, StringComparer.Ordinal))
        {
            problems.Add("conditional-msbuild-property:" + conditional);
        }

        var outputValues = DistinctValues(properties, "OutputType");
        if (outputValues.Count > 1)
        {
            problems.Add("conflicting-output-type");
        }

        var outputType = outputValues.Count == 1
            ? ParseOutputType(outputValues[0])
            : DefaultOutputType(sdk);
        if (outputType == ProjectOutputType.Unsupported && outputValues.Count == 1)
        {
            problems.Add("unsupported-output-type:" + outputValues[0]);
        }

        var testFlags = DistinctValues(properties, "IsTestProject");
        bool? isTestProject = null;
        if (testFlags.Count > 1)
        {
            problems.Add("conflicting-is-test-project");
        }
        else if (testFlags.Count == 1)
        {
            if (bool.TryParse(testFlags[0], out var parsed))
            {
                isTestProject = parsed;
            }
            else
            {
                problems.Add("malformed-is-test-project");
            }
        }

        var startupObjects = DistinctValues(properties, "StartupObject");
        if (startupObjects.Count > 1)
        {
            problems.Add("conflicting-startup-object");
        }

        var frameworks = ReadFrameworks(properties, inheritedFrameworks, problems);
        var testMarkers = ReadTestMarkers(project, problems);

        if (isTestProject == false && (sdk == ProjectSdkKind.MstestSdk || testMarkers.Count > 0))
        {
            problems.Add("test-declaration-conflict");
        }

        var testDeclared = isTestProject == true || sdk == ProjectSdkKind.MstestSdk || testMarkers.Count > 0;
        var webDeclared = sdk is ProjectSdkKind.WebSdk or ProjectSdkKind.BlazorWebAssemblySdk;
        var aspNetCoreReference = HasAspNetCoreReference(project, problems);

        if (testDeclared && webDeclared)
        {
            problems.Add("test-project-uses-web-sdk");
        }

        if (testDeclared && sdk == ProjectSdkKind.WorkerSdk)
        {
            problems.Add("test-project-uses-worker-sdk");
        }

        var hasProgramFile = false;
        try
        {
            hasProgramFile = Directory
                .EnumerateFiles(directoryPath, "Program.cs", SearchOption.AllDirectories)
                .Any(static path => !IsBuildOutput(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problems.Add("project-content-unreadable");
        }

        var hasExplicitEntryPoint = startupObjects.Count > 0;
        if (testDeclared
            && outputType is ProjectOutputType.Exe or ProjectOutputType.WinExe
            && (hasProgramFile || hasExplicitEntryPoint))
        {
            problems.Add("disguised-executable:test-project-declares-entry-point");
        }

        if (testDeclared && outputType == ProjectOutputType.WinExe)
        {
            problems.Add("test-project-uses-windows-executable-output");
        }

        return new ArchitectureProject(
            relativePath,
            directoryPath,
            sdk,
            outputType,
            testDeclared,
            webDeclared,
            aspNetCoreReference,
            hasProgramFile,
            hasExplicitEntryPoint,
            frameworks,
            testMarkers,
            problems.Count == 0 ? null : string.Join("|", problems));
    }

    private static ArchitectureProject InvalidProject(string relativePath, string directoryPath, string problem)
        => new(
            relativePath,
            directoryPath,
            ProjectSdkKind.Unsupported,
            ProjectOutputType.Unsupported,
            TestDeclared: false,
            WebDeclared: false,
            AspNetCoreReference: false,
            HasProgramFile: false,
            HasExplicitEntryPoint: false,
            Frameworks: [],
            TestMarkers: [],
            Problem: problem);

    private static ProjectSdkKind ClassifySdk(string sdkName)
    {
        var name = sdkName.Trim();
        var versionSeparator = name.IndexOf('/');
        if (versionSeparator > 0)
        {
            name = name[..versionSeparator];
        }

        return name switch
        {
            "Microsoft.NET.Sdk" => ProjectSdkKind.NetSdk,
            "Microsoft.NET.Sdk.Web" => ProjectSdkKind.WebSdk,
            "Microsoft.NET.Sdk.Razor" => ProjectSdkKind.RazorSdk,
            "Microsoft.NET.Sdk.Worker" => ProjectSdkKind.WorkerSdk,
            "Microsoft.NET.Sdk.BlazorWebAssembly" => ProjectSdkKind.BlazorWebAssemblySdk,
            "MSTest.Sdk" => ProjectSdkKind.MstestSdk,
            _ => ProjectSdkKind.Unsupported
        };
    }

    private static ProjectOutputType DefaultOutputType(ProjectSdkKind sdk) => sdk switch
    {
        ProjectSdkKind.WebSdk
            or ProjectSdkKind.WorkerSdk
            or ProjectSdkKind.BlazorWebAssemblySdk
            or ProjectSdkKind.MstestSdk => ProjectOutputType.Exe,
        ProjectSdkKind.NetSdk or ProjectSdkKind.RazorSdk => ProjectOutputType.Library,
        _ => ProjectOutputType.Unsupported
    };

    private static ProjectOutputType ParseOutputType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "library" => ProjectOutputType.Library,
        "exe" => ProjectOutputType.Exe,
        "winexe" => ProjectOutputType.WinExe,
        _ => ProjectOutputType.Unsupported
    };

    private static IReadOnlyList<string> DistinctValues(Dictionary<string, List<string>> properties, string name)
        => properties.TryGetValue(name, out var values)
            ? values
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray()
            : [];

    /// <summary>
    /// Reconstructs the nearest `Directory.Build.props` target framework so a legitimate central TFM is still
    /// accepted, while any inherited OutputType/test declaration is refused as an unclassifiable override.
    /// </summary>
    private static IReadOnlyList<string> ReadInheritedFrameworkDefaults(
        string root,
        string projectDirectory,
        List<string> problems)
    {
        var directory = Path.GetFullPath(projectDirectory);
        while (true)
        {
            var targets = Path.Combine(directory, "Directory.Build.targets");
            if (File.Exists(targets))
            {
                InspectInheritedMsbuildFile(targets, root, allowFrameworks: false, problems);
            }

            var props = Path.Combine(directory, "Directory.Build.props");
            if (File.Exists(props))
            {
                return InspectInheritedMsbuildFile(props, root, allowFrameworks: true, problems);
            }

            if (string.Equals(directory, root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent is null || !IsInside(root, parent))
            {
                break;
            }

            directory = parent;
        }

        return [];
    }

    private static IReadOnlyList<string> InspectInheritedMsbuildFile(
        string path,
        string root,
        bool allowFrameworks,
        List<string> problems)
    {
        var relativePath = Path.GetRelativePath(root, path);
        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using var reader = XmlReader.Create(path, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            problems.Add("inherited-msbuild-file-unreadable:" + relativePath);
            return [];
        }

        var project = document.Root;
        if (project is null || !string.Equals(project.Name.LocalName, "Project", StringComparison.Ordinal))
        {
            problems.Add("inherited-msbuild-file-invalid:" + relativePath);
            return [];
        }

        var frameworks = new List<string>();
        if (project.Elements().Any(static element => string.Equals(element.Name.LocalName, "Import", StringComparison.Ordinal)))
        {
            problems.Add("inherited-msbuild-import:" + relativePath);
        }

        foreach (var group in project.Elements()
                     .Where(static element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.Ordinal)))
        {
            var groupConditioned = !string.IsNullOrWhiteSpace(group.Attribute("Condition")?.Value);
            foreach (var property in group.Elements())
            {
                var name = property.Name.LocalName;
                var isFramework = name is "TargetFramework" or "TargetFrameworks";
                var isClassification = name is "OutputType" or "IsTestProject" or "StartupObject" or "UseAppHost";
                if (!isFramework && !isClassification)
                {
                    continue;
                }

                if (groupConditioned
                    || !string.IsNullOrWhiteSpace(property.Attribute("Condition")?.Value)
                    || property.Value.Contains("$(", StringComparison.Ordinal))
                {
                    problems.Add("inherited-msbuild-property-conditional:" + relativePath + ":" + name);
                    continue;
                }

                if (isFramework && allowFrameworks)
                {
                    frameworks.AddRange(property.Value.Split(
                        ';',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
                else
                {
                    problems.Add("inherited-msbuild-override:" + relativePath + ":" + name);
                }
            }
        }

        foreach (var itemGroup in project.Elements()
                     .Where(static element => string.Equals(element.Name.LocalName, "ItemGroup", StringComparison.Ordinal)))
        {
            foreach (var item in itemGroup.Elements())
            {
                if (item.Name.LocalName is "PackageReference" or "FrameworkReference" or "ProjectReference")
                {
                    problems.Add("inherited-msbuild-item:" + relativePath + ":" + item.Name.LocalName);
                }
            }
        }

        return frameworks
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadFrameworks(
        Dictionary<string, List<string>> properties,
        IReadOnlyList<string> inheritedFrameworks,
        List<string> problems)
    {
        var single = DistinctValues(properties, "TargetFramework");
        var many = DistinctValues(properties, "TargetFrameworks");

        if (single.Count > 0 && many.Count > 0)
        {
            problems.Add("ambiguous-target-framework");
            return [];
        }

        var declared = single.Count > 0 ? single : many;
        if (declared.Count == 0)
        {
            declared = inheritedFrameworks;
        }

        if (declared.Count == 0)
        {
            problems.Add("missing-target-framework");
            return [];
        }

        var frameworks = declared
            .SelectMany(static value => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        if (frameworks.Length == 0)
        {
            problems.Add("missing-target-framework");
        }

        return frameworks;
    }

    private static IReadOnlyList<string> ReadTestMarkers(XElement project, List<string> problems)
    {
        var markers = new List<string>();

        foreach (var itemGroup in project.Elements()
                     .Where(static element => string.Equals(element.Name.LocalName, "ItemGroup", StringComparison.Ordinal)))
        {
            var groupConditioned = !string.IsNullOrWhiteSpace(itemGroup.Attribute("Condition")?.Value);
            foreach (var reference in itemGroup.Elements()
                         .Where(static element => string.Equals(element.Name.LocalName, "PackageReference", StringComparison.Ordinal)))
            {
                var include = reference.Attribute("Include")?.Value.Trim();
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                var separator = include.IndexOf('/');
                var packageName = separator > 0 ? include[..separator] : include;
                if (!TestPackageMarkers.Contains(packageName))
                {
                    continue;
                }

                var conditioned = groupConditioned
                                  || !string.IsNullOrWhiteSpace(reference.Attribute("Condition")?.Value)
                                  || (reference.Attribute("Version")?.Value.Contains("$(", StringComparison.Ordinal) ?? false);

                if (conditioned)
                {
                    problems.Add("conditional-test-package:" + packageName);
                }

                markers.Add(packageName);
            }
        }

        return markers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasAspNetCoreReference(XElement project, List<string> problems)
    {
        var referenced = false;

        foreach (var itemGroup in project.Elements()
                     .Where(static element => string.Equals(element.Name.LocalName, "ItemGroup", StringComparison.Ordinal)))
        {
            foreach (var reference in itemGroup.Elements()
                         .Where(static element => string.Equals(element.Name.LocalName, "FrameworkReference", StringComparison.Ordinal)))
            {
                if (!string.Equals(reference.Attribute("Include")?.Value.Trim(), "Microsoft.AspNetCore.App", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(itemGroup.Attribute("Condition")?.Value)
                    || !string.IsNullOrWhiteSpace(reference.Attribute("Condition")?.Value))
                {
                    problems.Add("conditional-aspnetcore-framework-reference");
                }

                referenced = true;
            }
        }

        return referenced;
    }

    private static bool IsInside(string root, string path)
    {
        var full = Path.GetFullPath(path);
        return full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
    }

    internal static IEnumerable<string> EnumerateReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (IsBuildOutput(entry) || string.Equals(Path.GetFileName(entry), ".git", StringComparison.Ordinal))
                {
                    continue;
                }

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    yield return Path.GetRelativePath(root, entry);
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
           || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}

/// <summary>
/// Security = a defined, versioned policy for this alpha: no reparse escape, no executable payloads, no obvious
/// secret material, and a closed tool/subcommand surface. It is not a vulnerability scan and does not claim to be
/// one (v0.4 FAC-P0-02.5).
/// </summary>
public sealed class SecurityPolicyReviewCheck : IReviewCheck
{
    private static readonly string[] ExecutableExtensions =
        [".exe", ".dll", ".so", ".dylib", ".ps1", ".bat", ".cmd", ".sh", ".com", ".scr", ".msi"];

    private static readonly string[] SecretFileNames =
        [".env", ".env.local", "id_rsa", "id_ed25519", "id_dsa", "secrets.json"];

    public ReviewCheckKind Kind => ReviewCheckKind.Security;

    public ValueTask<ReviewCheckExecution> ExecuteAsync(ReviewCheckContext context, CancellationToken cancellationToken = default)
    {
        var checks = new List<ArchitectureCheck>();

        var escapes = ArchitectureInspection.EnumerateReparsePoints(context.Worktree).ToArray();
        checks.Add(new ArchitectureCheck("no-reparse-escape", escapes.Length == 0, escapes.Length == 0 ? "none" : string.Join(",", escapes)));

        var payloads = EnumeratePayloads(context.Worktree).ToArray();
        checks.Add(new ArchitectureCheck("no-executable-payload", payloads.Length == 0, payloads.Length == 0 ? "none" : string.Join(",", payloads)));

        var secrets = EnumerateSecretMaterial(context.Worktree).ToArray();
        checks.Add(new ArchitectureCheck("no-secret-material", secrets.Length == 0, secrets.Length == 0 ? "none" : string.Join(",", secrets)));

        var toolIds = RealModelImplementationRunner.PinnedToolIds.Select(static id => id.Value).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        checks.Add(new ArchitectureCheck(
            "pinned-tool-surface",
            toolIds.Length == 6 && toolIds.Distinct(StringComparer.Ordinal).Count() == 6 && toolIds.All(static id => id.StartsWith("factory.tool.", StringComparison.Ordinal)),
            string.Join(",", toolIds)));

        var subcommands = RunCommandTool.AllowedSubcommandsForPolicy.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        checks.Add(new ArchitectureCheck(
            "closed-dotnet-subcommands",
            subcommands.SequenceEqual(["build", "new", "publish", "restore", "run", "test"], StringComparer.Ordinal),
            string.Join(",", subcommands)));

        checks.Add(new ArchitectureCheck(
            "child-output-redaction",
            string.Equals(RunCommandTool.RedactionPolicyVersion, "factory/redaction@1", StringComparison.Ordinal),
            RunCommandTool.RedactionPolicyVersion));

        var passed = checks.All(static check => check.Passed);
        var detail = string.Join(";", checks.Select(static check => check.Name + "=" + (check.Passed ? "pass" : "fail:" + check.Detail)));
        var subject = ReviewEvidence.Digest(ReviewPolicy.SecurityPolicyVersion + "|" + string.Join("|", toolIds));

        return ValueTask.FromResult(new ReviewCheckExecution(
            Kind,
            "security.alpha-policy",
            "factory-security-policy",
            ReviewPolicy.CheckRunnerVersion,
            context.HeadSha,
            ReviewPolicy.SecurityPolicyVersion,
            passed ? ReviewCheckExecution.PassedOutcome : ReviewCheckExecution.FailedOutcome,
            subject,
            ReviewEvidence.Digest(subject + "|" + detail),
            DateTimeOffset.UtcNow,
            detail));
    }

    private static IEnumerable<string> EnumeratePayloads(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file) || file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var extension = Path.GetExtension(file);
            if (ExecutableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.GetRelativePath(root, file);
            }
        }
    }

    private static IEnumerable<string> EnumerateSecretMaterial(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file) || file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var name = Path.GetFileName(file);
            if (SecretFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                || name.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".p12", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.GetRelativePath(root, file);
            }
        }
    }

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
           || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
