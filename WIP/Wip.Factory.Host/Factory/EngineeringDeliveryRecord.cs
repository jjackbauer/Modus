using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wip.Factory.Host.Factory;

/// <summary>Stable engineering status. These are the only two statuses this record may carry.</summary>
public static class EngineeringStatus
{
    public const string Complete = "ENGINEERING_COMPLETE";
    public const string Blocked = "ENGINEERING_BLOCKED";
}

/// <summary>
/// Acceptance is deliberately a separate axis. Engineering completion never depends on an acceptance verdict, and a
/// historical failure stays a historical failure rather than becoming a new certification.
/// </summary>
public static class EngineeringAcceptanceStatus
{
    public const string NotAccepted = "NOT_ACCEPTED";
    public const string HistoricalFailurePreserved = "HISTORICAL_FAILURE_PRESERVED";
    public const string NotRun = "NOT_RUN";
}

public sealed record EngineeringTestCaseResult(
    string Suite,
    string Project,
    int Executed,
    int Passed,
    int Failed,
    int Skipped,
    string Command,
    string TrxDigest)
{
    public int NotExecuted => Math.Max(0, Executed - Passed - Failed - Skipped);
}

public sealed record EngineeringGateResult(
    string Id,
    string Status,
    bool Mandatory,
    string Evidence,
    string Detail)
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string NotRun = "not-run";
    public const string Blocked = "blocked";

    public bool Passed => string.Equals(Status, Pass, StringComparison.Ordinal);
}

public sealed record HistoricalAcceptanceRecord(
    string Label,
    string EvaluatedAtUtc,
    string CodeRevision,
    string HostDigest,
    string Pa01,
    string Pa02,
    string Pair,
    string EvidenceDigest,
    string Note)
{
    public bool PairFailed => string.Equals(Pair, "fail", StringComparison.Ordinal);
}

