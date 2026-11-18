using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Core.Operations;
using Xunit;

namespace Wip.Factory.Core.Tests;

public sealed class OperatorSurfaceTests
{
    [Fact]
    public void StatusSurface_GivenPopulatedFactory_ExposesAllSixRequiredViews()
    {
        var surface = new FactoryStatusSurface();

        surface.Record(FactoryView.WorkQueue, "pending", "3");
        surface.Record(FactoryView.WorkQueue, "blocked", "1");
        surface.Record(FactoryView.ProductionPipeline, "throughput-per-hour", "4.2");
        surface.Record(FactoryView.Agents, "active-profiles", "9");
        surface.Record(FactoryView.Quality, "reviews-accepted", "7");
        surface.Record(FactoryView.Improvement, "promotions", "1");
        surface.Record(FactoryView.Traceability, "linked-work-items", "12");

        Assert.Equal(6, surface.All().Count);
        Assert.Empty(surface.MissingViews());

        Assert.Equal(2, surface.Get(FactoryView.WorkQueue).Entries.Count);
        Assert.Contains(surface.Get(FactoryView.ProductionPipeline).Entries, static entry => entry.Value == "4.2");

        // An empty surface reports what is missing rather than looking complete.
        var empty = new FactoryStatusSurface();
        Assert.Equal(6, empty.MissingViews().Count);
    }

    [Fact]
    public void Controls_GivenEachOperationalControl_AppliesAndRecordsAnAuditEntry()
    {
        var controls = new FactoryControlSurface();
        var now = DateTimeOffset.UtcNow;

        var operable = Enum.GetValues<OperationalControl>()
            .Where(static control => control != OperationalControl.MutateEvidence)
            .ToArray();

        foreach (var control in operable)
        {
            var entry = controls.Apply(control, "target-1", "operator", now);

            Assert.True(entry.Applied, control.ToString());
            Assert.Null(entry.Reason);
        }

        Assert.Equal(operable.Length, controls.Audit.Count);
        Assert.Equal(operable.Length, controls.AuditOf("target-1").Count);
        Assert.Empty(controls.AuditOf("other-target"));

        // Every control is represented in the audit, so none can be invoked silently.
        foreach (var control in operable)
        {
            Assert.Contains(controls.Audit, entry => entry.Control == control);
        }
    }

    [Fact]
    public void Evidence_GivenInPlaceMutationAttempt_IsRejectedAndDigestUnchanged()
    {
        var vault = new ImmutableEvidenceVault();
        var artifact = new ArtifactId("review-R-821");

        var digest = "sha256:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("original review content"))).ToLowerInvariant();

        vault.Put(artifact, "original review content", digest);

        var verified = vault.Verify(artifact);
        Assert.True(verified.Verified);
        Assert.Equal(digest, verified.Digest);

        var mutation = vault.TryMutate(artifact, "a quietly rewritten review");
        Assert.False(mutation.Mutated);
        Assert.Equal(FactoryControlSurface.EvidenceImmutableCategory, mutation.Reason);

        // The content is byte-identical afterwards and the digest still verifies.
        var afterAttempt = vault.Verify(artifact);
        Assert.True(afterAttempt.Verified);
        Assert.Equal(digest, afterAttempt.Digest);

        // The control surface refuses the same action and audits the refusal.
        var controls = new FactoryControlSurface();
        var entry = controls.Apply(
            OperationalControl.MutateEvidence,
            artifact.Value,
            "operator",
            DateTimeOffset.UtcNow);

        Assert.False(entry.Applied);
        Assert.Contains(FactoryControlSurface.EvidenceImmutableCategory, entry.Reason, StringComparison.Ordinal);
        Assert.Single(controls.Audit);
    }

}
