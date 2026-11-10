using Wip.Abstractions.Capabilities;
using Wip.Tholdem.Agents.Equity;
using Wip.Tholdem.Cards;

namespace Wip.Tholdem.Agents.Runtime;

/// <summary>
/// Equity request. <see cref="Samples"/> is the requested Monte Carlo budget; a non-positive value
/// means "use the tool's configured budget" because an omitted optional tool argument and an explicit
/// zero are indistinguishable after JSON binding.
/// </summary>
public sealed record EquityToolRequest(
    IReadOnlyList<string> HoleCards,
    IReadOnlyList<string> Board,
    int Samples = 2000,
    ulong? Seed = null);

public sealed record EquityToolResult(double Equity, int Samples, int Wins, int Ties, int Losses);

/// <summary>
/// Seeded Monte Carlo equity over two hole cards and the current board. Parsing or duplicate-card
/// problems throw <see cref="ArgumentException"/>; the tool invoker turns those into a deterministic
/// tool error instead of a model-visible exception.
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

        var holeCards = ParseCards(request.HoleCards, "holeCards");
        var board = ParseCards(request.Board, "board");
        var samples = request.Samples > 0
            ? Math.Min(request.Samples, _sampleBudget)
            : _sampleBudget;

        var estimate = MonteCarloEquity.Estimate(holeCards, board, request.Seed ?? DefaultSeed, samples);
        return new ValueTask<EquityToolResult>(new EquityToolResult(
            estimate.Equity,
            estimate.Samples,
            estimate.Wins,
            estimate.Ties,
            estimate.Losses));
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
