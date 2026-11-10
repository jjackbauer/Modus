namespace Wip.Tholdem.Agents.Equity;

/// <summary>One Monte Carlo equity estimate and its sample counts.</summary>
public sealed record EquityResult(double Equity, int Samples, int Wins, int Ties, int Losses);