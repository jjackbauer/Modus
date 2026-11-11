using Xunit;

namespace Wip.Tholdem.Tests.Host;

/// <summary>
/// The additive <c>train4</c> command: a tiny four-seat run exits cleanly, writes its isolated
/// strategy/report artifacts and never writes the v1 champion pointer.
/// </summary>
public sealed class Train4CommandTests
{
    [Fact]
    public void Run_GivenTinyRun_WritesFourSeatArtifactsAndNoV1Pointer()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-train4");
        try
        {
            var result = HostCliTestSupport.Run(
                "train4",
                "--seed",
                "1099511627776",
                "--iterations",
                "1",
                "--selection-blocks",
                "1",
                "--selection-hands",
                "1",
                "--holdout-blocks",
                "1",
                "--holdout-hands",
                "1",
                "--margin",
                "5",
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);

            Assert.True(Directory.Exists(Path.Combine(dataDirectory, "four-seat", "strategies")));
            Assert.True(Directory.Exists(Path.Combine(dataDirectory, "four-seat", "reports")));

            var iterationLines = result.Output
                .Split('\n')
                .Select(static line => line.TrimEnd('\r'))
                .Where(static line => line.StartsWith("iteration=", StringComparison.Ordinal))
                .ToArray();
            Assert.Single(iterationLines);
            Assert.Contains("iteration=1", iterationLines[0], StringComparison.Ordinal);
            Assert.Contains("final champion:", result.Output, StringComparison.Ordinal);

            Assert.False(
                File.Exists(Path.Combine(dataDirectory, "active-champion.json")),
                "train4 must not write the v1 champion pointer.");
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}