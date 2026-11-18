using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// The operation ledger (v0.3 §5.2): an effect has a stable identity derived from what it is, a durable
/// pending/applied record, and a retry of the same effect reuses the original identity.
/// </summary>
public sealed class OperationLedgerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factory-ops-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Operation_GivenThreeWritesInStage_HasThreeUniqueStableIds()
    {
        string Id(int ordinal) => FactoryOperationLedger.DeriveOperationId(
            "W-1", "E-1", "implementation", "A-1", ordinal, "write-file", "src/Program.cs", "sha256:content");

        var first = Id(1);
        var second = Id(2);
        var third = Id(3);

        Assert.NotEqual(first, second);
        Assert.NotEqual(second, third);
        Assert.NotEqual(first, third);

        // The same effect derives the same identity every time.
        Assert.Equal(first, Id(1));
        Assert.StartsWith("op-", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Operation_GivenReplayOfSameEffect_ReusesOriginalOperationId()
    {
        var ledger = new FactoryOperationLedger(_root);
        const string operationId = "op-fixed";

        var first = ledger.Begin(operationId, "W-1", "E-1", "release", "A-1", 1, "git-merge", "/repo", "head-1");
        var second = ledger.Begin(operationId, "W-1", "E-1", "release", "A-1", 1, "git-merge", "/repo", "head-1");

        Assert.Equal(first.OperationId, second.OperationId);
        Assert.Single(ledger.All());
        Assert.False(ledger.IsApplied(operationId));

        ledger.MarkApplied(operationId, "merged-sha", "git:rev-parse");

        Assert.True(ledger.IsApplied(operationId));
        Assert.Equal(OperationState.Applied, ledger.Latest(operationId)!.State);
        Assert.Equal("merged-sha", ledger.Latest(operationId)!.ExternalIdentity);
    }

    [Fact]
    public void Operation_GivenRestart_AppliedStateIsDurable()
    {
        var ledger = new FactoryOperationLedger(_root);
        const string operationId = "op-durable";

        ledger.Begin(operationId, "W-1", "E-1", "release", "A-1", 1, "git-merge", "/repo", "head-1");
        ledger.MarkApplied(operationId, "merged-sha", "git:rev-parse");

        // A fresh ledger over the same control root must reconcile without repeating the effect.
        var reopened = new FactoryOperationLedger(_root);
        Assert.True(reopened.IsApplied(operationId));
        Assert.Equal("merged-sha", reopened.Latest(operationId)!.ExternalIdentity);
    }

    [Fact]
    public void Operation_GivenFailure_RecordsTheReason()
    {
        var ledger = new FactoryOperationLedger(_root);
        ledger.Begin("op-fail", "W-1", "E-1", "release", "A-1", 1, "git-merge", "/repo", "head-1");
        ledger.MarkFailed("op-fail", "merge-conflict:CONFLICT");

        Assert.False(ledger.IsApplied("op-fail"));
        Assert.Equal(OperationState.Failed, ledger.Latest("op-fail")!.State);
        Assert.Contains("merge-conflict", ledger.Latest("op-fail")!.Reason, StringComparison.Ordinal);
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