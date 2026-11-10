using System.Globalization;
using System.Net;
using System.Text;

namespace Wip.Tholdem.Web.Recording;

/// <summary>
/// Server-rendered replay table for one recorded hand. The HTML is a fragment consumed by the
/// viewer's replay surface and asserted directly by the committed HTTP tests: CSS-drawn card faces
/// (never images), five board slots, a dealer button, a winner badge and hand-strength names.
/// </summary>
public static class ReplayTableRenderer
{
    private static readonly string[] StreetNames = ["Preflop", "Flop", "Turn", "River", "Showdown", "Complete"];

    private static readonly string[] SuitNames = ["clubs", "diamonds", "hearts", "spades"];

    private static readonly string[] SuitGlyphs = ["♣", "♦", "♥", "♠"];

    private static readonly string[] RankNames =
        ["Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Jack", "Queen", "King", "Ace"];

    private const string RankLetters = "23456789TJQKA";

    private const string SuitLetters = "cdhs";

    public static string Render(string sessionId, RecordedHand hand)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(sessionId);

        var handNames = hand.HandNames;
        var seats = hand.HoleCards.Count;
        var builder = new StringBuilder(4096 + (seats * 640));
        builder.Append("<article class=\"hand-replay\"");
        builder.Append(" data-session=\"").Append(WebUtility.HtmlEncode(sessionId)).Append('"');
        builder.Append(" data-hand-number=\"").Append(Number(hand.HandNumber)).Append('"');
        builder.Append(" data-seats=\"").Append(Number(seats)).Append('"');
        builder.Append(" data-showdown=\"").Append(hand.Showdown ? "true" : "false").Append("\">");

        builder.Append("<header class=\"hand-replay-header\">");
        builder.Append("<h2 class=\"hand-replay-title\">Hand ").Append(Number(hand.HandNumber)).Append("</h2>");
        builder.Append("<p class=\"replay-showdown\">Showdown: ").Append(hand.Showdown ? "yes" : "no").Append("</p>");
        builder.Append("<p class=\"replay-seat-count\">Seats: ").Append(Number(seats)).Append("</p>");
        builder.Append("<p class=\"replay-pot\">Pot awarded: ").Append(Number(hand.PotAwarded)).Append("</p>");
        builder.Append("</header>");

        builder.Append("<div class=\"poker-table replay-table seats-").Append(Number(seats)).Append("\">");
        builder.Append("<div class=\"replay-seats\">");
        for (var seat = 0; seat < seats; seat++)
        {
            AppendSeat(builder, hand, seat, handNames[seat], hand.HoleCards[seat]);
        }

        builder.Append("</div>");
        AppendBoard(builder, hand.Board);
        builder.Append("</div>");

