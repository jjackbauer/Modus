using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Domain;

public enum BonesBoardSide
{
    Left = 1,
    Right = 2,
}

public enum BonesRoundOutcomeKind
{
    Dominoes = 1,
    Blocked = 2,
}

public enum BonesEventKind
{
    Play = 1,
    Pass = 2,
}

public sealed record BonesRoundConfig
{
    public BonesRoundConfig(BonesGameId gameId, int shuffleSeed, BonesTableConfig? tableConfig = null)
    {
        GameId = gameId;
        ShuffleSeed = shuffleSeed;
        TableConfig = tableConfig ?? BonesTableConfig.Default;
        TableConfig.EnsurePlayerCount(TableConfig.PlayerCount);
    }

    public BonesGameId GameId { get; }

    public int ShuffleSeed { get; }

    public BonesTableConfig TableConfig { get; }
}

public sealed record BonesBoard
{
    public static BonesBoard Empty { get; } = new(ImmutableArray<BonesChainTile>.Empty);

    public BonesBoard(ImmutableArray<BonesChainTile> tiles)
    {
        Tiles = tiles;
    }

    public ImmutableArray<BonesChainTile> Tiles { get; }

    public bool IsEmpty => Tiles.IsEmpty;

    public BonesBoardEnd? LeftEnd => IsEmpty ? null : new BonesBoardEnd(Tiles[0].ChainLeftPip);

    public BonesBoardEnd? RightEnd => IsEmpty ? null : new BonesBoardEnd(Tiles[^1].ChainRightPip);

    public void ValidateChainEdges()
    {
        for (var index = 0; index < Tiles.Length - 1; index++)
        {
            var left = Tiles[index];
            var right = Tiles[index + 1];
            if (left.ChainRightPip != right.ChainLeftPip)
            {
                throw new InvalidOperationException(
                    $"Chain edge invariant violated between index {index} and {index + 1}: " +
                    $"exit {left.ChainRightPip.Value} does not match entry {right.ChainLeftPip.Value}.");
            }
        }
    }
}

public sealed record BonesHands
{
    public BonesHands(IReadOnlyDictionary<BonesPlayerId, BonesHand> handsByPlayer)
    {
        ArgumentNullException.ThrowIfNull(handsByPlayer);
        if (handsByPlayer.Count != BonesTableConfig.FixedPlayerCount)
            throw new ArgumentException($"Expected hands for {BonesTableConfig.FixedPlayerCount} players.", nameof(handsByPlayer));

        HandsByPlayer = handsByPlayer.ToImmutableDictionary();
    }

    public ImmutableDictionary<BonesPlayerId, BonesHand> HandsByPlayer { get; }

    public BonesHand GetHand(BonesPlayerId playerId) => HandsByPlayer[playerId];
}

public sealed record BonesMove
{
    public static BonesMove Pass(BonesMoveId moveId, BonesPlayerId playerId) =>
        new(moveId, playerId, null, null);

    public static BonesMove Play(BonesMoveId moveId, BonesPlayerId playerId, BonesTile tile, BonesBoardSide side) =>
        new(moveId, playerId, tile, side);

    public BonesMove(BonesMoveId moveId, BonesPlayerId playerId, BonesTile? tile, BonesBoardSide? side)
    {
        MoveId = moveId;
        PlayerId = playerId;
        Tile = tile;
        Side = side;

        if (tile is null ^ side is null)
            throw new ArgumentException("Play moves require both tile and side; pass moves require neither.");
    }

    public BonesMoveId MoveId { get; }

    public BonesPlayerId PlayerId { get; }

    public BonesTile? Tile { get; }

    public BonesBoardSide? Side { get; }

    public bool IsPass => Tile is null;
}

public sealed record BonesEvent
{
    public BonesEvent(int turnIndex, BonesPlayerId playerId, BonesEventKind kind, BonesTile? tile, BonesBoardSide? side)
    {
        if (turnIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(turnIndex));

        TurnIndex = turnIndex;
        PlayerId = playerId;
        Kind = kind;
        Tile = tile;
        Side = side;
    }

    public int TurnIndex { get; }

    public BonesPlayerId PlayerId { get; }

    public BonesEventKind Kind { get; }

    public BonesTile? Tile { get; }

    public BonesBoardSide? Side { get; }
}

public sealed record BonesRoundState
{
    public BonesRoundState(
        BonesGameId gameId,
        BonesBoard board,
        BonesHands hands,
        BonesPlayerId currentPlayer,
        BonesTile openingTile,
        int passStreak,
        IReadOnlyList<BonesEvent> eventLog,
        BonesRoundOutcomeKind? outcome = null,
        BonesPlayerId? winner = null)
    {
        GameId = gameId;
        Board = board;
        Hands = hands;
        CurrentPlayer = currentPlayer;
        OpeningTile = openingTile;
        PassStreak = passStreak;
        EventLog = eventLog.ToImmutableArray();
        Outcome = outcome;
        Winner = winner;
    }

    public BonesGameId GameId { get; }

    public BonesBoard Board { get; }

    public BonesHands Hands { get; }

    public BonesPlayerId CurrentPlayer { get; }

    public BonesTile OpeningTile { get; }

    public int PassStreak { get; }

    public ImmutableArray<BonesEvent> EventLog { get; }

    public BonesRoundOutcomeKind? Outcome { get; }

    public BonesPlayerId? Winner { get; }
}

public sealed record BonesRoundScore
{
    public BonesRoundScore(BonesPlayerId winner, int points, BonesRoundOutcomeKind outcome)
    {
        Winner = winner;
        Points = points;
        Outcome = outcome;
    }

    public BonesPlayerId Winner { get; }

    public int Points { get; }

    public BonesRoundOutcomeKind Outcome { get; }
}
