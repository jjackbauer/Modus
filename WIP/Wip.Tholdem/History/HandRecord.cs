using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;

namespace Wip.Tholdem.History;

/// <summary>
/// One action in a recorded hand. <see cref="Amount"/> is the action's "to" amount for bets and
/// raises and zero for every other kind; <see cref="PotAfter"/> is the pot after the action and
/// any immediate settlement.
/// </summary>
public sealed record HandActionRecord(int Street, int Seat, string Kind, long Amount, long PotAfter);

/// <summary>
/// A complete, replayable hand record with a canonical JSON form and a SHA-256 fingerprint.
/// </summary>
/// <remarks>
/// A two-seat record whose starting stacks are all equal serializes with the original v1 schema so
/// every recorded hash keeps verifying. Every other record — three or more seats, or two seats with
/// different starting stacks — serializes with the v2 schema, which carries
/// <see cref="StartingStacks"/>. The two-seat constructor members and
/// <see cref="HoleCardsSeat0"/>/<see cref="HoleCardsSeat1"/> remain as compatibility accessors
/// for the existing v1 parser, the CLI replay output and the viewer.
/// </remarks>
public sealed record HandRecord(
    int HandNumber,
    ulong Seed,
    long SmallBlind,
    long BigBlind,
    long StartingStack,
    int ButtonSeat,
    IReadOnlyList<Card> HoleCardsSeat0,
    IReadOnlyList<Card> HoleCardsSeat1,
    IReadOnlyList<Card> Board,
    IReadOnlyList<HandActionRecord> Actions,
    bool Showdown,
    IReadOnlyList<int> Winners,
    long PotAwarded,
    IReadOnlyList<long> FinalStacks)
{
    public int SeatCount { get; init; } = 2;

    /// <summary>Per-seat starting stacks. Defaults to the scalar stack repeated for two seats.</summary>
    public IReadOnlyList<long> StartingStacks { get; init; } = new[] { StartingStack, StartingStack };

    public IReadOnlyList<IReadOnlyList<Card>> HoleCards { get; init; } =
        new IReadOnlyList<Card>[] { HoleCardsSeat0, HoleCardsSeat1 };

    public IReadOnlyList<PotAward> Pots { get; init; } = Array.Empty<PotAward>();

    public static HandRecord From(TableState finalState)
    {
        ArgumentNullException.ThrowIfNull(finalState);

        if (!finalState.IsComplete)
        {
            throw new InvalidOperationException("A hand record requires a complete hand.");
        }

        var actions = new HandActionRecord[finalState.ActionLog.Count];
        for (var index = 0; index < actions.Length; index++)
        {
            var entry = finalState.ActionLog[index];
            actions[index] = new HandActionRecord(
                entry.Street,
                entry.Seat,
                entry.Kind.ToString(),
                entry.Amount,
                entry.PotAfter);
        }

        var holeCards = new IReadOnlyList<Card>[finalState.SeatCount];
        for (var seat = 0; seat < finalState.SeatCount; seat++)
        {
            holeCards[seat] = finalState.HoleCards[seat].ToArray();
        }

        var pots = new PotAward[finalState.Pots.Count];
        for (var index = 0; index < pots.Length; index++)
        {
            pots[index] = new PotAward(finalState.Pots[index].Amount, finalState.Pots[index].Winners.ToArray());
        }

        return new HandRecord(
            finalState.HandNumber,
            finalState.Seed,
            finalState.SmallBlind,
            finalState.BigBlind,
            finalState.StartingStack,
            finalState.ButtonSeat,
            finalState.HoleCardsSeat0.ToArray(),
            finalState.HoleCardsSeat1.ToArray(),
            finalState.Board.ToArray(),
            actions,
            finalState.ShowdownReached,
            finalState.Winners.ToArray(),
            finalState.AwardedPot,
            finalState.Stacks.ToArray())
        {
            SeatCount = finalState.SeatCount,
            StartingStacks = finalState.StartingStacks.ToArray(),
            HoleCards = holeCards,
            Pots = pots,
        };
    }

    public string ToCanonicalJson() => HandRecordCanonicalWriter.Write(this);

    public string Hash
    {
        get
        {
            var json = ToCanonicalJson();
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
