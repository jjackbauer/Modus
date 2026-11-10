using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>The result of verifying one persisted ledger's hash chain.</summary>
public sealed record LedgerIntegrity(
    bool Present,
    bool Verified,
    int Entries,
    int ChainedEntries,
    int LegacyEntries,
    string HeadHash,
    string Reason)
{
    public const string VerifiedReason = "verified";

    public const string AbsentReason = "ledger-absent";

    public const string LegacyReason = "verified-with-legacy-prefix";

    public const string TamperedReason = "ledger-hash-chain-mismatch";
}

/// <summary>
/// An append-only, hash-chained ledger envelope. Each entry is the existing core JSON plus
/// <c>previousHash</c> and <c>entryHash</c>, where
/// <c>entryHash = SHA-256(previousHash + "|" + coreJson)</c> and the first entry links to
/// <see cref="GenesisPreviousHash"/>. Entries written before this chain existed are accepted only as
/// an unanchored prefix; the next write upgrades the whole ledger.
/// </summary>
/// <remarks>
/// This is tamper-<em>evident</em>, not tamper-proof: an attacker who can rewrite the file can also
/// recompute every hash because the construction is public and unsigned. It detects accidental or
/// naive edits, not a determined re-chainer. The residual is stated in the threat model (A4).
/// </remarks>
public static class LedgerChain
{
    public const string GenesisPreviousHash =
        "0000000000000000000000000000000000000000000000000000000000000000";

    public const string LegacyPreviousHash = "legacy-prefix";

    public const string PartialChainReason = "ledger-partial-chain-fields";

    public const string BrokenLinkReason = "ledger-broken-link";

    public const string EntryHashReason = "ledger-entry-hash-mismatch";

    public const string MisplacedLegacyReason = "ledger-legacy-after-chain";

    public const string NotJsonReason = "ledger-not-json-array";

    private const string PreviousHashMarker = ",\"previousHash\":";

    private const string EntryHashMarker = ",\"entryHash\":";

    public static string HashEntry(string previousHash, string coreJson) =>
        StrategyArtifact.ComputeHash(previousHash + "|" + coreJson);

    public static string AppendChain(string coreJson, string previousHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(coreJson);
        ArgumentException.ThrowIfNullOrEmpty(previousHash);

        var builder = new StringBuilder(coreJson.Length + 160);
        builder.Append(coreJson, 0, coreJson.Length - 1);
        builder.Append(",\"previousHash\":");
        CanonicalJsonWriter.Text(builder, previousHash);
        builder.Append(",\"entryHash\":");
        CanonicalJsonWriter.Text(builder, HashEntry(previousHash, coreJson));
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// Splits a persisted entry into its core JSON (the pre-chain payload) and its chain fields.
    /// Returns false when the entry carries exactly one of the two chain fields, or when the markers
    /// are malformed.
    /// </summary>
    public static bool TrySplit(
        string entryJson,
        out string coreJson,
        out string? previousHash,
        out string? entryHash)
    {
        coreJson = entryJson;
        previousHash = null;
        entryHash = null;

        var previousIndex = entryJson.LastIndexOf(PreviousHashMarker, StringComparison.Ordinal);
        var entryIndex = entryJson.LastIndexOf(EntryHashMarker, StringComparison.Ordinal);
        if (previousIndex < 0 && entryIndex < 0)
        {
            return true;
        }

        if (previousIndex < 0 || entryIndex < 0 || entryIndex < previousIndex)
        {
            return false;
        }

        var previousValueStart = previousIndex + PreviousHashMarker.Length;
        var entryValueStart = entryIndex + EntryHashMarker.Length;
        if (previousValueStart >= entryIndex || entryValueStart >= entryJson.Length)
        {
            return false;
        }

        var previousValue = entryJson[previousValueStart..entryIndex];
        var entryValue = entryJson[entryValueStart..];
        if (previousValue.Length < 2
            || previousValue[0] != '"'
            || previousValue[^1] != '"'
            || entryValue.Length < 3
            || entryValue[0] != '"'
            || entryValue[^2] != '"'
            || entryValue[^1] != '}')
        {
            return false;
        }

        previousHash = previousValue[1..^1];
        entryHash = entryValue[1..^2];
        coreJson = entryJson[..previousIndex] + "}";
        return true;
    }

    /// <summary>Verifies one ledger array. Never throws for malformed content; it fails closed.</summary>
    public static LedgerIntegrity Verify(string? ledgerJson)
    {
        if (string.IsNullOrWhiteSpace(ledgerJson))
        {
            return new LedgerIntegrity(false, true, 0, 0, 0, GenesisPreviousHash, LedgerIntegrity.AbsentReason);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(ledgerJson);
        }
        catch (JsonException)
        {
            return new LedgerIntegrity(true, false, 0, 0, 0, GenesisPreviousHash, NotJsonReason);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new LedgerIntegrity(true, false, 0, 0, 0, GenesisPreviousHash, NotJsonReason);
            }

            var entries = 0;
            var chained = 0;
            var legacy = 0;
            var sawChain = false;
            string previousEntryHash = GenesisPreviousHash;
            string headHash = GenesisPreviousHash;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                entries++;
                if (element.ValueKind != JsonValueKind.Object)
                {
                    return new LedgerIntegrity(true, false, entries, chained, legacy, headHash, NotJsonReason);
                }

                var raw = element.GetRawText();
                if (!TrySplit(raw, out var core, out var previousHash, out var entryHash))
                {
                    return new LedgerIntegrity(true, false, entries, chained, legacy, headHash, PartialChainReason);
                }

                if (previousHash is null || entryHash is null)
                {
                    if (sawChain)
                    {
                        return new LedgerIntegrity(true, false, entries, chained, legacy, headHash, MisplacedLegacyReason);
                    }

                    legacy++;
                    previousEntryHash = string.Empty;
                    continue;
                }

                sawChain = true;
                if (chained == 0)
                {
                    if (previousHash != GenesisPreviousHash && previousHash != LegacyPreviousHash)
                    {
                        return new LedgerIntegrity(true, false, entries, chained, legacy, headHash, BrokenLinkReason);
                    }

                    if (legacy > 0 && previousHash != LegacyPreviousHash)
                    {
                        return new LedgerIntegrity(true, false, entries, chained, legacy, headHash, BrokenLinkReason);
                    }
                }
                else if (previousHash != previousEntryHash)
                {
                    return new LedgerIntegrity(true, false, entries, chained, legacy, headHash, BrokenLinkReason);
                }

                if (HashEntry(previousHash, core) != entryHash)
                {
                    return new LedgerIntegrity(true, false, entries, chained, legacy, headHash, EntryHashReason);
                }

                chained++;
                previousEntryHash = entryHash;
                headHash = entryHash;
            }

            return new LedgerIntegrity(
                true,
                true,
                entries,
                chained,
                legacy,
                headHash,
                legacy > 0 ? LedgerIntegrity.LegacyReason : LedgerIntegrity.VerifiedReason);
        }
    }

    /// <summary>Re-writes the whole ordered ledger as a chain starting at genesis.</summary>
    public static string BuildChainedArray(IReadOnlyList<string> coreEntries)
    {
        ArgumentNullException.ThrowIfNull(coreEntries);

        var builder = new StringBuilder(2 + (coreEntries.Count * 384));
        builder.Append('[');
        var previous = GenesisPreviousHash;
        for (var index = 0; index < coreEntries.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var full = AppendChain(coreEntries[index], previous);
            builder.Append(full);
            previous = HashEntry(previous, coreEntries[index]);
        }

        builder.Append(']');
        return builder.ToString();
    }
}
