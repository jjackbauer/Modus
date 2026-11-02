using System.Text;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Benchmark;

public enum GateRule
{
    AndLegacy,
    OrCurrent,
    ConfidenceComposite,
}

public sealed record GateArm(string Name, bool Paired, int MatchCount, GateRule Rule);

public sealed record ArmResult(
    string Name,
    bool Paired,
    int MatchCount,
    double MeanSelectedScoreDifferential,
    double CiLow,
    double CiHigh,
    double DeltaVsNeverPromote,
    double DeltaVsBaseline,
    double DeltaVsBaselineCiLow,
    double DeltaVsBaselineCiHigh,
    double PromotionRate,
    double TruePositiveRate,
    double FalsePositiveRate,
    int Promotions,
    int Decisions,
    int MatchesPerReplication);

public sealed record GateStudyResult(
    int Replications,
    int PairCount,
    int EvaluationMasterSeed,
    int TargetScore,
    IReadOnlyList<ArmResult> Arms,
    double NeverPromoteMeanFinalScoreDifferential,
    double AlwaysPromoteMeanFinalScoreDifferential,
    double ElapsedMilliseconds);

public static class GateStudy
{
    public static GateStudyResult Run(
        IReadOnlyList<StudyPair> pairs,
        IReadOnlyDictionary<string, Type> strategyTypes,
        IReadOnlyDictionary<int, string> opponentBySeat,
        IReadOnlyDictionary<string, double> arenaScoreDifferential,
        IReadOnlyList<GateArm> arms,
        int replications,
        int evaluationMasterSeed,
        int targetScore,
        int maxDegreeOfParallelism)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var maxMatches = arms.Max(arm => arm.MatchCount);
        var samples = BuildSamples(
            pairs,
            strategyTypes,
            opponentBySeat,
            replications,
            maxMatches,
            evaluationMasterSeed,
            targetScore,
            maxDegreeOfParallelism);

        var armResults = new List<ArmResult>(arms.Count);
        var replicationResults = new List<double[]>(arms.Count);
        foreach (var arm in arms)
        {
            var perReplication = new double[replications];
            var promotions = 0;
            var decisions = 0;
            var truePositives = 0;
            var falsePositives = 0;
            var trulyBetter = 0;
            var trulyWorse = 0;

            foreach (var pair in pairs)
            {
                var incumbentStrength = arenaScoreDifferential.TryGetValue(pair.IncumbentKey, out var inc) ? inc : 0.0;
                var candidateStrength = arenaScoreDifferential.TryGetValue(pair.CandidateKey, out var cand) ? cand : 0.0;
                if (candidateStrength > incumbentStrength)
                    trulyBetter++;
                else if (candidateStrength < incumbentStrength)
                    trulyWorse++;
            }

            for (var replication = 0; replication < replications; replication++)
            {
                var strengths = new double[pairs.Count];
                for (var pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
                {
                    var pair = pairs[pairIndex];
                    var incumbentStrength = arenaScoreDifferential.TryGetValue(pair.IncumbentKey, out var inc) ? inc : 0.0;
                    var candidateStrength = arenaScoreDifferential.TryGetValue(pair.CandidateKey, out var cand) ? cand : 0.0;
                    var promoted = Decide(arm, samples[pairIndex][replication], pair);
                    decisions++;
                    if (promoted)
                    {
                        promotions++;
                        if (candidateStrength > incumbentStrength)
                            truePositives++;
                        else if (candidateStrength < incumbentStrength)
                            falsePositives++;
                        strengths[pairIndex] = candidateStrength;
                    }
                    else
                    {
                        strengths[pairIndex] = incumbentStrength;
                    }
                }

                perReplication[replication] = strengths.Average();
            }

            var mean = perReplication.Average();
            var interval = Stats.MeanConfidenceInterval95(perReplication);
            replicationResults.Add(perReplication);
            armResults.Add(new ArmResult(
                arm.Name,
                arm.Paired,
                arm.MatchCount,
                mean,
                interval.Low,
                interval.High,
                mean,
                0.0,
                0.0,
                0.0,
                decisions == 0 ? 0 : promotions / (double)decisions,
                trulyBetter == 0 ? 0 : truePositives / (double)(trulyBetter * replications),
                trulyWorse == 0 ? 0 : falsePositives / (double)(trulyWorse * replications),
                promotions,
                decisions,
                pairs.Count * arm.MatchCount * 2));
        }

        var baselineIndex = arms.ToList().FindIndex(arm => arm.Name.StartsWith("or_independent", StringComparison.Ordinal));
        if (baselineIndex >= 0)
        {
            var baseline = replicationResults[baselineIndex];
            for (var index = 0; index < armResults.Count; index++)
            {
                if (index == baselineIndex)
                    continue;

                var differences = new double[replications];
                for (var replication = 0; replication < replications; replication++)
                    differences[replication] = replicationResults[index][replication] - baseline[replication];

                var meanDelta = differences.Average();
                var deltaInterval = Stats.MeanConfidenceInterval95(differences);
                armResults[index] = armResults[index] with
                {
                    DeltaVsBaseline = meanDelta,
                    DeltaVsBaselineCiLow = deltaInterval.Low,
                    DeltaVsBaselineCiHigh = deltaInterval.High,
                };
            }
        }

        var neverPromote = MeanStrength(pairs, arenaScoreDifferential, useCandidate: false);
        var alwaysPromote = MeanStrength(pairs, arenaScoreDifferential, useCandidate: true);

        for (var index = 0; index < armResults.Count; index++)
        {
            armResults[index] = armResults[index] with
            {
                DeltaVsNeverPromote = armResults[index].MeanSelectedScoreDifferential - neverPromote,
            };
        }

        started.Stop();
        return new GateStudyResult(
            replications,
            pairs.Count,
            evaluationMasterSeed,
            targetScore,
            armResults,
            neverPromote,
            alwaysPromote,
            started.Elapsed.TotalMilliseconds);
    }

