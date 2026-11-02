using System.Diagnostics;
using Wip.ShellHost.Hosting;

namespace Wip.Shell.E2E.Compliance;

public enum WipRequirementsDocumentKind
{
    Bootstrap,
    NextSteps,
    DotNetDevelopmentFlows,
    ContributorReadmes,
    DynamicSystemPrompt,
    ValidationDotNet,
    ShellHost,
    IntegrityRemediation,
    Bones,
    BonesHost,
    BonesDeepSeek,
    BonesViewerVisualization,
    BonesViewerReplayScaling,
    BonesViewerBoardChainLayout,
    BonesViewerDominoRendering,
    BonesViewerLayoutAndStability,
    BonesScriptStrategies,
    BonesLearningLoopReliability,
    BonesViewerLearningLoopStatus,
    BonesBoardOrientation,
    BonesAdversarialCoLearning,
    BonesPerStageModelRouting,
    BonesStatusApiFixes,
}

public sealed record WipRequirementsDocumentRegistration(
    WipRequirementsDocumentKind Kind,
    string DisplayName,
    string RelativePath,
    string OwningTestProjectDirectory,
    string OwningTestProjectFile,
    bool UseNoBuildForComplianceExecution = false);

public sealed record ChecklistToTestBindingRule(
    string TraitName,
    string CanonicalComplianceTestName,
    string BindingDescription);

public sealed record UncheckedChecklistViolation(
    string DocumentRelativePath,
    string ChecklistItemText);

public sealed record MappedTestFailureViolation(
    string DocumentRelativePath,
    string ChecklistItemText,
    string TestProjectPath,
    string TestFilter,
    int ExitCode,
    string Output);

public sealed record BehaviorProofComplianceGateResult(
    bool IsCompliant,
    IReadOnlyList<UncheckedChecklistViolation> UncheckedViolations,
    IReadOnlyList<MappedTestFailureViolation> MappedTestFailures,
    IReadOnlyList<string> Messages)
{
    public static BehaviorProofComplianceGateResult Compliant()
        => new(true, [], [], []);

    public static BehaviorProofComplianceGateResult Failed(
        IReadOnlyList<UncheckedChecklistViolation> uncheckedViolations,
        IReadOnlyList<MappedTestFailureViolation> mappedTestFailures,
        IReadOnlyList<string> messages)
        => new(
            uncheckedViolations.Count == 0 && mappedTestFailures.Count == 0 && messages.Count == 0,
            uncheckedViolations,
            mappedTestFailures,
            messages);
}

public sealed record BehaviorProofTestExecutionResult(
    string TestProjectPath,
    string TestFilter,
    int ExitCode,
    string Output);

public interface IBehaviorProofTestExecutor
{
    Task<BehaviorProofTestExecutionResult> ExecuteAsync(
        string repositoryRoot,
        WipRequirementsDocumentRegistration document,
        IReadOnlyList<string> testNames,
        CancellationToken cancellationToken = default);
}

public static class BehaviorProofComplianceRegistry
{
    public const string CanonicalComplianceTestName =
        "BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEachItem";

    public const string ChecklistItemTraitName = "ChecklistItem";

    public const string SkipMappedTestExecutionEnvironmentVariable = "WIP_SKIP_COMPLIANCE_MAPPED_TEST_EXECUTION";

    public static ChecklistToTestBindingRule DefaultBindingRule { get; } = new(
        TraitName: ChecklistItemTraitName,
        CanonicalComplianceTestName: CanonicalComplianceTestName,
        BindingDescription:
            "Checklist items bind to executable xUnit tests through Trait(\"ChecklistItem\", \"<item text>\") and each document's owning assembly hosts the canonical compliance test.");

