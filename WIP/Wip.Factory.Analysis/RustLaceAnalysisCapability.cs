using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wip.Factory.Analysis;

/// <summary>One observed stage: how much work it completed and how much service time it consumed.</summary>
public sealed record StageObservation(
    string Stage,
    int Completions,
    double TotalServiceSeconds,
    double VisitsPerCompletion = 1.0,
    int Servers = 1);

/// <summary>Observed factory demand used to build the queueing model.</summary>
public sealed record StageNetworkModel(
    double ArrivalRatePerSecond,
    IReadOnlyList<StageObservation> Stages,
    int ObservationWindowSeconds);

/// <summary>A prediction produced by the modelling engine, with its inputs and uncertainty.</summary>
public sealed record PredictionArtifact(
    string PredictedBottleneck,
    double PredictedThroughputBound,
    IReadOnlyDictionary<string, string> EngineFields,
    string ModelInputs,
    string ForecastUncertainty,
    string EngineProtocolVersion);

/// <summary>What the model predicted versus what the factory actually did (F-K.5).</summary>
public sealed record ComparisonArtifact(
    string PredictedBottleneck,
    double PredictedThroughputBound,
    double ObservedThroughput,
    double ObservedP95LeadTimeSeconds,
    double ThroughputError,
    bool ExplanationHeld,
    string Basis);

/// <summary>
/// The RustLace modelling capability (F-K.1, F-K.2). The engine runs as a bounded child process with
/// its registry payloads suppressed, and a non-answer is reported as a failure, never as a prediction.
/// </summary>
public sealed class RustLaceAnalysisCapability
{
    private readonly string _binaryPath;
    private readonly TimeSpan _timeout;

