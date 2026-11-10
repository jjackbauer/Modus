using Xunit;

namespace Wip.Tholdem.Tests.Runtime;

public sealed class RuntimeSourceBoundaryTests
{
    [Fact]
    public void RuntimeSources_GivenHiddenInformationScan_ExpectedNoSpectatorOrDeckIdentifiers()
    {
        var directory = Path.Combine(
            AgentTestSupport.RepositoryRoot(),
            "WIP",
            "Wip.Tholdem.Agents",
            "Runtime");
        Assert.True(Directory.Exists(directory), $"Missing runtime source directory: {directory}");

        var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
        Assert.True(files.Length >= 6, $"Expected the runtime agent sources, found {files.Length} file(s).");

        var forbidden = new[] { "TableState", "SpectatorSnapshot", "HoleCardsSeat0", "HoleCardsSeat1", "Deck" };
        var offenders = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{token}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Runtime sources reference forbidden hidden-information tokens: " + string.Join(", ", offenders));
    }
}
