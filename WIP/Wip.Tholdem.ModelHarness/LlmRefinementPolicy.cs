using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;

namespace Wip.Tholdem.ModelHarness;

/// <summary>
/// The live refiner: one bounded DeepSeek call per refinement iteration, transcribed before its
/// proposal is returned, with exactly one repair attempt and no tool surface. A denied environment,
/// an unreadable lineage, a spent call budget, a malformed reply and a transport failure are all
/// refusals: the policy never fabricates a proposal, and it never decides promotion.
/// </summary>
public sealed class LlmRefinementPolicy : IRefinementPolicy
{
    /// <summary>The flag that must be 1 or true before any live path is constructed.</summary>
    public const string LiveFlagVariable = "THOLDEM_LIVE_MODEL";

    /// <summary>The key variable that must be non-empty; its value is never read beyond emptiness.</summary>
    public const string ApiKeyVariable = "DEEPSEEK_API_KEY";

    /// <summary>The maximum length of a rationale code.</summary>
    public const int MaximumRationaleCodeLength = 100;

    private static readonly string[] LiveEnabledValues = ["1", "true"];

    private readonly IChatModel _model;

    private readonly LlmRefinerOptions _options;

    private readonly string _dataDirectory;

    private readonly LlmTranscriptStore _transcripts;

    private readonly CapabilityContext _capabilityContext;

    private readonly Stopwatch _wallClock = Stopwatch.StartNew();

    private int _callsUsed;

    public LlmRefinementPolicy(
        IChatModel model,
        LlmRefinerOptions options,
        string dataDirectory,
        LlmTranscriptStore transcripts,
        Func<string, string?>? environmentLookup = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(transcripts);

        _model = model;
        _options = options;
        _dataDirectory = dataDirectory;
        _transcripts = transcripts;
        _capabilityContext = new CapabilityContext(
            new SessionId("tholdem-llm-refiner"),
            Path.GetFullPath(dataDirectory));

        var lookup = environmentLookup ?? Environment.GetEnvironmentVariable;
        if (!IsLiveEnabled(lookup(LiveFlagVariable)))
        {
            throw new LlmRefinementRefusalException(
                LlmRefinementRefusalException.LiveModelDisabledCode,
                LiveFlagVariable + " must be set to 1 or true to enable the live refinement path.");
        }

        if (string.IsNullOrEmpty(lookup(ApiKeyVariable)))
        {
            throw new LlmRefinementRefusalException(
                LlmRefinementRefusalException.LiveModelDisabledCode,
                ApiKeyVariable + " must be set to a non-empty value to enable the live refinement path.");
        }
    }

    public RefinementProposal Propose(RefinementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var iteration = context.Iteration;
        var correlationId = "llm-" + iteration.ToString(CultureInfo.InvariantCulture);

        // M-02 context: measured data, the write-only memory snapshot and the verified lineage. The
        // lineage pointer is read first so an unreadable chain refuses before any model call.
        var memory = new HarnessMemoryStore(_dataDirectory).Load().Entries;
        var lineage = ReadLineage();
        var promptContext = new LlmPromptContext(
            iteration,
            context.Incumbent,
            context.Analysis,
            context.Signatures?.Signals ?? Array.Empty<FailureSignal>(),
            memory,
            lineage);
        var userText = LlmPromptBuilder.BuildUserPrompt(promptContext);
        var promptText = LlmPromptBuilder.SystemPromptText + "\n\n" + userText;

        var first = Complete(
            CreateRequest(correlationId, [ChatMessage.System(LlmPromptBuilder.SystemPromptText), ChatMessage.User(userText)]),
            promptText,
            iteration,
            correlationId);

        LlmProposal proposal;
        try
        {
            proposal = LlmProposalParser.Parse(first.Response.Message.Content);
            AppendResponse(iteration, correlationId, first.Response, promptText, first.ElapsedMilliseconds, LlmTranscriptOutcomes.Ok, null);
        }
        catch (LlmProposalParseException exception)
        {
            AppendResponse(
                iteration,
                correlationId,
                first.Response,
                promptText,
                first.ElapsedMilliseconds,
                LlmTranscriptOutcomes.Malformed,
                ErrorText(exception));

            if (_options.MaxRepairRetries != 1)
            {
                throw Malformed(exception);
            }

            // M-03: exactly one repair attempt with the failed reply and its stable parser code.
            var repairInstruction = "Your previous reply was invalid (" + exception.Code
                + "). Reply with exactly one JSON object and no other text.";
            var repairPromptText = promptText + "\n\n" + first.Response.Message.Content + "\n\n" + repairInstruction;
            var repair = Complete(
                CreateRequest(
                    correlationId,
                    [
                        ChatMessage.System(LlmPromptBuilder.SystemPromptText),
                        ChatMessage.User(userText),
                        ChatMessage.Assistant(first.Response.Message.Content),
                        ChatMessage.User(repairInstruction),
                    ]),
                repairPromptText,
                iteration,
                correlationId);

            try
            {
                proposal = LlmProposalParser.Parse(repair.Response.Message.Content);
                AppendResponse(
                    iteration,
                    correlationId,
                    repair.Response,
                    repairPromptText,
                    repair.ElapsedMilliseconds,
                    LlmTranscriptOutcomes.Repaired,
                    null);
            }
            catch (LlmProposalParseException repairException)
            {
                AppendResponse(
                    iteration,
                    correlationId,
                    repair.Response,
                    repairPromptText,
                    repair.ElapsedMilliseconds,
                    LlmTranscriptOutcomes.Malformed,
                    ErrorText(repairException));
                throw Malformed(repairException);
            }
        }

        var rationaleCode = proposal.MemoryKey is { Length: > 0 } key ? "llm-" + key : "llm-proposal";
        if (rationaleCode.Length > MaximumRationaleCodeLength)
        {
            rationaleCode = rationaleCode[..MaximumRationaleCodeLength];
        }

        return new RefinementProposal(proposal.Genome.Clamp(), rationaleCode);
    }

