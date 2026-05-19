using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class MakefileTargetsTests
{
    [Fact]
    public void Makefile_GivenRunHostTarget_ExpectedInvokesModusHostWithDeterministicPluginPath()
    {
        var content = ReadRootMakefile();

        Assert.Contains("run-host:", content, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project $(HOST_PROJECT) -- $(PLUGINS_PATH)", content, StringComparison.Ordinal);
        Assert.Contains("HOST_PROJECT=src/Modus.Host/Modus.Host.csproj", content, StringComparison.Ordinal);
        Assert.Contains("PLUGINS_PATH=plugins", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Makefile_GivenBuildAndTestTargets_ExpectedMapsToSolutionBuildAndTestCommands()
    {
        var content = ReadRootMakefile();

        Assert.Contains("build:", content, StringComparison.Ordinal);
        Assert.Contains("dotnet build $(SOLUTION)", content, StringComparison.Ordinal);
        Assert.Contains("test:", content, StringComparison.Ordinal);
        Assert.Contains("dotnet test $(SOLUTION)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Makefile_GivenTestNoBuildTarget_ExpectedRunsFastValidationWithoutRebuild()
    {
        var content = ReadRootMakefile();

        Assert.Contains("test-no-build:", content, StringComparison.Ordinal);
        Assert.Contains("dotnet test $(SOLUTION) --no-build", content, StringComparison.Ordinal);
    }

    private static string ReadRootMakefile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            var makefilePath = Path.Combine(directory.FullName, "Makefile");
            if (File.Exists(solutionPath) && File.Exists(makefilePath))
            {
                return File.ReadAllText(makefilePath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Modus.slnx and Makefile.");
    }
}