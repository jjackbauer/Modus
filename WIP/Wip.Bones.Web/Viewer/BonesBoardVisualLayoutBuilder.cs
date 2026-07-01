using Wip.Bones.Domain;

namespace Wip.Bones.Web.Viewer;

public sealed class BonesBoardVisualLayoutBuilder
{
    private readonly BonesBoardChainGeometryBuilder _geometryBuilder;

    public BonesBoardVisualLayoutBuilder(BonesBoardChainGeometryBuilder? geometryBuilder = null)
    {
        _geometryBuilder = geometryBuilder ?? new BonesBoardChainGeometryBuilder();
    }

    public BonesBoardVisualLayout BuildLayout(BonesBoard board, IReadOnlyList<BonesEvent> events)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(events);

        var playEvents = events
            .Where(static e => e.Kind == BonesEventKind.Play && e.Tile is not null)
            .OrderBy(static e => e.TurnIndex)
            .ToArray();

        if (playEvents.Length == 0 || board.Tiles.IsEmpty)
            return CreateLayout([]);

        var eventByTile = new Dictionary<(int LowPip, int HighPip), BonesEvent>(playEvents.Length);
        foreach (var playEvent in playEvents)
        {
            var tile = playEvent.Tile!.Value;
            IndexPlayEvent(eventByTile, tile.LowPip.Value, tile.HighPip.Value, playEvent);
        }

        var openingTile = playEvents[0].Tile!.Value;
        var openingIndex = -1;
        for (var index = 0; index < board.Tiles.Length; index++)
        {
            var tile = board.Tiles[index].Tile;
            if (tile.LowPip == openingTile.LowPip && tile.HighPip == openingTile.HighPip)
            {
                openingIndex = index;
                break;
            }
        }

        if (openingIndex < 0)
            throw new InvalidOperationException("Board tiles do not include the opening play.");

        var placements = new List<BonesBoardTilePlacement>(board.Tiles.Length);
        for (var chainIndex = 0; chainIndex < board.Tiles.Length; chainIndex++)
        {
            var boardTile = board.Tiles[chainIndex].Tile;
            if (!TryResolvePlayEvent(boardTile, eventByTile, out var playEvent))
            {
                throw new InvalidOperationException(
                    $"Board tile {boardTile.LowPip.Value}-{boardTile.HighPip.Value} has no matching play event.");
            }

            var (facingLowPip, facingHighPip) = BonesBoardTileFacingResolver.ResolveChainFacing(board.Tiles, chainIndex);
            var isOpening = chainIndex == openingIndex;
            var gridX = chainIndex - openingIndex;
            var gridY = 0;
            var orientation = isOpening || boardTile.IsDouble
                ? BonesTileOrientation.Vertical
                : BonesTileOrientation.Horizontal;

            placements.Add(BonesBoardTileFacingResolver.AssignFacingPips(new BonesBoardTilePlacement
            {
                FacingLowPip = facingLowPip,
                FacingHighPip = facingHighPip,
                ChainIndex = chainIndex,
                LowPip = boardTile.LowPip.Value,
                HighPip = boardTile.HighPip.Value,
                IsDouble = boardTile.IsDouble,
                Orientation = orientation,
                GridX = gridX,
                GridY = gridY,
                PlayedBySeat = playEvent.PlayerId.Seat,
            }));
        }

        ValidateChainConnectivity(placements);
        return CreateLayout(placements);
    }

    public static void ValidateChainConnectivity(IReadOnlyList<BonesBoardTilePlacement> tiles)
    {
        for (var index = 0; index < tiles.Count - 1; index++)
        {
            var left = tiles[index];
            var right = tiles[index + 1];
            if (GetConnectingPip(left, right) is null)
            {
                throw new InvalidOperationException(
                    $"Chain connectivity invariant violated between chain index {left.ChainIndex} and {right.ChainIndex}: " +
                    $"no matching pip on touching faces.");
            }
        }
    }

    internal static int? GetConnectingPip(BonesBoardTilePlacement left, BonesBoardTilePlacement right)
    {
        if (left.FacingHighPip == right.FacingLowPip)
            return left.FacingHighPip;

        return null;
    }

    private static void IndexPlayEvent(
        IDictionary<(int LowPip, int HighPip), BonesEvent> eventByTile,
        int lowPip,
        int highPip,
        BonesEvent playEvent)
    {
        eventByTile[(lowPip, highPip)] = playEvent;
        if (lowPip != highPip)
            eventByTile[(highPip, lowPip)] = playEvent;
    }

    private static bool TryResolvePlayEvent(
        BonesTile boardTile,
        IReadOnlyDictionary<(int LowPip, int HighPip), BonesEvent> eventByTile,
        out BonesEvent playEvent) =>
        eventByTile.TryGetValue((boardTile.LowPip.Value, boardTile.HighPip.Value), out playEvent);

    private BonesBoardVisualLayout CreateLayout(IReadOnlyList<BonesBoardTilePlacement> placements)
    {
        if (placements.Count == 0)
        {
            return new BonesBoardVisualLayout
            {
                Tiles = placements,
                MinGridX = 0,
                MaxGridX = 0,
                MinGridY = 0,
                MaxGridY = 0,
                ColumnCount = 0,
                RowCount = 0,
                FineColumnCount = 0,
                FineRowCount = 0,
                OccupiedWidthPixels = 0,
                OccupiedHeightPixels = 0,
            };
        }

        var minGridX = placements.Min(static tile => tile.GridX);
        var maxGridX = placements.Max(static tile => tile.GridX);
        var minGridY = placements.Min(static tile => tile.GridY);
        var maxGridY = placements.Max(static tile => tile.GridY);

        var layout = new BonesBoardVisualLayout
        {
            Tiles = placements,
            MinGridX = minGridX,
            MaxGridX = maxGridX,
            MinGridY = minGridY,
            MaxGridY = maxGridY,
            ColumnCount = maxGridX - minGridX + 1,
            RowCount = maxGridY - minGridY + 1,
        };

        return _geometryBuilder.AssignFineGridCells(layout);
    }
}