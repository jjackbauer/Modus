using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wip.Factory.Host.Factory;

/// <summary>One scenario result in one phase. The phase is part of the identity, never inferred.</summary>
public sealed record AcceptanceScenarioResult(string Id, string Phase, bool Passed, string? Detail = null);

/// <summary>
/// The evidence a harness produces for one blind/known-input run. It states identities (host/config/contract), the
/// model evidence class, and the per-phase scenario results; the evaluator, not the producer, decides pass/fail.
/// </summary>
public sealed record AcceptanceEvidence(
    string Gate,
    string HostDigest,
    string ConfigDigest,
    string RequirementDigest,
    string ContractDigest,
    string ModelEvidenceKind,
    string ModelProvider,
    bool KnownInput,
    bool Blind,
    IReadOnlyList<AcceptanceScenarioResult> PreMerge,
    IReadOnlyList<AcceptanceScenarioResult> PostMerge,
    bool Released,
    string MergedSha,
    string TargetHeadSha,
    bool OperationallyValidated,
    DateTimeOffset RecordedAtUtc);

/// <summary>
/// Evaluator-owned expectations. The implementation may not author, edit or infer these; the harness only compares
/// its evidence to them (v0.4 FAC-P0-04.1/2).
/// </summary>
public sealed record AcceptanceExpectations(
    string Gate,
    IReadOnlyList<string> ExpectedScenarioIds,
    string ApprovedContractDigest,
    string FrozenHostDigest,
    bool RequireLiveModel,
    bool EvaluatorOwned,
    string EvaluatorId);

/// <summary>The evaluator's verdict. <see cref="Passed"/> is true only when every expectation is positively met.</summary>
public sealed record AcceptanceVerdict(bool Passed, string? FailureCategory, IReadOnlyList<string> Reasons)
{
    public static AcceptanceVerdict Refuse(params string[] reasons)
        => new(false, reasons.Length == 0 ? "acceptance-failed" : reasons[0], reasons);

    public static AcceptanceVerdict Accept() => new(true, null, []);
}

/// <summary>
/// Deterministic acceptance evaluator. It refuses missing, empty, duplicated, unexpected, failed or unparseable
/// results in either phase, refuses an auto-approved/uninspected contract, refuses a changed host binary between
/// blind tasks, and refuses a known-input run relabelled as blind.
/// </summary>
public static class AcceptanceEvaluator
{
    public const string EvaluatorMissing = "acceptance-evaluator-missing";
    public const string ExpectationsEmpty = "acceptance-expectations-empty";
    public const string ExpectationsDuplicate = "acceptance-expectation-duplicate";
    public const string ScenarioMissing = "acceptance-scenario-missing";
    public const string ScenarioUnexpected = "acceptance-scenario-unexpected";
    public const string ScenarioDuplicate = "acceptance-scenario-duplicate";
    public const string ScenarioFailed = "acceptance-scenario-failed";
    public const string ScenarioUnparseable = "acceptance-scenario-unparseable";
    public const string ContractNotInspected = "acceptance-contract-not-inspected";
    public const string HostChanged = "acceptance-host-changed";
    public const string LiveModelMissing = "acceptance-live-model-missing";
    public const string KnownInputLabelledBlind = "acceptance-known-input-labelled-blind";
    public const string NotReleased = "acceptance-not-released";
    public const string GateMismatch = "acceptance-gate-mismatch";

    public static AcceptanceVerdict Evaluate(AcceptanceEvidence evidence, AcceptanceExpectations expectations)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(expectations);

        var reasons = new List<string>();

        if (!expectations.EvaluatorOwned || string.IsNullOrWhiteSpace(expectations.EvaluatorId))
        {
            reasons.Add(EvaluatorMissing);
        }

        if (!string.Equals(evidence.Gate, expectations.Gate, StringComparison.Ordinal))
        {
            reasons.Add(GateMismatch + ":" + evidence.Gate + "!=" + expectations.Gate);
        }

        var expected = expectations.ExpectedScenarioIds?.ToArray() ?? [];
        if (expected.Length == 0)
        {
            reasons.Add(ExpectationsEmpty);
        }

        if (expected.Distinct(StringComparer.Ordinal).Count() != expected.Length)
        {
            reasons.Add(ExpectationsDuplicate);
        }

        // The proposed contract must have been independently inspected: its digest must equal the evaluator's.
        if (string.IsNullOrWhiteSpace(expectations.ApprovedContractDigest)
            || !string.Equals(evidence.ContractDigest, expectations.ApprovedContractDigest, StringComparison.Ordinal))
        {
            reasons.Add(ContractNotInspected);
        }

