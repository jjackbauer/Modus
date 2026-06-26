using Wip.Validation.DotNet.DotNet;
using Xunit;

namespace Wip.Validation.DotNet.Tests.DotNet;

public sealed class RoslynAstAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_GivenProjectPathMissingOnDisk_ReturnsFailedResultWithDeterministicNotFoundEvidence()
    {
        await using var fixture = await TempCSharpProject.CreateAsync(
            [
                new ProjectDocument(
                    "Program.cs",
                    "namespace SampleApp;\n\npublic static class Program\n{\n    public static int Main() => 0;\n}\n")
            ]);

        var analyzer = new RoslynAstAnalyzer();

        var result = await analyzer.AnalyzeAsync(
            new AstValidationRequest(fixture.RepositoryPath, "src/SampleApp/Missing.csproj"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AstValidationOutcome.Failed, result.Outcome);
        Assert.Equal(0, result.DocumentCount);
        Assert.Equal(0, result.SyntaxTreeCount);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("Project file not found", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_GivenCompilableProject_ReturnsSucceededResultWithParsedSyntaxTrees()
    {
        await using var fixture = await TempCSharpProject.CreateAsync(
            [
                new ProjectDocument(
                    "Program.cs",
                    "namespace SampleApp;\n\npublic static class Program\n{\n    public static int Main()\n    {\n        return 0;\n    }\n}\n")
            ]);

        var analyzer = new RoslynAstAnalyzer();

        var result = await analyzer.AnalyzeAsync(
            new AstValidationRequest(fixture.RepositoryPath, fixture.ProjectRelativePath),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AstValidationOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.DocumentCount);
        Assert.Equal(1, result.SyntaxTreeCount);
        Assert.Empty(result.Diagnostics);
        Assert.Null(result.FailureReason);
        Assert.True(result.CompletedAtUtc >= result.StartedAtUtc);
    }

    [Fact]
    public async Task AnalyzeAsync_GivenKnownSyntaxError_ReturnsFailedResultWithExpectedDiagnosticRuleAndLocation()
    {
        await using var fixture = await TempCSharpProject.CreateAsync(
            [
                new ProjectDocument(
                    "Broken.cs",
                    "namespace SampleApp;\n\npublic static class Broken\n{\n    public static int Compute()\n    {\n        return 42\n    }\n}\n")
            ]);

        var analyzer = new RoslynAstAnalyzer();

        var result = await analyzer.AnalyzeAsync(
            new AstValidationRequest(fixture.RepositoryPath, fixture.ProjectRelativePath),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AstValidationOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.DocumentCount);
        Assert.Equal(1, result.SyntaxTreeCount);
        Assert.Equal("AST analysis found one or more error diagnostics.", result.FailureReason);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("CS1002", diagnostic.Id);
        Assert.Equal("Error", diagnostic.Severity);
        Assert.Equal("; expected", diagnostic.Message);
        Assert.Equal("src/SampleApp/Broken.cs", diagnostic.FilePath);
        Assert.Equal(7, diagnostic.StartLine);
        Assert.Equal(18, diagnostic.StartColumn);
        Assert.Equal(7, diagnostic.EndLine);
        Assert.Equal(18, diagnostic.EndColumn);
    }

    [Fact]
    public async Task AnalyzeAsync_GivenTimeoutExceeded_ReturnsTimedOutFailureWithRetainedEvidence()
    {
        var largeSource = "namespace SampleApp;\n\npublic static class Program\n{\n" +
            string.Concat(Enumerable.Range(0, 120_000).Select(static index => $"    private static int Value{index} = {index};\\n")) +
            "}\n";

        await using var fixture = await TempCSharpProject.CreateAsync(
            [
                new ProjectDocument("Program.cs", largeSource)
            ]);

        var analyzer = new RoslynAstAnalyzer();

        var result = await analyzer.AnalyzeAsync(
            new AstValidationRequest(
                fixture.RepositoryPath,
                fixture.ProjectRelativePath,
                Timeout: TimeSpan.FromMilliseconds(1)),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AstValidationOutcome.Failed, result.Outcome);
        Assert.Equal("AST analysis timed out.", result.FailureReason);
        Assert.True(result.DocumentCount >= 0);
        Assert.True(result.SyntaxTreeCount >= 0);
    }

    [Fact]
    public async Task AnalyzeAsync_GivenCancellationRequested_ReturnsDeterministicCanceledFailureContract()
    {
        await using var fixture = await TempCSharpProject.CreateAsync(
            [
                new ProjectDocument(
                    "Program.cs",
                    "namespace SampleApp;\n\npublic static class Program\n{\n    public static int Main() => 0;\n}\n")
            ]);

        var analyzer = new RoslynAstAnalyzer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await analyzer.AnalyzeAsync(
            new AstValidationRequest(fixture.RepositoryPath, fixture.ProjectRelativePath),
            cts.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(AstValidationOutcome.Failed, result.Outcome);
        Assert.Equal("AST analysis canceled.", result.FailureReason);
    }

    [Fact]
    public async Task AnalyzeAsync_GivenUnsupportedCompileItems_IgnoresUnsupportedFilesAndParsesCSharpInputsOnly()
    {
        await using var fixture = await TempCSharpProject.CreateAsync(
            [
                new ProjectDocument(
                    "Program.cs",
                    "namespace SampleApp;\n\npublic static class Program\n{\n    public static int Main() => 0;\n}\n")
            ]);

        var projectPath = Path.Combine(fixture.RepositoryPath, fixture.ProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(
            projectPath,
            """
                        <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                                <Compile Include="Program.cs" />
                                <Compile Include="README.md" />
              </ItemGroup>
            </Project>
            """,
            CancellationToken.None);

        await File.WriteAllTextAsync(
            Path.Combine(fixture.RepositoryPath, "src", "SampleApp", "README.md"),
            "unsupported file",
            CancellationToken.None);

        var analyzer = new RoslynAstAnalyzer();

        var result = await analyzer.AnalyzeAsync(
            new AstValidationRequest(fixture.RepositoryPath, fixture.ProjectRelativePath),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AstValidationOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.DocumentCount);
        Assert.Equal(1, result.SyntaxTreeCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task AnalyzeAsync_GivenPartialFileAccessFailure_RetainsEvidenceFromParsedDocuments()
    {
        await using var fixture = await TempCSharpProject.CreateAsync(
            [
                new ProjectDocument(
                    "A_Broken.cs",
                    "namespace SampleApp;\n\npublic static class Broken\n{\n    public static int Compute()\n    {\n        return 42\n    }\n}\n"),
                new ProjectDocument(
                    "B_Locked.cs",
                    "namespace SampleApp;\n\npublic static class Locked\n{\n    public static int Main() => 0;\n}\n")
            ]);

        var lockedFilePath = Path.Combine(fixture.RepositoryPath, "src", "SampleApp", "B_Locked.cs");
        await using var lockStream = new FileStream(
            lockedFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var analyzer = new RoslynAstAnalyzer();

        var result = await analyzer.AnalyzeAsync(
            new AstValidationRequest(fixture.RepositoryPath, fixture.ProjectRelativePath),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AstValidationOutcome.Failed, result.Outcome);
        Assert.StartsWith("AST analysis failed:", result.FailureReason, StringComparison.Ordinal);
        Assert.Equal(2, result.DocumentCount);
        Assert.Equal(1, result.SyntaxTreeCount);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("CS1002", diagnostic.Id);
        Assert.Equal("src/SampleApp/A_Broken.cs", diagnostic.FilePath);
    }

    private sealed record ProjectDocument(string RelativePath, string Content);

    private sealed class TempCSharpProject : IAsyncDisposable
    {
        private TempCSharpProject(string repositoryPath, string projectRelativePath)
        {
            RepositoryPath = repositoryPath;
            ProjectRelativePath = projectRelativePath;
        }

        public string RepositoryPath { get; }

        public string ProjectRelativePath { get; }

        public static async Task<TempCSharpProject> CreateAsync(IReadOnlyList<ProjectDocument> documents)
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-ast-analyzer-{Guid.NewGuid():N}");
            var projectDirectory = Path.Combine(repositoryPath, "src", "SampleApp");
            Directory.CreateDirectory(projectDirectory);

            var projectPath = Path.Combine(projectDirectory, "SampleApp.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """,
                CancellationToken.None);

            foreach (var document in documents)
            {
                var documentPath = Path.Combine(projectDirectory, document.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
                await File.WriteAllTextAsync(documentPath, document.Content, CancellationToken.None);
            }

            return new TempCSharpProject(repositoryPath, "src/SampleApp/SampleApp.csproj");
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RepositoryPath))
                Directory.Delete(RepositoryPath, recursive: true);

            return ValueTask.CompletedTask;
        }
    }
}