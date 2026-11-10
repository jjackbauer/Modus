using System.Text.Json;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// Read-back helpers for the persisted training artifacts. The promotion endpoint uses these to
/// rebuild the exact report the loop wrote instead of trusting a client or an in-memory verdict.
/// </summary>
public static class EvaluationReportJson
{
    public const string NotJsonReason = "report-not-json";

    public const string ShapeReason = "report-shape-invalid";

    public const string NotCanonicalReason = "report-not-canonical";

    public static bool TryParse(string? json, out EvaluationReport? report, out string reason)
    {
        report = null;
        reason = NotJsonReason;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                reason = ShapeReason;
                return false;
            }

            try
            {
                var candidate = new EvaluationReport(
                    root.GetProperty("challengerId").GetString() ?? string.Empty,
                    root.GetProperty("incumbentId").GetString() ?? string.Empty,
                    root.GetProperty("seeds").GetInt32(),
                    root.GetProperty("handsPerMatch").GetInt32(),
                    root.GetProperty("bigBlind").GetInt64(),
                    root.GetProperty("meanBbPer100").GetDouble(),
                    new ConfidenceInterval(
                        root.GetProperty("bbPer100Ci").GetProperty("low").GetDouble(),
                        root.GetProperty("bbPer100Ci").GetProperty("high").GetDouble()),
                    root.GetProperty("lowerBoundBbPer100").GetDouble(),
                    root.GetProperty("challengerWins").GetInt32(),
                    root.GetProperty("incumbentWins").GetInt32(),
                    root.GetProperty("ties").GetInt32(),
                    root.GetProperty("challengerNetChips").GetInt64());

                if (candidate.Seeds < 1
                    || candidate.HandsPerMatch < 1
                    || candidate.BigBlind < 2
                    || string.IsNullOrWhiteSpace(candidate.ChallengerId)
                    || string.IsNullOrWhiteSpace(candidate.IncumbentId))
                {
                    reason = ShapeReason;
                    return false;
                }

                if (!string.Equals(candidate.ToCanonicalJson(), json, StringComparison.Ordinal))
                {
                    reason = NotCanonicalReason;
                    return false;
                }

                report = candidate;
                reason = LedgerIntegrity.VerifiedReason;
                return true;
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException
                or FormatException or OverflowException or ArgumentException)
            {
                reason = ShapeReason;
                return false;
            }
        }
    }
}

/// <summary>Read-only access to a data directory's training ledger and its hash chain.</summary>
public static class TrainingLedger
{
    public static LedgerIntegrity Verify(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);

        var path = Path.Combine(dataDirectory, TrainingLoop.LedgerFileName);
        if (!File.Exists(path))
        {
            return LedgerChain.Verify(null);
        }

        try
        {
            return LedgerChain.Verify(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new LedgerIntegrity(
                true,
                false,
                0,
                0,
                0,
                LedgerChain.GenesisPreviousHash,
                "ledger-unreadable");
        }
    }
}
