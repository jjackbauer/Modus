using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Wip.Factory.Wip.Governance;
using Wip.Factory.Wip.Isolation;

namespace Wip.Factory.Host.Factory;

/// <summary>The versioned review policy an approval is bound to (v0.4 FAC-P0-02).</summary>
public static class ReviewPolicy
{
    public const string CheckRunnerVersion = "factory-review-checks@2";
    public const string ValidationPolicy = "policy://factory/review/validation@2";
    public const string CoveragePolicy = "policy://factory/review/requirement-coverage@2";
    public const string ArchitecturePolicy = "policy://factory/review/architecture@2";
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

        var projects = Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !IsBuildOutput(path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        checks.Add(new ArchitectureCheck(
            "single-repository",
            !Directory.EnumerateDirectories(root, ".git", SearchOption.AllDirectories)
                .Any(directory => !string.Equals(Path.GetFullPath(directory), Path.Combine(root, ".git"), StringComparison.OrdinalIgnoreCase)),
            projects.Length > 0 ? "no nested repository" : "no-project-file"));

        checks.Add(new ArchitectureCheck("project-outside-worktree", projects.All(path => IsInside(root, path)), projects.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var frameworks = projects
            .SelectMany(ReadTargetFrameworks)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        checks.Add(new ArchitectureCheck(
            "framework-net10",
            frameworks.Length > 0 && frameworks.All(static framework => string.Equals(framework, ReviewPolicy.SupportedFramework, StringComparison.Ordinal)),
            frameworks.Length == 0 ? "no-target-framework" : string.Join(",", frameworks)));

        var webProjects = projects.Where(IsAspNetCoreProject).ToArray();
        var nonWebProjects = projects.Where(path => !IsAspNetCoreProject(path)).ToArray();
        var unsupportedApplications = nonWebProjects
            .Where(path => !IsTestProject(path) && IsExecutableProject(path))
            .ToArray();

        checks.Add(new ArchitectureCheck(
            "aspnetcore-web-sdk",
            projects.Length > 0 && webProjects.Length >= 1,
            webProjects.Length + "/" + projects.Length + " web projects; "
            + nonWebProjects.Count(IsTestProject) + " test projects; "
            + nonWebProjects.Count(path => !IsTestProject(path)) + " library projects"));

        checks.Add(new ArchitectureCheck(
            "no-unsupported-application-project",
            unsupportedApplications.Length == 0,
            unsupportedApplications.Length == 0
                ? "one web application; test/library projects permitted"
                : string.Join(",", unsupportedApplications.Select(path => Path.GetRelativePath(root, path)))));

        var entryPoint = Directory
            .EnumerateFiles(root, "Program.cs", SearchOption.AllDirectories)
            .Where(static path => !IsBuildOutput(path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        var entryPointOk = entryPoint is not null && HasEntryPoint(entryPoint);
        checks.Add(new ArchitectureCheck(
            "entry-point",
            entryPointOk,
            entryPoint is null ? "no-Program.cs" : Path.GetRelativePath(root, entryPoint)));

        var escapes = EnumerateReparsePoints(root).ToArray();
        checks.Add(new ArchitectureCheck(
            "no-reparse-escape",
            escapes.Length == 0,
            escapes.Length == 0 ? "none" : string.Join(",", escapes)));

        var passed = checks.All(static check => check.Passed);
        var subject = ReviewEvidence.Digest("architecture|" + root + "|" + string.Join("|", projects.Select(Path.GetFileName)));
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

    private static IEnumerable<string> ReadTargetFrameworks(string projectPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch (Exception exception) when (exception is IOException or System.Xml.XmlException)
        {
            yield break;
        }

        foreach (var element in document.Descendants())
        {
            if (element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            {
                foreach (var framework in element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    yield return framework;
                }
            }
        }
    }

    private static bool IsAspNetCoreProject(string projectPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch (Exception exception) when (exception is IOException or System.Xml.XmlException)
        {
            return false;
        }

        var project = document.Root;
        if (project is not null
            && project.Attribute("Sdk")?.Value.Contains("Microsoft.NET.Sdk.Web", StringComparison.Ordinal) == true)
        {
            return true;
        }

        return document.Descendants().Any(element =>
            element.Name.LocalName == "FrameworkReference"
            && string.Equals(element.Attribute("Include")?.Value, "Microsoft.AspNetCore.App", StringComparison.Ordinal));
    }

    /// <summary>A test project is permitted alongside the one web application.</summary>
    private static bool IsTestProject(string projectPath)
    {
        var name = Path.GetFileNameWithoutExtension(projectPath);
        if (name.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var text = File.ReadAllText(projectPath);
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

    /// <summary>A non-test project that declares an executable output is a second application, not a library.</summary>
    private static bool IsExecutableProject(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            return document.Descendants()
                .Where(element => element.Name.LocalName == "OutputType")
                .Any(element => element.Value.Contains("Exe", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or System.Xml.XmlException)
        {
            return false;
        }
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
