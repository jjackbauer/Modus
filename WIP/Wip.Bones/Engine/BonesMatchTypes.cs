using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Engine;

public sealed record BonesMatchConfig
{
    public BonesMatchConfig(
        BonesGameId gameId,
        int seed,
        int targetScore,
        IReadOnlyDictionary<BonesPlayerId, IBonesPlayerSlot> playerSlots)
    {
        if (targetScore <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetScore), targetScore, "Target score must be positive.");

        ArgumentNullException.ThrowIfNull(playerSlots);

        if (playerSlots.Count != BonesTableConfig.FixedPlayerCount)
        {
            throw new ArgumentException(
                $"Exactly {BonesTableConfig.FixedPlayerCount} player slots must be registered.",
                nameof(playerSlots));
        }

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            if (!playerSlots.ContainsKey(playerId))
            {
                throw new ArgumentException(
                    $"Player slot for seat {seat} is not registered.",
                    nameof(playerSlots));
            }

            if (playerSlots[playerId] is null)
                throw new ArgumentException($"Player slot for seat {seat} cannot be null.", nameof(playerSlots));
        }

        GameId = gameId;
        Seed = seed;
        TargetScore = targetScore;
        PlayerSlots = playerSlots.ToImmutableDictionary();
    }

    public BonesGameId GameId { get; }

    public int Seed { get; }

    public int TargetScore { get; }

    public ImmutableDictionary<BonesPlayerId, IBonesPlayerSlot> PlayerSlots { get; }
}

public sealed record BonesMatchRoundRecord
{
    public BonesMatchRoundRecord(int roundNumber, BonesRoundScore score, IReadOnlyList<BonesEvent> eventLog)
    {
        if (roundNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(roundNumber));

        RoundNumber = roundNumber;
        Score = score ?? throw new ArgumentNullException(nameof(score));
        EventLog = eventLog.ToImmutableArray();
    }

    public int RoundNumber { get; }

    public BonesRoundScore Score { get; }

    public ImmutableArray<BonesEvent> EventLog { get; }
}

public sealed record BonesMatchResult
{
    public BonesMatchResult(
        BonesGameId gameId,
        BonesPlayerId winner,
        IReadOnlyDictionary<BonesPlayerId, int> cumulativeScores,
        IReadOnlyList<BonesMatchRoundRecord> rounds,
        IReadOnlyList<BonesEvent> transcript)
    {
        GameId = gameId;
        Winner = winner;
        CumulativeScores = cumulativeScores.ToImmutableDictionary();
        Rounds = rounds.ToImmutableArray();
        Transcript = transcript.ToImmutableArray();
    }

    public BonesGameId GameId { get; }

    public BonesPlayerId Winner { get; }

    public ImmutableDictionary<BonesPlayerId, int> CumulativeScores { get; }

    public ImmutableArray<BonesMatchRoundRecord> Rounds { get; }

    public ImmutableArray<BonesEvent> Transcript { get; }
}