using System.Globalization;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;

namespace Wip.Tholdem.Web.Training;

/// <summary>
/// The mini-RSI HTTP surface. The run only *finds* an eligible proposer; the active-proposer pointer
/// stays untouched until an explicit, re-verified promotion. A meta promotion never changes
/// <c>active-champion.json</c>.
/// </summary>
public static class RsiEndpoints
{
    public static void MapRsiRoutes(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(
            "/api/rsi",
            (RsiJobRequest? request, RsiJobRegistry registry, TholdemViewerOptions options) =>
            {
                var validated = RsiJobValidator.Validate(request, options.DataDirectory, out var error);
                if (validated is null)
                {
                    return Results.Json(error, TholdemWebHost.JsonOptions, statusCode: 400);
                }

                var acceptedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                var submission = registry.Submit(validated, acceptedAtUtc);
                if (!submission.Accepted)
                {
                    return Results.Json(submission.Error, TholdemWebHost.JsonOptions, statusCode: submission.StatusCode);
                }

                return Results.Json(
                    new
                    {
                        jobId = validated.JobId,
                        acceptedAtUtc = submission.Job!.AcceptedAtUtc,
                        optionsHash = validated.OptionsHash,
                    },
                    TholdemWebHost.JsonOptions,
                    statusCode: 202);
            });

        app.MapGet(
            "/api/rsi",
            (RsiJobRegistry registry) => Results.Json(registry.Recent(), TholdemWebHost.JsonOptions));

        app.MapGet(
            "/api/rsi/{jobId}",
            (string jobId, RsiJobRegistry registry) =>
            {
                var job = registry.TryGet(jobId);
                return job is null ? Results.NotFound() : Results.Json(job.Snapshot(), TholdemWebHost.JsonOptions);
            });

        app.MapDelete(
            "/api/rsi/{jobId}",
            (string jobId, RsiJobRegistry registry) =>
            {
                if (!registry.TryRequestCancel(jobId, out var reason))
                {
                    return reason == "unknown-job"
                        ? Results.NotFound()
                        : Results.Json(new { error = reason, jobId }, TholdemWebHost.JsonOptions, statusCode: 409);
                }

                return Results.Json(new { status = "cancelling", jobId }, TholdemWebHost.JsonOptions, statusCode: 202);
            });

        app.MapPost(
            "/api/rsi/{jobId}/promote",
            (string jobId, PromoteRequest? request, RsiJobRegistry registry, TholdemViewerOptions options) =>
                Promote(jobId, request, registry, options));
    }

    private static IResult Promote(
        string jobId,
        PromoteRequest? request,
        RsiJobRegistry registry,
        TholdemViewerOptions options)
    {
        var job = registry.TryGet(jobId);
        if (job is null)
        {
            return Results.NotFound();
        }

        if (request?.Confirm is not true)
        {
            return Refuse("confirmation-required", "The promote request must carry confirm: true.");
        }

        if (!job.IsComplete)
        {
            return Refuse("job-not-complete", "Only a completed job can be promoted.", new { status = job.Status });
        }

        var eligible = job.LastEligible;
        if (eligible?.Document is null)
        {
            return Refuse("no-eligible-proposer", "The run produced no eligible proposer.");
        }

        var document = eligible.Document;
        string? documentText;
        try
        {
            documentText = MetaStore.TryReadEvaluationDocument(
                options.DataDirectory,
                document.CandidateId,
                document.IncumbentId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            documentText = null;
        }

        if (documentText is null)
        {
            return Refuse("document-missing", "The persisted meta evaluation document is missing.");
        }

        if (!string.Equals(
                StrategyArtifact.ComputeHash(documentText),
                document.DocumentHash,
                StringComparison.Ordinal))
        {
            return Refuse("document-hash-mismatch", "The persisted meta evaluation changed after the job completed.");
        }

        if (!MetaEvaluationJson.TryParse(documentText, out var parsed, out var parseReason) || parsed is null)
        {
            return Refuse("document-invalid", "The persisted meta evaluation is not canonical.", new { reason = parseReason });
        }

        var frozen = FrozenComponents.Capture();
        if (!MetaDecisionVerifier.Verify(parsed, frozen, out var verifyReason))
        {
            return Refuse("meta-verification-failed", "The meta gate no longer verifies.", new { reason = verifyReason });
        }

        var ledger = MetaStore.VerifyLedger(options.DataDirectory);
        if (!ledger.Verified)
        {
            return Refuse("ledger-integrity", "The meta ledger failed its hash-chain check.", new { reason = ledger.Reason });
        }

        MetaArtifact? candidate;
        try
        {
            var proposerPath = Path.Combine(
                MetaStore.MetaDirectory(options.DataDirectory),
                MetaStore.ProposersDirectoryName,
                parsed.CandidateId + ".json");
            if (!File.Exists(proposerPath)
                || !MetaArtifact.TryParse(File.ReadAllText(proposerPath), out candidate)
                || candidate is null)
            {
                return Refuse("proposer-artifact-invalid", "The candidate proposer artifact is missing or fails its content hash.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Refuse("proposer-artifact-invalid", "The candidate proposer artifact could not be read.");
        }

        MetaArtifact current;
        try
        {
            current = MetaStore.ReadActiveProposer(options.DataDirectory);
        }
        catch (InvalidOperationException)
        {
            return Refuse("active-proposer-missing", "The active proposer pointer is missing or unreadable.");
        }

        if (string.Equals(current.Id, parsed.CandidateId, StringComparison.Ordinal))
        {
            job.MarkPromoted("promoted-by-endpoint");
            return Results.Json(
                new
                {
                    status = "already-promoted",
                    proposerId = parsed.CandidateId,
                    documentHash = parsed.DocumentHash,
                    verified = true,
                },
                TholdemWebHost.JsonOptions);
        }

        if (!string.Equals(current.Id, parsed.IncumbentId, StringComparison.Ordinal))
        {
            return Refuse(
                "proposer-changed",
                "The active proposer moved after this job completed; a stale promotion would roll it back.",
                new { currentProposerId = current.Id });
        }

        MetaStore.WriteActiveProposer(options.DataDirectory, candidate, parsed.DocumentHash);
        job.MarkPromoted("promoted-by-endpoint");
        return Results.Json(
            new
            {
                status = "promoted",
                proposerId = parsed.CandidateId,
                documentHash = parsed.DocumentHash,
                verified = true,
                ledgerHead = ledger.HeadHash,
            },
            TholdemWebHost.JsonOptions);
    }

    private static IResult Refuse(string error, string message, object? detail = null) =>
        Results.Json(
            new { error, message, detail },
            TholdemWebHost.JsonOptions,
            statusCode: 409);
}
