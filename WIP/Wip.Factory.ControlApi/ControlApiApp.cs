using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Wip.Factory.Contracts.Work;
using Wip.Factory.ControlApi.Contracts;
using Wip.Factory.ControlApi.Infrastructure;
using Wip.Factory.ControlApi.Services;
using Wip.Factory.Host.Factory;

namespace Wip.Factory.ControlApi;

/// <summary>Composition root for the versioned HTTP adapter. All routes are under /v1 and authenticated.</summary>
public static class ControlApiApp
{
    public static WebApplication Build(ControlApiOptions options)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(console =>
        {
            console.SingleLine = false;
            console.TimestampFormat = "HH:mm:ss ";
        });
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        var bind = options.BindAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || options.BindAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? options.BindAddress
                : "http://" + options.BindAddress;
        builder.WebHost.UseUrls(bind);

        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.DictionaryKeyPolicy = null;
            json.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new ApiAuditStore(options.AuditDirectory));
        builder.Services.AddSingleton<FactoryService>();
        builder.Services.AddSingleton<MutationRunner>();

        var app = builder.Build();
        app.UseAdapterErrors();
        app.UseAdapterSecurity();
        app.MapControlApi();
        return app;
    }

    private static void MapControlApi(this WebApplication app)
    {
        app.MapGet("/v1/live", () => Results.Json(new { status = "ok" }, ApiJson.Options));

        app.MapGet("/v1/meta", (FactoryService service) => Json(service.ReadModel.GetMeta()));

        app.MapGet("/v1/health", async (FactoryService service, CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetHealthAsync(cancellationToken)));

        app.MapGet("/v1/factory", (FactoryService service) => Json(service.ReadModel.GetFactory()));

        app.MapGet("/v1/settings", (FactoryService service) => Json(service.ReadModel.GetSettings()));

        app.MapGet("/v1/work-items", async (
            FactoryService service,
            string? state,
            string? search,
            string? sort,
            string? indicator,
            int? limit,
            int? offset,
            CancellationToken cancellationToken) =>
        {
            var query = new ReadModels.WorkQuery(
                state,
                search,
                sort,
                Math.Clamp(limit ?? 50, 1, 500),
                Math.Max(offset ?? 0, 0),
                indicator);
            return Json(await service.ReadModel.GetWorkItemsAsync(query, cancellationToken));
        });

        app.MapPost("/v1/requirement/preview", async (
            HttpContext context,
            PreviewRequestDto request,
            FactoryService service,
            CancellationToken cancellationToken) =>
        {
            ApiIdentity.RequireCaller(context);
            return Json(Preview(request));
        });

        app.MapPost("/v1/work-items", async (
            HttpContext context,
            SubmitRequestDto request,
            FactoryService service,
            MutationRunner mutations,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var caller = ApiIdentity.RequireCaller(context);
            if (request.ExplicitlyConfirmed != true)
            {
                throw ApiException.BadRequest(
                    "confirmation-required",
                    "Submitting a requirement to the factory requires explicit confirmation that this is the exact requirement document to submit.");
            }

            var (document, _) = RequirementDocument.Parse(request.Requirement, request.Title);
            var duplicate = false;
            if (document is not null)
            {
                var snapshot = await service.ReadModel.LoadAsync(cancellationToken).ConfigureAwait(false);
                duplicate = snapshot.Statuses.Any(status =>
                    string.Equals(status.RequirementDigest, document.Digest, StringComparison.Ordinal));
            }

            return await mutations.RunAsync(
                context,
                caller,
                "work.submit",
                document?.Digest ?? "requirement-unparsed",
                request,
                async token =>
                {
                    var result = await service.Runtime
                        .SubmitAsync(request.Requirement, request.Title, request.AcceptanceContractJson, token)
                        .ConfigureAwait(false);
                    if (!result.Accepted)
                    {
                        throw new ApiException(
                            422,
                            "requirement-refused",
                            "The factory refused the requirement: " + string.Join("; ", result.Errors),
                            "validation");
                    }

                    var planning = request.Planning is null
                        ? PlanningStore.Empty
                        : await service.Planner
                            .SetAsync(result.WorkItemId!, request.Planning, caller.Actor, token)
                            .ConfigureAwait(false);
                    if (request.Planning is null)
                    {
                        planning = await service.Planner.GetAsync(result.WorkItemId!, token).ConfigureAwait(false);
                    }

                    var body = new SubmitResponseDto(
                        true,
                        result.WorkItemId!,
                        result.State!,
                        result.RequirementDigest!,
                        duplicate,
                        planning,
                        "dispatch");
                    return new MutationOutcome(
                        duplicate ? 200 : 201,
                        body,
                        "Submitted requirement " + result.RequirementDigest,
                        null,
                        1);
                },
                cancellationToken).ConfigureAwait(false);
        });

        app.MapGet("/v1/work-items/{workItemId}", async (
            string workItemId,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetDetailAsync(workItemId, cancellationToken)));

        app.MapGet("/v1/work-items/{workItemId}/events", async (
            string workItemId,
            long? after,
            int? limit,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetEventsAsync(
                workItemId,
                after ?? -1,
                Math.Clamp(limit ?? 200, 1, 2000),
                cancellationToken)));

        app.MapGet("/v1/work-items/{workItemId}/contract", async (
            string workItemId,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetContractAsync(workItemId, cancellationToken)));

        app.MapGet("/v1/work-items/{workItemId}/sessions", async (
            string workItemId,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetWorkSessionsAsync(workItemId, cancellationToken)));

        app.MapGet("/v1/work-items/{workItemId}/lineage", async (
            string workItemId,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetLineageAsync(workItemId, cancellationToken)));

        app.MapGet("/v1/work-items/{workItemId}/diff", async (
            string workItemId,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetDiffAsync(workItemId, cancellationToken)));

        app.MapGet("/v1/work-items/{workItemId}/files/{**path}", async (
            string workItemId,
            string path,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetFileAsync(workItemId, path, cancellationToken)));

        app.MapPost("/v1/work-items/{workItemId}/planning", async (
            HttpContext context,
            string workItemId,
            PlanningUpdateRequestDto request,
            FactoryService service,
            MutationRunner mutations,
            CancellationToken cancellationToken) =>
        {
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "work.planning.update", workItemId, request, async token =>
            {
                var summary = await service.ReadModel.GetSummaryAsync(workItemId, token).ConfigureAwait(false)
                              ?? throw ApiException.NotFound("work-item-not-found", "No work item with that identifier exists.");
                if (request.ExpectedVersion is { } expected && expected != summary.Version)
                {
                    throw ApiException.Conflict(
                        "version-conflict",
                        "The work item moved from the version this planning edit was based on. Refresh before editing planning metadata.");
                }

                var planning = await service.Planner.SetAsync(
                    workItemId,
                    new PlanningInputDto(request.Priority, request.DeadlineUtc, request.Tags, request.Links, request.Notes),
                    caller.Actor,
                    token).ConfigureAwait(false);
                return new MutationOutcome(200, planning, "Planning metadata updated (not factory authority).", summary.Version, summary.Version);
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapPost("/v1/work-items/{workItemId}/contract/approve", async (
            HttpContext context,
            string workItemId,
            ContractApproveRequestDto request,
            FactoryService service,
            MutationRunner mutations,
            CancellationToken cancellationToken) =>
        {
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "contract.approve", workItemId, request, async token =>
            {
                if (!request.Confirmed)
                {
                    throw ApiException.BadRequest("confirmation-required", "Contract approval requires explicit confirmation of the exact proposal digest.");
                }

                var current = await service.ReadModel.GetContractAsync(workItemId, token).ConfigureAwait(false);
                if (!current.Executable)
                {
                    throw ApiException.Conflict(
                        "contract-not-executable",
                        "The proposal has no executable HTTP contract. Approving it would freeze an oracle the review checks cannot run. Revise the requirement with an explicit acceptance contract instead.");
                }

                var (ok, error) = await service.Runtime
                    .ContractApproveAsync(workItemId, request.ProposalDigest, token)
                    .ConfigureAwait(false);
                if (!ok)
                {
                    throw Conflict(error, "The factory refused contract approval.");
                }

                return new MutationOutcome(200, new MutationResultDto(
                    true, "contract.approve", workItemId,
                    "Contract frozen at " + request.ProposalDigest,
                    null, null, null, null, DateTimeOffset.UtcNow));
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapPost("/v1/work-items/{workItemId}/contract/reject", async (
            HttpContext context,
            string workItemId,
            ContractRejectRequestDto request,
            FactoryService service,
            MutationRunner mutations,
            CancellationToken cancellationToken) =>
        {
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "contract.reject", workItemId, request, async token =>
            {
                var (ok, error) = await service.Runtime
                    .ContractRejectAsync(workItemId, request.ProposalDigest, request.Reason, token)
                    .ConfigureAwait(false);
                if (!ok)
                {
                    throw Conflict(error, "The factory refused contract rejection.");
                }

                return new MutationOutcome(200, new MutationResultDto(
                    true, "contract.reject", workItemId,
                    "Contract proposal rejected: " + (request.Reason ?? "operator-rejected"),
                    null, null, null, null, DateTimeOffset.UtcNow));
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapPost("/v1/work-items/{workItemId}/contract/approve-policy", async (
            HttpContext context,
            string workItemId,
            FactoryService service,
            MutationRunner mutations,
            CancellationToken cancellationToken) =>
        {
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "contract.approve-policy", workItemId, new { }, async token =>
            {
                var (ok, error, decision) = await service.Runtime
                    .ContractPolicyApproveAsync(workItemId, token)
                    .ConfigureAwait(false);
                if (!ok)
                {
                    throw Conflict(error, "The factory refused policy approval.");
                }

                return new MutationOutcome(200, new MutationResultDto(
                    true, "contract.approve-policy", workItemId,
                    "Contract policy-approved at " + decision?.ContractDigest,
                    null, null, null, null, DateTimeOffset.UtcNow));
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapPost("/v1/work-items/{workItemId}/merge-approvals", async (
            HttpContext context,
            string workItemId,
            MergeApprovalRequestDto request,
            FactoryService service,
            MutationRunner mutations,
            CancellationToken cancellationToken) =>
        {
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "merge.approve", workItemId, request, async token =>
            {
                if (!string.Equals(request.Confirmation.Trim(), request.ReviewDigest.Trim(), StringComparison.Ordinal))
                {
                    throw ApiException.BadRequest(
                        "confirmation-mismatch",
                        "Merge authorization must confirm the exact review digest. Type the digest to acknowledge exactly what is being authorized.");
                }

                var (ok, error) = await service.Runtime
                    .ApproveMergeAsync(workItemId, request.ReviewDigest, token)
                    .ConfigureAwait(false);
                if (!ok)
                {
                    throw Conflict(error, "The factory refused merge authorization.");
                }

                return new MutationOutcome(200, new MutationResultDto(
                    true, "merge.approve", workItemId,
                    "Merge authorized for review " + request.ReviewDigest,
                    null, null, null, null, DateTimeOffset.UtcNow));
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapPost("/v1/dispatch", async (
            HttpContext context,
            DispatchRequestDto? request,
            FactoryService service,
            MutationRunner mutations,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "factory.dispatch", "factory-instance", request ?? new DispatchRequestDto(null), async token =>
            {
                var job = await service.Dispatch
                    .StartAsync(request?.MaxPasses ?? 16, caller.Actor, loggerFactory.CreateLogger("Dispatch"))
                    .ConfigureAwait(false);
                return new MutationOutcome(202, new MutationResultDto(
                    true, "factory.dispatch", "factory-instance",
                    "Dispatch cycle accepted as " + job.JobId,
                    null, null, job, null, DateTimeOffset.UtcNow));
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapGet("/v1/dispatch", async (FactoryService service) =>
            Json(await service.Dispatch.StatusAsync()));

        app.MapGet("/v1/jobs/{jobId}", async (string jobId, FactoryService service) =>
        {
            var job = await service.Dispatch.GetAsync(jobId);
            return job is null
                ? throw ApiException.NotFound("job-not-found", "No adapter job with that identifier is known in this process.")
                : Json(job);
        });

        app.MapGet("/v1/work-items/{workItemId}/serve", async (
            string workItemId,
            FactoryService service,
            CancellationToken cancellationToken) =>
        {
            var summary = await service.ReadModel.GetSummaryAsync(workItemId, cancellationToken).ConfigureAwait(false)
                          ?? throw ApiException.NotFound("work-item-not-found", "No work item with that identifier exists.");
            var serving = service.Serving.GetServing(workItemId, string.Empty);
            return Json(new ServeStatusDto(service.Serving.GetJob(workItemId), serving));
        });

        app.MapPost("/v1/work-items/{workItemId}/serve", async (
            HttpContext context,
            string workItemId,
            ServeRequestDto request,
            FactoryService service,
            MutationRunner mutations,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "release.serve.start", workItemId, request, async token =>
            {
                var job = await service.Serving
                    .StartAsync(workItemId, request.DurationSeconds, caller.Actor, loggerFactory.CreateLogger("Serve"))
                    .ConfigureAwait(false);
                return new MutationOutcome(202, new MutationResultDto(
                    true, "release.serve.start", workItemId,
                    "Supervised serve window accepted as " + job.JobId,
                    null, null, null, job, DateTimeOffset.UtcNow));
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapDelete("/v1/work-items/{workItemId}/serve", async (
            HttpContext context,
            string workItemId,
            FactoryService service,
            MutationRunner mutations,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "release.serve.stop", workItemId, new { }, async token =>
            {
                var job = await service.Serving
                    .StopAsync(workItemId, caller.Actor, loggerFactory.CreateLogger("Serve"))
                    .ConfigureAwait(false);
                return new MutationOutcome(200, new MutationResultDto(
                    true, "release.serve.stop", workItemId,
                    "Serve cancellation requested; final process state is recorded by the supervisor.",
                    null, null, null, job, DateTimeOffset.UtcNow));
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapGet("/v1/approvals", async (FactoryService service, CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetApprovalsAsync(cancellationToken)));

        app.MapGet("/v1/reviews", async (FactoryService service, CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetReviewsAsync(cancellationToken)));

        app.MapGet("/v1/releases", async (FactoryService service, CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetReleasesAsync(cancellationToken)));

        app.MapGet("/v1/releases/{workItemId}", async (
            string workItemId,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetReleaseAsync(workItemId, cancellationToken)));

        app.MapGet("/v1/templates", (FactoryService service) =>
            Json(service.ReadModel.GetTemplates()));

        app.MapPost("/v1/templates", async (
            HttpContext context,
            TemplateRegisterRequestDto request,
            FactoryService service,
            MutationRunner mutations,
            CancellationToken cancellationToken) =>
        {
            ApiIdentity.RequireAdmin(context);
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "template.register", "template", request, async token =>
            {
                var template = await service.ReadModel.RegisterTemplateAsync(request.ManifestJson, token).ConfigureAwait(false);
                return new MutationOutcome(201, template, "Template registered; it is not approved until an operator approves its digest.");
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapPost("/v1/templates/{templateId}/{version:int}/approve", async (
            HttpContext context,
            string templateId,
            int version,
            TemplateApproveRequestDto request,
            FactoryService service,
            MutationRunner mutations,
            CancellationToken cancellationToken) =>
        {
            ApiIdentity.RequireAdmin(context);
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "template.approve", templateId + "@" + version, request, async token =>
            {
                var template = await service.ReadModel
                    .ApproveTemplateAsync(templateId, version, request.ManifestDigest, request.Confirmed, token)
                    .ConfigureAwait(false);
                return new MutationOutcome(200, template, "Template version approved at " + request.ManifestDigest);
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapPost("/v1/templates/{templateId}/{version:int}/revoke", async (
            HttpContext context,
            string templateId,
            int version,
            FactoryService service,
            MutationRunner mutations,
            CancellationToken cancellationToken) =>
        {
            ApiIdentity.RequireAdmin(context);
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "template.revoke", templateId + "@" + version, new { }, async token =>
            {
                var template = await service.ReadModel.RevokeTemplateAsync(templateId, version, token).ConfigureAwait(false);
                return new MutationOutcome(200, template, "Template version revoked.");
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapGet("/v1/agents", async (FactoryService service, CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetAgentsAsync(cancellationToken)));

        app.MapGet("/v1/agents/{definitionId}", async (
            string definitionId,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetAgentAsync(definitionId, cancellationToken)));

        app.MapGet("/v1/agents/{definitionId}/profiles", async (
            string definitionId,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetProfilesForDefinitionAsync(definitionId, cancellationToken)));

        app.MapPost("/v1/agents/{definitionId}/profiles", async (
            HttpContext context,
            string definitionId,
            ProfileDraftInputDto request,
            FactoryService service,
            MutationRunner mutations,
            CancellationToken cancellationToken) =>
        {
            ApiIdentity.RequireAdmin(context);
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "agent.profile.publish", definitionId, request, async token =>
            {
                var profile = await service.ReadModel.CreateProfileAsync(definitionId, request, token).ConfigureAwait(false);
                return new MutationOutcome(201, profile, "Published immutable profile version " + profile.Version + ".");
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapPost("/v1/agents/{definitionId}/profiles/{version:int}/promote", async (
            HttpContext context,
            string definitionId,
            int version,
            ProfilePromoteRequestDto request,
            FactoryService service,
            MutationRunner mutations,
            CancellationToken cancellationToken) =>
        {
            ApiIdentity.RequireAdmin(context);
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "agent.profile.promote", definitionId + "@" + version, request, async token =>
            {
                var profile = await service.ReadModel.PromoteProfileAsync(definitionId, version, request.Confirmed, token).ConfigureAwait(false);
                return new MutationOutcome(200, profile, "Active pointer moved to version " + version + " for future sessions.");
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapPost("/v1/agents/{definitionId}/rollback", async (
            HttpContext context,
            string definitionId,
            FactoryService service,
            MutationRunner mutations,
            CancellationToken cancellationToken) =>
        {
            ApiIdentity.RequireAdmin(context);
            var caller = ApiIdentity.RequireCaller(context);
            return await mutations.RunAsync(context, caller, "agent.profile.rollback", definitionId, new { }, async token =>
            {
                var profile = await service.ReadModel.RollBackProfileAsync(definitionId, token).ConfigureAwait(false);
                return new MutationOutcome(200, profile, "Active pointer restored to the previous version.");
            }, cancellationToken).ConfigureAwait(false);
        });

        app.MapGet("/v1/analytics", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetAnalyticsAsync(from, to, cancellationToken)));

        app.MapGet("/v1/analytics/definitions", async (FactoryService service, CancellationToken cancellationToken) =>
        {
            var analytics = await service.ReadModel
                .GetAnalyticsAsync(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
            return Json(analytics.Definitions);
        });

        app.MapGet("/v1/pipeline", async (
            DateTimeOffset? at,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetPipelineAsync(at, cancellationToken)));

        app.MapGet("/v1/audit", async (
            int? limit,
            int? offset,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.Audit.ReadAsync(
                Math.Clamp(limit ?? 50, 1, 500),
                Math.Max(offset ?? 0, 0),
                cancellationToken)));

        app.MapGet("/v1/events", async (
            long? after,
            int? limit,
            FactoryService service,
            CancellationToken cancellationToken) =>
            Json(await service.ReadModel.GetEventStreamPageAsync(
                after ?? -1,
                Math.Clamp(limit ?? 200, 1, 1000),
                cancellationToken)));

        app.MapGet("/v1/events/stream", EventStreamAsync);
    }

    private static PreviewDto Preview(PreviewRequestDto request)
    {
        var errors = new List<string>();
        var (document, parseErrors) = RequirementDocument.Parse(request.Requirement, request.Title);
        errors.AddRange(parseErrors);

        AcceptanceContract? contract = null;
        if (!string.IsNullOrWhiteSpace(request.AcceptanceContractJson))
        {
            var (parsed, contractErrors) = AcceptanceContract.Parse(request.AcceptanceContractJson);
            contract = parsed;
            errors.AddRange(contractErrors);
        }

        return new PreviewDto(
            document is not null && errors.Count == 0,
            errors,
            document?.Title,
            document?.SupportedClassId,
            document?.Expectations ?? [],
            document?.Digest,
            contract?.Digest,
            document?.RawText,
            contract?.Scenarios.Select((scenario, index) => new ContractScenarioDto(
                "AC-" + (index + 1).ToString(CultureInfo.InvariantCulture),
                scenario.Method,
                scenario.Path,
                "Expected HTTP " + scenario.ExpectedStatus.ToString(CultureInfo.InvariantCulture) + " at " + scenario.Path)).ToArray() ?? []);
    }

    private static async Task EventStreamAsync(
        HttpContext context,
        long? after,
        FactoryService service,
        CancellationToken cancellationToken)
    {
        ApiIdentity.RequireCaller(context);
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache, no-transform";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        var cursor = after ?? -1;
        if (context.Request.Headers.TryGetValue("Last-Event-ID", out var lastEventId)
            && long.TryParse(lastEventId.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var last))
        {
            cursor = Math.Max(cursor, last);
        }

        try
        {
            var watermark = await service.ReadModel.GetEventWatermarkAsync(cancellationToken).ConfigureAwait(false);
            if (cursor > watermark.LatestSequence)
            {
                var resync = new StreamResyncDto("cursor-ahead", watermark.LatestSequence, DateTimeOffset.UtcNow);
                await WriteEventAsync(context, "resync", null, resync, cancellationToken).ConfigureAwait(false);
                cursor = watermark.LatestSequence;
            }

            await context.Response.WriteAsync("retry: 2000\n\n", cancellationToken).ConfigureAwait(false);
            await WriteEventAsync(
                context,
                "connected",
                null,
                new HeartbeatDto(DateTimeOffset.UtcNow, watermark.LatestSequence, true),
                cancellationToken).ConfigureAwait(false);

            var lastWrite = DateTimeOffset.UtcNow;
            while (!cancellationToken.IsCancellationRequested)
            {
                var page = await service.ReadModel
                    .GetEventStreamPageAsync(cursor, 200, cancellationToken)
                    .ConfigureAwait(false);

                if (page.Items.Count > 0)
                {
                    foreach (var envelope in page.Items)
                    {
                        await WriteEventAsync(context, "factory", envelope.Sequence, envelope, cancellationToken).ConfigureAwait(false);
                        cursor = envelope.Sequence;
                    }

                    lastWrite = DateTimeOffset.UtcNow;
                    continue;
                }

                if (DateTimeOffset.UtcNow - lastWrite > TimeSpan.FromSeconds(10))
                {
                    var health = await service.ReadModel.GetHealthAsync(cancellationToken).ConfigureAwait(false);
                    await WriteEventAsync(
                        context,
                        "heartbeat",
                        null,
                        new HeartbeatDto(DateTimeOffset.UtcNow, cursor, health.Healthy),
                        cancellationToken).ConfigureAwait(false);
                    lastWrite = DateTimeOffset.UtcNow;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            // The client disconnected while the response was being written.
        }
    }

    private static async Task WriteEventAsync(
        HttpContext context,
        string eventName,
        long? id,
        object data,
        CancellationToken cancellationToken)
    {
        var builder = new System.Text.StringBuilder();
        if (id is not null)
        {
            builder.Append("id: ").Append(id.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        builder.Append("event: ").Append(eventName).Append('\n');
        builder.Append("data: ").Append(JsonSerializer.Serialize(data, ApiJson.Options)).Append("\n\n");
        await context.Response.WriteAsync(builder.ToString(), cancellationToken).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ApiException Conflict(string? error, string message)
        => new(409, string.IsNullOrWhiteSpace(error) ? "factory-refused" : error, message + " (" + (error ?? "unnamed") + ")", "conflict");

    private static IResult Json(object value, int statusCode = 200)
        => Results.Json(value, ApiJson.Options, statusCode: statusCode);
}

/// <summary>Entry point used by both the executable and the HTTP integration tests.</summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var (options, errors) = ControlApiOptions.FromEnvironment(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
            }

            return 64;
        }

        var app = ControlApiApp.Build(options);
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogInformation(
            "Wip.Factory control adapter {Version} starting: instance={Instance} repo={Repository} root={Root} runner={Runner} execution={Execution} bind={Bind}",
            options.AdapterVersion,
            options.InstanceId,
            options.Factory.TargetRepository,
            options.Factory.Root,
            options.ImplementationRunnerName,
            options.ExecutionEnabled,
            options.BindAddress);

        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }
}
