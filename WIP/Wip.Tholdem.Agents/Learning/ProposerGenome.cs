using System.Globalization;
using System.Text;
using Wip.Tholdem.Agents.Learning.Refinement;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>How the deterministic proposer chooses which genome parameter to move.</summary>
public enum ParameterSelectionPolicy
{
    /// <summary>The shipped policy: the measured session and the iteration parity choose the parameter.</summary>
    SessionDriven,

    /// <summary>A fixed four-parameter cycle, independent of the measured session.</summary>
    Cyclic,

    /// <summary>Always move a sizing or bluff parameter, never a threshold.</summary>
    AggressiveFirst,
}

/// <summary>
/// The proposer's own policy as data: the mutation step, how a parameter is selected, the share of
/// the evaluation budget it spends and the text template that is copied into a proposed strategy
/// prompt. Nothing here is executable; <see cref="PromptTemplateRenderer"/> performs literal
/// placeholder substitution only.
/// </summary>
public sealed record ProposerGenome(
    double MutationStep = 0.02,
    ParameterSelectionPolicy ParameterSelectionPolicy = ParameterSelectionPolicy.SessionDriven,
    double EvaluationBudgetShare = 1.0,
    string PromptTemplate = StrategyGenome.DefaultPromptText,
    RefinementStrategy RefinementStrategy = RefinementStrategy.SessionDriven)
{
    public const double MinimumMutationStep = 0.001;

    public const double MaximumMutationStep = 0.25;

    public const double MinimumEvaluationBudgetShare = 0.1;

    public const double MaximumEvaluationBudgetShare = 1.0;

    public const int MaximumPromptTemplateLength = 2000;

    /// <summary>The shipped proposer policy, byte-identical in behaviour to the pre-meta proposer.</summary>
    public static ProposerGenome Default { get; } = new();

    public ProposerGenome Clamp() => this with
    {
        MutationStep = Math.Clamp(MutationStep, MinimumMutationStep, MaximumMutationStep),
        EvaluationBudgetShare = Math.Clamp(
            EvaluationBudgetShare,
            MinimumEvaluationBudgetShare,
            MaximumEvaluationBudgetShare),
        PromptTemplate = SanitizePrompt(PromptTemplate),
    };

    /// <summary>
    /// True when the two proposer policies would make the same decisions. The prompt template is
    /// deliberately excluded: changing only the template changes an artifact hash, not behaviour,
    /// and the meta-loop refuses such an inert candidate before spending any evaluation (A7). The
    /// refinement strategy is included because it selects a different challenger path.
    /// </summary>
    public bool BehaviorEquals(ProposerGenome? other) =>
        other is not null
        && MutationStep == other.MutationStep
        && ParameterSelectionPolicy == other.ParameterSelectionPolicy
        && EvaluationBudgetShare == other.EvaluationBudgetShare
        && RefinementStrategy == other.RefinementStrategy;

    /// <summary>
    /// Turns arbitrary text into bounded prompt data: no control characters other than newline and
    /// tab, no empty value, and at most <see cref="MaximumPromptTemplateLength"/> UTF-16 characters.
    /// It never interprets the text as a template engine or code.
    /// </summary>
    public static string SanitizePrompt(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return StrategyGenome.DefaultPromptText;
        }

        var builder = new StringBuilder(Math.Min(template.Length, MaximumPromptTemplateLength));
        foreach (var character in template)
        {
            if (builder.Length >= MaximumPromptTemplateLength)
            {
                break;
            }

            if (character is '\n' or '\t' || character >= ' ')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append(' ');
            }
        }

        var sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? StrategyGenome.DefaultPromptText : sanitized;
    }
}

/// <summary>
/// Literal placeholder substitution for prompt data. This is <see cref="string.Replace(string, string)"/>
/// over a fixed placeholder table; the template is never executed, parsed or sent to a model, and an
/// unknown placeholder is left untouched. Offline evaluation of the resulting text is explicitly out
/// of scope; the meta gate is blind to prompt-only changes.
/// </summary>
public static class PromptTemplateRenderer
{
    public static string Render(string template, StrategyGenome genome, double mutationStep, int iteration)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(genome);

        var rendered = template
            .Replace("{raiseThreshold}", Number(genome.RaiseThreshold), StringComparison.Ordinal)
            .Replace("{callThreshold}", Number(genome.CallThreshold), StringComparison.Ordinal)
            .Replace("{bluffFrequency}", Number(genome.BluffFrequency), StringComparison.Ordinal)
            .Replace("{sizingMultiplier}", Number(genome.SizingMultiplier), StringComparison.Ordinal)
            .Replace("{step}", Number(mutationStep), StringComparison.Ordinal)
            .Replace("{iteration}", iteration.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return ProposerGenome.SanitizePrompt(rendered);
    }

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
