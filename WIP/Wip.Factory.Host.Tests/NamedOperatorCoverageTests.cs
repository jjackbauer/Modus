using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Persistence;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Core.Fanout;
using Wip.Factory.Host.Factory;
using Wip.Factory.Persistence;
using Wip.Factory.Wip.Governance;
using Wip.Factory.Wip.Isolation;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// Exact v0.3 names whose mechanism already exists, each asserting the spec behaviour rather than merely carrying
/// the name. These close traceability gaps without inventing pass-through tests (v0.4 FAC-P1-08.2–3).
/// </summary>
public sealed class NamedOperatorCoverageTests
{
    private static string Requirement(int index = 1) => $"""
        ---
        title: Ping API {index}
        supportedClass: aspnetcore-http-net10
        ---

        Create an HTTP JSON API with endpoint number {index}.

        ## Acceptance expectations
        - GET /ping returns 200.
        """;

    private const string ContractJson = """
        {
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "scenarios": [ { "id": "AC-1", "method": "GET", "path": "/ping", "expect": { "status": 200 } } ]
        }
        """;

    private static FactoryControlRoot Root(out string basePath)
    {
        basePath = Path.Combine(Path.GetTempPath(), "factory-namedcov-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);
        return root!;
    }

    [Fact]
    public async Task Intake_GivenFileWithoutAcceptanceExpectations_IsRefused()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);

        var result = await runtime.SubmitAsync("---\ntitle: x\nsupportedClass: aspnetcore-http-net10\n---\n\nJust prose.\n");