/// <summary>
/// Version-bound engineering delivery evidence. The status is derived from executed tests and mandatory engineering
/// gates; acceptance fields are descriptive history and can never raise the status.
/// </summary>
public sealed record EngineeringDeliveryRecord(
    string SchemaVersion,
    string Status,
    string CodeRevision,
    string Branch,
    string GeneratedAtUtc,
    string BuildStatus,
    string BuildCommand,
    int BuildErrors,
    int BuildWarnings,
    string BuildDigest,
    IReadOnlyList<EngineeringTestCaseResult> TestSuites,
    IReadOnlyList<EngineeringGateResult> EngineeringGates,
    HistoricalAcceptanceRecord HistoricalAcceptance,
    string AcceptanceStatus,
    IReadOnlyList<string> KnownLimitations,
    string EvidenceDigest)
{
    public const string Schema = "factory/engineering-delivery@1";

    public int TotalExecuted => TestSuites.Sum(static suite => suite.Executed);

    public int TotalPassed => TestSuites.Sum(static suite => suite.Passed);

    public int TotalFailed => TestSuites.Sum(static suite => suite.Failed);

    public int TotalSkipped => TestSuites.Sum(static suite => suite.Skipped);

    public static EngineeringDeliveryRecord Create(
        string codeRevision,
        string branch,
        DateTimeOffset generatedAtUtc,
        string buildStatus,
        string buildCommand,
        int buildErrors,
        int buildWarnings,
        string buildDigest,
        IReadOnlyList<EngineeringTestCaseResult> testSuites,
        IReadOnlyList<EngineeringGateResult> engineeringGates,
        HistoricalAcceptanceRecord historicalAcceptance,
        string acceptanceStatus,
        IReadOnlyList<string> knownLimitations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentNullException.ThrowIfNull(testSuites);
        ArgumentNullException.ThrowIfNull(engineeringGates);
        ArgumentNullException.ThrowIfNull(historicalAcceptance);
        ArgumentNullException.ThrowIfNull(knownLimitations);

        var status = DeriveStatus(buildStatus, buildErrors, testSuites, engineeringGates);
        var provisional = new EngineeringDeliveryRecord(
            Schema,
            status,
            codeRevision,
            branch,
            generatedAtUtc.ToUniversalTime().ToString("O"),
            buildStatus,
            buildCommand,
            buildErrors,
            buildWarnings,
            buildDigest,
            testSuites,
            engineeringGates,
            historicalAcceptance,
            acceptanceStatus,
            knownLimitations,
            EvidenceDigest: string.Empty);

        var record = provisional with { EvidenceDigest = provisional.ComputeDigest() };
        record.Validate();
        return record;
    }

    public static string DeriveStatus(
        string buildStatus,
        int buildErrors,
        IReadOnlyList<EngineeringTestCaseResult> testSuites,
        IReadOnlyList<EngineeringGateResult> engineeringGates)
    {
        var mandatoryGates = engineeringGates.Where(static gate => gate.Mandatory).ToArray();
        var buildOk = string.Equals(buildStatus, "pass", StringComparison.Ordinal) && buildErrors == 0;
        var testsOk = testSuites.Count > 0
                      && testSuites.All(static suite => suite.Executed > 0 && suite.Failed == 0 && suite.Passed + suite.Failed <= suite.Executed)
                      && testSuites.Sum(static suite => suite.Executed) > 0;
        var gatesOk = mandatoryGates.Length > 0 && mandatoryGates.All(static gate => gate.Passed);

        return buildOk && testsOk && gatesOk ? EngineeringStatus.Complete : EngineeringStatus.Blocked;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static EngineeringDeliveryRecord FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var record = JsonSerializer.Deserialize<EngineeringDeliveryRecord>(json, JsonOptions)
            ?? throw new InvalidOperationException("engineering-record-invalid:empty");
        record.Validate();
        return record;
    }

    public void Validate()
    {
        if (!string.Equals(SchemaVersion, Schema, StringComparison.Ordinal))
        {
            throw Invalid("unsupported schema version");
        }

        if (Status is not (EngineeringStatus.Complete or EngineeringStatus.Blocked))
        {
            throw Invalid("status must be ENGINEERING_COMPLETE or ENGINEERING_BLOCKED; ACCEPTED and PRODUCTION_READY are not engineering statuses");
        }

        if (!IsRevision(CodeRevision))
        {
            throw Invalid("code revision must be a hexadecimal Git revision");
        }

        if (string.IsNullOrWhiteSpace(Branch))
        {
            throw Invalid("branch is required");
        }

        if (!DateTimeOffset.TryParse(GeneratedAtUtc, out _))
        {
            throw Invalid("generatedAtUtc must be an ISO-8601 instant");
        }

        if (BuildStatus is not ("pass" or "fail"))
        {
            throw Invalid("build status must be pass or fail");
        }

        if (BuildErrors < 0 || BuildWarnings < 0)
        {
            throw Invalid("build counts cannot be negative");
        }

        if (string.IsNullOrWhiteSpace(BuildCommand))
        {
            throw Invalid("build command is required");
        }

        if (!IsDigest(BuildDigest))
        {
            throw Invalid("build digest must be sha256-prefixed");
        }

        if (TestSuites.Count == 0)
        {
            throw Invalid("at least one executed test suite is required");
        }

        foreach (var suite in TestSuites)
        {
            if (string.IsNullOrWhiteSpace(suite.Suite) || string.IsNullOrWhiteSpace(suite.Project))
            {
                throw Invalid("test suite and project names are required");
            }

            if (suite.Executed < 0 || suite.Passed < 0 || suite.Failed < 0 || suite.Skipped < 0)
            {
                throw Invalid($"suite '{suite.Suite}' has invalid counts");
            }

            if (suite.Executed == 0 && string.Equals(Status, EngineeringStatus.Complete, StringComparison.Ordinal))
            {
                throw Invalid($"suite '{suite.Suite}' executed no cases but the record claims engineering completion");
            }

            if (suite.Passed + suite.Failed > suite.Executed)
            {
                throw Invalid($"suite '{suite.Suite}' counts do not partition executed cases");
            }

            if (!IsDigest(suite.TrxDigest))
            {
                throw Invalid($"suite '{suite.Suite}' must bind a TRX digest");
            }
        }

        var mandatory = EngineeringGates.Where(static gate => gate.Mandatory).ToArray();
        if (mandatory.Length == 0)
        {
            throw Invalid("at least one mandatory engineering gate is required");
        }

        foreach (var gate in EngineeringGates)
        {
            if (string.IsNullOrWhiteSpace(gate.Id) || string.IsNullOrWhiteSpace(gate.Evidence))
            {
                throw Invalid("every engineering gate needs an id and evidence");
            }

            if (gate.Status is not (EngineeringGateResult.Pass or EngineeringGateResult.Fail or EngineeringGateResult.NotRun or EngineeringGateResult.Blocked))
            {
                throw Invalid($"gate '{gate.Id}' has an unsupported status");
            }

            if (gate.Mandatory && gate.Id.Contains("acceptance", StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid("independent acceptance cannot be a mandatory engineering gate");
            }
        }

        if (AcceptanceStatus is not (EngineeringAcceptanceStatus.NotAccepted
            or EngineeringAcceptanceStatus.HistoricalFailurePreserved
            or EngineeringAcceptanceStatus.NotRun))
        {
            throw Invalid("acceptance status must be NOT_ACCEPTED, HISTORICAL_FAILURE_PRESERVED or NOT_RUN");
        }

        ValidateHistoricalAcceptance(HistoricalAcceptance);

        if (string.Equals(Status, EngineeringStatus.Complete, StringComparison.Ordinal))
        {
            if (!string.Equals(BuildStatus, "pass", StringComparison.Ordinal) || BuildErrors != 0)
            {
                throw Invalid("ENGINEERING_COMPLETE requires a passing build");
            }

            if (TotalFailed != 0)
            {
                throw Invalid("ENGINEERING_COMPLETE requires zero failed test cases");
            }

            if (mandatory.Any(static gate => !gate.Passed))
            {
                throw Invalid("ENGINEERING_COMPLETE requires every mandatory engineering gate to pass");
            }

            if (KnownLimitations.Count == 0)
            {
                throw Invalid("ENGINEERING_COMPLETE must record known limitations (use an explicit none-known entry)");
            }
        }
        else if (!string.Equals(BuildStatus, "pass", StringComparison.Ordinal)
                 || BuildErrors != 0
                 || TotalFailed != 0
                 || mandatory.Any(static gate => !gate.Passed))
        {
            // Blocked is the honest status whenever a mandatory fact is not green.
        }
        else
        {
            throw Invalid("ENGINEERING_BLOCKED requires a failed build, failed test or failed mandatory gate");
        }

        if (!IsDigest(EvidenceDigest))
        {
            throw Invalid("evidence digest must be sha256-prefixed");
        }

        if (!string.Equals(EvidenceDigest, ComputeDigest(), StringComparison.Ordinal))
        {
            throw Invalid("evidence digest does not match the record content");
        }
    }

    private static void ValidateHistoricalAcceptance(HistoricalAcceptanceRecord acceptance)
    {
        ArgumentNullException.ThrowIfNull(acceptance);
        if (string.IsNullOrWhiteSpace(acceptance.Label)
            || string.IsNullOrWhiteSpace(acceptance.CodeRevision)
            || string.IsNullOrWhiteSpace(acceptance.HostDigest)
            || string.IsNullOrWhiteSpace(acceptance.EvidenceDigest))
        {
            throw Invalid("historical acceptance must be version-bound");
        }

        foreach (var verdict in new[] { acceptance.Pa01, acceptance.Pa02, acceptance.Pair })
        {
            if (verdict is not ("pass" or "fail" or "not-run"))
            {
                throw Invalid("historical acceptance verdicts must be pass, fail or not-run");
            }
        }

        if (!DateTimeOffset.TryParse(acceptance.EvaluatedAtUtc, out _))
        {
            throw Invalid("historical acceptance needs an ISO-8601 evaluatedAtUtc");
        }
    }

    private string ComputeDigest()
    {
        var canonical = this with { EvidenceDigest = string.Empty };
        var payload = JsonSerializer.Serialize(canonical, JsonOptions);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static bool IsRevision(string value)
        => value.Length >= 7 && value.All(static character => char.IsAsciiHexDigit(character));

    private static bool IsDigest(string value)
        => value.StartsWith("sha256:", StringComparison.Ordinal) && value.Length == 71;

    private static InvalidOperationException Invalid(string detail)
        => new("engineering-record-invalid:" + detail);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

/// <summary>Input to the `factory engineering record` command; the record derives status and digest itself.</summary>
public sealed record EngineeringRecordInput(
    string CodeRevision,
    string Branch,
    DateTimeOffset GeneratedAtUtc,
    string BuildStatus,
    string BuildCommand,
    int BuildErrors,
    int BuildWarnings,
    string BuildDigest,
    IReadOnlyList<EngineeringTestCaseResult> TestSuites,
    IReadOnlyList<EngineeringGateResult> EngineeringGates,
    HistoricalAcceptanceRecord HistoricalAcceptance,
    string AcceptanceStatus,
    IReadOnlyList<string> KnownLimitations)
{
    public EngineeringDeliveryRecord CreateRecord()
        => EngineeringDeliveryRecord.Create(
            CodeRevision,
            Branch,
            GeneratedAtUtc,
            BuildStatus,
            BuildCommand,
            BuildErrors,
            BuildWarnings,
            BuildDigest,
            TestSuites,
            EngineeringGates,
            HistoricalAcceptance,
            AcceptanceStatus,
            KnownLimitations);
}

/// <summary>Input to the `factory engineering manifest` command.</summary>
public sealed record CandidateManifestInput(
    string Status,
    string SourceRevision,
    string Branch,
    string SourceTreeDigest,
    string DependencyIdentity,
    IReadOnlyList<CandidateDigestEntry> Configuration,
    IReadOnlyList<CandidateDigestEntry> Artifacts,
    IReadOnlyList<string> BuildInstructions,
    IReadOnlyList<EngineeringGateResult> EngineeringGates,
    string EngineeringRecordDigest,
    DateTimeOffset GeneratedAtUtc)
{
    public CandidateManifest CreateManifest()
        => CandidateManifest.Create(
            Status,
            SourceRevision,
            Branch,
            SourceTreeDigest,
            DependencyIdentity,
            Configuration,
            Artifacts,
            BuildInstructions,
            EngineeringGates,
            EngineeringRecordDigest,
            GeneratedAtUtc);
}

/// <summary>One path/digest binding in a candidate manifest.</summary>
public sealed record CandidateDigestEntry(string Path, string Digest, long Bytes);

/// <summary>
/// Reproducible candidate handoff. It binds the source revision, dependency identity, configuration and artifacts to
/// the engineering gate results, without claiming acceptance.
/// </summary>
public sealed record CandidateManifest(
    string SchemaVersion,
    string Status,
    string SourceRevision,
    string Branch,
    string SourceTreeDigest,
    string DependencyIdentity,
    IReadOnlyList<CandidateDigestEntry> Configuration,
    IReadOnlyList<CandidateDigestEntry> Artifacts,
    IReadOnlyList<string> BuildInstructions,
    IReadOnlyList<EngineeringGateResult> EngineeringGates,
    string EngineeringRecordDigest,
    string GeneratedAtUtc,
    string EvidenceDigest)
{
    public const string Schema = "factory/candidate-manifest@1";

    public static CandidateManifest Create(
        string status,
        string sourceRevision,
        string branch,
        string sourceTreeDigest,
        string dependencyIdentity,
        IReadOnlyList<CandidateDigestEntry> configuration,
        IReadOnlyList<CandidateDigestEntry> artifacts,
        IReadOnlyList<string> buildInstructions,
        IReadOnlyList<EngineeringGateResult> engineeringGates,
        string engineeringRecordDigest,
        DateTimeOffset generatedAtUtc)
    {
        var provisional = new CandidateManifest(
            Schema,
            status,
            sourceRevision,
            branch,
            sourceTreeDigest,
            dependencyIdentity,
            configuration,
            artifacts,
            buildInstructions,
            engineeringGates,
            engineeringRecordDigest,
            generatedAtUtc.ToUniversalTime().ToString("O"),
            EvidenceDigest: string.Empty);
        var manifest = provisional with { EvidenceDigest = provisional.ComputeDigest() };
        manifest.Validate();
        return manifest;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CandidateManifest FromJson(string json)
    {
        var manifest = JsonSerializer.Deserialize<CandidateManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("candidate-manifest-invalid:empty");
        manifest.Validate();
        return manifest;
    }

    public void Validate()
    {
        if (!string.Equals(SchemaVersion, Schema, StringComparison.Ordinal))
        {
            throw Invalid("unsupported schema version");
        }

        if (!string.Equals(Status, EngineeringStatus.Complete, StringComparison.Ordinal)
            && !string.Equals(Status, EngineeringStatus.Blocked, StringComparison.Ordinal))
        {
            throw Invalid("status must be ENGINEERING_COMPLETE or ENGINEERING_BLOCKED");
        }

        if (SourceRevision.Length < 7 || !SourceRevision.All(static character => char.IsAsciiHexDigit(character)))
        {
            throw Invalid("source revision must be a hexadecimal Git revision");
        }

        if (string.IsNullOrWhiteSpace(Branch)
            || !IsDigest(SourceTreeDigest)
            || string.IsNullOrWhiteSpace(DependencyIdentity)
            || !IsDigest(EngineeringRecordDigest))
        {
            throw Invalid("source, dependency and engineering-record identity are required");
        }

        if (Configuration.Count == 0 || Artifacts.Count == 0)
        {
            throw Invalid("configuration and artifact digests are required");
        }

        foreach (var entry in Configuration.Concat(Artifacts))
        {
            if (string.IsNullOrWhiteSpace(entry.Path) || !IsDigest(entry.Digest) || entry.Bytes < 0)
            {
                throw Invalid("every digest entry needs a path, sha256 digest and byte count");
            }
        }

        if (BuildInstructions.Count == 0 || BuildInstructions.Any(string.IsNullOrWhiteSpace))
        {
            throw Invalid("build instructions are required");
        }

        var mandatory = EngineeringGates.Where(static gate => gate.Mandatory).ToArray();
        if (mandatory.Length == 0)
        {
            throw Invalid("at least one mandatory engineering gate is required");
        }

        if (string.Equals(Status, EngineeringStatus.Complete, StringComparison.Ordinal) && mandatory.Any(static gate => !gate.Passed))
        {
            throw Invalid("ENGINEERING_COMPLETE requires every mandatory engineering gate to pass");
        }

        if (!DateTimeOffset.TryParse(GeneratedAtUtc, out _))
        {
            throw Invalid("generatedAtUtc must be an ISO-8601 instant");
        }

        if (!string.Equals(EvidenceDigest, ComputeDigest(), StringComparison.Ordinal))
        {
            throw Invalid("evidence digest does not match the manifest content");
        }
    }

    private string ComputeDigest()
    {
        var canonical = this with { EvidenceDigest = string.Empty };
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, JsonOptions)))).ToLowerInvariant();
    }

    private static bool IsDigest(string value)
        => value.StartsWith("sha256:", StringComparison.Ordinal) && value.Length == 71;

    private InvalidOperationException Invalid(string detail)
        => new("candidate-manifest-invalid:" + detail);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
