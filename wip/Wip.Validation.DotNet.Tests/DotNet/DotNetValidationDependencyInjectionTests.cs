using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Artifacts.Local;
using Wip.Validation.DotNet.DotNet;
using Xunit;

namespace Wip.Validation.DotNet.Tests.DotNet;

public sealed class DotNetValidationDependencyInjectionTests
{
    [Fact]
    public void ServiceCollection_GivenAddWipValidationDotNet_ResolvesIRoslynAstAnalyzerWithExpectedLifetime()
    {
        var services = new ServiceCollection();
        services.AddWipValidationDotNet();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var analyzerA1 = scopeA.ServiceProvider.GetRequiredService<IRoslynAstAnalyzer>();
        var analyzerA2 = scopeA.ServiceProvider.GetRequiredService<IRoslynAstAnalyzer>();
        var analyzerB = scopeB.ServiceProvider.GetRequiredService<IRoslynAstAnalyzer>();

        Assert.Same(analyzerA1, analyzerA2);
        Assert.NotSame(analyzerA1, analyzerB);
        Assert.IsType<RoslynAstAnalyzer>(analyzerA1);
    }

    [Fact]
    public async Task DotNetValidationValidator_GivenResolvedAnalyzer_UsesContainerInstanceDuringAstExecution()
    {
        await using var fixture = await TempRepository.CreateAsync();
        var trackingAnalyzer = new TrackingAstAnalyzer();

        var services = new ServiceCollection();
        services.AddWipValidationDotNet();
        services.AddScoped<IArtifactStore>(_ => new WipArtifactStoreLocal(fixture.RepositoryPath));
        services.AddScoped<IRoslynAstAnalyzer>(_ => trackingAnalyzer);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var validator = scope.ServiceProvider.GetRequiredService<IValidator<DotNetValidationRequest, DotNetValidationResult>>();
        var context = new CapabilityContext(new SessionId("session-di-ast"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, trackingAnalyzer.InvocationCount);
        var invokedRequest = Assert.Single(trackingAnalyzer.Requests);
        Assert.Equal(fixture.RepositoryPath, invokedRequest.RepositoryPath);
        Assert.Equal("src/Example/Example.csproj", invokedRequest.ProjectPath);
    }

    [Fact]
    public async Task ServiceProvider_GivenMultipleValidationSessions_PreservesExpectedAnalyzerIsolationAcrossScopes()
    {
        await using var fixture = await TempRepository.CreateAsync();
        var services = new ServiceCollection();
        services.AddWipValidationDotNet();
        services.AddScoped<IArtifactStore>(_ => new WipArtifactStoreLocal(fixture.RepositoryPath));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var analyzerA = scopeA.ServiceProvider.GetRequiredService<IRoslynAstAnalyzer>();
        var analyzerB = scopeB.ServiceProvider.GetRequiredService<IRoslynAstAnalyzer>();

        Assert.NotSame(analyzerA, analyzerB);
    }

    private sealed class TrackingAstAnalyzer : IRoslynAstAnalyzer
    {
        private readonly List<AstValidationRequest> _requests = [];

        public int InvocationCount => _requests.Count;

        public IReadOnlyList<AstValidationRequest> Requests => _requests;

        public ValueTask<AstValidationResult> AnalyzeAsync(AstValidationRequest request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return ValueTask.FromResult(
                AstValidationResult.Success(
                    documentCount: 1,
                    syntaxTreeCount: 1,
                    diagnostics: [],
                    startedAtUtc: DateTimeOffset.UtcNow,
                    completedAtUtc: DateTimeOffset.UtcNow));
        }
    }

    private sealed class TempRepository : IAsyncDisposable
    {
        private TempRepository(string repositoryPath, string dotNetStubPath)
        {
            RepositoryPath = repositoryPath;
            DotNetStubPath = dotNetStubPath;
        }

        public string RepositoryPath { get; }

        public string DotNetStubPath { get; }

        public static async ValueTask<TempRepository> CreateAsync()
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-validation-di-{Guid.NewGuid():N}");
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

            return new TempRepository(repositoryPath, dotNetStubPath);
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