using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// The engineering record is version-bound, derives its status from executed tests and mandatory engineering gates,
/// and keeps independent acceptance on a separate axis. A historical failure is preserved, never converted.
/// </summary>
public sealed class EngineeringDeliveryRecordTests
{
    private const string Revision = "d35bff73cf62cc3842ba77cd98b2a870c8e1d3a1";

    [Fact]
    public void EngineeringRecord_GivenAllMandatoryGatesPass_DerivesComplete()
    {
        var record = Complete();

        Assert.Equal(EngineeringStatus.Complete, record.Status);
        Assert.Equal(100, record.TotalExecuted);
        Assert.Equal(100, record.TotalPassed);
        Assert.Equal(0, record.TotalFailed);
        Assert.Equal(EngineeringAcceptanceStatus.NotAccepted, record.AcceptanceStatus);
    }

    [Fact]
    public void EngineeringRecord_GivenMandatoryGateFailure_DerivesBlocked()
    {
        var record = Create(gates:
        [
            Gate("build"),
            Gate("tests", EngineeringGateResult.Fail, detail: "one suite failed")
        ]);

        Assert.Equal(EngineeringStatus.Blocked, record.Status);
    }

    [Fact]
    public void EngineeringRecord_GivenFailedTestButGreenGates_DerivesBlocked()
    {
        var record = Create(suites:
        [
            new EngineeringTestCaseResult("host", "host.csproj", 10, 9, 1, 0, "dotnet test", Digest('b'))
        ]);

        Assert.Equal(EngineeringStatus.Blocked, record.Status);
    }

