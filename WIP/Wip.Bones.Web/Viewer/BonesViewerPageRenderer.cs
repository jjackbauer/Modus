using System.Globalization;
using System.Net;
using System.Text;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Web.Viewer;

public static class BonesViewerPageRenderer
{
    public static string Render(
        BonesMatchViewModel snapshot,
        BonesMatchFrame? frame,
        int? selectedTurnIndex)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var leftEnd = frame?.LeftEndPip ?? snapshot.LeftEndPip;
        var rightEnd = frame?.RightEndPip ?? snapshot.RightEndPip;
        var activeSeat = frame?.ActiveSeat ?? snapshot.ActiveSeat;
        var handCounts = frame?.HandTileCountsBySeat ?? snapshot.HandTileCountsBySeat;
        var handTiles = frame?.HandTilesBySeat ?? snapshot.HandTilesBySeat;
        var boardLayout = frame?.BoardLayout ?? snapshot.BoardLayout;
        var playerColors = frame?.PlayerColorsBySeat ?? snapshot.PlayerColorsBySeat;
        var turnIndex = selectedTurnIndex ?? frame?.TurnIndex ?? Math.Max(0, snapshot.FrameCount - 1);
        var maxTurn = Math.Max(0, snapshot.FrameCount - 1);

        var eventKind = frame?.EventKind.ToString().ToLowerInvariant() ?? "none";
        var playedLow = frame?.PlayedTileLowPip;
        var playedHigh = frame?.PlayedTileHighPip;
        var boardSide = frame?.BoardSide?.ToString().ToLowerInvariant();

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine($"  <title>Bones Match {WebUtility.HtmlEncode(snapshot.MatchId)}</title>");
        builder.AppendLine("  <link rel=\"stylesheet\" href=\"/viewer/viewer.css\" />");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <main id=\"bones-viewer\" class=\"viewer\"");
        builder.AppendLine($"       data-session-id=\"{WebUtility.HtmlEncode(snapshot.SessionId)}\"");
        builder.AppendLine($"       data-match-id=\"{WebUtility.HtmlEncode(snapshot.MatchId)}\"");
        builder.AppendLine($"       data-frame-count=\"{snapshot.FrameCount}\"");
        builder.AppendLine($"       data-revision=\"{snapshot.Revision}\">");
        builder.AppendLine("    <header class=\"viewer-header\">");
        builder.AppendLine("      <h1>Bones Match Viewer</h1>");
        builder.AppendLine($"      <p class=\"match-id\">Match: {WebUtility.HtmlEncode(snapshot.MatchId)}</p>");
        builder.AppendLine("    </header>");
        builder.AppendLine("    <div class=\"viewer-shell\">");
        builder.AppendLine("      <aside class=\"viewer-sidebar\" aria-label=\"Match sidebar\">");
        BonesViewerLearningLoopStatusMarkup.Append(builder, "        ");
        builder.AppendLine("        <section class=\"hands\" aria-label=\"Player hands\">");

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var count = handCounts.TryGetValue(seat, out var tileCount) ? tileCount : 0;
            var score = snapshot.CumulativeScoresBySeat.TryGetValue(seat, out var cumulative) ? cumulative : 0;
            var seatClass = seat == activeSeat ? "hand seat-active" : "hand";
            builder.AppendLine($"          <article class=\"{seatClass}\" data-seat=\"{seat}\">");
            builder.AppendLine($"            <h2>Seat {seat}</h2>");
            builder.AppendLine("            <div class=\"hand-tiles\">");
            if (handTiles.TryGetValue(seat, out var tiles))
            {
                foreach (var tile in tiles)
                    builder.AppendLine("              " + BonesDominoTileMarkup.RenderHandTile(tile, playerColors));
            }

            builder.AppendLine("            </div>");
            builder.AppendLine($"            <span class=\"tile-count\" data-count=\"{count}\">{count} tiles</span>");
            builder.AppendLine($"            <span class=\"cumulative-score\" data-score=\"{score}\">Score: {score}</span>");
            builder.AppendLine("          </article>");
        }

        builder.AppendLine("        </section>");
        builder.AppendLine($"        <p id=\"active-seat\" class=\"active-seat\" data-seat=\"{activeSeat}\">Active seat: {activeSeat}</p>");
        builder.AppendLine("        <p id=\"last-move\" class=\"last-move\"");
        builder.AppendLine($"           data-kind=\"{eventKind}\"");
        builder.AppendLine($"           data-low-pip=\"{FormatOptional(playedLow)}\"");
        builder.AppendLine($"           data-high-pip=\"{FormatOptional(playedHigh)}\"");
        builder.AppendLine($"           data-board-side=\"{boardSide ?? string.Empty}\">");
        builder.AppendLine($"          {FormatLastMove(frame)}");
        builder.AppendLine("        </p>");

        var showRoundCompleteChrome = frame?.IsRoundComplete == true
            || (frame is null && snapshot.IsComplete && selectedTurnIndex is null);
        if (showRoundCompleteChrome && (frame?.WinnerSeat ?? snapshot.WinnerSeat) is int winnerSeat)
        {
            var roundPipScore = frame?.RoundPipScore ?? snapshot.RoundPipScore;
            builder.AppendLine($"        <p id=\"winner\" class=\"winner\" data-seat=\"{winnerSeat}\">Winner: Seat {winnerSeat}</p>");
            builder.AppendLine($"        <p id=\"round-score\" class=\"round-score\" data-pip-score=\"{FormatOptional(roundPipScore)}\">Round pip score: {FormatOptional(roundPipScore)}</p>");
        }
        else
        {
            builder.AppendLine("        <p id=\"winner\" class=\"winner hidden\" data-seat=\"\"></p>");
            builder.AppendLine("        <p id=\"round-score\" class=\"round-score hidden\" data-pip-score=\"\"></p>");
        }

        builder.AppendLine("        <label class=\"scrubber-label\" for=\"replay-scrubber\">Replay turn</label>");
        builder.AppendLine($"        <input id=\"replay-scrubber\" type=\"range\" min=\"0\" max=\"{maxTurn}\" value=\"{turnIndex}\" data-turn-index=\"{turnIndex}\" />");
        builder.AppendLine($"        <output id=\"turn-index\" for=\"replay-scrubber\" data-turn-index=\"{turnIndex}\">Turn {turnIndex}</output>");
        builder.AppendLine("      </aside>");
        builder.AppendLine("      <section class=\"viewer-main\" aria-label=\"Board canvas\">");
        builder.AppendLine("        <section id=\"bones-board\" class=\"board\" aria-label=\"Domino board\">");
        builder.AppendLine($"          <span id=\"left-end-pip\" class=\"board-end\" data-pip=\"{FormatOptional(leftEnd)}\">{FormatOptional(leftEnd)}</span>");
        var chainScale = BonesBoardChainScaleCalculator.ComputeTransformScale(
            boardLayout,
            BonesBoardChainScaleCalculator.DefaultChainSlotWidthPixels,
            BonesBoardChainScaleCalculator.DefaultContainerHeightPixels);
        var fineColumns = Math.Max(
            boardLayout.FineColumnCount > 0 ? boardLayout.FineColumnCount : boardLayout.ColumnCount * 2,
            2);
        var fineRows = Math.Max(
            boardLayout.FineRowCount > 0 ? boardLayout.FineRowCount : boardLayout.RowCount * 2,
            2);
        builder.AppendLine(
            "          <div id=\"board-chain\" class=\"board-chain\" " +
            $"data-min-grid-x=\"{boardLayout.MinGridX}\" data-max-grid-x=\"{boardLayout.MaxGridX}\" " +
            $"data-min-grid-y=\"{boardLayout.MinGridY}\" data-max-grid-y=\"{boardLayout.MaxGridY}\" " +
            $"data-column-count=\"{boardLayout.ColumnCount}\" data-row-count=\"{boardLayout.RowCount}\" " +
            $"data-board-chain-scale=\"{chainScale.ToString(CultureInfo.InvariantCulture)}\" " +
            $"style=\"--board-chain-scale: {chainScale.ToString(CultureInfo.InvariantCulture)}; " +
            $"--board-chain-columns: {fineColumns}; --board-chain-rows: {fineRows}; " +
            $"--board-chain-occupied-width: {boardLayout.OccupiedWidthPixels}; " +
            $"--board-chain-occupied-height: {boardLayout.OccupiedHeightPixels};\">");
        builder.AppendLine("            <div class=\"board-chain-inner\">");
        foreach (var placement in boardLayout.Tiles.OrderBy(static t => t.ChainIndex))
            builder.AppendLine("              " + BonesDominoTileMarkup.RenderBoardTile(placement, playerColors, boardLayout));

        builder.AppendLine("            </div>");
        builder.AppendLine("          </div>");
        builder.AppendLine($"          <span id=\"right-end-pip\" class=\"board-end\" data-pip=\"{FormatOptional(rightEnd)}\">{FormatOptional(rightEnd)}</span>");
        builder.AppendLine("        </section>");
        builder.AppendLine("      </section>");
        builder.AppendLine("    </div>");
        builder.AppendLine("  </main>");
        builder.AppendLine("  <script src=\"/viewer/viewer.js\"></script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    private static string FormatOptional(int? value) => value?.ToString() ?? string.Empty;

    private static string FormatLastMove(BonesMatchFrame? frame)
    {
        if (frame is null)
            return "No move selected";

        if (frame.EventKind == BonesEventKind.Pass)
            return $"Pass at turn {frame.TurnIndex}";

        if (frame.PlayedTileLowPip is null || frame.PlayedTileHighPip is null)
            return $"Play at turn {frame.TurnIndex}";

        return $"Played {frame.PlayedTileLowPip}-{frame.PlayedTileHighPip} on {frame.BoardSide} at turn {frame.TurnIndex}";
    }
}
