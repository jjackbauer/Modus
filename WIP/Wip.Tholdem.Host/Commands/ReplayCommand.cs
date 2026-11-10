using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Host;

/// <summary>
/// Replays one recorded hand after proving the file bytes are exactly the canonical JSON the record
/// hashes to, then proves those bytes against an external anchor: the session manifest's
/// <c>handHashes[handNumber - 1]</c> when the file sits beside a <c>session.json</c>, or an explicit
/// <c>--expected-hash</c> for a file outside a session. A file with neither anchor is still replayed
/// but says so (<c>integrity: unanchored</c>).
/// </summary>
internal static class ReplayCommand
{
    private const int Sha256HexLength = 64;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static int Execute(CliOptions options, TextWriter output)
    {
        if (options.Positionals.Count != 1)
        {
            throw new CliUsageException("replay requires exactly one hand-record file path.");
        }

        var expectedHash = ReadExpectedHash(options);
        var path = Path.GetFullPath(options.Positionals[0]);
        if (!File.Exists(path))
        {
            throw new CliArtifactException($"hand record '{path}' does not exist.");
        }

        var bytes = File.ReadAllBytes(path);
        HandRecord record;
        try
        {
            record = HandRecordJson.Parse(StrictUtf8.GetString(bytes));
        }
        catch (FormatException exception)
        {
            throw new CliArtifactException(
                $"hand record '{path}' is not a canonical hand record: {exception.Message}",
                exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new CliArtifactException(
                $"hand record '{path}' is not UTF-8 text: {exception.Message}",
                exception);
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        // The file must be byte-identical to the canonical re-serialization of what it parses to.
        // This is self-referential, so it is only a canonical-form check: the external anchor below
        // is what detects a semantic edit that keeps the canonical form.
        if (!string.Equals(actualHash, record.Hash, StringComparison.Ordinal))
        {
            throw new CliInvariantException(
                $"replay hash mismatch: file sha256 {actualHash} does not equal record hash {record.Hash}.");
        }

        var manifestAnchor = TryReadManifestAnchor(path, record.HandNumber);
        if (manifestAnchor is { } anchor && !string.Equals(actualHash, anchor.Hash, StringComparison.Ordinal))
        {
            throw new CliInvariantException(
                $"replay hash mismatch for hand {record.HandNumber}: file sha256 {actualHash} does not "
                + $"match session manifest '{anchor.ManifestPath}' handHashes[{anchor.Index}] {anchor.Hash}.");
        }

        if (expectedHash is not null && !string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw new CliInvariantException(
                $"replay hash mismatch: file sha256 {actualHash} does not match --expected-hash {expectedHash}.");
        }

        TableState replay;
        try
        {
            replay = HeadsUpHandEngine.Replay(record);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new CliInvariantException($"replay invariant failed: {exception.Message}", exception);
        }

        WriteHand(output, record, replay);
        if (manifestAnchor is null && expectedHash is null)
        {
            output.WriteLine("integrity: unanchored");
        }

        return TholdemCli.ExitOk;
    }

    private static string? ReadExpectedHash(CliOptions options)
    {
        var value = options.Get("expected-hash");
        if (value is null)
        {
            return null;
        }

        if (value.Length != Sha256HexLength || !IsLowercaseHex(value))
        {
            throw new CliUsageException(
                $"option '--expected-hash' must be 64 lowercase hexadecimal characters; observed '{value}'.");
        }

        return value;
    }

    private static bool IsLowercaseHex(string value)
    {
        foreach (var character in value)
        {
            var isLowercaseHexDigit = character is (>= '0' and <= '9') or (>= 'a' and <= 'f');
            if (!isLowercaseHexDigit)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the external anchor from a sibling <c>session.json</c>. A directory without a manifest,
    /// or a manifest without a <c>handHashes</c> array, yields no anchor; a manifest that cannot be
    /// read as JSON, or that has no entry for this hand, is an integrity failure rather than a
    /// silent fallback to unanchored replay.
    /// </summary>
    private static ManifestAnchor? TryReadManifestAnchor(string handPath, int handNumber)
    {
        var directory = Path.GetDirectoryName(handPath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var manifestPath = Path.Combine(directory, "session.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("handHashes", out var handHashes)
                || handHashes.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var index = handNumber - 1;
            if (index >= handHashes.GetArrayLength())
            {
                throw new CliInvariantException(
                    $"replay integrity: session manifest '{manifestPath}' has no handHashes entry "
                    + $"for hand {handNumber}.");
            }

            var entry = handHashes[index];
            if (entry.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.GetString()))
            {
                throw new CliInvariantException(
                    $"replay integrity: session manifest '{manifestPath}' handHashes[{index}] "
                    + "is not a hash string.");
            }

            return new ManifestAnchor(manifestPath, index, entry.GetString()!);
        }
        catch (JsonException exception)
        {
            throw new CliInvariantException(
                $"replay integrity: session manifest '{manifestPath}' is not valid JSON: {exception.Message}",
                exception);
        }
    }

    private static void WriteHand(TextWriter output, HandRecord record, TableState replay)
    {
        output.WriteLine($"hand: {record.HandNumber}");
        output.WriteLine($"board: {Cards(record.Board)}");
        output.WriteLine($"seat 0: {Cards(record.HoleCardsSeat0)}");
        output.WriteLine($"seat 1: {Cards(record.HoleCardsSeat1)}");
        output.WriteLine("actions:");
        foreach (var action in record.Actions)
        {
            output.WriteLine(
                $"  {StreetName(action.Street)} seat={action.Seat} kind={action.Kind} "
                + $"amount={action.Amount} potAfter={action.PotAfter}");
        }

        output.WriteLine($"showdown: {CliJson.Format(record.Showdown)}");
        output.WriteLine($"winners: {string.Join(", ", record.Winners)}");
        output.WriteLine($"pot awarded: {record.PotAwarded}");
        output.WriteLine($"final stacks: {string.Join(" / ", record.FinalStacks)}");
        output.WriteLine(
            $"replayed stacks: {string.Join(" / ", replay.Stacks)}  pot: {replay.Pot}");
    }

    private static string Cards(IReadOnlyList<Card> cards) =>
        cards.Count == 0 ? "-" : string.Join(" ", cards.Select(static card => card.ToString()));

    private static string StreetName(int street) => street switch
    {
        0 => "preflop",
        1 => "flop",
        2 => "turn",
        3 => "river",
        4 => "showdown",
        5 => "complete",
        _ => "street-" + street.ToString(CultureInfo.InvariantCulture),
    };

    private readonly record struct ManifestAnchor(string ManifestPath, int Index, string Hash);
}
