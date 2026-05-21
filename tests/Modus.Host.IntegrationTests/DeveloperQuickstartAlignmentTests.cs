using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class DeveloperQuickstartAlignmentTests
{
    [Fact]
    [Trait("ChecklistItem", "Provide end-to-end developer quickstart for creating, wiring, running, and validating plugins against current APIs")]
    public void DeveloperQuickstartAlignment_GivenNewDeveloper_ExpectedMinimalPathFromCloneToRunningPlugin()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("## Developer Quickstart", rootReadme, StringComparison.Ordinal);
        Assert.Contains("Create plugin project under the host-scanned `plugins` folder", rootReadme, StringComparison.Ordinal);
        Assert.Contains("dotnet new classlib --framework net10.0 --name Plugin.Weather -o plugins/Plugin.Weather", rootReadme, StringComparison.Ordinal);
        Assert.Contains("dotnet build plugins/Plugin.Weather/Plugin.Weather.csproj", rootReadme, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project src/Modus.Host/Modus.Host.csproj -- plugins --run-once", rootReadme, StringComparison.Ordinal);
        Assert.Contains("stage=di outcome=success", rootReadme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Provide end-to-end developer quickstart for creating, wiring, running, and validating plugins against current APIs")]
    public void DeveloperQuickstartAlignment_GivenCurrentApiSurface_ExpectedCommandsAndCodeSamplesMatchRepositoryBehavior()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("using Modus.Core.Plugins;", rootReadme, StringComparison.Ordinal);
        Assert.Contains("public sealed class WeatherPlugin : SingletonPlugin<WeatherPlugin>, IWeatherPluginContract", rootReadme, StringComparison.Ordinal);
        Assert.Contains("public override PluginId PluginId => new(\"Plugin.Weather\");", rootReadme, StringComparison.Ordinal);
        Assert.Contains("public override ContractName ContractName => new(\"Modus.PluginContract\");", rootReadme, StringComparison.Ordinal);
        Assert.Contains("public override IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName(\"Weather.GetCurrent\")];", rootReadme, StringComparison.Ordinal);
        Assert.Contains("<ModusOperations>Weather.GetCurrent</ModusOperations>", rootReadme, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            var filePath = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(solutionPath) && File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root containing Modus.slnx and {relativePath}.");
    }
}