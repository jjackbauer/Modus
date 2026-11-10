namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// The promotion gate's economic and statistical requirements: the challenger's lower confidence
/// bound must clear <see cref="MarginBbPer100"/> and the evaluation must cover at least
/// <see cref="MinSeeds"/> held-out seeds. <see cref="Z"/> is the 97.5% normal quantile.
/// </summary>
public sealed record EvaluationCriteria(
    double MarginBbPer100 = 5.0,
    int MinSeeds = 24,
    double Z = 1.959963984540054);