    private static double MeanStrength(
        IReadOnlyList<StudyPair> pairs,
        IReadOnlyDictionary<string, double> strengths,
        bool useCandidate)
    {
        var total = 0.0;
        foreach (var pair in pairs)
        {
            var key = useCandidate ? pair.CandidateKey : pair.IncumbentKey;
            total += strengths.TryGetValue(key, out var value) ? value : 0.0;
        }

        return total / pairs.Count;
    }

    private static bool Decide(GateArm arm, EvalSample[] samples, StudyPair pair)
    {
        var count = Math.Min(arm.MatchCount, samples.Length);
        var candidateWins = 0;
        var incumbentWins = 0;
        var candidateScores = new double[count];
        var incumbentScores = new double[count];

        for (var index = 0; index < count; index++)
        {
            var sample = samples[index];
            candidateScores[index] = sample.CandidateScore;
            incumbentScores[index] = arm.Paired ? sample.IncumbentPairedScore : sample.IncumbentIndependentScore;
            if (sample.CandidateWon)
                candidateWins++;
            if (arm.Paired ? sample.IncumbentPairedWon : sample.IncumbentIndependentWon)
                incumbentWins++;
        }

        var winRateDelta = (candidateWins - incumbentWins) / (double)count;
        var scoreDelta = candidateScores.Average() - incumbentScores.Average();

        const double winRateFloor = -0.10;
        const double scoreFloor = -2.0;

        if (winRateDelta < winRateFloor || scoreDelta < scoreFloor)
            return false;

        return arm.Rule switch
        {
            GateRule.AndLegacy => winRateDelta >= 0.02 && scoreDelta >= 1.0,
            GateRule.OrCurrent => winRateDelta >= 0.02 || scoreDelta >= 1.0,
            GateRule.ConfidenceComposite => PassesCompositeGate(
                count,
                candidateWins,
                incumbentWins,
                candidateScores,
                incumbentScores,
                winRateDelta,
                scoreDelta),
            _ => false,
        };
    }

    private static bool PassesCompositeGate(
        int count,
        int candidateWins,
        int incumbentWins,
        IReadOnlyList<double> candidateScores,
        IReadOnlyList<double> incumbentScores,
        double winRateDelta,
        double scoreDelta)
    {
        if (count < 4)
            return false;

        var differences = new double[count];
        for (var index = 0; index < count; index++)
            differences[index] = candidateScores[index] - incumbentScores[index];

        var scoreStandardError = Stats.PairedStandardError(differences);
        if (double.IsNaN(scoreStandardError) || scoreStandardError <= 0)
            scoreStandardError = 1e-9;

        var scoreZ = scoreDelta / scoreStandardError;
        var candidateRate = candidateWins / (double)count;
        var incumbentRate = incumbentWins / (double)count;
        var winStandardError = Math.Sqrt(
            (candidateRate * (1.0 - candidateRate) / count)
            + (incumbentRate * (1.0 - incumbentRate) / count));
        if (winStandardError <= 0)
            winStandardError = 1e-9;

        var winZ = winRateDelta / winStandardError;
        var compositeZ = scoreZ + winZ;

        return compositeZ >= 1.0
            || scoreDelta - (1.2816 * scoreStandardError) > 0;
    }

