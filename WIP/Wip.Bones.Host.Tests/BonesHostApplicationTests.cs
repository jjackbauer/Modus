using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Wip.Bones.Host;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostApplicationTests
{
    private const string WebServerItem = BonesHostRequirementsChecklistItems.WebServer;
    private const string HostStartupDiItem = BonesHostRequirementsChecklistItems.HostStartupDi;
    private const string StubModelProvidersItem = BonesHostRequirementsChecklistItems.StubModelProviders;
    private const string HostProjectFoundationItem = BonesHostRequirementsChecklistItems.HostProjectFoundation;

    [Fact]
    [Trait("ChecklistItem", WebServerItem)]
    [Trait("ChecklistItem", HostStartupDiItem)]
    [Trait("ChecklistItem", StubModelProvidersItem)]
    public async Task BonesHostApplication_GivenDefaultConfiguration_ExpectedStartsWebServerAndBackgroundLoopTogether()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var viewer = await client.GetAsync("/viewer/index.html");
        Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);

        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();
        Assert.NotNull(loopHost);

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        Assert.True(status.IsRunning);
    }

    [Fact]
    [Trait("ChecklistItem", HostProjectFoundationItem)]
    public void BonesHostApplication_GivenProjectReferences_ExpectedDoesNotReferenceShellHostOrShellAssemblies()
    {
        var hostAssembly = typeof(BonesHostApplication).Assembly;
        var referencedAssemblyNames = hostAssembly.GetReferencedAssemblies()
            .Select(static name => name.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Wip.ShellHost", referencedAssemblyNames);
        Assert.DoesNotContain("Wip.Shell", referencedAssemblyNames);

        var projectPath = Path.Combine(
            BonesHostTestPaths.FindRepositoryRoot(),
            "WIP",
            "Wip.Bones.Host",
            "Wip.Bones.Host.csproj");
        var projectText = File.ReadAllText(projectPath);

        Assert.DoesNotContain("Wip.ShellHost", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Wip.Shell.csproj", projectText, StringComparison.OrdinalIgnoreCase);
    }
}