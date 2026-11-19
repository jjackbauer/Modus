using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// The lifecycle from an approved contract to review, and the production gate that refuses an attempt which
/// produced no attributable source. A deterministic runner stands in for the model here; the real-model path
/// is exercised end to end by the operator run, not by this suite.
/// </summary>
public sealed class ImplementationStageTests
{
    private const string Requirement = """
        ---
        title: Book catalog HTTP API
        supportedClass: aspnetcore-http-net10
        ---

        Create an HTTP JSON API to add, retrieve and delete books.

        ## Acceptance expectations
        - Creating a book returns 201 with a stable identifier.
        - Retrieving the identifier returns the created title.
        """;

    private sealed class ScriptedRunner : IImplementationStageRunner
    {
        private readonly ImplementationOutcome _outcome;

        public ScriptedRunner(ImplementationOutcome outcome) => _outcome = outcome;

        public int Calls { get; private set; }

        public ImplementationRequest? Last { get; private set; }

        public ValueTask<ImplementationOutcome> RunAsync(ImplementationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Last = request;
            return ValueTask.FromResult(_outcome);
        }
    }

    private static FactoryControlRoot Root(out string repository)
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-impl-" + Guid.NewGuid().ToString("N"));
        repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);
        return root!;
    }

    private static async Task<(FactoryRuntime Runtime, string WorkItemId)> ApprovedAsync(
        FactoryControlRoot root,
        IImplementationStageRunner? runner)
    {
        var runtime = new FactoryRuntime(root, runner);
        var submit = await runtime.SubmitAsync(Requirement);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);
        var (proposal, _) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.NotNull(proposal);
        Assert.True((await runtime.ContractApproveAsync(submit.WorkItemId!, proposal!.ProposalDigest)).Ok);

        return (runtime, submit.WorkItemId!);
    }

    [Fact]
    public async Task Lifecycle_GivenApprovedContract_AdvancesThroughDeterministicGatesToInReview()
    {
        var root = Root(out _);
        var runner = new ScriptedRunner(ImplementationOutcome.Ok("base-1", "head-1", 3, "sha256:diff"));
        var (runtime, _) = await ApprovedAsync(root, runner);

        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("InReview", status.State);
        Assert.Equal(1, runner.Calls);
        // The runner received the requirement text, not a placeholder.
        Assert.Contains("Book catalog", runner.Last!.RequirementText, StringComparison.Ordinal);
        Assert.Equal(2, runner.Last.Expectations.Count);
    }

    [Fact]
    public async Task Production_GivenEmptyDiff_FailsStage()
    {
        var root = Root(out _);
        // A runner that claims success but reports zero changed files: the factory must not believe it.
        var runner = new ScriptedRunner(new ImplementationOutcome(true, "base-1", "head-1", "sha256:diff", 0, null, null));
        var (runtime, _) = await ApprovedAsync(root, runner);

        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("InImplementation", status.State);
        Assert.DoesNotContain("InReview", status.State, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_GivenConversationalAnswerWithoutDiff_FailsNoAttributableChange()
    {
        var root = Root(out _);
        var runner = new ScriptedRunner(ImplementationOutcome.Failed(ImplementationOutcome.NoAttributableChange));
        var (runtime, _) = await ApprovedAsync(root, runner);

        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("InImplementation", status.State);
    }

    [Fact]
    public async Task Lifecycle_GivenNoImplementationRunner_FailsWithCategory()
    {
        var root = Root(out _);
        var (runtime, _) = await ApprovedAsync(root, runner: null);

        await runtime.RunAsync(16);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("InImplementation", status.State);
    }

    [Fact]
    public async Task Lifecycle_GivenApprovedContract_ReachesRequirementsAlignedBeforeImplementation()
    {
        var root = Root(out _);
        var runner = new ScriptedRunner(ImplementationOutcome.Ok("b", "h", 1, "d"));
        var runtime = new FactoryRuntime(root, runner);

        var submit = await runtime.SubmitAsync(Requirement);
        await runtime.RunAsync(16);

        var afterProposal = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("Proposed", afterProposal.State);

        var (proposal, _) = await runtime.ContractShowAsync(submit.WorkItemId!);
        await runtime.ContractApproveAsync(submit.WorkItemId!, proposal!.ProposalDigest);

        // One pass advances to RequirementsAligned; the implementation must not run before that.
        await runtime.RunAsync(1);
        var first = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("RequirementsAligned", first.State);
        Assert.Equal(0, runner.Calls);
    }
}