        // The frozen host binary/config must not change between the tasks.
        if (string.IsNullOrWhiteSpace(expectations.FrozenHostDigest)
            || !string.Equals(evidence.HostDigest, expectations.FrozenHostDigest, StringComparison.Ordinal))
        {
            reasons.Add(HostChanged);
        }

        CheckPhase("pre-merge", evidence.PreMerge, expected, reasons);
        CheckPhase("post-merge", evidence.PostMerge, expected, reasons);

        if (expectations.RequireLiveModel
            && !string.Equals(evidence.ModelEvidenceKind, "live", StringComparison.Ordinal))
        {
            reasons.Add(LiveModelMissing + ":" + evidence.ModelEvidenceKind);
        }

        if (evidence.KnownInput && evidence.Blind)
        {
            reasons.Add(KnownInputLabelledBlind);
        }

        if (!evidence.Released
            || !string.Equals(evidence.MergedSha, evidence.TargetHeadSha, StringComparison.Ordinal)
            || !evidence.OperationallyValidated)
        {
            reasons.Add(NotReleased);
        }

        return reasons.Count == 0 ? AcceptanceVerdict.Accept() : AcceptanceVerdict.Refuse([.. reasons]);
    }

    /// <summary>
    /// PA-01/PA-02 must run on the same frozen binary and configuration. Each run must pass its own evaluator
    /// expectations, and both must report the same host/config digests.
    /// </summary>
    public static AcceptanceVerdict EvaluateBlindPair(
        AcceptanceEvidence first,
        AcceptanceExpectations firstExpectations,
        AcceptanceEvidence second,
        AcceptanceExpectations secondExpectations)
    {
        var firstVerdict = Evaluate(first, firstExpectations);
        var secondVerdict = Evaluate(second, secondExpectations);

        var reasons = new List<string>();
        if (!firstVerdict.Passed)
        {
            reasons.AddRange(firstVerdict.Reasons.Select(static reason => "PA-01:" + reason));
        }

        if (!secondVerdict.Passed)
        {
            reasons.AddRange(secondVerdict.Reasons.Select(static reason => "PA-02:" + reason));
        }

        if (!string.Equals(first.HostDigest, second.HostDigest, StringComparison.Ordinal))
        {
            reasons.Add(HostChanged + ":between-tasks");
        }

        if (!string.Equals(first.ConfigDigest, second.ConfigDigest, StringComparison.Ordinal))
        {
            reasons.Add("acceptance-config-changed:between-tasks");
        }

        if (first.Blind != second.Blind || !first.Blind || !second.Blind)
        {
            reasons.Add("acceptance-blind-pair-required");
        }

        return reasons.Count == 0 ? AcceptanceVerdict.Accept() : AcceptanceVerdict.Refuse([.. reasons]);
    }

    private static void CheckPhase(
        string phase,
        IReadOnlyList<AcceptanceScenarioResult>? actual,
        IReadOnlyList<string> expected,
        List<string> reasons)
    {
        var results = actual?.Where(result => string.Equals(result.Phase, phase, StringComparison.Ordinal)).ToArray() ?? [];

        if (results.Length == 0)
        {
            reasons.Add(ScenarioMissing + ":" + phase + ":all");
            return;
        }

        var ids = results.Select(static result => result.Id).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            reasons.Add(ScenarioDuplicate + ":" + phase);
        }

        foreach (var id in expected)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                reasons.Add(ScenarioUnparseable + ":" + phase);
                continue;
            }

            var matches = results.Where(result => string.Equals(result.Id, id, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
            {
                reasons.Add(ScenarioMissing + ":" + phase + ":" + id);
                continue;
            }

            if (matches.Length > 1)
            {
                reasons.Add(ScenarioDuplicate + ":" + phase + ":" + id);
            }

            if (!matches[0].Passed)
            {
                reasons.Add(ScenarioFailed + ":" + phase + ":" + id);
            }
        }

        foreach (var result in results)
        {
            if (!expected.Contains(result.Id, StringComparer.Ordinal))
            {
                reasons.Add(ScenarioUnexpected + ":" + phase + ":" + result.Id);
            }
        }
    }
}

/// <summary>JSON shape helpers for the evaluator CLI and the acceptance harness.</summary>
public static class AcceptanceJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AcceptanceEvidence? ReadEvidence(string json)
        => JsonSerializer.Deserialize<AcceptanceEvidence>(json, Options);

    public static AcceptanceExpectations? ReadExpectations(string json)
        => JsonSerializer.Deserialize<AcceptanceExpectations>(json, Options);
}