    public RustLaceAnalysisCapability(string binaryPath, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);
        _binaryPath = binaryPath;
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
    }

    public const string EngineUnavailableCategory = "engine-unavailable";
    public const string EngineTimedOutCategory = "engine-timed-out";
    public const string EngineRejectedInputCategory = "engine-rejected-input";

    /// <summary>Builds the model script. Deterministic, so the same observations always model identically.</summary>
    public static string BuildScript(StageNetworkModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var stations = model.Stages.Select(static stage =>
        {
            var serviceSeconds = stage.Completions == 0
                ? 0.0
                : stage.TotalServiceSeconds / stage.Completions;

            // The engine reads an explicit server count as an M/M/c station, and c = 1 is not a
            // supported discipline: a single-server station must omit the argument entirely.
            return stage.Servers > 1
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "sys_station(\"{0}\", {1}, {2}, {3})",
                    stage.Stage,
                    serviceSeconds.ToString("0.#####", CultureInfo.InvariantCulture),
                    stage.VisitsPerCompletion.ToString("0.#####", CultureInfo.InvariantCulture),
                    stage.Servers)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "sys_station(\"{0}\", {1}, {2})",
                    stage.Stage,
                    serviceSeconds.ToString("0.#####", CultureInfo.InvariantCulture),
                    stage.VisitsPerCompletion.ToString("0.#####", CultureInfo.InvariantCulture));
        });

        return string.Format(
            CultureInfo.InvariantCulture,
            "sys_bottlenecks(sys_open({0}, [{1}]))",
            model.ArrivalRatePerSecond.ToString("0.#####", CultureInfo.InvariantCulture),
            string.Join(", ", stations));
    }

    /// <summary>
    /// The exact bounded argument vector the adapter uses (F-K.1): JSON mode, registry payloads
    /// suppressed and an explicit cancellation budget, so the invocation cannot run unbounded.
    /// </summary>
    public IReadOnlyList<string> BuildArguments(StageNetworkModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return
        [
            "--eval",
            BuildScript(model),
            "--json",
            "--omit-functions",
            "--omit-variables",
            "--cancel-after",
            ((int)_timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)
        ];
    }

    public async ValueTask<(PredictionArtifact? Prediction, string? FailureCategory)> PredictAsync(
        StageNetworkModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!File.Exists(_binaryPath))
        {
            return (null, EngineUnavailableCategory + ":binary-not-found");
        }

        var script = BuildScript(model);
        var info = new ProcessStartInfo(_binaryPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in BuildArguments(model))
        {
            info.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, EngineUnavailableCategory + ":" + exception.Message);
        }

        if (process is null)
        {
            return (null, EngineUnavailableCategory + ":process-start-returned-null");
        }

        using (process)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);

            var stopwatch = Stopwatch.StartNew();
            string stdout;
            string stderr;
            try
            {
                stdout = await process.StandardOutput.ReadToEndAsync(timeoutSource.Token).ConfigureAwait(false);
                stderr = await process.StandardError.ReadToEndAsync(timeoutSource.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return (null, EngineTimedOutCategory);
            }

            stopwatch.Stop();

            // A budget that elapsed must be reported as a timeout even when the killed process also
            // exited non-zero; otherwise a timeout is misreported as bad input. Elapsed time is used
            // as well as the cancellation flag, because a kill can win the race against the timer.
            if (timeoutSource.IsCancellationRequested || stopwatch.Elapsed >= _timeout)
            {
                return (null, EngineTimedOutCategory + ":budget-exceeded");
            }

            if (process.ExitCode != 0)
            {
                return (null, EngineRejectedInputCategory + ":" + stderr.Trim());
            }

            Envelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<Envelope>(stdout);
            }
            catch (JsonException exception)
            {
                return (null, EngineUnavailableCategory + ":unparseable-envelope:" + exception.Message);
            }

            if (envelope is null || !envelope.Ok)
            {
                return (null, EngineRejectedInputCategory + ":ok-false");
            }

            if (envelope.Cancellation?.Exceeded == true)
            {
                return (null, EngineTimedOutCategory + ":cancellation-exceeded");
            }

            var fields = envelope.Result?.Structured?.Fields?
                .Where(static field => field.Value is not null)
                .ToDictionary(
                    static field => field!.Name!,
                    static field => field!.Value!.Value ?? string.Empty,
                    StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);

            // The engine reports the bottleneck station by flag inside its record display, and the
            // throughput bound in its exact repeating-decimal notation (0.0(5) = 1/18).
            var bottleneck = ExtractBottleneck(envelope.Result?.Display) ?? "unknown";

            var throughput = 0.0;
            foreach (var key in new[] { "throughput_bound", "bound", "throughput" })
            {
                if (fields.TryGetValue(key, out var raw) && TryParseEngineNumber(raw, out var parsed))
                {
                    throughput = parsed;
                    break;
                }
            }

            return (new PredictionArtifact(
                bottleneck,
                throughput,
                fields,
                script,
                "model assumes Poisson arrivals and load-independent service; verify against observed traces before acting",
                envelope.ProtocolVersion?.ToString(CultureInfo.InvariantCulture) ?? "unknown"), null);
        }
    }

    /// <summary>
    /// Compares a prediction with the observed window. The explanation counts as held only when the
    /// predicted bound is at or above the observed throughput and within a factor of two.
    /// </summary>
    public static ComparisonArtifact Compare(
        PredictionArtifact prediction,
        double observedThroughput,
        double observedP95LeadTimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        if (observedThroughput <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(observedThroughput), observedThroughput, "Observed throughput must be positive.");
        }

        var error = prediction.PredictedThroughputBound <= 0
            ? double.NaN
            : Math.Abs(prediction.PredictedThroughputBound - observedThroughput) / observedThroughput;

        var held = prediction.PredictedThroughputBound > 0
            && prediction.PredictedThroughputBound >= observedThroughput * 0.5
            && prediction.PredictedThroughputBound <= observedThroughput * 2.0;

        return new ComparisonArtifact(
            prediction.PredictedBottleneck,
            prediction.PredictedThroughputBound,
            observedThroughput,
            observedP95LeadTimeSeconds,
            Math.Round(error, 4),
            held,
            held
                ? "predicted bound lies within a factor of two of observed throughput"
                : "predicted bound is more than a factor of two from observed throughput");
    }

    /// <summary>
    /// Reads the exact decimal notation the engine emits, including repeating decimals such as
    /// <c>0.0(5)</c> (= 1/18) which a plain double parse rejects.
    /// </summary>
    internal static bool TryParseEngineNumber(string raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        var open = text.IndexOf('(');
        var close = text.IndexOf(')');

        if (open > 0 && close > open)
        {
            var repeating = text[(open + 1)..close];
            var head = text[..open];

            if (!int.TryParse(repeating, NumberStyles.None, CultureInfo.InvariantCulture, out var digits))
            {
                return false;
            }

            var dot = head.IndexOf('.');
            var integerPart = dot < 0 ? head : head[..dot];
            var fixedPart = dot < 0 ? string.Empty : head[(dot + 1)..];

            if (!double.TryParse(integerPart.Length == 0 ? "0" : integerPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var integer))
            {
                return false;
            }

            if (!double.TryParse("0." + (fixedPart.Length == 0 ? "0" : fixedPart), NumberStyles.Float, CultureInfo.InvariantCulture, out var fixedValue))
            {
                return false;
            }

            var repeatingValue = digits / (Math.Pow(10, repeating.Length) - 1);
            var scale = Math.Pow(10, -fixedPart.Length);

            value = integer + fixedValue + (repeatingValue * scale);
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Finds the station flagged as the bottleneck in the engine's record display.</summary>
    internal static string? ExtractBottleneck(string? display)
    {
        if (string.IsNullOrWhiteSpace(display))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            display,
            @"id:\s*(?<id>[A-Za-z0-9_\-\.]+),\s*service_demand:[^,]*,\s*bottleneck:\s*True",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        return match.Success ? match.Groups["id"].Value : null;
    }

    private sealed class Envelope
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("protocolVersion")]
        public int? ProtocolVersion { get; set; }

        [JsonPropertyName("cancellation")]
        public Cancellation? Cancellation { get; set; }

        [JsonPropertyName("result")]
        public Result? Result { get; set; }
    }

    private sealed class Cancellation
    {
        [JsonPropertyName("exceeded")]
        public bool Exceeded { get; set; }
    }

    private sealed class Result
    {
        [JsonPropertyName("display")]
        public string? Display { get; set; }

        [JsonPropertyName("structured")]
        public Structured? Structured { get; set; }
    }

    private sealed class Structured
    {
        [JsonPropertyName("fields")]
        public List<Field?>? Fields { get; set; }
    }

    private sealed class Field
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("value")]
        public FieldValue? Value { get; set; }
    }

    private sealed class FieldValue
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }
}