    [Fact]
    public void EngineeringRecord_GivenAcceptanceStatusAccepted_Refuses()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Create(acceptanceStatus: "ACCEPTED"));
        Assert.Contains("acceptance status", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringRecord_GivenStatusAcceptedInsteadOfEngineeringStatus_Refuses()
    {
        var record = Complete() with { Status = "ACCEPTED" };

        var exception = Assert.Throws<InvalidOperationException>(record.Validate);
        Assert.Contains("ACCEPTED", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringRecord_GivenContradictoryTestTotals_Refuses()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Create(suites:
        [
            new EngineeringTestCaseResult("host", "host.csproj", 1, 2, 0, 0, "dotnet test", Digest('b'))
        ]));

        Assert.Contains("counts do not partition", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringRecord_GivenAcceptanceAsMandatoryEngineeringGate_Refuses()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Create(gates:
        [
            Gate("build"),
            Gate("independent-acceptance")
        ]));

        Assert.Contains("acceptance cannot be a mandatory engineering gate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringRecord_GivenBlockedStatusWhenEverythingPasses_Refuses()
    {
        var record = Complete() with { Status = EngineeringStatus.Blocked };

        Assert.Throws<InvalidOperationException>(record.Validate);
    }

    [Fact]
    public void EngineeringRecord_GivenSameInputs_ProducesStableDigest()
    {
        var first = Complete();
        var second = Complete();

        Assert.Equal(first.EvidenceDigest, second.EvidenceDigest);
    }

    [Fact]
    public void EngineeringRecord_GivenTamperedJson_DigestRefuses()
    {
        var json = Complete().ToJson().Replace("\"branch\": \"feature/wip-factory-alpha\"", "\"branch\": \"main\"", StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidOperationException>(() => EngineeringDeliveryRecord.FromJson(json));
        Assert.Contains("digest does not match", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringRecord_GivenHistoricalFailedAcceptance_KeepsEngineeringCompleteAndAcceptanceSeparate()
    {
        var record = Complete(acceptanceStatus: EngineeringAcceptanceStatus.HistoricalFailurePreserved, pair: "fail");

        Assert.Equal(EngineeringStatus.Complete, record.Status);
        Assert.Equal(EngineeringAcceptanceStatus.HistoricalFailurePreserved, record.AcceptanceStatus);
        Assert.True(record.HistoricalAcceptance.PairFailed);
        Assert.Equal("fail", record.HistoricalAcceptance.Pa01);
        Assert.Equal("pass", record.HistoricalAcceptance.Pa02);
    }

    [Fact]
    public void EngineeringRecord_GivenCompleteWithoutKnownLimitations_Refuses()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Create(limitations: []));
        Assert.Contains("known limitations", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringRecord_GivenMissingTestSuites_Refuses()
    {
        Assert.Throws<InvalidOperationException>(() => Create(suites: []));
    }

    [Fact]
    public void EngineeringRecord_GivenNonHexRevision_Refuses()
    {
        Assert.Throws<InvalidOperationException>(() => Create(revision: "not-a-revision"));
    }

    [Fact]
    public void CandidateManifest_GivenCompleteEngineeringRecord_Validates()
    {
        var manifest = Candidate();

        Assert.Equal(EngineeringStatus.Complete, manifest.Status);
        Assert.Equal(Revision, manifest.SourceRevision);
        Assert.NotEmpty(manifest.Configuration);
        Assert.NotEmpty(manifest.Artifacts);
        Assert.NotEmpty(manifest.BuildInstructions);
    }

    [Fact]
    public void CandidateManifest_GivenTamperedArtifactDigest_DigestRefuses()
    {
        var manifest = Candidate();
        var json = manifest.ToJson().Replace("\"branch\": \"feature/wip-factory-alpha\"", "\"branch\": \"main\"", StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => CandidateManifest.FromJson(json));
    }

    [Fact]
    public void CandidateManifest_GivenFailedMandatoryGateWithCompleteStatus_Refuses()
    {
        var manifest = Candidate() with
        {
            EngineeringGates =
            [
                Gate("build"),
                Gate("tests", EngineeringGateResult.Fail)
            ]
        };

        Assert.Throws<InvalidOperationException>(manifest.Validate);
    }

    private static EngineeringDeliveryRecord Complete(
        string acceptanceStatus = EngineeringAcceptanceStatus.NotAccepted,
        string pair = "fail")
        => Create(acceptanceStatus: acceptanceStatus, pair: pair);

    private static EngineeringDeliveryRecord Create(
        string revision = Revision,
        IReadOnlyList<EngineeringTestCaseResult>? suites = null,
        IReadOnlyList<EngineeringGateResult>? gates = null,
        string acceptanceStatus = EngineeringAcceptanceStatus.NotAccepted,
        string pair = "fail",
        IReadOnlyList<string>? limitations = null)
        => EngineeringDeliveryRecord.Create(
            revision,
            "feature/wip-factory-alpha",
            DateTimeOffset.Parse("2026-11-19T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            "pass",
            "dotnet build Wip.slnx",
            0,
            0,
            Digest('a'),
            suites ?? [new EngineeringTestCaseResult("host", "Wip.Factory.Host.Tests.csproj", 100, 100, 0, 0, "dotnet test", Digest('b'))],
            gates ?? [Gate("build"), Gate("tests"), Gate("review")],
            new HistoricalAcceptanceRecord(
                "2026-11-19 independent acceptance",
                "2026-11-19T12:00:00Z",
                "e29df4d",
                Digest('c'),
                "fail",
                "pass",
                pair,
                Digest('d'),
                "PA-01 failed at the provisioning retry; PA-02 passed; pair failed."),
            acceptanceStatus,
            limitations ?? ["Windows-only process identity proof; no OS-level crash injection during the merge syscall."]);

    private static CandidateManifest Candidate()
    {
        var record = Complete();
        return CandidateManifest.Create(
            record.Status,
            Revision,
            "feature/wip-factory-alpha",
            Digest('e'),
            "dotnet-sdk-10.0.103;Wip.slnx;host-csproj+" + Digest('f'),
            [new CandidateDigestEntry("WIP/Wip.Factory.Host/Wip.Factory.Host.csproj", Digest('1'), 512)],
            [new CandidateDigestEntry("WIP/Wip.Factory.Host/bin/Debug/net10.0/Wip.Factory.Host.dll", Digest('2'), 1024)],
            ["dotnet build Wip.slnx", "dotnet test WIP/Wip.Factory.Host.Tests/Wip.Factory.Host.Tests.csproj"],
            record.EngineeringGates,
            record.EvidenceDigest,
            DateTimeOffset.Parse("2026-11-19T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static EngineeringGateResult Gate(string id, string status = EngineeringGateResult.Pass, string detail = "executed")
        => new(id, status, Mandatory: true, Evidence: "evidence://" + id, Detail: detail);

    private static string Digest(char value) => "sha256:" + new string(value, 64);
}
