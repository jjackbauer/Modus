using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// Composes a match by randomly drawing 4 strategies from a <see cref="StrategyPool"/>,
/// assigning each to a distinct seat (1-4), and producing the player slots needed
/// to run the match via <see cref="BonesMatchSimulator"/>.
/// </summary>
public sealed class PopulationMatchComposer
{
    private readonly BonesStrategyScriptHost _scriptHost;

    public PopulationMatchComposer(BonesStrategyScriptHost? scriptHost = null)
    {
        _scriptHost = scriptHost ?? new BonesStrategyScriptHost();
    }

    /// <summary>
    /// Composes a match by drawing 4 strategies from <paramref name="pool"/>,
    /// assigning them to seats 1-4 in the order they were drawn.
    /// </summary>
    /// <returns>
    /// A <see cref="PopulationMatchComposition"/> with the seat→strategy mapping
    /// and ready-to-use player slots.
    /// </returns>
    public PopulationMatchComposition ComposeMatch(StrategyPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        var matchup = pool.GetRandomMatchup(4);
        var slots = ImmutableDictionary.CreateBuilder<BonesPlayerId, IBonesPlayerSlot>();

        for (var i = 0; i < matchup.Entries.Count; i++)
        {
            var seat = BonesPlayerId.MinSeat + i;
            var playerId = new BonesPlayerId(seat);
            var entry = matchup.Entries[i];

            var slot = CreatePlayerSlot(entry);
            slots.Add(playerId, slot);
        }

        return new PopulationMatchComposition(slots.ToImmutable(), matchup.Entries);
    }

    private IBonesPlayerSlot CreatePlayerSlot(StrategyEntry entry)
    {
        if (entry.Kind == BonesStrategyKind.Script)
        {
            return new BonesScriptStrategyPlayerSlot(
                _scriptHost,
                entry.Id,
                entry.Source,
                executionMode: BonesScriptExecutionMode.Direct);
        }

        // Non-script strategies fall back to first legal move.
        // Markdown strategies would require a play model provider and context;
        // that path is wired in P9 (multi-match context enhancement).
        return new BonesFirstLegalMovePlayerSlot();
    }
}

/// <summary>
/// The result of composing a population match: the seat→slot mapping
/// and the list of drawn strategy entries.
/// </summary>
public sealed record PopulationMatchComposition
{
    public PopulationMatchComposition(
        IReadOnlyDictionary<BonesPlayerId, IBonesPlayerSlot> playerSlots,
        IReadOnlyList<StrategyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(playerSlots);
        ArgumentNullException.ThrowIfNull(entries);

        if (playerSlots.Count != BonesTableConfig.FixedPlayerCount)
        {
            throw new ArgumentException(
                $"Exactly {BonesTableConfig.FixedPlayerCount} player slots must be assigned.",
                nameof(playerSlots));
        }

        if (entries.Count != BonesTableConfig.FixedPlayerCount)
        {
            throw new ArgumentException(
                $"Exactly {BonesTableConfig.FixedPlayerCount} entries must be present.",
                nameof(entries));
        }

        PlayerSlots = playerSlots;
        Entries = entries;
    }

    public IReadOnlyDictionary<BonesPlayerId, IBonesPlayerSlot> PlayerSlots { get; }

    public IReadOnlyList<StrategyEntry> Entries { get; }
}