        Assert.False(result.Accepted);
        Assert.Contains(RequirementDocument.NoExpectationsCategory, result.Errors);
        Assert.Empty(await runtime.StatusAsync());
    }

    [Fact]
    public async Task Intake_GivenUnsupportedClass_IsRefusedBeforeImplementation()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);

        var result = await runtime.SubmitAsync("---\ntitle: x\nsupportedClass: mobile-app\n---\n\n- one expectation\n");

        Assert.False(result.Accepted);
        Assert.Contains(result.Errors, static error => error.StartsWith(RequirementDocument.UnsupportedClassCategory, StringComparison.Ordinal));
        Assert.Empty(await runtime.StatusAsync());
    }

    [Fact]
    public async Task Intake_GivenRestart_RequirementAndContractProposalAreRecoverable()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);
        var submit = await runtime.SubmitAsync(Requirement(), null, ContractJson);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(8);
        var (proposal, error) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.Null(error);
        Assert.NotNull(proposal);

        var restarted = new FactoryRuntime(root);
        var (recovered, recoveredError) = await restarted.ContractShowAsync(submit.WorkItemId!);

        Assert.Null(recoveredError);
        Assert.Equal(proposal!.ProposalDigest, recovered!.ProposalDigest);
        Assert.Equal(submit.RequirementDigest, (await restarted.StatusAsync()).Single().RequirementDigest);
    }

    [Fact]
    public async Task Host_GivenSecondRun_ResumesWithoutRepeatingAppliedOperations()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);
        Assert.True((await runtime.SubmitAsync(Requirement())).Accepted);

        await runtime.RunAsync(8);
        var proposals = (await runtime.EventsAsync((await runtime.StatusAsync()).Single().WorkItemId))
            .Count(static envelope => envelope.Type == FactoryEventTypes.ContractProposed);

        var second = await runtime.RunAsync(8);

        Assert.Equal(1, proposals);
        Assert.Equal(1, (await runtime.EventsAsync((await runtime.StatusAsync()).Single().WorkItemId))
            .Count(static envelope => envelope.Type == FactoryEventTypes.ContractProposed));
        Assert.All(second, static report => Assert.Equal(0, report.Claimed));
    }

    [Fact]
    public async Task StageHandler_GivenArchitectureGate_IsDeterministicNotModelDriven()
    {
        var repository = Path.Combine(Path.GetTempPath(), "factory-arch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);

        var snapshot = new WorkItemSnapshot(
            new WorkItemId("W-arch"), FactoryWorkState.RequirementsAligned, 2, repository, [], DateTimeOffset.UtcNow);
        var trigger = new FactoryEventEnvelope(
            new FactoryEventId("E-arch"),
            OperatorEvents.RequirementAligned,
            "test",
            "W-arch",
            DateTimeOffset.UtcNow,
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workItemId"] = "W-arch",
                ["contractDigest"] = "sha256:contract"
            });

        // No implementation runner, candidate host, or model is supplied: the gate must not need one.
        var handler = new FactoryStageDispatchHandler();
        var first = await handler.HandleAsync(snapshot, trigger);
        var second = await handler.HandleAsync(snapshot, trigger);

        Assert.Equal(StageDispatchKind.Advanced, first.Kind);
        Assert.Equal(FactoryWorkState.ArchitectureRefined, first.NextState);
        Assert.Equal(first.NextState, second.NextState);
        Assert.Equal(
            first.Events.Single().Data["checks"],
            second.Events.Single().Data["checks"]);
        Assert.Contains("supported-class", first.Events.Single().Data["checks"], StringComparison.Ordinal);
    }

    private sealed class NoIsolationRunner : IProcessRunner
    {
        public ValueTask<BoundedExecutionResult> RunAsync(
            string command,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            ProcessBounds bounds,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new BoundedExecutionResult(
                true, 0, string.Empty, string.Empty, TimeSpan.Zero, false, false, IsolationApplied: false));
    }

    private sealed class LargeOutputRunner : IProcessRunner
    {
        public ValueTask<BoundedExecutionResult> RunAsync(
            string command,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            ProcessBounds bounds,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new BoundedExecutionResult(
                true, 0, new string('x', 9000), string.Empty, TimeSpan.Zero, false, false, IsolationApplied: true));
    }

    [Fact]
    public async Task ToolGateway_GivenMissingIsolation_FailsClosed()
    {
        var worktree = Path.Combine(Path.GetTempPath(), "factory-named-bounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(worktree);
        var tool = new RunCommandTool(new NoIsolationRunner());
        var context = new CapabilityContext(new SessionId("session-1"), worktree);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await tool.ExecuteAsync(new RunCommandRequest("build", []), context, CancellationToken.None));

        Assert.Contains(RunCommandTool.IsolationUnavailableCategory, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolGateway_GivenLargeOutput_BoundsCaptureMemoryAndMarksTruncation()
    {
        var worktree = Path.Combine(Path.GetTempPath(), "factory-named-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(worktree);
        var tool = new RunCommandTool(new LargeOutputRunner());
        var context = new CapabilityContext(new SessionId("session-1"), worktree);

        var result = await tool.ExecuteAsync(new RunCommandRequest("build", []), context, CancellationToken.None);

        Assert.EndsWith("[truncated]", result.StandardOutput, StringComparison.Ordinal);
        Assert.True(result.StandardOutput.Length < 9000);
        Assert.True(result.StandardOutput.Length <= 8500);
    }

    [Fact]
    public async Task Lifecycle_GivenSubmission_EmitsConsumableRequirementSubmitted()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);
        var submit = await runtime.SubmitAsync(Requirement());
        Assert.True(submit.Accepted);

        var submitted = (await runtime.EventsAsync(submit.WorkItemId!))
            .Single(static envelope => envelope.Type == FactoryEventTypes.RequirementSubmitted);
        Assert.Equal(Requirement(), submitted.Data["requirement"]);
        Assert.Equal(submit.RequirementDigest, submitted.Data["requirementDigest"]);

        // Consumable: a dispatch pass turns it into a durable proposal without an extra submission.
        await runtime.RunAsync(8);
        Assert.NotNull((await runtime.ContractShowAsync(submit.WorkItemId!)).Proposal);
    }

    [Fact]
    public async Task Lifecycle_GivenPendingContractApproval_StopsWithoutBusyLoop()
    {
        var root = Root(out _);
        var runtime = new FactoryRuntime(root);
        Assert.True((await runtime.SubmitAsync(Requirement())).Accepted);

        await runtime.RunAsync(8);
        var second = await runtime.RunAsync(8);

        Assert.Equal("Proposed", Assert.Single(await runtime.StatusAsync()).State);
        Assert.All(second, static report => Assert.Equal(0, report.Claimed));
    }

    [Fact]
    public void Contract_GivenUnknownOrRevokedTemplate_FallsBackToHumanApproval()
    {
        var directory = Path.Combine(Path.GetTempPath(), "factory-named-template-" + Guid.NewGuid().ToString("N"));
        var registry = new FactoryTemplateRegistry(directory);
        var template = JsonNode.Parse("""
            {
              "templateId": "ping-service",
              "templateVersion": 1,
              "schemaVersion": 1,
              "supportedClass": "aspnetcore-http-net10",
              "parameters": {},
              "scenarios": [ { "id": "AC-1", "method": "GET", "path": "/ping", "expect": { "status": 200 } } ]
            }
            """)!;

        var manifest = registry.Register(template);
        Assert.True(registry.Approve("ping-service", 1, manifest.ManifestDigest));
        registry.Revoke("ping-service", 1);

        var proposal = JsonNode.Parse("""
            {
              "templateId": "ping-service",
              "templateVersion": 1,
              "parameters": {},
              "scenarios": [ { "id": "AC-1", "method": "GET", "path": "/ping", "expect": { "status": 200 } } ]
            }
            """)!;

        var unknown = FactoryTemplatePolicy.Evaluate(registry, proposal);
        Assert.False(unknown.Approved);

        var revoked = FactoryTemplatePolicy.Evaluate(registry, proposal);
        Assert.False(revoked.Approved);
        Assert.StartsWith(FactoryTemplateRegistry.Revoked, revoked.Reason, StringComparison.Ordinal);

        var revokedManifest = JsonNode.Parse(template.ToJsonString())!;
        revokedManifest["templateId"] = "unknown-template";
        var neverRegistered = FactoryTemplatePolicy.Evaluate(registry, JsonNode.Parse(revokedManifest.ToJsonString())!);
        Assert.False(neverRegistered.Approved);
        Assert.StartsWith(FactoryTemplateRegistry.UnknownTemplate, neverRegistered.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_GivenPassingFlagWithoutExecutionProof_RefusesAcceptance()
    {
        var review = new FactoryReviewService().EvaluateWithEvidence(
            new ReviewRequirement("W-1", "req-1", ["AC-1"]),
            new ReviewBaseline("head-1", "base-1", "main", "policy", "guidelines"),
            new Dictionary<string, string>(),
            [
                Complete(ReviewCheckKind.Validation),
                Complete(ReviewCheckKind.RequirementCoverage),
                Complete(ReviewCheckKind.ArchitecturalConformance),
                Complete(ReviewCheckKind.Security) with { ArtifactDigest = string.Empty }
            ],
            findings: [],
            exclusions: [],
            riskClassification: "alpha-standard",
            reviewerProfileVersion: new AgentProfileVersion(1),
            reviewerSessionId: new SessionId("review-1"),
            requiredHeadSha: "head-1",
            requiredContractDigest: "sha256:contract");

        Assert.False(review.Accepted);
        Assert.StartsWith(FactoryReviewService.EvidenceIncompleteCategory + ":security", review.Reason, StringComparison.Ordinal);
    }

    private static ReviewCheckExecution Complete(ReviewCheckKind kind)
        => new(
            kind,
            kind.ToString(),
            "test-checker",
            "1.0.0",
            "head-1",
            "policy://test@1",
            ReviewCheckExecution.PassedOutcome,
            "sha256:contract",
            "sha256:artifact-" + kind,
            DateTimeOffset.UtcNow,
            "ok");

    [Fact]
    public async Task Lifecycle_GivenEarlyFailure_UsesLegalBlockedOrCancelledTransition()
    {
        var directory = Path.Combine(Path.GetTempPath(), "factory-named-early-" + Guid.NewGuid().ToString("N"));
        var store = new LocalFactoryStore(directory);
        var workItem = new WorkItemId("W-dependent");

        var snapshot = new WorkItemSnapshot(
            workItem,
            FactoryWorkState.RequirementsAligned,
            1,
            directory,
            [new WorkDependency(new WorkItemId("WORK-uncommitted"), DependencyKind.WorkItem, "missing-prerequisite")],
            DateTimeOffset.UtcNow);

        var trigger = new FactoryEventEnvelope(
            new FactoryEventId("E-dependent"),
            FactoryEventTypes.ContractApproved,
            "test",
            workItem.Value,
            DateTimeOffset.UtcNow,
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FactoryDispatchOptions.WorkItemDataKey] = workItem.Value
            });

        Assert.True((await store.CommitAsync(new WorkItemCommit(snapshot, 0, [trigger]))).Succeeded);

        var worker = new FactoryDispatchWorker(
            store, store, store, new FactoryStageDispatchHandler(),
            new FactoryDispatchOptions(new SubscriberId("test-early"), MaxAttemptsPerEvent: 3));

        var report = await worker.RunOnceAsync();

        Assert.Equal(1, report.Blocked);
        Assert.Equal(FactoryWorkState.Blocked, (await store.LoadAsync(workItem))!.State);
    }

    private const string CaptureContractJson = """
        {
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "scenarios": [
            { "id": "AC-1", "method": "POST", "path": "/books",
              "request": { "json": { "title": "Dune" } },
              "expect": { "status": 201, "jsonSubset": { "title": "Dune" } },
              "capture": { "bookId": "$.id" } },
            { "id": "AC-2", "method": "GET", "path": "/books/{bookId}", "expect": { "status": 200 } }
          ]
        }
        """;

    private sealed class CaptureServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly string _id;

        public CaptureServer(string id)
        {
            _id = id;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _ = Task.Run(LoopAsync);
        }

        public Uri BaseAddress { get; }

        public string? LastBookId { get; private set; }

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        try
                        {
                            var stream = client.GetStream();
                            var buffer = new byte[8192];
                            var read = await stream.ReadAsync(buffer, _cts.Token);
                            var request = Encoding.ASCII.GetString(buffer, 0, read);
                            var requestLine = request.Split("\r\n")[0].Split(' ');
                            var method = requestLine[0];
                            var path = requestLine.Length > 1 ? requestLine[1] : "/";

                            int status;
                            string body;
                            if (method == "POST" && path == "/books")
                            {
                                status = 201;
                                body = $$"""{"id":"{{_id}}","title":"Dune"}""";
                            }
                            else if (method == "GET" && path.StartsWith("/books/", StringComparison.Ordinal))
                            {
                                LastBookId = path["/books/".Length..];
                                status = LastBookId == _id ? 200 : 404;
                                body = status == 200 ? $$"""{"id":"{{_id}}"}""" : "{}";
                            }
                            else
                            {
                                status = 404;
                                body = "{}";
                            }

                            var response = $"HTTP/1.1 {status} {(status == 201 ? "Created" : status == 200 ? "OK" : "Not Found")}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
                            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), _cts.Token);
                        }
                        catch (Exception)
                        {
                        }
                    }
                });
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }

    [Fact]
    public async Task Contract_GivenSeparateApplicationRun_ResetsScenarioState()
    {
        using var first = new CaptureServer("a1");
        using var second = new CaptureServer("b1");
        var contract = AcceptanceContract.Parse(CaptureContractJson).Contract!;
        var runner = new AcceptanceContractRunner();

        var firstRun = await runner.RunAsync(contract, first.BaseAddress);
        var secondRun = await runner.RunAsync(contract, second.BaseAddress);

        Assert.True(firstRun.Passed, firstRun.FailureSummary);
        Assert.True(secondRun.Passed, secondRun.FailureSummary);
        Assert.Equal("a1", first.LastBookId);
        Assert.Equal("b1", second.LastBookId);
    }

    [Fact]
    public async Task Store_GivenDependencyRelease_BlockedItemWakesOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), "factory-named-wake-" + Guid.NewGuid().ToString("N"));
        var store = new LocalFactoryStore(directory);
        var prerequisite = new WorkItemId("WORK-prerequisite");
        var dependent = new WorkItemId("WORK-dependent");

        Assert.True((await store.CommitAsync(new WorkItemCommit(
            new WorkItemSnapshot(prerequisite, FactoryWorkState.Released, 1, directory, [], DateTimeOffset.UtcNow),
            0,
            []))).Succeeded);

        var trigger = new FactoryEventEnvelope(
            new FactoryEventId("E-wake"),
            FactoryEventTypes.ContractApproved,
            "test",
            dependent.Value,
            DateTimeOffset.UtcNow,
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FactoryDispatchOptions.WorkItemDataKey] = dependent.Value
            });

        Assert.True((await store.CommitAsync(new WorkItemCommit(
            new WorkItemSnapshot(
                dependent,
                FactoryWorkState.Blocked,
                1,
                directory,
                [new WorkDependency(prerequisite, DependencyKind.WorkItem, "released-prerequisite")],
                DateTimeOffset.UtcNow),
            0,
            [trigger]))).Succeeded);

        var worker = new FactoryDispatchWorker(
            store, store, store, new FactoryStageDispatchHandler(),
            new FactoryDispatchOptions(new SubscriberId("test-wake"), MaxAttemptsPerEvent: 3));

        var first = await worker.RunOnceAsync();
        var second = await worker.RunOnceAsync();

        Assert.Equal(1, first.Claimed);
        Assert.Equal(FactoryWorkState.ReadyForImplementation, (await store.LoadAsync(dependent))!.State);
        Assert.Equal(2, (await store.LoadAsync(dependent))!.Version);
        Assert.Equal(0, second.Claimed);
    }

    private sealed class FailingStageHandler : IStageDispatchHandler
    {
        public ValueTask<StageDispatchOutcome> HandleAsync(
            WorkItemSnapshot workItem,
            FactoryEventEnvelope trigger,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(StageDispatchOutcome.Failed("synthetic-stage-failure"));
    }

    [Fact]
    public async Task StageHandler_GivenExhaustedAttempts_QuarantinesWithPersistedCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), "factory-named-quarantine-" + Guid.NewGuid().ToString("N"));
        var store = new LocalFactoryStore(directory);
        var workItem = new WorkItemId("W-quarantine");
        var trigger = new FactoryEventEnvelope(
            new FactoryEventId("E-quarantine"),
            FactoryEventTypes.RequirementSubmitted,
            "test",
            workItem.Value,
            DateTimeOffset.UtcNow,
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FactoryDispatchOptions.WorkItemDataKey] = workItem.Value
            });

        var snapshot = new WorkItemSnapshot(
            workItem, FactoryWorkState.Proposed, 1, directory, [], DateTimeOffset.UtcNow);
        Assert.True((await store.CommitAsync(new WorkItemCommit(snapshot, 0, [trigger]))).Succeeded);

        var worker = new FactoryDispatchWorker(
            store, store, store, new FailingStageHandler(),
            new FactoryDispatchOptions(new SubscriberId("test-quarantine"), MaxAttemptsPerEvent: 2));

        var first = await worker.RunOnceAsync();
        var second = await worker.RunOnceAsync();

        Assert.Equal(1, first.Failed);
        Assert.Equal(1, second.Quarantined);

        var quarantined = Assert.Single(await store.ReadQuarantinedAsync());
        Assert.Equal(2, quarantined.Attempts);
        Assert.Contains("synthetic-stage-failure", quarantined.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_GivenSubmittedRequirement_RunsWithoutFactorySourceChange()
    {
        var root = Root(out _);
        var sourceRoot = Path.Combine(RepositoryRoot(), "WIP", "Wip.Factory.Host", "Factory");
        var before = Directory.EnumerateFiles(sourceRoot, "*.cs")
            .ToDictionary(static path => path, Hash);

        var runtime = new FactoryRuntime(root);
        Assert.True((await runtime.SubmitAsync(Requirement())).Accepted);
        await runtime.RunAsync(8);
        await runtime.RunAsync(8);

        var after = Directory.EnumerateFiles(sourceRoot, "*.cs")
            .ToDictionary(static path => path, Hash);

        Assert.Equal(before.Count, after.Count);
        Assert.All(before, pair => Assert.Equal(pair.Value, after[pair.Key]));
    }

    [Fact]
    public void Workflow_GivenRealFactoryHost_NeverInvokesDemoImplementation()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "WIP", "Wip.Factory.Host", "Program.cs"));

        var operatorGate = program.IndexOf("FactoryCli.ExecuteAsync", StringComparison.Ordinal);
        var demoGate = program.IndexOf("string.Equals(verb, \"demo\"", StringComparison.Ordinal);

        Assert.True(operatorGate >= 0, "the operator path must call FactoryCli");
        Assert.True(demoGate > operatorGate, "demos must be gated after/outside the operator verb list");
        Assert.Contains("verb is \"requirement\" or \"contract\" or \"run\" or \"status\" or \"approve\" or \"serve\" or \"template\" or \"acceptance\"", program, StringComparison.Ordinal);
    }

    private static string Hash(string path)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wip.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }
}
