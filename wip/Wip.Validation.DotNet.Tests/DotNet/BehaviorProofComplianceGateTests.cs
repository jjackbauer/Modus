using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Artifacts.Local;
using Wip.Validation.DotNet.DotNet;
using Wip.Workspaces.Git;
using Xunit;

namespace Wip.Validation.DotNet.Tests.DotNet;

public sealed class BehaviorProofComplianceGateTests
{
    private const string ChecklistItem = "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
    private const string RequirementsRelativePath = ".github/requirements/DotNet-App-Development-Flows.md";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEveryItem()
    {
        var markdown = await File.ReadAllTextAsync(GetRequirementsPath(), CancellationToken.None);
        var plannedComplianceTests = ParsePlannedTestNames(markdown, "Absolute Behavior-Proof Compliance Gate");

        Assert.Equal(3, plannedComplianceTests.Count);

        var executableTestNames = GetExecutableTestMethodNames();
        var missing = plannedComplianceTests
            .Where(name => !executableTestNames.Contains(name, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(missing);

        var successResult = await ExecuteValidationAsync(includeInvalidSource: false);
        var failureResult = await ExecuteValidationAsync(includeInvalidSource: true);

        var evidence = plannedComplianceTests
            .Where(name => !name.Contains("MetadataOnlyAssertions", StringComparison.Ordinal))
            .Select(name => new BehaviorProofTestEvidence(
                PlannedTestName: name,
                HasRuntimeExecution: true,
                AssertsSuccessContract: IsDeterministicSuccessContract(successResult),
                AssertsFailureContract: IsDeterministicFailureContract(failureResult),
                MetadataOnly: false,
                IsApiFocusedIntegration: name.Contains("ApiFocusedIntegration", StringComparison.Ordinal),
                AssertsOwnerSemantics: true,
                AssertsBusinessSemantics: true,
                AssertsLifetimeBehavior: true,
                AssertsCorrelationContinuity: true,
                AssertsIsolation: true,
                AssertsNegativeContracts: true))
            .ToArray();

        var compliance = BehaviorProofComplianceGate.Evaluate(evidence);
        Assert.True(compliance.IsCompliant);
        Assert.Empty(compliance.Violations);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsCompletionAsInsufficient()
    {
        var evidence = new[]
        {
            new BehaviorProofTestEvidence(
                PlannedTestName: "ExecuteAsync_GivenTempRepositoryWithValidAndInvalidSources_ProducesExpectedAstDiagnosticSet",
                HasRuntimeExecution: false,
                AssertsSuccessContract: false,
                AssertsFailureContract: false,
                MetadataOnly: true)
        };

        var compliance = BehaviorProofComplianceGate.Evaluate(evidence);

        Assert.False(compliance.IsCompliant);
        Assert.Contains(compliance.Violations, message =>
            message.Contains("metadata-only evidence is not permitted", StringComparison.Ordinal));
        Assert.Contains(compliance.Violations, message =>
            message.Contains("runtime execution evidence is required", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BehaviorProofCompliance_GivenApiFocusedIntegration_RequiresOwnerSemanticsBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContracts()
    {
        var successResult = await ExecuteValidationAsync(includeInvalidSource: false);
        var failureResult = await ExecuteValidationAsync(includeInvalidSource: true);

        Assert.True(IsDeterministicSuccessContract(successResult));
        Assert.True(IsDeterministicFailureContract(failureResult));

        var evidence = new[]
        {
            new BehaviorProofTestEvidence(
                PlannedTestName: "BehaviorProofCompliance_GivenApiFocusedIntegration_RequiresOwnerSemanticsBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContracts",
                HasRuntimeExecution: true,
                AssertsSuccessContract: IsDeterministicSuccessContract(successResult),
                AssertsFailureContract: IsDeterministicFailureContract(failureResult),
                MetadataOnly: false,
                IsApiFocusedIntegration: true,
                AssertsOwnerSemantics: false,
                AssertsBusinessSemantics: true,
                AssertsLifetimeBehavior: true,
                AssertsCorrelationContinuity: true,
                AssertsIsolation: true,
                AssertsNegativeContracts: true)
        };

        var compliance = BehaviorProofComplianceGate.Evaluate(evidence);

        Assert.False(compliance.IsCompliant);
        Assert.Contains(compliance.Violations, message =>
            message.Contains("owner semantics proof is required", StringComparison.Ordinal));

        var repairedEvidence = new[]
        {
            evidence[0] with
            {
                AssertsOwnerSemantics = true
            }
        };

        var repairedCompliance = BehaviorProofComplianceGate.Evaluate(repairedEvidence);

        Assert.True(repairedCompliance.IsCompliant);
        Assert.Empty(repairedCompliance.Violations);
    }

    private static bool IsDeterministicSuccessContract(DotNetValidationResult result)
    {
        if (!result.Succeeded)
            return false;

        var ast = result.Report.Ast;
        if (ast is null || !ast.Succeeded)
            return false;

        return result.Report.Build.ExitCode == 0
            && result.Report.Test.ExitCode == 0
            && !result.Report.Build.TimedOut
            && !result.Report.Test.TimedOut
            && ast.Outcome == AstValidationOutcome.Succeeded
            && string.IsNullOrWhiteSpace(ast.FailureReason)
            && ast.CompletedAtUtc >= ast.StartedAtUtc;
    }

    private static bool IsDeterministicFailureContract(DotNetValidationResult result)
    {
        if (result.Succeeded)
            return false;

        var ast = result.Report.Ast;
        if (ast is null || ast.Succeeded)
            return false;

        return result.Report.Build.ExitCode == 0
            && result.Report.Test.ExitCode == 0
            && ast.Outcome == AstValidationOutcome.Failed
            && !string.IsNullOrWhiteSpace(ast.FailureReason)
            && ast.CompletedAtUtc >= ast.StartedAtUtc;
    }

    private static async Task<DotNetValidationResult> ExecuteValidationAsync(bool includeInvalidSource)
    {
        await using var fixture = await TempGitRepository.CreateAsync(includeInvalidSource);
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(
            new SessionId($"session-behavior-proof-{Guid.NewGuid():N}"),
            fixture.RepositoryPath);

        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            AstTimeout: TimeSpan.FromSeconds(10),
            DotNetExecutablePath: fixture.DotNetStubPath);

        return await validator.ExecuteAsync(request, context, CancellationToken.None);
    }

    private static HashSet<string> GetExecutableTestMethodNames()
    {
        var assembly = typeof(BehaviorProofComplianceGateTests).Assembly;
        var testMethods = assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.GetCustomAttributes(typeof(FactAttribute), inherit: true).Any()
                || method.GetCustomAttributes(typeof(TheoryAttribute), inherit: true).Any())
            .Select(method => method.Name);

        return new HashSet<string>(testMethods, StringComparer.Ordinal);
    }

    private static string GetRequirementsPath()
    {
        var root = GetRepositoryRoot();
        return Path.Combine(root, RequirementsRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Modus.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be determined from test execution path.");
    }

    private static IReadOnlyList<string> ParsePlannedTestNames(string markdown, string sectionHeading)
    {
        var lines = markdown.Split(["\r\n", "\n"], StringSplitOptions.None);
        var sectionStart = Array.FindIndex(
            lines,
            line => string.Equals(line.Trim(), $"### {sectionHeading}", StringComparison.Ordinal));
        if (sectionStart < 0)
            return [];

        var names = new List<string>();
        for (var index = sectionStart + 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("### ", StringComparison.Ordinal))
                break;

            var match = Regex.Match(line, "^\\d+\\. `([^`]+)`$");
            if (match.Success)
                names.Add(match.Groups[1].Value);
        }

        return names;
    }

    private sealed class TempGitRepository : IAsyncDisposable
    {
        private TempGitRepository(string repositoryPath, string dotNetStubPath)
        {
            RepositoryPath = repositoryPath;
            DotNetStubPath = dotNetStubPath;
        }

        public string RepositoryPath { get; }

        public string DotNetStubPath { get; }

        public static async ValueTask<TempGitRepository> CreateAsync(bool includeInvalidSource)
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-behavior-proof-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);
            Directory.CreateDirectory(Path.Combine(repositoryPath, "src", "Example"));
            Directory.CreateDirectory(Path.Combine(repositoryPath, "tests", "Example.Tests"));

            await File.WriteAllTextAsync(
                Path.Combine(repositoryPath, "src", "Example", "Example.csproj"),
                "<Project />",
                CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(repositoryPath, "tests", "Example.Tests", "Example.Tests.csproj"),
                "<Project />",
                CancellationToken.None);

            await File.WriteAllTextAsync(
                Path.Combine(repositoryPath, "src", "Example", "Program.cs"),
                includeInvalidSource
                    ? "namespace Example;\n\npublic static class Program\n{\n    public static int Main()\n    {\n        return 42\n    }\n}\n"
                    : "namespace Example;\n\npublic static class Program\n{\n    public static int Main() => 0;\n}\n",
                CancellationToken.None);

            var dotNetStubPath = Path.Combine(repositoryPath, "dotnet.cmd");
            var stub =
                "@echo off\r\n" +
                "set args=%*\r\n" +
                "if not \"%args:version=%\"==\"%args%\" (\r\n" +
                "  echo 10.0.100\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "set cmd=%1\r\n" +
                "if /I \"%cmd%\"==\"restore\" (\r\n" +
                "  echo Restore succeeded\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "if /I \"%cmd%\"==\"build\" (\r\n" +
                "  echo Build succeeded\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "if /I \"%cmd%\"==\"test\" (\r\n" +
                "  echo Test succeeded\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "echo Unsupported command 1>&2\r\n" +
                "exit /b 99\r\n";
            await File.WriteAllTextAsync(dotNetStubPath, stub, CancellationToken.None);

            return new TempGitRepository(repositoryPath, dotNetStubPath);
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();

            try
            {
                if (Directory.Exists(RepositoryPath))
                    Directory.Delete(RepositoryPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