    private CallResult Complete(
        ChatRequest request,
        string promptText,
        int iteration,
        string correlationId)
    {
        EnsureBudget(iteration, correlationId, promptText);
        _callsUsed++;
        var stopwatch = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(_options.Timeout);
        try
        {
            var response = _model
                .CompleteAsync(request, _capabilityContext, timeout.Token)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            stopwatch.Stop();
            return new CallResult(response, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            // The public seam carries no caller token, so a cancellation here is the per-call timeout.
            Append(
                iteration,
                correlationId,
                _options.ModelId,
                string.Empty,
                promptText,
                string.Empty,
                new LlmUsage(0, 0),
                stopwatch.ElapsedMilliseconds,
                LlmTranscriptOutcomes.Timeout,
                "transport-timeout");
            throw;
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();
            var outcome = ClassifyHttpFailure(exception.Message);
            Append(
                iteration,
                correlationId,
                _options.ModelId,
                string.Empty,
                promptText,
                string.Empty,
                new LlmUsage(0, 0),
                stopwatch.ElapsedMilliseconds,
                outcome,
                exception.Message);
            throw new LlmRefinementRefusalException(
                LlmRefinementRefusalException.ModelFailureCode,
                "The model transport failed (" + outcome + ").",
                exception);
        }
    }

    private void EnsureBudget(int iteration, string correlationId, string promptText)
    {
        if (_callsUsed + 1 > _options.MaxCallsPerRun)
        {
            var error = "The refinement call budget was exceeded: call "
                + (_callsUsed + 1).ToString(CultureInfo.InvariantCulture)
                + " would exceed MaxCallsPerRun="
                + _options.MaxCallsPerRun.ToString(CultureInfo.InvariantCulture)
                + ".";
            Append(
                iteration,
                correlationId,
                _options.ModelId,
                string.Empty,
                promptText,
                string.Empty,
                new LlmUsage(0, 0),
                0,
                LlmTranscriptOutcomes.BudgetExceeded,
                error);
            throw new LlmRefinementRefusalException(LlmRefinementRefusalException.BudgetExceededCode, error);
        }

        if (_options.MaxWallClock is { } maxWallClock && _wallClock.Elapsed > maxWallClock)
        {
            var error = "The refinement wall-clock budget was exceeded: "
                + _wallClock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)
                + " ms elapsed with MaxWallClock="
                + ((long)maxWallClock.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)
                + " ms.";
            Append(
                iteration,
                correlationId,
                _options.ModelId,
                string.Empty,
                promptText,
                string.Empty,
                new LlmUsage(0, 0),
                0,
                LlmTranscriptOutcomes.BudgetExceeded,
                error);
            throw new LlmRefinementRefusalException(LlmRefinementRefusalException.BudgetExceededCode, error);
        }
    }

    private ChatRequest CreateRequest(string correlationId, IReadOnlyList<ChatMessage> messages) =>
        new(
            _options.ModelId,
            messages,
            tools: Array.Empty<ToolDefinition>(),
            maxTokens: _options.MaxTokens,
            temperature: _options.Temperature,
            correlationId: correlationId);

