using Wip.Abstractions.Capabilities;

namespace Wip.Tholdem.Agents.Runtime;

public sealed record PotOddsRequest(long Pot, long AmountToCall);

public sealed record PotOddsResult(double PotOdds, double BreakEvenEquity, long CallAmount);

/// <summary>
/// Pot odds and the break-even equity a call needs:
/// <c>AmountToCall / (Pot + AmountToCall)</c> when there is something to call, otherwise zero.
/// </summary>
public sealed class PotOddsTool : ITool<PotOddsRequest, PotOddsResult>
{
    public ValueTask<PotOddsResult> ExecuteAsync(
        PotOddsRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Pot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Pot, "A pot cannot be negative.");
        }

        if (request.AmountToCall < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.AmountToCall,
                "An amount to call cannot be negative.");
        }

        var potOdds = request.AmountToCall > 0
            ? request.AmountToCall / (double)(request.Pot + request.AmountToCall)
            : 0d;

        return new ValueTask<PotOddsResult>(new PotOddsResult(potOdds, potOdds, request.AmountToCall));
    }
}
