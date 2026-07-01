using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostDeepSeekOperatorDocumentationTests
{
    private const string OperatorDocumentationItem = BonesDeepSeekHostRequirementsChecklistItems.OperatorDeepSeekSetup;

    [Fact]
    [Trait("ChecklistItem", OperatorDocumentationItem)]
    public void BonesHostDeepSeekOperatorDocumentation_GivenReadme_ExpectedDocumentsDeepSeekAndStubSetup()
    {
        var repositoryRoot = BonesHostTestPaths.FindRepositoryRoot();
        var readmePath = Path.Combine(repositoryRoot, "WIP", "Wip.Bones.Host", "README.md");
        var readme = File.ReadAllText(readmePath);

        Assert.Contains("DEEPSEEK_API_KEY", readme, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project WIP/Wip.Bones.Host", readme, StringComparison.Ordinal);
        Assert.Contains("provider=deepseek", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ModelProvider=stub", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/bones/status", readme, StringComparison.Ordinal);
    }
}