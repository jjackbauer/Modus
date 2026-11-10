using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests.Host;

public sealed class ReplayCommandTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public void Run_GivenRecordedHand_ExpectedExit0BoardHoleCardsActionsWinnersAndStacks()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-replay");
        try
        {
            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "play", "--seed", "42", "--hands", "1", "--allow-weak-seed", "--data-dir", dataDirectory).ExitCode);
            var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
            var handPath = Path.Combine(sessionDirectory, "hand-00001.json");
            Assert.True(File.Exists(handPath));
            var record = HandRecordJson.Parse(File.ReadAllText(handPath));

            var result = HostCliTestSupport.Run("replay", handPath);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            Assert.Contains("hand: 1", result.Output, StringComparison.Ordinal);
            Assert.Contains(
                "board: " + string.Join(" ", record.Board.Select(static card => card.ToString())),
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "seat 0: " + string.Join(" ", record.HoleCardsSeat0.Select(static card => card.ToString())),
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "seat 1: " + string.Join(" ", record.HoleCardsSeat1.Select(static card => card.ToString())),
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains("actions:", result.Output, StringComparison.Ordinal);
            Assert.Contains("showdown:", result.Output, StringComparison.Ordinal);
            Assert.Contains("winners:", result.Output, StringComparison.Ordinal);
            Assert.Contains("pot awarded:", result.Output, StringComparison.Ordinal);
            Assert.Contains("final stacks:", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("integrity: unanchored", result.Output, StringComparison.Ordinal);

            var actionLines = result.Output
                .Split('\n')
                .Select(static line => line.TrimEnd('\r'))
                .Where(static line => line.Contains("potAfter=", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(record.Actions.Count, actionLines.Length);
            Assert.Contains("potAfter=" + record.Actions[0].PotAfter, actionLines[0], StringComparison.Ordinal);
            Assert.Contains("kind=" + record.Actions[0].Kind, actionLines[0], StringComparison.Ordinal);
            Assert.Contains("seat=" + record.Actions[0].Seat, actionLines[0], StringComparison.Ordinal);
            Assert.Contains("amount=" + record.Actions[0].Amount, actionLines[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenCanonicalSemanticEditInsideSession_ExpectedExit5NamingManifestAndBothHashes()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-replay-semantic-edit");
        try
        {
            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "play", "--seed", "42", "--hands", "1", "--allow-weak-seed", "--data-dir", dataDirectory).ExitCode);
            var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
            var handPath = Path.Combine(sessionDirectory, "hand-00001.json");
            var manifestPath = Path.Combine(sessionDirectory, "session.json");
            var original = File.ReadAllText(handPath, Utf8NoBom);
            var edited = original.Replace("\"smallBlind\":5", "\"smallBlind\":6", StringComparison.Ordinal);
            Assert.NotEqual(original, edited);
            File.WriteAllText(handPath, edited, Utf8NoBom);

            var result = HostCliTestSupport.Run("replay", handPath);

            Assert.Equal(5, result.ExitCode);
            Assert.Equal(string.Empty, result.Output);
            Assert.Contains(manifestPath, result.Error, StringComparison.Ordinal);
            Assert.Contains("session.json", result.Error, StringComparison.Ordinal);
            Assert.Contains("hand 1", result.Error, StringComparison.Ordinal);

            var manifestEntry = ReadManifestHandHashes(manifestPath)[0];
            var actualHash = Sha256Hex(File.ReadAllBytes(handPath));
            Assert.Contains(manifestEntry, result.Error, StringComparison.Ordinal);
            Assert.Contains(actualHash, result.Error, StringComparison.Ordinal);
            Assert.NotEqual(manifestEntry, actualHash);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenCopyOutsideSessionWithManifestExpectedHash_ExpectedExit0()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-replay-anchored-copy");
        try
        {
            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "play", "--seed", "42", "--hands", "1", "--allow-weak-seed", "--data-dir", dataDirectory).ExitCode);
            var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
            var expectedHash = ReadManifestHandHashes(Path.Combine(sessionDirectory, "session.json"))[0];
            var copyPath = CopyHandOutsideSession(
                Path.Combine(sessionDirectory, "hand-00001.json"),
                dataDirectory);

            var result = HostCliTestSupport.Run(
                "replay", copyPath, "--expected-hash", expectedHash);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            Assert.Contains("hand: 1", result.Output, StringComparison.Ordinal);
            Assert.Contains("replayed stacks:", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("integrity: unanchored", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenCopyOutsideSessionWithWrongExpectedHash_ExpectedExit5()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-replay-wrong-anchor");
        try
        {
            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "play", "--seed", "42", "--hands", "1", "--allow-weak-seed", "--data-dir", dataDirectory).ExitCode);
            var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
            var copyPath = CopyHandOutsideSession(
                Path.Combine(sessionDirectory, "hand-00001.json"),
                dataDirectory);
            var wrongHash = new string('0', 64);

            var result = HostCliTestSupport.Run(
                "replay", copyPath, "--expected-hash", wrongHash);

            Assert.Equal(5, result.ExitCode);
            Assert.Equal(string.Empty, result.Output);
            Assert.Contains("--expected-hash", result.Error, StringComparison.Ordinal);
            Assert.Contains(wrongHash, result.Error, StringComparison.Ordinal);
            Assert.Contains(Sha256Hex(File.ReadAllBytes(copyPath)), result.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenNonCanonicalFile_ExpectedExit5()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-replay-noncanonical");
        try
        {
            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "play", "--seed", "42", "--hands", "1", "--allow-weak-seed", "--data-dir", dataDirectory).ExitCode);
            var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
            var original = File.ReadAllBytes(Path.Combine(sessionDirectory, "hand-00001.json"));
            var nonCanonical = new byte[original.Length + 1];
            Array.Copy(original, nonCanonical, original.Length);
            nonCanonical[^1] = (byte)' ';
            var copyPath = Path.Combine(dataDirectory, "noncanonical-hand.json");
            File.WriteAllBytes(copyPath, nonCanonical);

            var result = HostCliTestSupport.Run("replay", copyPath);

            Assert.Equal(5, result.ExitCode);
            Assert.Equal(string.Empty, result.Output);
            Assert.Contains("does not equal record hash", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenUnanchoredCopy_ExpectedExit0WithUnanchoredIntegrityNote()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-replay-unanchored");
        try
        {
            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "play", "--seed", "42", "--hands", "1", "--allow-weak-seed", "--data-dir", dataDirectory).ExitCode);
            var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
            var copyPath = CopyHandOutsideSession(
                Path.Combine(sessionDirectory, "hand-00001.json"),
                dataDirectory);

            var result = HostCliTestSupport.Run("replay", copyPath);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            Assert.Contains("hand: 1", result.Output, StringComparison.Ordinal);
            Assert.Contains("integrity: unanchored", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("integrity: verified", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenMalformedExpectedHash_ExpectedExit2()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-replay-bad-anchor");
        try
        {
            Assert.Equal(
                0,
                HostCliTestSupport.Run(
                    "play", "--seed", "42", "--hands", "1", "--allow-weak-seed", "--data-dir", dataDirectory).ExitCode);
            var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
            var copyPath = CopyHandOutsideSession(
                Path.Combine(sessionDirectory, "hand-00001.json"),
                dataDirectory);

            var shortValue = HostCliTestSupport.Run("replay", copyPath, "--expected-hash", "abc123");
            Assert.Equal(2, shortValue.ExitCode);
            Assert.Contains("--expected-hash", shortValue.Error, StringComparison.Ordinal);
            Assert.Contains("64 lowercase hexadecimal", shortValue.Error, StringComparison.Ordinal);

            var uppercaseValue = HostCliTestSupport.Run(
                "replay",
                copyPath,
                "--expected-hash",
                new string('A', 64));
            Assert.Equal(2, uppercaseValue.ExitCode);
            Assert.Contains("--expected-hash", uppercaseValue.Error, StringComparison.Ordinal);
            Assert.Contains("64 lowercase hexadecimal", uppercaseValue.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenMalformedFile_ExpectedExit4()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-replay-malformed");
        try
        {
            var malformedPath = Path.Combine(dataDirectory, "malformed-hand.json");
            File.WriteAllText(malformedPath, "{\"handNumber\":1}");
            var malformed = HostCliTestSupport.Run("replay", malformedPath);
            Assert.Equal(4, malformed.ExitCode);
            Assert.Contains("hand", malformed.Error, StringComparison.OrdinalIgnoreCase);

            var missing = HostCliTestSupport.Run("replay", Path.Combine(dataDirectory, "absent-hand.json"));
            Assert.Equal(4, missing.ExitCode);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static string CopyHandOutsideSession(string handPath, string dataDirectory)
    {
        var copyDirectory = Path.Combine(dataDirectory, "unanchored");
        Directory.CreateDirectory(copyDirectory);
        var copyPath = Path.Combine(copyDirectory, Path.GetFileName(handPath));
        File.WriteAllBytes(copyPath, File.ReadAllBytes(handPath));
        return copyPath;
    }

    private static string[] ReadManifestHandHashes(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        return document.RootElement
            .GetProperty("handHashes")
            .EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