        AppendWinners(builder, hand.Winners);
        AppendPotLayers(builder, hand.Pots);
        AppendActions(builder, hand);
        builder.Append("</article>");
        return builder.ToString();
    }

    public static string StreetName(int street) =>
        street >= 0 && street < StreetNames.Length ? StreetNames[street] : "Unknown";

    private static void AppendSeat(
        StringBuilder builder,
        RecordedHand hand,
        int seat,
        string handName,
        IReadOnlyList<string> holeCards)
    {
        var isWinner = hand.Winners.Contains(seat);
        var isDealer = hand.ButtonSeat == seat;
        var finalStack = hand.FinalStacks.Count > seat ? hand.FinalStacks[seat] : 0;
        var agent = hand.Agents.Count > seat && !string.IsNullOrWhiteSpace(hand.Agents[seat])
            ? hand.Agents[seat]
            : "—";

        builder.Append("<section class=\"replay-seat");
        if (isWinner)
        {
            builder.Append(" is-winner");
        }

        builder.Append("\" data-seat=\"").Append(Number(seat)).Append('"');
        if (isDealer)
        {
            builder.Append(" data-dealer=\"true\"");
        }

        builder.Append('>');
        builder.Append("<header class=\"seat-top\">");
        builder.Append("<h3 class=\"seat-name\">Seat ").Append(Number(seat)).Append("</h3>");
        if (isDealer)
        {
            builder.Append("<span class=\"dealer-button\" aria-label=\"Dealer button\" title=\"Dealer button\">D</span>");
        }

        if (isWinner)
        {
            builder.Append("<span class=\"winner-badge\">Winner</span>");
        }

        builder.Append("</header>");
        builder.Append("<p class=\"replay-agent\" data-seat-agent=\"")
            .Append(WebUtility.HtmlEncode(agent))
            .Append("\">Agent: ")
            .Append(WebUtility.HtmlEncode(agent))
            .Append("</p>");
        builder.Append("<p class=\"replay-stack\">Final stack: ").Append(Number(finalStack)).Append("</p>");
        builder.Append("<p class=\"replay-hole-cards\">Hole cards: ");
        for (var index = 0; index < 2; index++)
        {
            if (index < holeCards.Count)
            {
                AppendCard(builder, holeCards[index]);
            }
            else
            {
                AppendCardBack(builder);
            }
        }

        builder.Append("</p>");
        builder.Append("<p class=\"replay-hand-name\" data-hand-name=\"")
            .Append(WebUtility.HtmlEncode(handName))
            .Append("\">")
            .Append(WebUtility.HtmlEncode(handName))
            .Append("</p>");
        builder.Append("</section>");
    }

    private static void AppendBoard(StringBuilder builder, IReadOnlyList<string> board)
    {
        builder.Append("<div class=\"table-center\">");
        builder.Append("<div class=\"cards board replay-board\" aria-label=\"Board\">");
        for (var index = 0; index < 5; index++)
        {
            if (index < board.Count)
            {
                AppendCard(builder, board[index]);
            }
            else
            {
                AppendEmptyCard(builder);
            }
        }

        builder.Append("</div>");
        builder.Append("<p class=\"replay-street\">Board: ")
            .Append(board.Count switch
            {
                0 => "preflop",
                3 => "flop",
                4 => "flop and turn",
                _ => "complete",
            })
            .Append("</p>");
        builder.Append("</div>");
    }

    private static void AppendWinners(StringBuilder builder, IReadOnlyList<int> winners)
    {
        builder.Append("<p class=\"replay-winners\">Winners: ");
        if (winners.Count == 0)
        {
            builder.Append("none");
        }
        else
        {
            for (var index = 0; index < winners.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("Seat ").Append(Number(winners[index]));
            }
        }

        builder.Append("</p>");
    }

    /// <summary>
    /// The recorded pot layers (v2 hands only; a v1 two-seat hand has none). Each layer names its
    /// winners, so a multiway side pot is visible as its own row.
    /// </summary>
    private static void AppendPotLayers(StringBuilder builder, IReadOnlyList<RecordedPot> pots)
    {
        if (pots.Count == 0)
        {
            return;
        }

        builder.Append("<p class=\"replay-pots\">Pot layers: ");
        for (var index = 0; index < pots.Count; index++)
        {
            if (index > 0)
            {
                builder.Append("; ");
            }

            var pot = pots[index];
            builder.Append(Number(pot.Amount)).Append(" → ");
            if (pot.Winners.Count == 0)
            {
                builder.Append("no winner");
            }
            else
            {
                for (var winner = 0; winner < pot.Winners.Count; winner++)
                {
                    if (winner > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append("Seat ").Append(Number(pot.Winners[winner]));
                }
            }
        }

        builder.Append("</p>");
    }

    private static void AppendActions(StringBuilder builder, RecordedHand hand)
    {
        builder.Append("<h3 class=\"replay-log-title\">Action log</h3>");
        builder.Append("<ol class=\"action-log replay-actions\">");
        foreach (var action in hand.Actions)
        {
            var street = StreetName(action.Street);
            var kind = action.Kind ?? string.Empty;
            builder.Append("<li class=\"action-row action--").Append(KindSlug(kind)).Append("\"");
            builder.Append(" data-street=\"").Append(WebUtility.HtmlEncode(street)).Append('"');
            builder.Append(" data-seat=\"").Append(Number(action.Seat)).Append('"');
            builder.Append(" data-kind=\"").Append(WebUtility.HtmlEncode(kind)).Append('"');
            builder.Append(" data-amount=\"").Append(Number(action.Amount)).Append('"');
            builder.Append(" data-pot-after=\"").Append(Number(action.PotAfter)).Append("\">");
            builder.Append("<span class=\"chip chip--").Append(street.ToLowerInvariant()).Append("\">")
                .Append(WebUtility.HtmlEncode(street)).Append("</span>");
            builder.Append("<span class=\"action-seat\">Seat ").Append(Number(action.Seat)).Append("</span>");
            builder.Append("<span class=\"action-kind\">").Append(WebUtility.HtmlEncode(kind)).Append("</span>");
            builder.Append("<span class=\"action-amount\">")
                .Append(action.Amount == 0 ? "—" : Number(action.Amount))
                .Append("</span>");
            builder.Append("<span class=\"action-pot\">pot ").Append(Number(action.PotAfter)).Append("</span>");
            builder.Append("</li>");
        }

        AppendSettlement(builder, hand);
        builder.Append("</ol>");
    }

    /// <summary>
    /// The recorded action's <c>potAfter</c> is the pot after settlement (0 on the final fold), so a
    /// labelled settlement row keeps the awarded amount visible instead of ending the log on "pot 0".
    /// </summary>
    private static void AppendSettlement(StringBuilder builder, RecordedHand hand)
    {
        if (hand.PotAwarded <= 0)
        {
            return;
        }

        builder.Append("<li class=\"action-row action--settle\" data-settlement=\"true\"");
        builder.Append(" data-pot-awarded=\"").Append(Number(hand.PotAwarded)).Append("\">");
        builder.Append("<span class=\"chip chip--settle\">Result</span>");
        builder.Append("<span class=\"action-seat\">Settlement</span>");
        builder.Append("<span class=\"action-kind\">");
        if (hand.Winners.Count == 0)
        {
            builder.Append("No winner");
        }
        else
        {
            for (var index = 0; index < hand.Winners.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("Seat ").Append(Number(hand.Winners[index]));
            }
        }

        builder.Append(" awarded</span>");
        builder.Append("<span class=\"action-amount\">").Append(Number(hand.PotAwarded)).Append("</span>");
        builder.Append("<span class=\"action-pot\">pot awarded ").Append(Number(hand.PotAwarded)).Append("</span>");
        builder.Append("</li>");
    }

    private static void AppendCard(StringBuilder builder, string code)
    {
        if (!TryDescribeCard(code, out var rankLabel, out var suitIndex, out var ariaLabel))
        {
            AppendEmptyCard(builder);
            return;
        }

        var suitName = SuitNames[suitIndex];
        var color = suitIndex is 1 or 2 ? "red" : "black";
        builder.Append("<span class=\"card card--").Append(suitName).Append(" card--").Append(color).Append('"');
        builder.Append(" data-card=\"").Append(WebUtility.HtmlEncode(code)).Append('"');
        builder.Append(" aria-label=\"").Append(WebUtility.HtmlEncode(ariaLabel)).Append("\">");
        builder.Append("<span class=\"card__rank\">").Append(rankLabel).Append("</span>");
        builder.Append("<span class=\"card__suit\">").Append(SuitGlyphs[suitIndex]).Append("</span>");
        builder.Append("</span>");
    }

    private static void AppendEmptyCard(StringBuilder builder) =>
        builder.Append("<span class=\"card card--empty\" aria-label=\"Empty board slot\"></span>");

    private static void AppendCardBack(StringBuilder builder) =>
        builder.Append("<span class=\"card card--back\" aria-label=\"Face-down card\"></span>");

    private static bool TryDescribeCard(
        string? code,
        out string rankLabel,
        out int suitIndex,
        out string ariaLabel)
    {
        rankLabel = string.Empty;
        suitIndex = -1;
        ariaLabel = string.Empty;
        if (string.IsNullOrEmpty(code) || code.Length != 2)
        {
            return false;
        }

        var rankIndex = RankLetters.IndexOf(char.ToUpperInvariant(code[0]));
        suitIndex = SuitLetters.IndexOf(char.ToLowerInvariant(code[1]));
        if (rankIndex < 0 || suitIndex < 0)
        {
            return false;
        }

        rankLabel = code[0] is 'T' or 't' ? "10" : char.ToUpperInvariant(code[0]).ToString();
        ariaLabel = RankNames[rankIndex] + " of " + SuitNames[suitIndex];
        return true;
    }

    private static string KindSlug(string kind) => kind.ToLowerInvariant();

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
