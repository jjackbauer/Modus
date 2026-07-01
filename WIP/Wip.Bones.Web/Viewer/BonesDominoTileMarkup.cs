using System.Globalization;
using System.Net;
using System.Text;

namespace Wip.Bones.Web.Viewer;

public static class BonesDominoTileMarkup
{
    public static string RenderBoardTile(
        BonesBoardTilePlacement placement,
        IReadOnlyDictionary<int, string> playerColors,
        BonesBoardVisualLayout layout)
    {
        var seatColor = playerColors.TryGetValue(placement.PlayedBySeat, out var color)
            ? color
            : BonesPlayerColorPalette.GetColorForSeat(placement.PlayedBySeat);

        var orientation = placement.Orientation.ToString().ToLowerInvariant();
        var pipAxis = placement.PipAxis.ToString().ToLowerInvariant();
        var gridStyle = FormatGridPlacementStyle(placement, layout);
        var builder = new StringBuilder();
        builder.Append(
            $"<div class=\"domino-tile board-chain-tile\" data-orientation=\"{orientation}\" data-pip-axis=\"{pipAxis}\" " +
            $"data-low-pip=\"{placement.FacingLowPip}\" data-high-pip=\"{placement.FacingHighPip}\" " +
            $"data-facing-low-pip=\"{placement.FacingLowPip}\" data-facing-high-pip=\"{placement.FacingHighPip}\" " +
            $"data-played-by-seat=\"{placement.PlayedBySeat}\" data-seat-color=\"{WebUtility.HtmlEncode(seatColor)}\" " +
            $"data-grid-x=\"{placement.GridX}\" data-grid-y=\"{placement.GridY}\" " +
            $"data-is-double=\"{placement.IsDouble.ToString().ToLowerInvariant()}\" " +
            $"data-seat-marker-mode=\"divider\" {gridStyle}>");
        builder.Append(RenderHalf("domino-half-low", placement.FacingLowPip));
        builder.Append(RenderSeatColorElement(seatColor, useDividerFallback: true));
        builder.Append(RenderHalf("domino-half-high", placement.FacingHighPip));
        builder.Append("</div>");
        return builder.ToString();
    }

    public static string RenderHandTile(BonesHandTileView tile, IReadOnlyDictionary<int, string> playerColors)
    {
        var seatColor = playerColors.TryGetValue(tile.OwnerSeat, out var color)
            ? color
            : BonesPlayerColorPalette.GetColorForSeat(tile.OwnerSeat);

        var builder = new StringBuilder();
        builder.Append(
            $"<div class=\"domino-tile\" data-orientation=\"vertical\" data-pip-axis=\"vertical\" " +
            $"data-low-pip=\"{tile.LowPip}\" data-high-pip=\"{tile.HighPip}\" " +
            $"data-owner-seat=\"{tile.OwnerSeat}\" data-seat-color=\"{WebUtility.HtmlEncode(seatColor)}\" " +
            "data-seat-marker-mode=\"divider\">");
        builder.Append(RenderHalf("domino-half-low", tile.LowPip));
        builder.Append(RenderSeatColorElement(seatColor, useDividerFallback: true));
        builder.Append(RenderHalf("domino-half-high", tile.HighPip));
        builder.Append("</div>");
        return builder.ToString();
    }

    internal static string FormatGridPlacementStyle(
        BonesBoardTilePlacement placement,
        BonesBoardVisualLayout layout)
    {
        if (placement.FineColumnEnd > placement.FineColumnStart && placement.FineRowEnd > placement.FineRowStart)
        {
            return "style=\"" +
                   $"--tile-grid-column-start: {placement.FineColumnStart.ToString(CultureInfo.InvariantCulture)}; " +
                   $"--tile-grid-column-end: {placement.FineColumnEnd.ToString(CultureInfo.InvariantCulture)}; " +
                   $"--tile-grid-row-start: {placement.FineRowStart.ToString(CultureInfo.InvariantCulture)}; " +
                   $"--tile-grid-row-end: {placement.FineRowEnd.ToString(CultureInfo.InvariantCulture)}\"";
        }

        var column = (placement.GridX - layout.MinGridX) * 2 + 1;
        var row = (placement.GridY - layout.MinGridY) * 2 + 1;
        var columnSpan = placement.Orientation == BonesTileOrientation.Horizontal ? 2 : 1;
        var rowSpan = placement.Orientation == BonesTileOrientation.Vertical ? 2 : 1;
        return "style=\"" +
               $"--tile-grid-column-start: {column.ToString(CultureInfo.InvariantCulture)}; " +
               $"--tile-grid-column-end: {(column + columnSpan).ToString(CultureInfo.InvariantCulture)}; " +
               $"--tile-grid-row-start: {row.ToString(CultureInfo.InvariantCulture)}; " +
               $"--tile-grid-row-end: {(row + rowSpan).ToString(CultureInfo.InvariantCulture)}\"";
    }

    private static string RenderSeatColorElement(string seatColor, bool useDividerFallback)
    {
        if (useDividerFallback)
        {
        return $"<span class=\"domino-divider\" data-seat-color=\"{WebUtility.HtmlEncode(seatColor)}\" style=\"--seat-divider-color:{WebUtility.HtmlEncode(seatColor)}\"></span>";
        }

        return $"<span class=\"seat-color-marker\" style=\"background-color:{WebUtility.HtmlEncode(seatColor)}\"></span>";
    }

    private static string RenderHalf(string className, int pip)
    {
        var builder = new StringBuilder();
        builder.Append($"<div class=\"domino-half {className}\" data-pip=\"{pip}\">");
        builder.Append(RenderPips(pip));
        builder.Append("</div>");
        return builder.ToString();
    }

    private static string RenderPips(int pip)
    {
        var builder = new StringBuilder();
        foreach (var position in BonesStandardDominoPipLayout.GetGridPositions(pip))
            builder.Append($"<span class=\"pip\" data-position=\"{position}\"></span>");

        return builder.ToString();
    }
}
