using System.Globalization;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Web.Training;

namespace Wip.Tholdem.Web.Training;

/// <summary>
/// The training HTTP surface. Requests only validate and enqueue; the loop runs on the host's
/// background queue. Promotion is a second, read-back phase: it re-reads the persisted report,
/// recomputes it from the persisted artifacts and seeds, and re-runs the frozen verifier.
/// </summary>
public static class TrainingEndpoints
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void MapTrainingRoutes(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(
            "/api/training",
            (TrainingJobRequest? request, TrainingJobRegistry registry, TholdemViewerOptions options) =>
            {
                var validated = TrainingJobValidator.Validate(request, options.DataDirectory, out var error);
                if (validated is null)
                {
                    var status = error?.Error == "missing-body" ? 400 : 400;
                    return Results.Json(error, TholdemWebHost.JsonOptions, statusCode: status);
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
            "/api/training",
            (TrainingJobRegistry registry) =>
                Results.Json(registry.Recent(), TholdemWebHost.JsonOptions));

        app.MapGet(
            "/api/training/{jobId}",
            (string jobId, TrainingJobRegistry registry) =>
            {
                var job = registry.TryGet(jobId);
                return job is null
                    ? Results.NotFound()
                    : Results.Json(job.Snapshot(), TholdemWebHost.JsonOptions);
            });

        app.MapDelete(
            "/api/training/{jobId}",
            (string jobId, TrainingJobRegistry registry) =>
            {
                if (!registry.TryRequestCancel(jobId, out var reason))
                {
                    if (reason == "unknown-job")
                    {
                        return Results.NotFound();
                    }

                    return Results.Json(
                        new { error = reason, jobId },
                        TholdemWebHost.JsonOptions,
                        statusCode: 409);
                }

                return Results.Json(
                    new { status = "cancelling", jobId },
                    TholdemWebHost.JsonOptions,
                    statusCode: 202);
            });

        app.MapPost(
            "/api/training/{jobId}/promote",
            (string jobId, PromoteRequest? request, TrainingJobRegistry registry, TholdemViewerOptions options) =>
                Promote(jobId, request, registry, options));
    }

    private static IResult Promote(
        string jobId,
        PromoteRequest? request,
        TrainingJobRegistry registry,
        TholdemViewerOptions options)
    {
        var job = registry.TryGet(jobId);
        if (job is null)
        {
            return Results.NotFound();
        }

        // A UI confirmation is never authorization: the server requires the explicit flag and then
        // re-derives everything it needs from persisted artifacts (A8).
        if (request?.Confirm is not true)
        {
            return Refuse("confirmation-required", "The promote request must carry confirm: true.");
        }

        if (!job.IsComplete)
        {
            return Refuse("job-not-complete", "Only a completed job can be promoted.", new { status = job.Status });
        }

        var last = job.LastCompleted;
        if (last?.Report is null)
        {
            return Refuse("no-verifiable-report", "The last iteration has no evaluation report.");
        }

        if (!last.Verified || last.Outcome != TrainingLoop.PromotedOutcome)
        {
            return Refuse(
                "report-not-promotable",
                "The last report did not verify a promotion.",
                new { reason = last.Decision.Reason, outcome = last.Outcome });
        }

        var championId = last.ChampionId;
        var challengerId = last.ChallengerId;
        var reportPath = Path.Combine(
            options.DataDirectory,
            "evaluations",
            challengerId + "-vs-" + championId + ".json");
        if (!File.Exists(reportPath))
        {
            return Refuse("report-missing", "The persisted evaluation report is missing.", new { reportPath });
        }

        string reportText;
        try
        {
            reportText = File.ReadAllText(reportPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Refuse("report-unreadable", "The persisted evaluation report could not be read.");
        }

        if (!EvaluationReportJson.TryParse(reportText, out var parsed, out var parseReason) || parsed is null)
        {
            return Refuse("report-invalid", "The persisted evaluation report is not canonical.", new { reason = parseReason });
        }

        if (!string.Equals(parsed.ChallengerId, challengerId, StringComparison.Ordinal)
            || !string.Equals(parsed.IncumbentId, championId, StringComparison.Ordinal))
        {
            return Refuse("report-job-mismatch", "The persisted report names a different challenger or incumbent.");
        }

        if (!string.Equals(parsed.ReportHash, last.Report.ReportHash, StringComparison.Ordinal))
        {
            return Refuse("report-hash-mismatch", "The persisted report changed after the job completed.");
        }

        StrategyArtifact challengerArtifact;
        StrategyArtifact championArtifact;
        try
        {
            challengerArtifact = TrainingLoop.ReadPersistedStrategy(options.DataDirectory, challengerId);
            championArtifact = TrainingLoop.ReadPersistedStrategy(options.DataDirectory, championId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Refuse("strategy-artifact-invalid", "A strategy artifact is missing or fails its content hash.");
        }

        var evaluationBaseSeed = SeedDerivation.Derive(
            job.Validated.Options.Seed,
            TrainingLoop.EvaluationSeedHandBase + last.Iteration,
            0,
            0,
            TrainingLoop.EvaluationSeedStream);
        var recomputed = MatchEvaluator.Evaluate(
            new StrategyAgent(challengerArtifact.Genome, "challenger"),
            challengerId,
            new StrategyAgent(championArtifact.Genome, "champion"),
            championId,
            evaluationBaseSeed,
            parsed.Seeds,
            parsed.HandsPerMatch);
        if (!string.Equals(recomputed.ReportHash, parsed.ReportHash, StringComparison.Ordinal))
        {
            return Refuse(
                "report-recomputation-mismatch",
                "The persisted report does not reproduce from the persisted artifacts and seeds.");
        }

        var decision = new StatisticalPromotionPolicy().Decide(parsed, job.Validated.Criteria);
        if (!decision.Promoted)
        {
            return Refuse("report-rejected", "The re-derived decision does not promote.", new { reason = decision.Reason });
        }

        if (!PromotionVerifier.Verify(decision, parsed, job.Validated.Criteria, out var verifyReason))
        {
            return Refuse("report-verification-failed", "The frozen verifier refused the report.", new { reason = verifyReason });
        }

        var ledger = TrainingLedger.Verify(options.DataDirectory);
        if (!ledger.Verified)
        {
            return Refuse("ledger-integrity", "The training ledger failed its hash-chain check.", new { reason = ledger.Reason });
        }

        var currentChampion = TryReadChampionStrategyId(options.DataDirectory);
        if (string.Equals(currentChampion, challengerId, StringComparison.Ordinal))
        {
            job.MarkPromoted("promoted-by-endpoint");
            return Results.Json(
                new
                {
                    status = "already-promoted",
                    championId = challengerId,
                    reportHash = parsed.ReportHash,
                    verified = true,
                },
                TholdemWebHost.JsonOptions);
        }

        if (currentChampion is not null && !string.Equals(currentChampion, championId, StringComparison.Ordinal))
        {
            return Refuse(
                "champion-changed",
                "The active champion moved after this job completed; a stale promotion would roll it back.",
                new { currentChampionId = currentChampion });
        }

        WriteChampionPointer(options.DataDirectory, challengerId, last.Iteration, parsed.ReportHash);
        job.MarkPromoted("promoted-by-endpoint");
        return Results.Json(
            new
            {
                status = "promoted",
                championId = challengerId,
                reportHash = parsed.ReportHash,
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

    private static string? TryReadChampionStrategyId(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, TrainingLoop.ChampionFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.GetProperty("strategyId").GetString();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or IOException)
        {
            return null;
        }
    }

    private static void WriteChampionPointer(
        string dataDirectory,
        string strategyId,
        int iteration,
        string reportHash)
    {
        var builder = new StringBuilder(192);
        builder.Append("{\"strategyId\":\"");
        builder.Append(strategyId);
        builder.Append("\",\"iteration\":");
        builder.Append(iteration.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"evaluationReportHash\":\"");
        builder.Append(reportHash);
        builder.Append("\"}");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(
            Path.Combine(dataDirectory, TrainingLoop.ChampionFileName),
            builder.ToString(),
            Utf8);
    }
}
