using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>A paired evaluation request: the pre-registered plan, its pool and the seat mode.</summary>
public sealed record PopulationEvaluationRequest(
    EvaluationPlanV2 Plan,
    FrozenOpponentPool Pool,
    SeatPermutationMode SeatMode = SeatPermutationMode.FocalRotations);

/// <summary>
/// The paired focal-seat evaluator: for each composition and holdout block it seats the challenger
/// at one focal seat, seats the composition's opponents in the remaining seats, and compares the
/// focal seat's net chips against the incumbent on exactly the same deals. Every block contributes
/// one unit per composition; the estimate aggregates those units with
/// <see cref="BlockStatistics"/>. There is no clock, GUID, culture or ambient randomness.
/// </summary>
public static class PopulationMatchEvaluator
{
    public const string EvaluatorVersion = "tholdem.population-evaluator.v2.0.0";

    private const int SupportedSeats = 4;

    private const int FocalRotationCount = 4;

    private const int FullFactorialCount = 24;

    private const int OpponentOrdersPerFocalSeat = 6;

    public static PopulationEvaluationReport Evaluate(
        PopulationEvaluationRequest request,
        IPokerAgent challenger,
        string challengerId,
        string challengerPolicyHash,
        IPokerAgent incumbent,
        string incumbentId,
        string incumbentPolicyHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Plan);
        ArgumentNullException.ThrowIfNull(request.Pool);
        ArgumentNullException.ThrowIfNull(challenger);
        ArgumentNullException.ThrowIfNull(incumbent);

        ValidateAgentId(challengerId, nameof(challengerId));
        ValidateAgentId(incumbentId, nameof(incumbentId));
        if (string.Equals(challengerId, incumbentId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "agent-id-invalid: the challenger and incumbent ids must be distinct.",
                nameof(incumbentId));
        }

        ValidatePolicyHash(challengerPolicyHash, nameof(challengerPolicyHash));
        ValidatePolicyHash(incumbentPolicyHash, nameof(incumbentPolicyHash));

        var plan = request.Plan;
        var pool = request.Pool;
        if (plan.Game.Seats != SupportedSeats)
        {
            throw new ArgumentException(
                "seats-unsupported: the population evaluator is a four-seat evaluator; "
                + "the plan declares " + plan.Game.Seats + " seats.",
                nameof(request));
        }

        if (request.SeatMode is not (SeatPermutationMode.FocalRotations or SeatPermutationMode.FullFactorial))
        {
            throw new ArgumentException(
                "seat-mode-invalid: the seat permutation mode is not defined.",
                nameof(request));
        }

        if (!string.Equals(pool.PoolId, plan.OpponentPoolId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "pool-mismatch: the plan names pool '" + plan.OpponentPoolId
                + "' but the supplied pool is '" + pool.PoolId + "'.",
                nameof(request));
        }

        var compositionsById = new Dictionary<string, OpponentComposition>(StringComparer.Ordinal);
        foreach (var composition in pool.Compositions)
        {
            compositionsById[composition.CompositionId] = composition;
        }

        var compositions = new OpponentComposition[plan.Compositions.Count];
        for (var index = 0; index < plan.Compositions.Count; index++)
        {
            var compositionId = plan.Compositions[index];
            if (!compositionsById.TryGetValue(compositionId, out var composition))
            {
                throw new ArgumentException(
                    "composition-mismatch: the plan composition '" + compositionId
                    + "' does not resolve in pool '" + pool.PoolId + "'.",
                    nameof(request));
            }

            if (composition.OpponentIds.Count != SupportedSeats - 1)
            {
                throw new ArgumentException(
                    "composition-mismatch: the composition '" + compositionId + "' must name "
                    + (SupportedSeats - 1) + " opponents for a four-seat table; it names "
                    + composition.OpponentIds.Count + ".",
                    nameof(request));
            }

            compositions[index] = composition;
        }

        var seats = plan.Game.Seats;
        var blocks = plan.SeedBlocks;
        var handsPerMatch = plan.HandsPerSeatMatch;
        var bigBlind = plan.Game.BigBlind;
        var confidenceLevel = plan.ConfidenceLevel;
        var holdoutSeeds = plan.Holdout.Seeds;
        var bootstrapSeed = SeedDerivation.Derive(holdoutSeeds[0], 0xB007, 1, 0, 0);
        var fullFactorial = request.SeatMode == SeatPermutationMode.FullFactorial;
        var rotationsPerBlock = fullFactorial ? FullFactorialCount : FocalRotationCount;
        var permutationsPerSeat = fullFactorial ? OpponentOrdersPerFocalSeat : 1;

        var units = new List<EvaluationUnit>(compositions.Length * blocks);
        var perComposition = new List<CompositionEstimate>(compositions.Length);
        var overallBlockSums = new double[blocks];
        var perSeatBlockMeans = new double[seats][];
        for (var seat = 0; seat < seats; seat++)
        {
            perSeatBlockMeans[seat] = new double[blocks];
        }

        long challengerNetChips = 0;
        long incumbentNetChips = 0;
        var worstHandBbPer100 = double.PositiveInfinity;

