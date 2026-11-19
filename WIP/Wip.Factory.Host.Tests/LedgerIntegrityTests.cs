using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P1-04: the operation ledger is append-only and digest-framed. Only a torn trailing record may be discarded;
/// interior damage fails closed, applied effects are never overwritten by a failure, and applied is not verified.
/// </summary>
public sealed class LedgerIntegrityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factory-ledger-" + Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_root, "operations.log");

    [Fact]
    public void Ledger_GivenTornLastRecord_RecoversCommittedPrefix()
    {
        var ledger = new FactoryOperationLedger(_root);
        ledger.Begin("op-1", "W-1", "E-1", "release", "A-1", 1, "git-merge", "/repo", "head-1");
        ledger.MarkApplied("op-1", "merged-sha", "git:rev-parse");

        File.AppendAllText(LogPath, "{\"checksum\":\"sha256:truncated\",\"payload\":{\"OperationId\":");

        var reopened = new FactoryOperationLedger(_root);

        Assert.True(reopened.IsApplied("op-1"));
        Assert.Equal(1, reopened.TornRecordCount);
        Assert.Equal(2, reopened.All().Count);
    }

    [Fact]
    public void Ledger_GivenMalformedInteriorRecord_FailsClosed()
    {
        var ledger = new FactoryOperationLedger(_root);
        ledger.Begin("op-1", "W-1", "E-1", "release", "A-1", 1, "git-merge", "/repo", "head-1");
        ledger.Begin("op-2", "W-1", "E-1", "release", "A-1", 2, "git-merge", "/repo", "head-2");

        var lines = File.ReadAllLines(LogPath).ToList();
        lines.Insert(1, "{ not a ledger record");
        File.WriteAllLines(LogPath, lines);

        var reopened = new FactoryOperationLedger(_root);

        var corruption = Assert.Throws<JournalCorruptionException>(() => reopened.All());
        Assert.Contains("journal-corrupt", corruption.Message, StringComparison.Ordinal);
        Assert.Equal(2, corruption.LineNumber);
    }

    [Fact]
    public void Ledger_GivenReplaySameOperation_DoesNotRepeatEffect()
    {
        var ledger = new FactoryOperationLedger(_root);

        ledger.Begin("op-1", "W-1", "E-1", "release", "A-1", 1, "git-merge", "/repo", "head-1");
        ledger.Begin("op-1", "W-1", "E-1", "release", "A-1", 1, "git-merge", "/repo", "head-1");
        ledger.MarkApplied("op-1", "merged-sha", "git:rev-parse");
        ledger.MarkApplied("op-1", "merged-sha", "git:rev-parse");

        Assert.Equal(2, ledger.All().Count);
        Assert.True(ledger.IsApplied("op-1"));
    }

    [Fact]
    public void Ledger_GivenAppliedMergeWithoutProof_RequiresVerification()
    {
        var ledger = new FactoryOperationLedger(_root);
        ledger.Begin("op-1", "W-1", "E-1", "release", "A-1", 1, "git-merge", "/repo", "head-1");
        ledger.MarkApplied("op-1", "merged-sha", "git:rev-parse");

        Assert.True(ledger.IsApplied("op-1"));
        Assert.False(ledger.IsVerified("op-1"));
        Assert.Equal(VerificationState.Unverified, ledger.Latest("op-1")!.VerificationState);

        ledger.MarkVerified("op-1", "sha256:verification", "post-merge-contract-passed");

        Assert.True(ledger.IsVerified("op-1"));
        Assert.Equal("sha256:verification", ledger.Latest("op-1")!.VerificationDigest);
    }

    [Fact]
    public void Ledger_GivenAppliedEffect_FailureRequiresExplicitCorrection()
    {
        var ledger = new FactoryOperationLedger(_root);
        ledger.Begin("op-1", "W-1", "E-1", "release", "A-1", 1, "git-merge", "/repo", "head-1");
        ledger.MarkApplied("op-1", "merged-sha", "git:rev-parse");

        var refusal = Assert.Throws<InvalidOperationException>(() => ledger.MarkFailed("op-1", "late-failure"));
        Assert.StartsWith("operation-already-applied", refusal.Message, StringComparison.Ordinal);

        ledger.MarkCorrected("op-1", "reconciliation: tree rolled back by operator");

        Assert.Equal(OperationState.Corrected, ledger.Latest("op-1")!.State);
        Assert.Contains(ledger.All(), static record => record.State == OperationState.Applied);
        Assert.Contains(ledger.All(), static record => record.State == OperationState.Corrected);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
