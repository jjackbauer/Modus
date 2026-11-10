using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Agents.Learning;

namespace Wip.Tholdem.Web.Training;

/// <summary>One optional field error returned as 400. The field name is machine-readable.</summary>
public sealed record OptionError(
    string Error,
    string Field,
    string Message,
    double? Min = null,
    double? Max = null);

/// <summary>The documented POST /api/training body. Missing fields stay null so the validator can name them.</summary>
public sealed class TrainingJobRequest
{
    public ulong? Seed { get; init; }

    public int? Iterations { get; init; }

    public int? TrainingHands { get; init; }

    public int? EvaluationSeeds { get; init; }

    public int? EvaluationHands { get; init; }

    public double? MarginBbPer100 { get; init; }

    public int? MinSeeds { get; init; }

    public string? StrategyId { get; init; }

    public bool AllowWeakSeed { get; init; }
}

/// <summary>A validated request: the loop options, the criteria, the deterministic ids and the hash.</summary>
public sealed record ValidatedTrainingRequest(
    TrainingJobRequest Request,
    TrainingOptions Options,
    EvaluationCriteria Criteria,
    string OptionsHash,
    string JobId,
    bool WeakSeed);

/// <summary>Bounds and the deterministic option hash for one training job.</summary>
public static class TrainingJobValidator
{
    public const int MinIterations = 1;

    public const int MaxIterations = 50;

    public const int MinTrainingHands = 1;

    public const int MaxTrainingHands = 2000;

    public const int MinEvaluationSeeds = 2;

    public const int MaxEvaluationSeeds = 500;

    public const int MinEvaluationHands = 1;

    public const int MaxEvaluationHands = 50;

    public const double MinMarginBbPer100 = 0.0;

    public const double MaxMarginBbPer100 = 100.0;

    public const int MinMinSeeds = 2;

    public const int MaxMinSeeds = 1000;

    /// <summary>A weak seed observes its own deals; long runs amplify the inference. Cap those runs.</summary>
    public const int MaxWeakSeedIterations = 5;

    public static ValidatedTrainingRequest? Validate(
        TrainingJobRequest? request,
        string dataDirectory,
        out OptionError? error)
    {
        error = null;
        if (request is null)
        {
            error = new OptionError("missing-body", "body", "A JSON body is required.");
            return null;
        }

        if (request.Seed is null)
        {
            error = new OptionError("missing-option", "seed", "seed is required.");
            return null;
        }

        if (!SessionSeedPolicy.IsStrong(request.Seed.Value) && !request.AllowWeakSeed)
        {
            error = new OptionError(
                "weak-seed",
                "seed",
                "A seed below the minimum is guessable; opt in explicitly.",
                Min: SessionSeedPolicy.MinimumSeed);
            return null;
        }

        if (request.AllowWeakSeed && request.Iterations is > MaxWeakSeedIterations)
        {
            error = new OptionError(
                "weak-seed-iteration-cap",
                "iterations",
                $"A weak-seed run is capped at {MaxWeakSeedIterations} iterations.",
                Max: MaxWeakSeedIterations);
            return null;
        }

        if (!TryInt(request.Iterations, MinIterations, MaxIterations, "iterations", out var iterations, out error))
        {
            return null;
        }

        if (!TryInt(request.TrainingHands, MinTrainingHands, MaxTrainingHands, "trainingHands", out var trainingHands, out error))
        {
            return null;
        }

        if (!TryInt(request.EvaluationSeeds, MinEvaluationSeeds, MaxEvaluationSeeds, "evaluationSeeds", out var evaluationSeeds, out error))
        {
            return null;
        }

        if (!TryInt(request.EvaluationHands, MinEvaluationHands, MaxEvaluationHands, "evaluationHands", out var evaluationHands, out error))
        {
            return null;
        }

        if (request.MarginBbPer100 is null || !double.IsFinite(request.MarginBbPer100.Value))
        {
            error = new OptionError("invalid-option", "marginBbPer100", "marginBbPer100 must be a finite number.");
            return null;
        }

        if (request.MarginBbPer100.Value < MinMarginBbPer100 || request.MarginBbPer100.Value > MaxMarginBbPer100)
        {
            error = new OptionError(
                "invalid-option",
                "marginBbPer100",
                $"marginBbPer100 must be between {MinMarginBbPer100} and {MaxMarginBbPer100}.",
                Min: MinMarginBbPer100,
                Max: MaxMarginBbPer100);
            return null;
        }

        if (!TryInt(request.MinSeeds, MinMinSeeds, MaxMinSeeds, "minSeeds", out var minSeeds, out error))
        {
            return null;
        }

        var strategyId = string.IsNullOrWhiteSpace(request.StrategyId) ? null : request.StrategyId.Trim();
        if (strategyId is not null)
        {
            try
            {
                _ = TrainingLoop.ReadPersistedStrategy(dataDirectory, strategyId);
            }
            catch (Exception)
            {
                error = new OptionError(
                    "unknown-strategy",
                    "strategyId",
                    "strategyId must name a readable, content-hash-valid strategy artifact.");
                return null;
            }
        }

        var criteria = new EvaluationCriteria(request.MarginBbPer100.Value, minSeeds);
        var optionsHash = ComputeOptionsHash(
            request.Seed.Value,
            iterations,
            trainingHands,
            evaluationSeeds,
            evaluationHands,
            request.MarginBbPer100.Value,
            minSeeds,
            strategyId,
            request.AllowWeakSeed);
        var jobId = "train-" + optionsHash[..16];
        var options = new TrainingOptions(
            request.Seed.Value,
            iterations,
            trainingHands,
            evaluationSeeds,
            evaluationHands,
            criteria,
            dataDirectory,
            strategyId);
        return new ValidatedTrainingRequest(
            request,
            options,
            criteria,
            optionsHash,
            jobId,
            request.AllowWeakSeed && !SessionSeedPolicy.IsStrong(request.Seed.Value));
    }