        for (var compositionIndex = 0; compositionIndex < compositions.Length; compositionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var composition = compositions[compositionIndex];
            var blockMeans = new double[blocks];
            long compositionChallengerChips = 0;
            long compositionIncumbentChips = 0;

            for (var blockIndex = 0; blockIndex < blocks; blockIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var blockSeed = SeedDerivation.Derive(
                    holdoutSeeds[blockIndex],
                    0xE7A1 + compositionIndex,
                    1,
                    0,
                    0);
                var options = plan.Game.ToSimulationOptions(blockSeed, handsPerMatch);
                var assignments = BuildAssignments(seats, composition.OpponentIds, request.SeatMode);
                var assignmentMeans = new double[assignments.Count];
                var seatAssignmentSums = new double[seats];
                var seatAssignmentCounts = new int[seats];

                for (var assignmentIndex = 0; assignmentIndex < assignments.Count; assignmentIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var assignment = assignments[assignmentIndex];
                    var challengerTable = new IPokerAgent[seats];
                    var incumbentTable = new IPokerAgent[seats];
                    for (var seat = 0; seat < seats; seat++)
                    {
                        if (seat == assignment.FocalSeat)
                        {
                            challengerTable[seat] = challenger;
                            incumbentTable[seat] = incumbent;
                            continue;
                        }

                        var opponentId = assignment.OpponentBySeat[seat]!;
                        challengerTable[seat] = pool.Create(opponentId);
                        incumbentTable[seat] = pool.Create(opponentId);
                    }

                    var challengerRun = MatchSimulator.RunDetailed(
                        challengerTable,
                        options,
                        cancellationToken);
                    var incumbentRun = MatchSimulator.RunDetailed(
                        incumbentTable,
                        options,
                        cancellationToken);
                    AssertPairedDecks(challengerRun, incumbentRun);

                    var focalSeat = assignment.FocalSeat;
                    var start = plan.Game.StartingStacks is null
                        ? plan.Game.StartingStack
                        : plan.Game.StartingStacks[focalSeat];
                    long challengerChips = 0;
                    long incumbentChips = 0;
                    for (var hand = 0; hand < challengerRun.Outcomes.Count; hand++)
                    {
                        var challengerFinal = challengerRun.Outcomes[hand].FinalStacks[focalSeat];
                        var incumbentFinal = incumbentRun.Outcomes[hand].FinalStacks[focalSeat];
                        var challengerNet = challengerFinal - start;
                        var incumbentNet = incumbentFinal - start;
                        challengerChips += challengerNet;
                        incumbentChips += incumbentNet;
                        var handDiffBbPer100 =
                            (challengerNet - incumbentNet) / (double)bigBlind * 100.0;
                        if (handDiffBbPer100 < worstHandBbPer100)
                        {
                            worstHandBbPer100 = handDiffBbPer100;
                        }
                    }

                    var assignmentMean = (challengerChips - incumbentChips)
                        / (double)bigBlind
                        / handsPerMatch
                        * 100.0;
                    assignmentMeans[assignmentIndex] = assignmentMean;
                    seatAssignmentSums[focalSeat] += assignmentMean;
                    seatAssignmentCounts[focalSeat]++;
                    compositionChallengerChips += challengerChips;
                    compositionIncumbentChips += incumbentChips;
                }

                var blockMean = Mean(assignmentMeans);
                blockMeans[blockIndex] = blockMean;
                overallBlockSums[blockIndex] += blockMean;
                for (var seat = 0; seat < seats; seat++)
                {
                    perSeatBlockMeans[seat][blockIndex] +=
                        seatAssignmentSums[seat] / seatAssignmentCounts[seat];
                }

                units.Add(new EvaluationUnit(
                    composition.CompositionId,
                    blockIndex,
                    PopulationEvaluationReport.SeedFingerprint(blockSeed),
                    assignments.Count * handsPerMatch,
                    blockMean));
            }

            var compositionEstimate = BlockStatistics.Compute(
                blockMeans,
                confidenceLevel,
                bootstrapSeed);
            perComposition.Add(new CompositionEstimate(
                composition.CompositionId,
                compositionEstimate,
                compositionChallengerChips,
                compositionIncumbentChips));
            challengerNetChips += compositionChallengerChips;
            incumbentNetChips += compositionIncumbentChips;
        }

        var overallBlockMeans = new double[blocks];
        for (var blockIndex = 0; blockIndex < blocks; blockIndex++)
        {
            overallBlockMeans[blockIndex] =
                overallBlockSums[blockIndex] / compositions.Length;
        }

        var estimate = BlockStatistics.Compute(overallBlockMeans, confidenceLevel, bootstrapSeed);