    private static readonly WipRequirementsDocumentRegistration[] Documents =
    [
        new(
            WipRequirementsDocumentKind.Bootstrap,
            DisplayName: "Bootstrap",
            RelativePath: "WIP/requirements/WiP.Bootstrap.Requirements.md",
            OwningTestProjectDirectory: "WIP/Wip.Shell.E2E.Tests",
            OwningTestProjectFile: "WIP/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj"),
        new(
            WipRequirementsDocumentKind.NextSteps,
            DisplayName: "Next-Steps",
            RelativePath: ".github/requirements/Wip.Next-Steps.md",
            OwningTestProjectDirectory: "WIP/Wip.Shell.E2E.Tests",
            OwningTestProjectFile: "WIP/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj"),
        new(
            WipRequirementsDocumentKind.DotNetDevelopmentFlows,
            DisplayName: "DotNet-Development-Flows",
            RelativePath: ".github/requirements/Wip.DotNet-Development-Flows.md",
            OwningTestProjectDirectory: "WIP/Wip.Shell.E2E.Tests",
            OwningTestProjectFile: "WIP/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj"),
        new(
            WipRequirementsDocumentKind.ContributorReadmes,
            DisplayName: "Contributor-Readmes",
            RelativePath: ".github/requirements/WIP.Contributor-Readmes.md",
            OwningTestProjectDirectory: "WIP/Wip.Modus.Tests",
            OwningTestProjectFile: "WIP/Wip.Modus.Tests/Wip.Modus.Tests.csproj"),
        new(
            WipRequirementsDocumentKind.DynamicSystemPrompt,
            DisplayName: "Dynamic-System-Prompt",
            RelativePath: ".github/requirements/Wip.Dynamic-System-Prompt.md",
            OwningTestProjectDirectory: "WIP/Wip.Shell.Tests",
            OwningTestProjectFile: "WIP/Wip.Shell.Tests/Wip.Shell.Tests.csproj"),
        new(
            WipRequirementsDocumentKind.ValidationDotNet,
            DisplayName: "Validation.DotNet",
            RelativePath: ".github/requirements/Wip.Validation.DotNet.md",
            OwningTestProjectDirectory: "WIP/Wip.Validation.DotNet.Tests",
            OwningTestProjectFile: "WIP/Wip.Validation.DotNet.Tests/Wip.Validation.DotNet.Tests.csproj"),
        new(
            WipRequirementsDocumentKind.ShellHost,
            DisplayName: "ShellHost",
            RelativePath: ".github/requirements/Wip.ShellHost.md",
            OwningTestProjectDirectory: "WIP/Wip.ShellHost.Tests",
            OwningTestProjectFile: "WIP/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj"),
        new(
            WipRequirementsDocumentKind.IntegrityRemediation,
            DisplayName: "Integrity-Remediation",
            RelativePath: "harness/requirements/Wip.Integrity-Remediation.md",
            OwningTestProjectDirectory: "WIP/Wip.Shell.E2E.Tests",
            OwningTestProjectFile: "WIP/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.Bones,
            DisplayName: "Bones",
            RelativePath: "harness/requirements/Wip.Bones.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.E2E.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.E2E.Tests/Wip.Bones.E2E.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesHost,
            DisplayName: "BonesHost",
            RelativePath: "harness/requirements/Wip.Bones.Host.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Host.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesDeepSeek,
            DisplayName: "BonesDeepSeek",
            RelativePath: "harness/requirements/Wip.Bones.DeepSeek.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.ModelProviders.DeepSeek.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/Wip.Bones.ModelProviders.DeepSeek.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesViewerVisualization,
            DisplayName: "BonesViewerVisualization",
            RelativePath: "harness/requirements/Wip.Bones.ViewerVisualization.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Web.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesViewerReplayScaling,
            DisplayName: "BonesViewerReplayScaling",
            RelativePath: "harness/requirements/Wip.Bones.ViewerReplayScaling.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Web.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesViewerBoardChainLayout,
            DisplayName: "BonesViewerBoardChainLayout",
            RelativePath: "harness/requirements/Wip.Bones.ViewerBoardChainLayout.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Web.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesViewerDominoRendering,
            DisplayName: "BonesViewerDominoRendering",
            RelativePath: "harness/requirements/Wip.Bones.ViewerDominoRendering.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Web.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesViewerLayoutAndStability,
            DisplayName: "BonesViewerLayoutAndStability",
            RelativePath: "harness/requirements/Wip.Bones.ViewerLayoutAndStability.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Web.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesScriptStrategies,
            DisplayName: "BonesScriptStrategies",
            RelativePath: "harness/requirements/Wip.Bones.ScriptStrategies.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesLearningLoopReliability,
            DisplayName: "BonesLearningLoopReliability",
            RelativePath: "harness/requirements/Wip.Bones.LearningLoopReliability.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesViewerLearningLoopStatus,
            DisplayName: "BonesViewerLearningLoopStatus",
            RelativePath: "harness/requirements/Wip.Bones.ViewerLearningLoopStatus.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Web.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesBoardOrientation,
            DisplayName: "BonesBoardOrientation",
            RelativePath: "harness/requirements/Wip.Bones.BoardOrientation.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesAdversarialCoLearning,
            DisplayName: "BonesAdversarialCoLearning",
            RelativePath: "harness/requirements/Wip.Bones.AdversarialCoLearning.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesPerStageModelRouting,
            DisplayName: "BonesPerStageModelRouting",
            RelativePath: "harness/requirements/Wip.Bones.PerStageModelRouting.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
        new(
            WipRequirementsDocumentKind.BonesStatusApiFixes,
            DisplayName: "BonesStatusApiFixes",
            RelativePath: "harness/requirements/Wip.Bones.StatusApiFixes.md",
            OwningTestProjectDirectory: "WIP/Wip.Bones.Host.Tests",
            OwningTestProjectFile: "WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj",
            UseNoBuildForComplianceExecution: true),
    ];

    public static IReadOnlyList<WipRequirementsDocumentRegistration> RegisteredDocuments => Documents;

    public static IReadOnlyList<string> ParseUncheckedChecklistItems(string requirementsMarkdown)
        => BehaviorProofPolicy.ParseUncheckedChecklistItems(requirementsMarkdown);

    public static IReadOnlyList<UncheckedChecklistViolation> EvaluateUncheckedChecklistItems(
        string repositoryRoot,
        IReadOnlyList<WipRequirementsDocumentRegistration>? documents = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var violations = new List<UncheckedChecklistViolation>();
        foreach (var document in documents ?? Documents)
        {
            var documentPath = Path.Combine(repositoryRoot, NormalizePath(document.RelativePath));
            if (!File.Exists(documentPath))
            {
                continue;
            }

            var markdown = File.ReadAllText(documentPath);
            foreach (var uncheckedItem in ParseUncheckedChecklistItems(markdown))
            {
                violations.Add(new UncheckedChecklistViolation(document.RelativePath, uncheckedItem));
            }
        }

        return violations;
    }

    public static IReadOnlyList<UncheckedChecklistViolation> EvaluateUncheckedChecklistItemsForMarkdown(
        WipRequirementsDocumentRegistration document,
        string requirementsMarkdown)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementsMarkdown);

