using Wip.Factory.Contracts.Work;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

public sealed class OperatorLifecycleTests
{
    private const string Requirement = """
        ---
        title: Book catalog HTTP API
        supportedClass: aspnetcore-http-net10
        ---

        Create an HTTP JSON API to add, retrieve and delete books.

        ## Acceptance expectations
        - Creating a book with title and author returns 201 and a stable identifier.
        - Retrieving the identifier returns the created title and author.
        - Deleting the identifier returns 204; retrieving it afterward returns 404.
        """;

    private static FactoryControlRoot Root(out string basePath)
    {
        basePath = Path.Combine(Path.GetTempPath(), "factory-cli-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);
        return root!;
    }

    [Fact]
    public async Task Intake_GivenRequirementFile_CommitsWorkItemAndEmitsRequirementSubmitted()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);

        var result = await runtime.SubmitAsync(Requirement);

        Assert.True(result.Accepted, string.Join(";", result.Errors));
        Assert.StartsWith("WORK-", result.WorkItemId, StringComparison.Ordinal);
        Assert.Equal("Proposed", result.State);
        Assert.StartsWith("sha256:", result.RequirementDigest, StringComparison.Ordinal);

        // The submitted event is durable and consumable by a fresh runtime over the same control root.
        var fresh = new FactoryRuntime(root);
        var statuses = await fresh.StatusAsync();
        var status = Assert.Single(statuses);
        Assert.Equal(result.WorkItemId, status.WorkItemId);
        Assert.Equal("Proposed", status.State);
        Assert.Equal(result.RequirementDigest, status.RequirementDigest);
        Assert.True(status.EventCount >= 1, $"expected at least one durable event, saw {status.EventCount}");
    }

    [Fact]
    public async Task Intake_GivenIdenticalContentTwice_IsIdempotent()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);

        var first = await runtime.SubmitAsync(Requirement);
        var second = await runtime.SubmitAsync(Requirement);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.Equal(first.WorkItemId, second.WorkItemId);
        Assert.Single(await runtime.StatusAsync());
    }

    [Fact]
    public async Task Intake_GivenUnsupportedClassOrMissingExpectations_IsRefusedBeforeImplementation()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);

        var noClass = await runtime.SubmitAsync("---\ntitle: x\n---\n\n- one expectation\n");
        Assert.False(noClass.Accepted);
        Assert.Contains(noClass.Errors, static e => e.StartsWith(RequirementDocument.UnsupportedClassCategory, StringComparison.Ordinal));

        var noExpectations = await runtime.SubmitAsync("---\ntitle: x\nsupportedClass: aspnetcore-http-net10\n---\n\nJust prose.\n");
        Assert.False(noExpectations.Accepted);
        Assert.Contains(noExpectations.Errors, static e => e == RequirementDocument.NoExpectationsCategory);

        // A refused submission leaves no work item behind.
        Assert.Empty(await runtime.StatusAsync());
    }

    [Fact]
    public async Task Lifecycle_GivenContractProposal_RecordsWithoutStateTransition()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);

        var submit = await runtime.SubmitAsync(Requirement);
        await runtime.RunAsync();

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("Proposed", status.State);            // no transition
        Assert.True(status.Version > WorkItemSnapshot.MinimumVersion); // facts still committed
        Assert.NotNull(status.ProposalDigest);

        var (proposal, error) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.Null(error);
        Assert.NotNull(proposal);
        Assert.Equal(3, proposal!.Scenarios.Count);
        Assert.Equal("AC-1", proposal.Scenarios[0].Id);
        // The proposal is not yet an executable oracle, and says so rather than pretending.
        Assert.False(proposal.Executable);
    }

    [Fact]
    public async Task Lifecycle_GivenContractApproved_TransitionsProposedToRequirementsAligned()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);

        var submit = await runtime.SubmitAsync(Requirement);
        await runtime.RunAsync();

        var (proposal, _) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.NotNull(proposal);

        var approved = await runtime.ContractApproveAsync(submit.WorkItemId!, proposal!.ProposalDigest);
        Assert.True(approved.Ok, approved.Error);

        // Exactly one pass: this advances to RequirementsAligned and no further.
        await runtime.RunAsync(1);

        // A fresh process reads the advanced state from durable storage only.
        var fresh = await new FactoryRuntime(root).StatusAsync();
        var status = Assert.Single(fresh);
        Assert.Equal("RequirementsAligned", status.State);
        Assert.Equal(proposal.ProposalDigest, status.FrozenContractDigest);
    }

    [Fact]
    public async Task Lifecycle_GivenWrongContractDigest_RefusesApproval()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);

        var submit = await runtime.SubmitAsync(Requirement);
        await runtime.RunAsync();

        var (wrong, error) = await runtime.ContractApproveAsync(submit.WorkItemId!, "sha256:0000");
        Assert.False(wrong);
        Assert.Equal("contract-digest-mismatch", error);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("Proposed", status.State);
    }

    [Fact]
    public async Task Host_GivenControlRootInsideTarget_RefusesToStart()
    {
        var repository = Path.Combine(Path.GetTempPath(), "factory-inside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(repository, ".factory"));

        Assert.Null(root);
        Assert.Contains(FactoryControlRoot.InsideTargetCategory, error, StringComparison.Ordinal);
    }
}
