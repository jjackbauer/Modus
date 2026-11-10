using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;

namespace Wip.Tholdem.Web.Live;

/// <summary>
/// The three deterministic baseline bots that may be seated in a live match. Names are part of the
/// HTTP contract; an unknown name is rejected with 400 rather than replaced by a default.
/// </summary>
public static class LiveAgentCatalog
{
    public const string RandomLegal = "random-legal";

    public const string TightAggressive = "tight-aggressive";

    public const string PotOddsEquity = "pot-odds-equity";

    public static IReadOnlyList<string> Names { get; } = [RandomLegal, TightAggressive, PotOddsEquity];

    public static bool TryCreate(string? name, out IPokerAgent agent)
    {
        switch (name)
        {
            case RandomLegal:
                agent = new RandomLegalAgent();
                return true;
            case TightAggressive:
                agent = new TightAggressiveAgent();
                return true;
            case PotOddsEquity:
                agent = new PotOddsEquityAgent();
                return true;
            default:
                agent = null!;
                return false;
        }
    }
}