        return ParseUncheckedChecklistItems(requirementsMarkdown)
            .Select(item => new UncheckedChecklistViolation(document.RelativePath, item))
            .ToArray();
    }

    public static MappedTestFailureViolation CreateMappedTestFailureViolation(
        WipRequirementsDocumentRegistration document,
        string checklistItemText,
        BehaviorProofTestExecutionResult executionResult)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(checklistItemText);
        ArgumentNullException.ThrowIfNull(executionResult);

        return new MappedTestFailureViolation(
            document.RelativePath,
            checklistItemText,
            executionResult.TestProjectPath,
            executionResult.TestFilter,
            executionResult.ExitCode,
            executionResult.Output);
    }

    public static BehaviorProofComplianceGateResult EvaluateMappedTestFailures(
        IReadOnlyList<MappedTestFailureViolation> mappedTestFailures)
    {
        ArgumentNullException.ThrowIfNull(mappedTestFailures);

        if (mappedTestFailures.Count == 0)
        {
            return BehaviorProofComplianceGateResult.Compliant();
        }

        var messages = mappedTestFailures
            .Select(static failure =>
                $"Mapped test failure for '{failure.DocumentRelativePath}' checklist '{failure.ChecklistItemText}': " +
                $"project='{failure.TestProjectPath}' filter='{failure.TestFilter}' exitCode={failure.ExitCode}.{Environment.NewLine}{failure.Output}")
            .ToArray();

        return BehaviorProofComplianceGateResult.Failed([], mappedTestFailures, messages);
    }

    public static BehaviorProofComplianceGateResult EvaluateUncheckedViolations(
        IReadOnlyList<UncheckedChecklistViolation> uncheckedViolations)
    {
        ArgumentNullException.ThrowIfNull(uncheckedViolations);

        if (uncheckedViolations.Count == 0)
        {
            return BehaviorProofComplianceGateResult.Compliant();
        }

        var messages = uncheckedViolations
            .Select(static violation =>
                $"Unchecked checklist item in '{violation.DocumentRelativePath}': {violation.ChecklistItemText}")
            .ToArray();

        return BehaviorProofComplianceGateResult.Failed(uncheckedViolations, [], messages);
    }

    public static bool ShouldSkipMappedTestExecution()
    {
        var value = Environment.GetEnvironmentVariable(SkipMappedTestExecutionEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static BehaviorProofComplianceGateResult CreateAggregatedGateResult(
        IReadOnlyList<string> registrationErrors,
        IReadOnlyList<UncheckedChecklistViolation> uncheckedViolations,
        IReadOnlyList<MappedTestFailureViolation> mappedTestFailures)
    {
        ArgumentNullException.ThrowIfNull(registrationErrors);
        ArgumentNullException.ThrowIfNull(uncheckedViolations);
        ArgumentNullException.ThrowIfNull(mappedTestFailures);

        if (registrationErrors.Count == 0
            && uncheckedViolations.Count == 0
            && mappedTestFailures.Count == 0)
        {
            return BehaviorProofComplianceGateResult.Compliant();
        }

        var messages = registrationErrors
            .Concat(EvaluateUncheckedViolations(uncheckedViolations).Messages)
            .Concat(EvaluateMappedTestFailures(mappedTestFailures).Messages)
            .ToArray();

        return new BehaviorProofComplianceGateResult(
            IsCompliant: false,
            UncheckedViolations: uncheckedViolations,
            MappedTestFailures: mappedTestFailures,
            Messages: messages);
    }

    public static IReadOnlyList<WipRequirementsDocumentRegistration> SelectDocumentsWithCompleteChecklists(
        string repositoryRoot,
        IReadOnlyList<WipRequirementsDocumentRegistration>? documents = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        return (documents ?? Documents)
            .Where(document =>
            {
                var documentPath = Path.Combine(repositoryRoot, NormalizePath(document.RelativePath));
                if (!File.Exists(documentPath))
                {
                    return false;
                }

                var markdown = File.ReadAllText(documentPath);
                return ParseUncheckedChecklistItems(markdown).Count == 0;
            })
            .ToArray();
    }

    public static async Task<BehaviorProofComplianceGateResult> VerifyAllAsync(
        string repositoryRoot,
        IBehaviorProofTestExecutor? testExecutor = null,
        IReadOnlyList<WipRequirementsDocumentRegistration>? documents = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var scope = documents ?? Documents;
        var registrationErrors = ValidateRegistrations(repositoryRoot, scope);
        var uncheckedViolations = EvaluateUncheckedChecklistItems(repositoryRoot, scope);

        var mappedFailures = new List<MappedTestFailureViolation>();
        var executor = testExecutor;
        if (executor is null && !ShouldSkipMappedTestExecution())
        {
            executor = new DotNetBehaviorProofTestExecutor();
        }

        if (executor is not null)
        {
            foreach (var document in scope)
            {
                var execution = await executor.ExecuteAsync(
                    repositoryRoot,
                    document,
                    [CanonicalComplianceTestName],
                    cancellationToken);

                if (execution.ExitCode != 0)
                {
                    mappedFailures.Add(CreateMappedTestFailureViolation(
                        document,
                        $"[{document.DisplayName}] {CanonicalComplianceTestName}",
                        execution));
                }
            }
        }

        return CreateAggregatedGateResult(registrationErrors, uncheckedViolations, mappedFailures);
    }

    public static IReadOnlyList<string> ValidateRegistrations(
        string repositoryRoot,
        IReadOnlyList<WipRequirementsDocumentRegistration>? documents = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var errors = new List<string>();
        foreach (var document in documents ?? Documents)
        {
            if (string.IsNullOrWhiteSpace(document.OwningTestProjectDirectory))
            {
                errors.Add($"Document '{document.DisplayName}' is missing an owning test project directory.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(document.OwningTestProjectFile))
            {
                errors.Add($"Document '{document.DisplayName}' is missing an owning test project file.");
                continue;
            }

            var projectDirectory = Path.Combine(repositoryRoot, NormalizePath(document.OwningTestProjectDirectory));
            if (!Directory.Exists(projectDirectory))
            {
                errors.Add(
                    $"Document '{document.DisplayName}' owning test directory does not exist: {document.OwningTestProjectDirectory}");
            }

            var projectPath = Path.Combine(repositoryRoot, NormalizePath(document.OwningTestProjectFile));
            if (!File.Exists(projectPath))
            {
                errors.Add(
                    $"Document '{document.DisplayName}' owning test project does not exist: {document.OwningTestProjectFile}");
            }

            var requirementsPath = Path.Combine(repositoryRoot, NormalizePath(document.RelativePath));
            if (!File.Exists(requirementsPath))
            {
                errors.Add(
                    $"Document '{document.DisplayName}' requirements file does not exist: {document.RelativePath}");
            }
        }

        return errors;
    }

    public static string BuildMappedTestFilter(IReadOnlyList<string> testNames)
        => string.Join("|", testNames.Select(static name => $"FullyQualifiedName~{name}"));

    private static string NormalizePath(string relativePath)
        => relativePath.Replace('/', Path.DirectorySeparatorChar);
}

public sealed class DotNetBehaviorProofTestExecutor : IBehaviorProofTestExecutor
{
    public async Task<BehaviorProofTestExecutionResult> ExecuteAsync(
        string repositoryRoot,
        WipRequirementsDocumentRegistration document,
        IReadOnlyList<string> testNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(testNames);

        var projectPath = Path.Combine(
            repositoryRoot,
            document.OwningTestProjectFile.Replace('/', Path.DirectorySeparatorChar));
        var filter = BehaviorProofComplianceRegistry.BuildMappedTestFilter(testNames);

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
        startInfo.ArgumentList.Add("--no-build");

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
        return new BehaviorProofTestExecutionResult(projectPath, filter, process.ExitCode, output);
    }
}