    /// <summary>
    /// SHA-256 over the canonical accepted options in a fixed key order. The hash never includes the
    /// data directory or a clock, so the same options derive the same job id in every process.
    /// </summary>
    public static string ComputeOptionsHash(
        ulong seed,
        int iterations,
        int trainingHands,
        int evaluationSeeds,
        int evaluationHands,
        double marginBbPer100,
        int minSeeds,
        string? strategyId,
        bool allowWeakSeed)
    {
        var builder = new StringBuilder(256);
        builder.Append("{\"seed\":");
        builder.Append(seed.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"iterations\":");
        builder.Append(iterations.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"trainingHands\":");
        builder.Append(trainingHands.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"evaluationSeeds\":");
        builder.Append(evaluationSeeds.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"evaluationHands\":");
        builder.Append(evaluationHands.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"marginBbPer100\":");
        builder.Append(marginBbPer100.ToString("R", CultureInfo.InvariantCulture));
        builder.Append(",\"minSeeds\":");
        builder.Append(minSeeds.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"strategyId\":");
        if (strategyId is null)
        {
            builder.Append("null");
        }
        else
        {
            builder.Append('"').Append(strategyId.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
        }

        builder.Append(",\"allowWeakSeed\":");
        builder.Append(allowWeakSeed ? "true" : "false");
        builder.Append('}');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static bool TryInt(
        int? value,
        int minimum,
        int maximum,
        string field,
        out int parsed,
        out OptionError? error)
    {
        parsed = 0;
        error = null;
        if (value is null)
        {
            error = new OptionError("missing-option", field, field + " is required.", Min: minimum, Max: maximum);
            return false;
        }

        if (value.Value < minimum || value.Value > maximum)
        {
            error = new OptionError(
                "invalid-option",
                field,
                $"{field} must be between {minimum} and {maximum}.",
                Min: minimum,
                Max: maximum);
            return false;
        }

        parsed = value.Value;
        return true;
    }
}

/// <summary>One completed iteration as exposed by the job API.</summary>
public sealed record TrainingJobEntry(
    int Iteration,
    string ChampionId,
    string ChallengerId,
    double? MeanBbPer100,
    double? LowerBoundBbPer100,
    double MarginBbPer100,
    int? Seeds,
    string Decision,
    bool Verified,
    string Outcome);

/// <summary>The last iteration's report summary as exposed by the job API.</summary>
public sealed record TrainingReportSummary(
    double MeanBbPer100,
    double LowerBoundBbPer100,
    double MarginBbPer100,
    int Seeds,
    string Decision,
    bool Verified);

/// <summary>The GET /api/training/{jobId} body.</summary>
public sealed record TrainingJobSnapshot(
    string JobId,
    string Status,
    string Phase,
    int Iteration,
    int TotalIterations,
    string? ChampionId,
    string? ChallengerId,
    TrainingReportSummary? LastReport,
    IReadOnlyList<TrainingJobEntry> Entries,
    string? Error,
    string AcceptedAtUtc,
    string OptionsHash,
    bool CanPromote,
    string? PromotionStatus,
    bool WeakSeed,
    string? FrozenManifestHash);

/// <summary>A bounded recent-jobs list plus the currently active job id.</summary>
public sealed record TrainingJobList(IReadOnlyList<TrainingJobSnapshot> Jobs, string? ActiveJobId);

/// <summary>The POST /api/training/{jobId}/promote body.</summary>
public sealed class PromoteRequest
{
    public bool? Confirm { get; init; }
}
