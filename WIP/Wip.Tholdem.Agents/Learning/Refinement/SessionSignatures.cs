using System.Text;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>The fixed names of the failure signals the extractor emits.</summary>
public static class FailureSignalNames
{
    public const string FoldToRaise = "fold-to-raise";

    public const string CallWithWeakHand = "call-with-weak-hand";

    public const string MissedValue = "missed-value";

    public const string AggressionByStreet = "aggression-by-street";

    public const string ShowdownLoss = "showdown-loss";

    public const string StackOffLoss = "stack-off-loss";
}

/// <summary>The fixed direction vocabulary of a failure signal.</summary>
public static class FailureSignalDirections
{
    public const string High = "high";

    public const string Low = "low";

    public const string Neutral = "neutral";
}

/// <summary>
/// One bounded, data-only signal: how often a named failure occurred (<see cref="Count"/>) out of how
/// many opportunities (<see cref="Sample"/>), with a fixed-vocabulary direction. It never carries a
/// seed, a card, an opponent name or free text.
/// </summary>
public sealed record FailureSignal(string Name, int Count, int Sample, double Rate, string Direction);

/// <summary>
/// The deterministic, bounded (at most twelve named signals) reduction of one recorded training
/// session. The canonical form is whitespace-free, culture-free and stable for the same records.
/// </summary>
public sealed record SessionSignatures(IReadOnlyList<FailureSignal> Signals)
{
    public const string Schema = "tholdem.signatures.v1";

    public const int MaximumSignals = 12;

    public static SessionSignatures Empty { get; } = new(Array.Empty<FailureSignal>());

    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(384);
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, Schema);
        builder.Append(",\"signals\":[");
        for (var index = 0; index < Signals.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var signal = Signals[index];
            builder.Append("{\"name\":");
            CanonicalJsonWriter.Text(builder, signal.Name);
            builder.Append(",\"count\":");
            CanonicalJsonWriter.Number(builder, signal.Count);
            builder.Append(",\"sample\":");
            CanonicalJsonWriter.Number(builder, signal.Sample);
            builder.Append(",\"rate\":");
            CanonicalJsonWriter.Number(builder, signal.Rate);
            builder.Append(",\"direction\":");
            CanonicalJsonWriter.Text(builder, signal.Direction);
            builder.Append('}');
        }

        builder.Append("]}");
        return builder.ToString();
    }
}
