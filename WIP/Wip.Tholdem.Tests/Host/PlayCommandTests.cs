using System.Text.Json;
using Xunit;

namespace Wip.Tholdem.Tests.Host;

public sealed class PlayCommandTests
{
    [Fact]
    public void Run_GivenSeedAndHands_ExpectedSessionManifestHandFilesAndMetricsTable()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-play");
        try
        {
            var result = HostCliTestSupport.Run(
                "play",
                "--seed",
                "42",
                "--hands",
                "5",
                "--allow-weak-seed",
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);

            var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
            Assert.True(File.Exists(Path.Combine(sessionDirectory, "session.json")));
            Assert.Equal(5, Directory.GetFiles(sessionDirectory, "hand-*.json").Length);

            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(sessionDirectory, "session.json")));
            Assert.Equal("tholdem.session.v1", manifest.RootElement.GetProperty("schema").GetString());
            Assert.Equal(42UL, manifest.RootElement.GetProperty("seed").GetUInt64());
            Assert.Equal(5, manifest.RootElement.GetProperty("hands").GetInt32());
            Assert.Equal(5, manifest.RootElement.GetProperty("handHashes").GetArrayLength());

            Assert.Contains("bb/100", result.Output, StringComparison.Ordinal);
            Assert.Contains("win rate", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("aggression", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("showdown", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("manifest sha256:", result.Output, StringComparison.OrdinalIgnoreCase);
            var manifestHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(sessionDirectory, "session.json")))).ToLowerInvariant();
            Assert.Contains(manifestHash, result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenSameSeedTwice_ExpectedByteIdenticalSessionFiles()
    {
        var firstDirectory = HostCliTestSupport.CreateDataDirectory("host-play-repro-first");
        var secondDirectory = HostCliTestSupport.CreateDataDirectory("host-play-repro-second");
        try
        {
            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "play", "--seed", "42", "--hands", "3", "--allow-weak-seed", "--data-dir", firstDirectory).ExitCode);
            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "play", "--seed", "42", "--hands", "3", "--allow-weak-seed", "--data-dir", secondDirectory).ExitCode);

            var firstSession = Assert.Single(HostCliTestSupport.SessionDirectories(firstDirectory));
            var secondSession = Assert.Single(HostCliTestSupport.SessionDirectories(secondDirectory));
            Assert.Equal(Path.GetFileName(firstSession), Path.GetFileName(secondSession));

            var firstFiles = RelativeFiles(firstSession);
            var secondFiles = RelativeFiles(secondSession);
            Assert.Equal(firstFiles, secondFiles);
            Assert.Equal(4, firstFiles.Length);
            foreach (var relativePath in firstFiles)
            {
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(firstSession, relativePath)),
                    File.ReadAllBytes(Path.Combine(secondSession, relativePath)));
            }
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenJsonFlag_ExpectedCanonicalReportOnly()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-play-json");
        try
        {
            var result = HostCliTestSupport.Run(
                "play", "--seed", "42", "--hands", "1", "--allow-weak-seed", "--json", "--data-dir", dataDirectory);

            Assert.Equal(0, result.ExitCode);
            using var report = JsonDocument.Parse(result.Output.Trim());
            Assert.Equal("tholdem.report.v1", report.RootElement.GetProperty("schema").GetString());
            Assert.Equal(42UL, report.RootElement.GetProperty("seed").GetUInt64());
            Assert.Equal(1, report.RootElement.GetProperty("hands").GetInt32());
            Assert.Equal(2, report.RootElement.GetProperty("metrics").GetArrayLength());
            Assert.DoesNotContain("bb/100", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static string[] RelativeFiles(string root) =>
        Directory
            .GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
}
