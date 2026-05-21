using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class BuiltInCapabilitiesDocumentationTests
{
    [Fact]
    [Trait("ChecklistItem", "Document REST out of the box, DI integration and lifetime selection, and scheduled jobs support in the root README")]
    public void BuiltInCapabilities_GivenRootReadme_ExpectedOpeningPitchIntroducesTheThreeBuiltInCapabilities()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("Modus is an open-source plugin platform built with .NET and C# that ships three built-in capabilities from the first host start:", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Automatic REST endpoint mapping for every plugin operation", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- First-class DI integration with explicit lifetime selection", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Scheduled job support for recurring and point-in-time plugin work", rootReadme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Document REST out of the box, DI integration and lifetime selection, and scheduled jobs support in the root README")]
    public void BuiltInCapabilities_GivenRootReadme_ExpectedRestAndOpenApiSectionDocumentsTheLiveHttpSurface()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("### Automatic REST Out of the Box", rootReadme, StringComparison.Ordinal);
        Assert.Contains("POST /api/{pluginId}/{operation}", rootReadme, StringComparison.Ordinal);
        Assert.Contains("AddOpenApi()", rootReadme, StringComparison.Ordinal);
        Assert.Contains("app.MapOpenApi()", rootReadme, StringComparison.Ordinal);
        Assert.Contains("/openapi/v1.json", rootReadme, StringComparison.Ordinal);
        Assert.Contains("Swagger UI", rootReadme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Document REST out of the box, DI integration and lifetime selection, and scheduled jobs support in the root README")]
    public void BuiltInCapabilities_GivenRootReadme_ExpectedDIAndScheduledJobsSectionsShowCurrentAPIs()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("services.AddModusPluginHosting(...)", rootReadme, StringComparison.Ordinal);
        Assert.Contains("SingletonPlugin<T>", rootReadme, StringComparison.Ordinal);
        Assert.Contains("ScopedPlugin<T>", rootReadme, StringComparison.Ordinal);
        Assert.Contains("TransientPlugin<T>", rootReadme, StringComparison.Ordinal);
        Assert.Contains("Plugin contract interfaces that extend `IPluginContract` are mapped automatically", rootReadme, StringComparison.Ordinal);
        Assert.Contains("IPluginScheduledEvents.RegisterSchedules(IPluginScheduler scheduler)", rootReadme, StringComparison.Ordinal);
        Assert.Contains("ScheduleRecurring", rootReadme, StringComparison.Ordinal);
        Assert.Contains("ScheduleAt", rootReadme, StringComparison.Ordinal);
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