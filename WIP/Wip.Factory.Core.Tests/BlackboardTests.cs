using Wip.Abstractions.Identifiers;
using Wip.Factory.Blackboard;
using Wip.Factory.Contracts.Blackboard;
using Wip.Factory.Contracts.Identifiers;
using Xunit;

namespace Wip.Factory.Core.Tests;

public sealed class BlackboardTests
{
    private static readonly WorkItemId Item = new("W-1042");
    private static readonly AgentDefinitionId Definition = new("factory.review-analyst");
    private static readonly AgentProfileVersion Version = new(1);

    private static AgentExecutionIdentity Principal(string role = "review-analyst", bool reflection = false) =>
        new(Definition, Version, new SessionId("session-1"), role, reflection);

    private static ContextQuery Query(int budget = 100_000, params BlackboardView[] views) =>
        new(Item, Definition, Version, views, budget);

    private static BlackboardReference Reference(
        string id,
        BlackboardView view,
        string owner,
        int tokens,
        BlackboardSensitivity sensitivity = BlackboardSensitivity.Shared,
        string role = "review-analyst") =>
        new(new ArtifactId(id), "1", view, owner, "sha256:" + id, sensitivity, role, tokens);

    private static async Task<FactoryBlackboard> SeededAsync()
    {
        var blackboard = new FactoryBlackboard();
        var views = Enum.GetValues<BlackboardView>();

        for (var index = 0; index < views.Length; index++)
        {
            await blackboard.PublishAsync(
                Reference($"artifact-{views[index]}", views[index], $"owner-{views[index]}", tokens: 100),
                Principal());
        }

        return blackboard;
    }

    [Fact]
    public async Task Blackboard_GivenResolveAcrossSixViews_ReturnsOwnerTaggedReferences()
    {
        var blackboard = await SeededAsync();

        var bundle = await blackboard.ResolveAsync(Query(), Principal());

        Assert.Equal(6, bundle.References.Count);
        Assert.Equal(6, bundle.CoveredViews.Count);
        Assert.Equal(Enum.GetValues<BlackboardView>().Length, bundle.CoveredViews.Count);

        // Every reference names its durable owner, so the blackboard is not an artifact-only index.
        Assert.All(bundle.References, static reference =>
        {
            Assert.Equal($"owner-{reference.View}", reference.DurableOwner);
            Assert.Equal("1", reference.Version);
            Assert.StartsWith("sha256:", reference.ContentDigest, StringComparison.Ordinal);
        });

        Assert.Contains(bundle.References, static r => r.View == BlackboardView.GitAndDelivery);
        Assert.Contains(bundle.References, static r => r.View == BlackboardView.OperationalObservations);
        Assert.Contains(bundle.References, static r => r.View == BlackboardView.AgentCapabilities);
    }

    [Fact]
    public async Task Blackboard_GivenRoleScopedQuery_ReturnsVersionPinnedBundleWithProvenance()
    {
        var blackboard = await SeededAsync();
        await blackboard.PublishAsync(
            Reference("private-review-notes", BlackboardView.SessionsAndExperience, "sessions", 50,
                BlackboardSensitivity.RolePrivate, role: "review-analyst"),
            Principal("review-analyst"));

        var bundle = await blackboard.ResolveAsync(
            Query(100_000, BlackboardView.SessionsAndExperience),
            Principal("review-analyst"));

        // The producing role is recorded from the producer identity, not from the caller's claim.
        Assert.All(bundle.References, static reference =>
            Assert.Equal("review-analyst", reference.ProducingRole));

        // A non-reflection principal in a different role can read shared material but not another
        // role's private material.
        var otherRole = await blackboard.ResolveAsync(
            Query(100_000, BlackboardView.SessionsAndExperience),
            Principal("implementations-analyst"));

        Assert.Contains(otherRole.References, static r => r.ArtifactId.Value == "artifact-SessionsAndExperience");
        Assert.DoesNotContain(otherRole.References, static r => r.ArtifactId.Value == "private-review-notes");
        Assert.Contains(otherRole.Withheld, static r => r.ArtifactId.Value == "private-review-notes");
    }

    [Fact]
    public async Task Blackboard_GivenReflectionPrincipal_ReadsNoPrivateToolArgumentsOrHoldoutLabels()
    {
        var blackboard = new FactoryBlackboard();

        await blackboard.PublishAsync(
            Reference("tool-arguments", BlackboardView.SessionsAndExperience, "sessions", 10,
                BlackboardSensitivity.RolePrivate, role: "implementations-analyst"),
            Principal("implementations-analyst"));

        await blackboard.PublishAsync(
            Reference("holdout-labels", BlackboardView.ProductAndArchitectureKnowledge, "holdouts", 10,
                BlackboardSensitivity.Holdout),
            Principal());

        await blackboard.PublishAsync(
            Reference("shared-guidelines", BlackboardView.ProductAndArchitectureKnowledge, "guidelines", 10),
            Principal());

        var reflection = Principal("requirements-analyst", reflection: true);
        var bundle = await blackboard.ResolveAsync(Query(), reflection);

        Assert.DoesNotContain(bundle.References, static r => r.ArtifactId.Value == "tool-arguments");
        Assert.DoesNotContain(bundle.References, static r => r.ArtifactId.Value == "holdout-labels");
        Assert.Contains(bundle.References, static r => r.ArtifactId.Value == "shared-guidelines");

        // Withheld material is reported, so a denial is never indistinguishable from an empty store.
        Assert.Contains(bundle.Withheld, static r => r.ArtifactId.Value == "tool-arguments");
        Assert.Contains(bundle.Withheld, static r => r.ArtifactId.Value == "holdout-labels");
    }

    [Fact]
    public async Task Blackboard_GivenOversizedQuery_ReturnsBoundedBundleWithTruncationMetadata()
    {
        var blackboard = new FactoryBlackboard();

        for (var index = 0; index < 5; index++)
        {
            await blackboard.PublishAsync(
                Reference($"artifact-{index}", BlackboardView.WorkAndDependencies, "work", tokens: 100),
                Principal());
        }

        var bundle = await blackboard.ResolveAsync(Query(250), Principal());

        Assert.True(bundle.TruncatedDueToBudget);
        Assert.True(bundle.EstimatedTokens <= 250);
        Assert.Equal(2, bundle.References.Count);
        Assert.Equal(3, bundle.TruncatedArtifactIds.Count);

        // The full budget is honoured: nothing is silently included beyond it.
        var generous = await blackboard.ResolveAsync(Query(1000), Principal());
        Assert.False(generous.TruncatedDueToBudget);
        Assert.Equal(5, generous.References.Count);
        Assert.Equal(500, generous.EstimatedTokens);
    }
}
