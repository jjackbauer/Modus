using System.Text.Json;
using Xunit;

namespace Wip.Tholdem.Tests.Host;

public sealed class RetentionCommandTests
{
    [Fact]
    public void Status_GivenSeedSession_ExpectedSessionHandsBytesAndOldestDate()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-retention-status");
        try
        {
            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "play", "--seed", "42", "--hands", "3", "--allow-weak-seed", "--data-dir", dataDirectory).ExitCode);

            var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
            var expectedBytes = Directory
                .GetFiles(sessionDirectory, "*", SearchOption.AllDirectories)
                .Sum(static path => new FileInfo(path).Length);

            var text = HostCliTestSupport.Run("retention", "--status", "--data-dir", dataDirectory);
            Assert.Equal(0, text.ExitCode);
            Assert.Contains("sessions: 1", text.Output, StringComparison.Ordinal);
            Assert.Contains("hands: 3", text.Output, StringComparison.Ordinal);
            Assert.Contains(
                "bytes: " + expectedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                text.Output,
                StringComparison.Ordinal);
            Assert.Contains("oldest session:", text.Output, StringComparison.Ordinal);

            var json = HostCliTestSupport.Run("retention", "--status", "--json", "--data-dir", dataDirectory);
            Assert.Equal(0, json.ExitCode);
            using var document = JsonDocument.Parse(json.Output.Trim());
            Assert.Equal("tholdem.retention.v1", document.RootElement.GetProperty("schema").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("sessions").GetInt32());
            Assert.Equal(3, document.RootElement.GetProperty("hands").GetInt32());
            Assert.Equal(expectedBytes, document.RootElement.GetProperty("bytes").GetInt64());
            Assert.EndsWith("Z", document.RootElement.GetProperty("oldestSession").GetString()!, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Purge_GivenNoConfirmation_ExpectedExit3PreviewAndNothingDeleted()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-retention-preview");
        try
        {
            var sessionDirectory = CreateOldSession(dataDirectory);

            var result = HostCliTestSupport.Run(
                "retention", "--purge", "--older-than-days", "0", "--data-dir", dataDirectory);

            Assert.Equal(3, result.ExitCode);
            Assert.Contains(sessionDirectory, result.Output, StringComparison.Ordinal);
            Assert.True(Directory.Exists(sessionDirectory), "A previewed purge must not delete anything.");
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Purge_GivenConfirmation_ExpectedExit0AndDeletedSession()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-retention-purge");
        try
        {
            var sessionDirectory = CreateOldSession(dataDirectory);

            var result = HostCliTestSupport.Run(
                "retention", "--purge", "--older-than-days", "0", "--yes", "--data-dir", dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(sessionDirectory, result.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(sessionDirectory), "A confirmed purge must delete the old session.");
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Purge_GivenMalformedOlderThanDays_ExpectedExit2()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-retention-bad-days");
        try
        {
            var result = HostCliTestSupport.Run(
                "retention", "--purge", "--older-than-days", "many", "--data-dir", dataDirectory);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("older-than-days", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static string CreateOldSession(string dataDirectory)
    {
        Assert.Equal(
            0,
            HostCliTestSupport.Run(
                "play", "--seed", "42", "--hands", "1", "--allow-weak-seed", "--data-dir", dataDirectory).ExitCode);
        var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
        var sessionJson = Path.Combine(sessionDirectory, "session.json");
        Assert.True(File.Exists(sessionJson));
        File.SetLastWriteTimeUtc(sessionJson, DateTime.UtcNow.AddDays(-3));
        return sessionDirectory;
    }
}