    private static EvalSample[][][] BuildSamples(
        IReadOnlyList<StudyPair> pairs,
        IReadOnlyDictionary<string, Type> strategyTypes,
        IReadOnlyDictionary<int, string> opponentBySeat,
        int replications,
        int maxMatches,
        int evaluationMasterSeed,
        int targetScore,
        int maxDegreeOfParallelism)
    {
        var samples = new EvalSample[pairs.Count][][];
        for (var pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
        {
            samples[pairIndex] = new EvalSample[replications][];
            for (var replication = 0; replication < replications; replication++)
                samples[pairIndex][replication] = new EvalSample[maxMatches];
        }

        var work = new List<(int PairIndex, int Replication)>();
        for (var pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
        {
            for (var replication = 0; replication < replications; replication++)
                work.Add((pairIndex, replication));
        }

        Parallel.ForEach(
            work,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            item =>
            {
                var pair = pairs[item.PairIndex];
                var stepSamples = samples[item.PairIndex][item.Replication];
                for (var matchIndex = 0; matchIndex < maxMatches; matchIndex++)
                {
                    var pairedSeed = BonesSeedDerivation.Derive(
                        evaluationMasterSeed,
                        pair.Seat,
                        item.PairIndex,
                        item.Replication,
                        matchIndex,
                        1);
                    var independentSeed = BonesSeedDerivation.Derive(
                        evaluationMasterSeed,
                        pair.Seat,
                        item.PairIndex,
                        item.Replication,
                        matchIndex,
                        2);

                    var candidate = RunEvaluationMatch(
                        pair.CandidateKey,
                        pair.Seat,
                        pairedSeed,
                        opponentBySeat,
                        strategyTypes,
                        targetScore);
                    var incumbentPaired = RunEvaluationMatch(
                        pair.IncumbentKey,
                        pair.Seat,
                        pairedSeed,
                        opponentBySeat,
                        strategyTypes,
                        targetScore);
                    var incumbentIndependent = RunEvaluationMatch(
                        pair.IncumbentKey,
                        pair.Seat,
                        independentSeed,
                        opponentBySeat,
                        strategyTypes,
                        targetScore);

                    stepSamples[matchIndex] = new EvalSample(
                        candidate.Won,
                        candidate.Score,
                        incumbentPaired.Won,
                        incumbentPaired.Score,
                        incumbentIndependent.Won,
                        incumbentIndependent.Score);
                }
            });

        return samples;
    }

    private static (bool Won, int Score) RunEvaluationMatch(
        string strategyKey,
        int learningSeat,
        int seed,
        IReadOnlyDictionary<int, string> opponentBySeat,
        IReadOnlyDictionary<string, Type> strategyTypes,
        int targetScore)
    {
        var slots = new Dictionary<BonesPlayerId, IBonesPlayerSlot>();
        var learningPlayerId = new BonesPlayerId(learningSeat);
        slots[learningPlayerId] = CreateSlot(strategyKey, strategyTypes);

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            if (playerId == learningPlayerId)
                continue;

            var opponentKey = opponentBySeat.TryGetValue(seat, out var key) ? key : string.Empty;
            slots[playerId] = CreateSlot(opponentKey, strategyTypes);
        }

        var config = new BonesMatchConfig(
            new BonesGameId($"gate-{strategyKey}-{seed}"),
            seed,
            targetScore,
            slots);
        var result = new BonesMatchSimulator().RunMatch(config);
        return (result.Winner == learningPlayerId, result.CumulativeScores[learningPlayerId]);
    }

    private static IBonesPlayerSlot CreateSlot(string strategyKey, IReadOnlyDictionary<string, Type> strategyTypes)
    {
        if (!strategyTypes.TryGetValue(strategyKey, out var type))
            return new DeterministicOpponents.FirstLegalMoveSlot();

        return (IBonesPlayerSlot)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Unable to instantiate '{type.FullName}'."));
    }

    public static string ToMarkdown(GateStudyResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("### Promotion gate study");
        builder.AppendLine();
        builder.AppendLine($"Pairs: {result.PairCount}; replications: {result.Replications}; evaluation target: {result.TargetScore}; evaluation seed: {result.EvaluationMasterSeed}.");
        builder.AppendLine();
        builder.AppendLine("| Arm | Paired | Matches/decision | Mean selected score diff | 95% CI | Delta vs never | Delta vs OR baseline [95% CI] | Promotion rate | True-positive rate | False-positive rate |");
        builder.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var arm in result.Arms)
        {
            builder.AppendLine(
                $"| {arm.Name} | {(arm.Paired ? "yes" : "no")} | {arm.MatchCount} | {arm.MeanSelectedScoreDifferential:F3} | [{arm.CiLow:F3}, {arm.CiHigh:F3}] | {arm.DeltaVsNeverPromote:+0.000;-0.000;0.000} | {arm.DeltaVsBaseline:+0.000;-0.000;0.000} [{arm.DeltaVsBaselineCiLow:+0.000;-0.000;0.000}, {arm.DeltaVsBaselineCiHigh:+0.000;-0.000;0.000}] | {arm.PromotionRate:P1} | {arm.TruePositiveRate:P1} | {arm.FalsePositiveRate:P1} |");
        }

        builder.AppendLine();
        builder.AppendLine($"Never-promote reference: {result.NeverPromoteMeanFinalScoreDifferential:F3}; always-promote reference: {result.AlwaysPromoteMeanFinalScoreDifferential:F3}.");
        return builder.ToString();
    }
}
