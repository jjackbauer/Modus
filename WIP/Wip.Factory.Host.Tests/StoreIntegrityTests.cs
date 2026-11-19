using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Persistence;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Host.Factory;
using Wip.Factory.Persistence;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P1-03 / K-11 / K-12: every shared-state mutation obeys one cross-process contract, a stale version cannot
/// overwrite newer state, a torn trailing record is recoverable, and interior corruption fails visibly.
/// </summary>
public sealed class StoreIntegrityTests
{
    private static string Requirement(int index) => $"""
        ---
        title: Ping API {index}
        supportedClass: aspnetcore-http-net10
        ---

        Create an HTTP JSON API with endpoint number {index}.

        ## Acceptance expectations
        - GET /ping returns 200.
        """;

    private static FactoryControlRoot Root(out string basePath)
    {
        basePath = Path.Combine(Path.GetTempPath(), "factory-store-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);
        return root!;
    }

    [Fact]
    public async Task Store_GivenConcurrentSubmitApproveAndRun_NoLostUpdates()
    {
        var root = Root(out _);
        var runtimes = Enumerable.Range(0, 4).Select(_ => new FactoryRuntime(root)).ToArray();

        var submissions = await Task.WhenAll(
            runtimes.Select((runtime, index) => runtime.SubmitAsync(Requirement(index)).AsTask()));

        Assert.All(submissions, submission => Assert.True(submission.Accepted, string.Join(";", submission.Errors)));
        Assert.Equal(4, (await runtimes[0].StatusAsync()).Count);

        var firstItem = submissions[0].WorkItemId!;
        await runtimes[0].RunAsync(8);
        var (proposal, error) = await runtimes[0].ContractShowAsync(firstItem);
        Assert.Null(error);
        Assert.NotNull(proposal);

        // A worker commit, an operator approval and another submission race on the same control root.
        var runTask = runtimes[0].RunAsync(8).AsTask();
        var approveTask = runtimes[1].ContractApproveAsync(firstItem, proposal!.ProposalDigest).AsTask();
        var submitTask = runtimes[2].SubmitAsync(Requirement(99)).AsTask();

        await Task.WhenAll(runTask, approveTask, submitTask);

        var approval = await approveTask;
        Assert.True(approval.Ok, approval.Error);
        Assert.Equal(5, (await new FactoryRuntime(root).StatusAsync()).Count);

        var events = await new FactoryRuntime(root).EventsAsync(firstItem);
        Assert.Contains(events, static e => e.Type == "factory.contract.approved.v1");

        // The approval fact survives; the next dispatch pass advances it.
        await new FactoryRuntime(root).RunAsync(16);
        Assert.NotEqual("Proposed", Assert.Single(await new FactoryRuntime(root).StatusAsync(), s => s.WorkItemId == firstItem).State);
    }

    [Fact]
    public async Task Store_GivenConcurrentTemplateApprovalAndReplace_NeverApprovesChangedContent()
    {
        var root = Root(out _);
        const string templateJson = """
            {
              "templateId": "ping-service",
              "templateVersion": 1,
              "schemaVersion": 1,
              "supportedClass": "aspnetcore-http-net10",
              "parameters": {},
              "scenarios": [ { "id": "AC-1", "method": "GET", "path": "/ping", "expect": { "status": 200 } } ]
            }
            """;

        var registering = new FactoryTemplateRegistry(root.Root);
        var manifest = registering.Register(System.Text.Json.Nodes.JsonNode.Parse(templateJson)!);

        var approving = new FactoryTemplateRegistry(root.Root);
        var changed = System.Text.Json.Nodes.JsonNode.Parse(
            templateJson.Replace("\"status\": 200", "\"status\": 201", StringComparison.Ordinal))!;

        var approveTask = Task.Run(() => approving.Approve(manifest.TemplateId, manifest.TemplateVersion, manifest.ManifestDigest));
        var replaceTask = Task.Run(() =>
        {
            try
            {
                registering.Register(changed);
                return "registered";
            }
            catch (TemplateVersionConflictException)
            {
                return "conflict";
            }
        });

        await Task.WhenAll(approveTask, replaceTask);

        Assert.Equal("conflict", await replaceTask);
        var (resolved, error) = new FactoryTemplateRegistry(root.Root).Resolve("ping-service", 1);
        Assert.Null(error);
        Assert.Equal(manifest.ManifestDigest, resolved!.ManifestDigest);
    }

    [Fact]
    public async Task Store_GivenProcessDeathDuringMutation_ReplaysConsistentState()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);
        var submitted = await runtime.SubmitAsync(Requirement(1));
        Assert.True(submitted.Accepted);

        // A process killed mid-append leaves an incomplete trailing record.
        var log = Path.Combine(root.Root, "factory.log");
        await File.AppendAllTextAsync(log, "{\"Kind\":\"work-commit\",\"Snapshot\":{\"WorkItemId\":\"WORK-tor");

        var restarted = new FactoryRuntime(root);
        var status = Assert.Single(await restarted.StatusAsync());

        Assert.Equal(submitted.WorkItemId, status.WorkItemId);
        Assert.Equal("Proposed", status.State);
        Assert.Equal(1, restarted.Store.TornRecordCount);
    }

    [Fact]
    public async Task Store_GivenCorruptInteriorRecord_RefusesOrQuarantinesExplicitly()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);
        Assert.True((await runtime.SubmitAsync(Requirement(1))).Accepted);
        await runtime.RunAsync(4);

        var log = Path.Combine(root.Root, "factory.log");
        var lines = (await File.ReadAllLinesAsync(log)).ToList();
        Assert.True(lines.Count >= 2, "the store needs at least two records to have an interior position");
        lines.Insert(1, "{ this is not a committed record");

        await File.WriteAllLinesAsync(log, lines);

        var corruption = await Assert.ThrowsAsync<FactoryStoreCorruptionException>(
            async () => await new FactoryRuntime(root).StatusAsync());

        Assert.Contains("store-corrupt", corruption.Message, StringComparison.Ordinal);
        Assert.True(corruption.LineNumber >= 2);
    }

    [Fact]
    public async Task Store_GivenStaleVersion_RejectsCommit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "factory-store-cas-" + Guid.NewGuid().ToString("N"));
        var store = new LocalFactoryStore(directory);
        var snapshot = new WorkItemSnapshot(
            new WorkItemId("W-1"), FactoryWorkState.Proposed, 1, "/repo", [], DateTimeOffset.UtcNow);

        var first = await store.CommitAsync(new WorkItemCommit(snapshot, 0, []));
        Assert.True(first.Succeeded);

        var stale = await store.CommitAsync(new WorkItemCommit(snapshot, 0, []));

        Assert.False(stale.Succeeded);
        Assert.Equal(CommitStatus.VersionConflict, stale.Status);
    }
}
