using System.Text.Json.Serialization;
using Wip.Tholdem.Engine;

namespace Wip.Tholdem.Web.Live;

/// <summary>
/// The spectator projection of one live match. Unlike <c>PlayerObservation</c> it deliberately shows
/// every seat's hole cards; it is created only inside the web host and is never passed to an agent.
/// <see cref="Seats"/> carries one <see cref="SpectatorSeat"/> per configured seat (2–6) for every
/// match status; <see cref="HandNames"/> is the legacy per-seat name list and
/// <see cref="HoleCardsSeat0"/>/<see cref="HoleCardsSeat1"/> keep the pre-multiway two-seat fields
/// describing seats 0 and 1. <see cref="AmountToCall"/> and <see cref="PotOdds"/> describe the
/// acting seat's price. <see cref="ActingSeat"/> is the seat that must act now (<c>-1</c> when no
/// seat is to act, i.e. the hand is complete or the match is not running/paused); the viewer's turn
/// ring follows this field, never the last action. <see cref="LastActionSeat"/> is the seat of the
/// most recent action in the current hand (<c>-1</c> before its first action) for a distinct "last
/// action" cue. <see cref="PotAwarded"/> is the pot the current hand just awarded once it is
/// complete, so the viewer can show the settlement instead of a post-settlement <c>Pot 0</c>.
/// </summary>
public sealed record SpectatorSnapshot(
    string Id,
    string Status,
    int HandNumber,
    Street Street,
    IReadOnlyList<string> Board,
    long Pot,
    long PotAwarded,
    IReadOnlyList<long> Stacks,
    IReadOnlyList<string> HoleCardsSeat0,
    IReadOnlyList<string> HoleCardsSeat1,
    int ButtonSeat,
    int ActingSeat,
    int LastActionSeat,
    IReadOnlyList<string> HandNames,
    long AmountToCall,
    double PotOdds,
    bool IsComplete,
    bool ShowdownReached,
    IReadOnlyList<int> Winners,
    IReadOnlyList<string> HandHashes,
    IReadOnlyList<SpectatorAction> Actions,
    IReadOnlyList<SpectatorSeat> Seats);

/// <summary>
/// One seat in a live spectator snapshot: the public table state for that seat plus its own hole
/// cards and friendly hand name. <see cref="Folded"/> and <see cref="AllIn"/> describe the current
/// (or last completed) hand; <see cref="Winner"/> and <see cref="Dealer"/> mark the settlement and
/// the button.
/// </summary>
public sealed record SpectatorSeat(
    int Seat,
    string Agent,
    long Stack,
    IReadOnlyList<string> HoleCards,
    bool Folded,
    bool AllIn,
    string HandName,
    bool Winner,
    bool Dealer);

/// <summary>One action in the spectator tail. <see cref="PotAfter"/> is the pot after settlement.</summary>
public sealed record SpectatorAction(
    int HandNumber,
    Street Street,
    int Seat,
    string Kind,
    long Amount,
    long PotAfter);

/// <summary>
/// The POST /api/live body. <see cref="Seed"/> also accepts a decimal string so a browser can send
/// the full 64-bit value the form generated without JSON-number rounding.
/// <see cref="AllowWeakSeed"/> must be exactly <c>true</c> to start a match below
/// <see cref="SessionSeedPolicy.MinimumSeed"/>; absent or <c>false</c> keeps the guard on.
/// </summary>
public sealed record StartLiveMatchRequest(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] ulong Seed,
    int Hands,
    int? DelayMs,
    string? Seat0Agent,
    string? Seat1Agent,
    bool? AllowWeakSeed,
    int? Seats = null,
    IReadOnlyList<string>? Agents = null,
    IReadOnlyList<long>? StartingStacks = null);

/// <summary>
/// The 201 POST /api/live response. <see cref="Seat0Agent"/>/<see cref="Seat1Agent"/> remain for
/// two-seat clients; <see cref="Seats"/> and <see cref="Agents"/> describe every configured seat.
/// </summary>
public sealed record LiveMatchStarted(
    string Id,
    ulong Seed,
    int Hands,
    int DelayMs,
    string Seat0Agent,
    string Seat1Agent,
    int Seats,
    IReadOnlyList<string> Agents);
