using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wip.Factory.ControlApi.Services;
using Xunit;

namespace Wip.Factory.ControlApi.Tests;

/// <summary>
/// Integration evidence for the versioned adapter. These tests use a real Kestrel adapter, a real git target
/// repository, the real FactoryRuntime and the factory's durable stores. The full lifecycle test additionally runs
/// the deterministic engineering runner through build, review, merge, post-merge verification and supervised serve.
/// It proves engineering integration; it is not independent product acceptance.
/// </summary>
public sealed class AdapterLifecycleTests
{
    private const string TemplateJson = """
        {
          "templateId": "ping-service",
          "templateVersion": 1,
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "parameters": { "path": { "type": "string", "allowed": ["/ping", "/health"] } },
          "scenarios": [
            { "id": "AC-1", "method": "GET", "path": "${path}",
              "expect": { "status": 200, "jsonSubset": { "status": "ok" } } }
          ]
        }
        """;

    [Fact]
    public async Task Live_DoesNotRequireCredential_HealthDoes()
    {
        await using var harness = await AdapterHarness.StartAsync();
        using var live = await harness.Client.GetAsync("/v1/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        using var health = await harness.Client.GetAsync("/v1/health");
        Assert.Equal(HttpStatusCode.Unauthorized, health.StatusCode);
        using var body = await AdapterHarness.JsonAsync(health);
        Assert.Equal("missing-service-credential", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submit_IsIdempotentByKeyAndDeduplicatedByRequirementDigest()
    {
        await using var harness = await AdapterHarness.StartAsync();
        using var first = await harness.SubmitAsync(AdapterHarness.Requirement, AdapterHarness.Contract, "submit-0001");
        var firstId = first.RootElement.GetProperty("workItemId").GetString();
        var firstDigest = first.RootElement.GetProperty("requirementDigest").GetString();

        using var replayResponse = await harness.SubmitRequestAsync(AdapterHarness.Requirement, AdapterHarness.Contract, "submit-0001");
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.True(replayResponse.Headers.TryGetValues("Idempotency-Replayed", out var replay) && replay.Single() == "true");
        using var replayBody = await AdapterHarness.JsonAsync(replayResponse);
        Assert.Equal(firstId, replayBody.RootElement.GetProperty("workItemId").GetString());

        var body = new
        {
            requirement = AdapterHarness.Requirement,
            acceptanceContractJson = AdapterHarness.Contract,
            planning = new { priority = "normal", tags = new[] { "integration" }, links = Array.Empty<string>(), notes = "adapter integration" },
            explicitlyConfirmed = true
        };
        using var duplicateResponse = await harness.PostAsync("/v1/work-items", body, "submit-0002");
        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        using var duplicate = await AdapterHarness.JsonAsync(duplicateResponse);
        Assert.True(duplicate.RootElement.GetProperty("duplicate").GetBoolean());
        Assert.Equal(firstId, duplicate.RootElement.GetProperty("workItemId").GetString());
        Assert.Equal(firstDigest, duplicate.RootElement.GetProperty("requirementDigest").GetString());
    }

    [Fact]
    public async Task InvalidRequirement_IsRefusedWithNamedReasons()
    {
        await using var harness = await AdapterHarness.StartAsync();
        var response = await harness.PostAsync(
            "/v1/work-items",
            new { requirement = "This has no metadata and no expectations.", explicitlyConfirmed = true },
            "invalid-0001");
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        using var body = await AdapterHarness.JsonAsync(response);
        Assert.Equal("requirement-refused", body.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("requirement-missing-title", body.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task ReadOnlyViewer_CannotMutateFactoryState()
    {
        await using var harness = await AdapterHarness.StartAsync();
        var response = await harness.PostAsync(
            "/v1/work-items",
            new { requirement = AdapterHarness.Requirement, explicitlyConfirmed = true },
            "viewer-0001",
            role: "viewer",
            actor: AdapterHarness.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = await AdapterHarness.JsonAsync(response);
        Assert.Equal("read-only-role", body.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var list = await harness.GetAsync("/v1/work-items", role: "viewer", actor: AdapterHarness.Viewer);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task ContractApproval_BindsExactProposalDigest()
    {
        await using var harness = await AdapterHarness.StartAsync();
        using var submitted = await harness.SubmitAsync(AdapterHarness.Requirement, AdapterHarness.Contract);
        var workItemId = submitted.RootElement.GetProperty("workItemId").GetString()!;

        using var dispatch = await harness.DispatchAndWaitAsync("dispatch-0001");
        Assert.Equal("completed", dispatch.RootElement.GetProperty("state").GetString());

        using var contractResponse = await harness.GetAsync($"/v1/work-items/{workItemId}/contract");
        using var contract = await AdapterHarness.JsonAsync(contractResponse);
        var proposalDigest = contract.RootElement.GetProperty("proposalDigest").GetString()!;

        var wrong = await harness.PostAsync(
            $"/v1/work-items/{workItemId}/contract/approve",
            new { proposalDigest = "sha256:deadbeef", confirmed = true },
            "approve-wrong-0001");
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);

        var approved = await harness.PostAsync(
            $"/v1/work-items/{workItemId}/contract/approve",
            new { proposalDigest, confirmed = true },
            "approve-right-0001");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        using var detailResponse = await harness.GetAsync($"/v1/work-items/{workItemId}");
        using var detail = await AdapterHarness.JsonAsync(detailResponse);
        Assert.Equal(proposalDigest, detail.RootElement.GetProperty("contract").GetProperty("frozenDigest").GetString());
        Assert.True(detail.RootElement.GetProperty("contract").GetProperty("isFrozen").GetBoolean());

        var again = await harness.PostAsync(
            $"/v1/work-items/{workItemId}/contract/approve",
            new { proposalDigest, confirmed = true },
            "approve-again-0001");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task ContractApproval_RefusesNonExecutableProposal()
    {
        await using var harness = await AdapterHarness.StartAsync();
        using var submitted = await harness.SubmitAsync(AdapterHarness.Requirement, string.Empty, "submit-no-contract-0001");
        var workItemId = submitted.RootElement.GetProperty("workItemId").GetString()!;

        using var dispatch = await harness.DispatchAndWaitAsync("dispatch-no-contract-0001");
        Assert.Equal("completed", dispatch.RootElement.GetProperty("state").GetString());

        using var contractResponse = await harness.GetAsync($"/v1/work-items/{workItemId}/contract");
        using var contract = await AdapterHarness.JsonAsync(contractResponse);
        Assert.False(contract.RootElement.GetProperty("executable").GetBoolean());
        var proposalDigest = contract.RootElement.GetProperty("proposalDigest").GetString()!;

        var response = await harness.PostAsync(
            $"/v1/work-items/{workItemId}/contract/approve",
            new { proposalDigest, confirmed = true },
            "approve-no-contract-0001");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = await AdapterHarness.JsonAsync(response);
        Assert.Equal("contract-not-executable", body.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var detailResponse = await harness.GetAsync($"/v1/work-items/{workItemId}");
        using var detail = await AdapterHarness.JsonAsync(detailResponse);
        Assert.False(detail.RootElement.GetProperty("contract").GetProperty("isFrozen").GetBoolean());
    }
    [Fact]
    public async Task MergeApproval_CannotBypassReviewEvidence()
    {
        await using var harness = await AdapterHarness.StartAsync();
        using var submitted = await harness.SubmitAsync(AdapterHarness.Requirement, AdapterHarness.Contract);
        var workItemId = submitted.RootElement.GetProperty("workItemId").GetString()!;
        var response = await harness.PostAsync(
            $"/v1/work-items/{workItemId}/merge-approvals",
            new { reviewDigest = "sha256:nothing", confirmation = "sha256:nothing" },
            "merge-bypass-0001");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = await AdapterHarness.JsonAsync(response);
        Assert.Contains("no-review-to-approve", body.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Dispatch_IsExclusiveAndRecordsExactlyOneJob()
    {
        await using var harness = await AdapterHarness.StartAsync();
        await harness.SubmitAsync(AdapterHarness.Requirement, AdapterHarness.Contract, "dispatch-exclusive-submit");

        var first = harness.PostAsync("/v1/dispatch", new { maxPasses = 4 }, "dispatch-exclusive-1");
        var second = harness.PostAsync("/v1/dispatch", new { maxPasses = 4 }, "dispatch-exclusive-2");
        var responses = await Task.WhenAll(first, second);
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Accepted);
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        using var status = await harness.GetAsync("/v1/dispatch");
        using var statusBody = await AdapterHarness.JsonAsync(status);
        Assert.True(statusBody.RootElement.TryGetProperty("recent", out _));
    }

    [Fact]
    public async Task Templates_RequireAdministratorAndExactDigest()
    {
        await using var harness = await AdapterHarness.StartAsync();
        var manifest = TemplateJson.Replace("\n", " ", StringComparison.Ordinal);
        var operatorAttempt = await harness.PostAsync(
            "/v1/templates",
            new { manifestJson = manifest },
            "template-register-operator",
            role: "operator");
        Assert.Equal(HttpStatusCode.Forbidden, operatorAttempt.StatusCode);

        using var registeredResponse = await harness.PostAsync(
            "/v1/templates",
            new { manifestJson = manifest },
            "template-register-admin",
            role: "admin",
            actor: AdapterHarness.Administrator);
        Assert.Equal(HttpStatusCode.Created, registeredResponse.StatusCode);
        using var registered = await AdapterHarness.JsonAsync(registeredResponse);
        var digest = registered.RootElement.GetProperty("manifestDigest").GetString()!;
        Assert.False(registered.RootElement.GetProperty("approved").GetBoolean());

        var wrong = await harness.PostAsync(
            "/v1/templates/ping-service/1/approve",
            new { manifestDigest = "sha256:wrong", confirmed = true },
            "template-approve-wrong",
            role: "admin",
            actor: AdapterHarness.Administrator);
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);

        using var approvedResponse = await harness.PostAsync(
            "/v1/templates/ping-service/1/approve",
            new { manifestDigest = digest, confirmed = true },
            "template-approve-right",
            role: "admin",
            actor: AdapterHarness.Administrator);
        Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
        using var approved = await AdapterHarness.JsonAsync(approvedResponse);
        Assert.True(approved.RootElement.GetProperty("approved").GetBoolean());
    }

    [Fact]
    public async Task EventStream_ReplaysDurableCursorAndEmitsHeartbeatShape()
    {
        await using var harness = await AdapterHarness.StartAsync();
        using var submitted = await harness.SubmitAsync(AdapterHarness.Requirement, AdapterHarness.Contract);
        var workItemId = submitted.RootElement.GetProperty("workItemId").GetString()!;

        using var streamResponse = await harness.GetStreamAsync("/v1/events/stream?after=-1");
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);

        await using var stream = await streamResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var events = new List<string>();
        while (!timeout.IsCancellationRequested && events.Count < 3)
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                break;
            }
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                events.Add(line);
            }
        }

        Assert.Contains(events, value => value.Contains("connected"));
        Assert.Contains(events, value => value.Contains("factory"));

        var pageResponse = await harness.GetAsync("/v1/events?after=-1&limit=10");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        using var page = await AdapterHarness.JsonAsync(pageResponse);
        Assert.True(page.RootElement.GetProperty("items").GetArrayLength() >= 1);
        Assert.Contains(workItemId, page.RootElement.GetProperty("items")[0].GetProperty("workItemId").GetString());
    }

    [Fact]
    public async Task FullLifecycle_ReachesVerifiedReleaseAndSupervisedServe()
    {
        await using var harness = await AdapterHarness.StartAsync(ImplementationMode.DeterministicDemo, executionEnabled: true);
        using var submitted = await harness.SubmitAsync(AdapterHarness.Requirement, AdapterHarness.Contract, "lifecycle-submit");
        var workItemId = submitted.RootElement.GetProperty("workItemId").GetString()!;

        using var firstCycle = await harness.DispatchAndWaitAsync("lifecycle-dispatch-1");
        Assert.Equal("completed", firstCycle.RootElement.GetProperty("state").GetString());

        using var contractResponse = await harness.GetAsync($"/v1/work-items/{workItemId}/contract");
        using var contract = await AdapterHarness.JsonAsync(contractResponse);
        var proposalDigest = contract.RootElement.GetProperty("proposalDigest").GetString()!;
        using var approved = await harness.PostAsync(
            $"/v1/work-items/{workItemId}/contract/approve",
            new { proposalDigest, confirmed = true },
            "lifecycle-approve-contract");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        using var secondCycle = await harness.DispatchAndWaitAsync("lifecycle-dispatch-2", timeoutSeconds: 180);
        Assert.Equal("completed", secondCycle.RootElement.GetProperty("state").GetString());

        using var readyResponse = await harness.GetAsync($"/v1/work-items/{workItemId}");
        using var ready = await AdapterHarness.JsonAsync(readyResponse);
        Assert.Equal("ReadyForDelivery", ready.RootElement.GetProperty("summary").GetProperty("state").GetString());
        var reviewDigest = ready.RootElement.GetProperty("reviews")[0].GetProperty("reviewDigest").GetString()!;

        using var mergeApproved = await harness.PostAsync(
            $"/v1/work-items/{workItemId}/merge-approvals",
            new { reviewDigest, confirmation = reviewDigest },
            "lifecycle-approve-merge");
        Assert.Equal(HttpStatusCode.OK, mergeApproved.StatusCode);

        using var thirdCycle = await harness.DispatchAndWaitAsync("lifecycle-dispatch-3", timeoutSeconds: 180);
        Assert.Equal("completed", thirdCycle.RootElement.GetProperty("state").GetString());

        using var releasedResponse = await harness.GetAsync($"/v1/work-items/{workItemId}");
        using var released = await AdapterHarness.JsonAsync(releasedResponse);
        Assert.Equal("Released", released.RootElement.GetProperty("summary").GetProperty("state").GetString());
        var release = released.RootElement.GetProperty("release");
        Assert.True(release.GetProperty("releaseRecorded").GetBoolean());
        Assert.True(release.GetProperty("verification").GetProperty("verified").GetBoolean());

        using var serveStart = await harness.PostAsync(
            $"/v1/work-items/{workItemId}/serve",
            new { durationSeconds = 25 },
            "lifecycle-serve-start");
        Assert.Equal(HttpStatusCode.Accepted, serveStart.StatusCode);

        string? url = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (DateTimeOffset.UtcNow < deadline && url is null)
        {
            await Task.Delay(500);
            using var serveResponse = await harness.GetAsync($"/v1/work-items/{workItemId}/serve");
            using var serve = await AdapterHarness.JsonAsync(serveResponse);
            var serving = serve.RootElement.GetProperty("serving");
            if (serving.GetProperty("active").GetBoolean())
            {
                if (serving.TryGetProperty("url", out var candidate))
                {
                    url = candidate.GetString();
                }
            }
        }

        Assert.NotNull(url);
        using var health = await harness.Client.GetAsync(url!.TrimEnd('/') + "/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var healthBody = await AdapterHarness.JsonAsync(health);
        Assert.Equal("ok", healthBody.RootElement.GetProperty("status").GetString());

        var observedDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < observedDeadline)
        {
            using var observedResponse = await harness.GetAsync($"/v1/work-items/{workItemId}");
            using var observed = await AdapterHarness.JsonAsync(observedResponse);
            if (observed.RootElement.GetProperty("summary").GetProperty("state").GetString() == "OperationallyValidated")
            {
                break;
            }

            await Task.Delay(250);
        }

        using var stop = await harness.DeleteAsync($"/v1/work-items/{workItemId}/serve", "lifecycle-serve-stop");
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);

        string? finalState = null;
        var finalDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < finalDeadline)
        {
            using var finalResponse = await harness.GetAsync($"/v1/work-items/{workItemId}");
            using var final = await AdapterHarness.JsonAsync(finalResponse);
            finalState = final.RootElement.GetProperty("summary").GetProperty("state").GetString();
            if (finalState == "OperationallyValidated")
            {
                break;
            }

            await Task.Delay(250);
        }

        Assert.Equal("OperationallyValidated", finalState);
    }
}