    private IReadOnlyList<StrategyLineageEntry> ReadLineage()
    {
        var pointerPath = Path.Combine(_dataDirectory, TrainingLoop.ChampionFileName);
        if (!File.Exists(pointerPath))
        {
            return Array.Empty<StrategyLineageEntry>();
        }

        string championId;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(pointerPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("strategyId", out var strategyIdElement)
                || strategyIdElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(strategyIdElement.GetString()))
            {
                throw new LlmRefinementRefusalException(
                    LlmRefinementRefusalException.LineageUnreadableCode,
                    "The champion pointer '" + TrainingLoop.ChampionFileName + "' has no non-empty strategyId.");
            }

            championId = strategyIdElement.GetString()!;
        }
        catch (JsonException exception)
        {
            throw new LlmRefinementRefusalException(
                LlmRefinementRefusalException.LineageUnreadableCode,
                "The champion pointer '" + TrainingLoop.ChampionFileName + "' is not readable JSON.",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new LlmRefinementRefusalException(
                LlmRefinementRefusalException.LineageUnreadableCode,
                "The champion pointer '" + TrainingLoop.ChampionFileName + "' could not be read.",
                exception);
        }

        try
        {
            return StrategyLineageReader.Read(_dataDirectory, championId);
        }
        catch (RefinementDataException exception)
        {
            throw new LlmRefinementRefusalException(
                LlmRefinementRefusalException.LineageUnreadableCode,
                "The strategy lineage could not be read (" + exception.Code + ").",
                exception);
        }
    }

    private void AppendResponse(
        int iteration,
        string correlationId,
        ChatResponse response,
        string promptText,
        long latencyMilliseconds,
        string outcome,
        string? error) =>
        Append(
            iteration,
            correlationId,
            _options.ModelId,
            response.ModelId,
            promptText,
            response.Message.Content,
            new LlmUsage(response.Usage?.InputTokens ?? 0, response.Usage?.OutputTokens ?? 0),
            latencyMilliseconds,
            outcome,
            error);

    private void Append(
        int iteration,
        string correlationId,
        string requestedModel,
        string servedModel,
        string promptText,
        string responseText,
        LlmUsage usage,
        long latencyMilliseconds,
        string outcome,
        string? error)
    {
        var transcript = LlmTranscript.Create(
            iteration,
            correlationId,
            requestedModel,
            servedModel,
            promptText,
            responseText,
            usage,
            latencyMilliseconds,
            outcome,
            error);
        _transcripts.AppendAsync(transcript, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    private static string ClassifyHttpFailure(string message)
    {
        if (ContainsStatusCode(message, static status => status is >= 500 and <= 599))
        {
            return LlmTranscriptOutcomes.Http5xx;
        }

        return ContainsStatusCode(message, static status => status == 429)
            ? LlmTranscriptOutcomes.Http429
            : LlmTranscriptOutcomes.Refused;
    }

    private static bool ContainsStatusCode(string message, Func<int, bool> predicate)
    {
        for (var index = 0; index + 2 < message.Length; index++)
        {
            if (!char.IsDigit(message[index])
                || !char.IsDigit(message[index + 1])
                || !char.IsDigit(message[index + 2]))
            {
                continue;
            }

            if (index > 0 && char.IsDigit(message[index - 1]))
            {
                continue;
            }

            if (index + 3 < message.Length && char.IsDigit(message[index + 3]))
            {
                continue;
            }

            var status = ((message[index] - '0') * 100)
                + ((message[index + 1] - '0') * 10)
                + (message[index + 2] - '0');
            if (predicate(status))
            {
                return true;
            }
        }

        return false;
    }

    private static string ErrorText(LlmProposalParseException exception) =>
        exception.Code + ": " + exception.Message;

    private static LlmRefinementRefusalException Malformed(LlmProposalParseException exception) =>
        new(
            LlmRefinementRefusalException.MalformedProposalCode,
            "The model reply was not a valid proposal (" + exception.Code + ").",
            exception);

    private static bool IsLiveEnabled(string? value)
    {
        foreach (var candidate in LiveEnabledValues)
        {
            if (string.Equals(candidate, value, StringComparison.Ordinal)
                || string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record CallResult(ChatResponse Response, long ElapsedMilliseconds);
}

/// <summary>A stable-coded refusal raised by the live refiner before a proposal can be produced.</summary>
public sealed class LlmRefinementRefusalException : Exception
{
    public const string LiveModelDisabledCode = "live-model-disabled";

    public const string LineageUnreadableCode = "lineage-unreadable";

    public const string BudgetExceededCode = "budget-exceeded";

    public const string MalformedProposalCode = "malformed-proposal";

    public const string ModelFailureCode = "model-failure";

    public LlmRefinementRefusalException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    /// <summary>The stable refusal code a caller can match without parsing the message.</summary>
    public string Code { get; }
}
