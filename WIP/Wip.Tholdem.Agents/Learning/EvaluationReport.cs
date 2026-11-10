using System.Text;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// The reproducible output of one paired challenger-versus-incumbent evaluation. Every value is a
/// pure function of the agents, the held-out seeds and the match size; there is no clock in it.
/// </summary>
public sealed record EvaluationReport(
    string ChallengerId,
    string IncumbentId,
    int Seeds,
    int HandsPerMatch,
    long BigBlind,
    double MeanBbPer100,
    ConfidenceInterval BbPer100Ci,
    double LowerBoundBbPer100,
    int ChallengerWins,
    int IncumbentWins,
    int Ties,
    long ChallengerNetChips)
{
    public string ReportHash => StrategyArtifact.ComputeHash(ToCanonicalJson());

    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(512);
        builder.Append("{\"challengerId\":");
        CanonicalJsonWriter.Text(builder, ChallengerId);
        builder.Append(",\"incumbentId\":");
        CanonicalJsonWriter.Text(builder, IncumbentId);
        builder.Append(",\"seeds\":");
        CanonicalJsonWriter.Number(builder, Seeds);
        builder.Append(",\"handsPerMatch\":");
        CanonicalJsonWriter.Number(builder, HandsPerMatch);
        builder.Append(",\"bigBlind\":");
        CanonicalJsonWriter.Number(builder, BigBlind);
        builder.Append(",\"meanBbPer100\":");
        CanonicalJsonWriter.Number(builder, MeanBbPer100);
        builder.Append(",\"bbPer100Ci\":{\"low\":");
        CanonicalJsonWriter.Number(builder, BbPer100Ci.Low);
        builder.Append(",\"high\":");
        CanonicalJsonWriter.Number(builder, BbPer100Ci.High);
        builder.Append("},\"lowerBoundBbPer100\":");
        CanonicalJsonWriter.Number(builder, LowerBoundBbPer100);
        builder.Append(",\"challengerWins\":");
        CanonicalJsonWriter.Number(builder, ChallengerWins);
        builder.Append(",\"incumbentWins\":");
        CanonicalJsonWriter.Number(builder, IncumbentWins);
        builder.Append(",\"ties\":");
        CanonicalJsonWriter.Number(builder, Ties);
        builder.Append(",\"challengerNetChips\":");
        CanonicalJsonWriter.Number(builder, ChallengerNetChips);
        builder.Append('}');
        return builder.ToString();
    }
}