        var perSeat = new List<SeatEstimate>(seats);
        var seatHands = blocks * compositions.Length * permutationsPerSeat * handsPerMatch;
        for (var seat = 0; seat < seats; seat++)
        {
            var seatBlockMeans = new double[blocks];
            for (var blockIndex = 0; blockIndex < blocks; blockIndex++)
            {
                seatBlockMeans[blockIndex] =
                    perSeatBlockMeans[seat][blockIndex] / compositions.Length;
            }

            perSeat.Add(new SeatEstimate(
                seat,
                BlockStatistics.Compute(seatBlockMeans, confidenceLevel, bootstrapSeed),
                seatHands));
        }

        var tail = new TailRisk(
            Min(overallBlockMeans),
            Percentile(overallBlockMeans, 0.05),
            worstHandBbPer100);

        return new PopulationEvaluationReport(
            challengerId,
            incumbentId,
            challengerPolicyHash,
            incumbentPolicyHash,
            plan.PlanHash,
            plan.Game.ConfigHash,
            pool.PoolId,
            pool.PoolHash,
            EvaluatorVersion,
            blocks,
            handsPerMatch,
            request.SeatMode.ToString(),
            rotationsPerBlock,
            estimate,
            units,
            perComposition,
            perSeat,
            tail,
            challengerNetChips,
            incumbentNetChips);
    }

    private static void ValidateAgentId(string? agentId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException(
                "agent-id-invalid: an agent id must be non-empty.",
                parameterName);
        }
    }

    private static void ValidatePolicyHash(string? policyHash, string parameterName)
    {
        if (policyHash is null || policyHash.Length != 64)
        {
            throw new ArgumentException(
                "policy-hash-invalid: a policy hash must be 64 lowercase hex characters.",
                parameterName);
        }

        foreach (var character in policyHash)
        {
            if (!((character >= '0' && character <= '9')
                  || (character >= 'a' && character <= 'f')))
            {
                throw new ArgumentException(
                    "policy-hash-invalid: a policy hash must be 64 lowercase hex characters.",
                    parameterName);
            }
        }
    }

    private static void AssertPairedDecks(MatchSimulation challengerRun, MatchSimulation incumbentRun)
    {
        if (challengerRun.HandSeeds.Count != incumbentRun.HandSeeds.Count)
        {
            throw new InvalidOperationException(
                "paired-deck-mismatch: the paired runs dealt different numbers of hands.");
        }

        for (var hand = 0; hand < challengerRun.HandSeeds.Count; hand++)
        {
            if (challengerRun.HandSeeds[hand] != incumbentRun.HandSeeds[hand])
            {
                throw new InvalidOperationException(
                    "paired-deck-mismatch: the paired runs dealt different hand seeds at hand "
                    + (hand + 1) + ".");
            }
        }
    }

    private static List<SeatAssignment> BuildAssignments(
        int seats,
        IReadOnlyList<string> opponentIds,
        SeatPermutationMode seatMode)
    {
        var assignments = new List<SeatAssignment>(
            seatMode == SeatPermutationMode.FullFactorial ? FullFactorialCount : FocalRotationCount);
        var ids = opponentIds.ToArray();

        if (seatMode == SeatPermutationMode.FocalRotations)
        {
            for (var focalSeat = 0; focalSeat < seats; focalSeat++)
            {
                assignments.Add(new SeatAssignment(focalSeat, SeatOpponents(seats, focalSeat, ids)));
            }

            return assignments;
        }

        var orders = new List<string[]>(OpponentOrdersPerFocalSeat);
        AddPermutations(ids, 0, orders);
        for (var focalSeat = 0; focalSeat < seats; focalSeat++)
        {
            foreach (var order in orders)
            {
                assignments.Add(new SeatAssignment(focalSeat, SeatOpponents(seats, focalSeat, order)));
            }
        }

        return assignments;
    }

    private static string?[] SeatOpponents(int seats, int focalSeat, IReadOnlyList<string> opponents)
    {
        var bySeat = new string?[seats];
        var next = 0;
        for (var seat = 0; seat < seats; seat++)
        {
            if (seat != focalSeat)
            {
                bySeat[seat] = opponents[next++];
            }
        }

        return bySeat;
    }

    private static void AddPermutations(string[] items, int index, List<string[]> results)
    {
        if (index == items.Length)
        {
            results.Add((string[])items.Clone());
            return;
        }

        for (var candidate = index; candidate < items.Length; candidate++)
        {
            (items[index], items[candidate]) = (items[candidate], items[index]);
            AddPermutations(items, index + 1, results);
            (items[index], items[candidate]) = (items[candidate], items[index]);
        }
    }

    private static double Mean(IReadOnlyList<double> values)
    {
        var sum = 0.0;
        for (var index = 0; index < values.Count; index++)
        {
            sum += values[index];
        }

        return sum / values.Count;
    }

    private static double Min(IReadOnlyList<double> values)
    {
        var minimum = values[0];
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index] < minimum)
            {
                minimum = values[index];
            }
        }

        return minimum;
    }

    /// <summary>Linear-interpolated percentile, matching <see cref="BlockStatistics"/>.</summary>
    private static double Percentile(IReadOnlyList<double> values, double probability)
    {
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var position = probability * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        var fraction = position - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
    }

    private sealed record SeatAssignment(int FocalSeat, string?[] OpponentBySeat);
}
