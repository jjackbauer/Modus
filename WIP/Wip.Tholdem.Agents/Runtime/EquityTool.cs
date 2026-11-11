using Wip.Abstractions.Capabilities;
using Wip.Tholdem.Agents.Equity;
using Wip.Tholdem.Cards;

namespace Wip.Tholdem.Agents.Runtime;

/// <summary>
/// Equity request. <see cref="Samples"/> is the requested Monte Carlo budget; a non-positive value
/// means "use the tool's configured budget" because an omitted optional tool argument and an explicit
/// zero are indistinguishable after JSON binding. <see cref="Opponents"/> is the number of live
/// opponent hands and must be between one and five.
/// </summary>
public sealed record EquityToolRequest(
    IReadOnlyList<string> HoleCards,
    IReadOnlyList<string> Board,
    int Samples = 2000,
    ulong? Seed = null,
    int Opponents = 1);

/// <summary>
/// One equity tool answer. <see cref="Equity"/> is the win-plus-half-tie equity for a heads-up
/// request and the expected pot share for a multiway request; <see cref="ExpectedPotShare"/> is
/// always the share of the pot the hand wins on average when ties split it. The counts always sum
/// to <see cref="Samples"/> and <see cref="Opponents"/> records how many opponent hands were used.
/// </summary>
public sealed record EquityToolResult(
    double Equity,
    int Samples,
    int Wins,
    int Ties,
    int Losses,
    double ExpectedPotShare = 0,
    int Opponents = 1);

/// <summary>
/// Seeded Monte Carlo equity over two hole cards and the current board against one to five uniformly
/// random opponent hands. A single opponent keeps the legacy heads-up estimate; two to five
/// opponents use the multiway estimator and report the expected pot share. Parsing or duplicate-card
/// problems throw <see cref="ArgumentException"/>; an opponent count outside 1..5 throws
/// <see cref="ArgumentOutOfRangeException"/>. The tool invoker turns those into a deterministic tool
/// error instead of a model-visible exception.
/// </summary>
public sealed class EquityTool : ITool<EquityToolRequest, EquityToolResult>
{
    public const int DefaultSamples = 2000;

    private const ulong DefaultSeed = 0UL;

    private readonly int _sampleBudget;

    public EquityTool(int sampleBudget = DefaultSamples)
    {
        if (sampleBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleBudget),
                sampleBudget,
                "The equity sample budget must be positive.");
        }

        _sampleBudget = sampleBudget;
    }

    public ValueTask<EquityToolResult> ExecuteAsync(
        EquityToolRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Opponents is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EquityToolRequest.Opponents),
                request.Opponents,
                "The opponent count must be between 1 and 5.");
        }

        var holeCards = ParseCards(request.HoleCards, "holeCards");
        var board = ParseCards(request.Board, "board");
        var samples = request.Samples > 0
            ? Math.Min(request.Samples, _sampleBudget)
            : _sampleBudget;

        if (request.Opponents == 1)
        {
            var legacy = MonteCarloEquity.Estimate(
                holeCards,
                board,
                request.Seed ?? DefaultSeed,
                samples);

            return new ValueTask<EquityToolResult>(new EquityToolResult(
                legacy.Equity,
                legacy.Samples,
                legacy.Wins,
                legacy.Ties,
                legacy.Losses,
                ExpectedPotShare: legacy.Equity,
                Opponents: 1));
        }

        var estimate = MultiwayEquity.Estimate(
            holeCards,
            board,
            request.Opponents,
            request.Seed ?? DefaultSeed,
            samples);

        return new ValueTask<EquityToolResult>(new EquityToolResult(
            estimate.ExpectedPotShare,
            estimate.Samples,
            estimate.Wins,
            estimate.Ties,
            estimate.Losses,
            estimate.ExpectedPotShare,
            estimate.OpponentCount));
    }

    private static IReadOnlyList<Card> ParseCards(IReadOnlyList<string>? cards, string name)
    {
        if (cards is null)
        {
            throw new ArgumentException($"'{name}' must be provided.", name);
        }

        var parsed = new Card[cards.Count];
        for (var index = 0; index < cards.Count; index++)
        {
            var text = cards[index];
            if (text is null)
            {
                throw new ArgumentException($"'{name}[{index}]' must be a card.", name);
            }

            try
            {
                parsed[index] = Card.Parse(text);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException($"'{name}[{index}]' is not a card: {exception.Message}", name, exception);
            }
        }

        return parsed;
    }
}
