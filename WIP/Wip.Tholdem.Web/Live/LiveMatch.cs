using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Web.Live;

/// <summary>
/// One paced live match for two to six seats. The worker replays exactly the simulator's rules —
/// hand <c>n</c> uses <c>SeedDerivation.Derive(seed, n, 0, 0, 0)</c>, <c>handNumber = n</c>,
/// <c>buttonSeat = (n - 1) % seats</c>, the configured per-seat starting stacks and a per-seat turn
/// action index — while the configured delay only paces progress. It never influences an agent
/// decision.
/// </summary>
public sealed class LiveMatch
{
    private const int MaxRetainedActions = 200;

    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IPokerAgent[] _agents;
    private readonly SimulationOptions _rules;
    private readonly long[] _startingStacks;
    private readonly List<SpectatorAction> _actions = [];
    private readonly List<string> _handHashes = [];
    private TableState? _state;
    private string _status = "running";
    private int _buttonSeat;
    private Task? _worker;

    internal LiveMatch(
        string id,
        ulong seed,
        int hands,
        int delayMs,
        IReadOnlyList<IPokerAgent> agents,
        IReadOnlyList<long>? startingStacks = null)
    {
        ArgumentNullException.ThrowIfNull(agents);
        if (agents.Count is < HoldemHandEngine.MinimumSeats or > HoldemHandEngine.MaximumSeats)
        {
            throw new ArgumentOutOfRangeException(
                nameof(agents),
                agents.Count,
                $"A live match must have between {HoldemHandEngine.MinimumSeats} and "
                + $"{HoldemHandEngine.MaximumSeats} seats.");
        }

        Id = id;
        Seed = seed;
        Hands = hands;
        DelayMs = delayMs;
        _agents = agents.ToArray();
        Agents = _agents.Select(static agent => agent.Name).ToArray();
        Seat0Agent = Agents[0];
        Seat1Agent = Agents[1];
        _rules = new SimulationOptions(seed, hands, StartingStacks: startingStacks);
        _startingStacks = startingStacks?.ToArray()
            ?? Enumerable.Repeat(_rules.StartingStack, _agents.Length).ToArray();
    }

    public string Id { get; }

    public ulong Seed { get; }

    public int Hands { get; }

    public int DelayMs { get; }

    public int SeatCount => _agents.Length;

    public IReadOnlyList<string> Agents { get; }

    public string Seat0Agent { get; }

    public string Seat1Agent { get; }

    /// <summary>True while the match is running or paused; completed/stopped matches are inactive.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _status is "running" or "paused";
            }
        }
    }

    internal void Start()
    {
        _worker = Task.Run(RunAsync);
    }

    public SpectatorSnapshot Snapshot()
    {
        lock (_gate)
        {
            var state = _state;
            var actions = _actions.Count <= MaxRetainedActions
                ? _actions.ToArray()
                : _actions.GetRange(_actions.Count - MaxRetainedActions, MaxRetainedActions).ToArray();
            var seats = _agents.Length;
            var holeCards = new IReadOnlyList<Card>[seats];
            for (var seat = 0; seat < seats; seat++)
            {
                // Hole cards are internal to TableState; PlayerObservation is the public projection
                // and is used here for each seat separately.
                holeCards[seat] = state is null
                    ? Array.Empty<Card>()
                    : PlayerObservation.ForSeat(state, seat).OwnHoleCards;
            }

            var stacks = state?.Stacks.ToArray() ?? _startingStacks.ToArray();
            var handNames = state is not null && state.Board.Count >= 3
                ? HandNaming.ForSeats(state.Board, holeCards)
                : Enumerable.Repeat(HandNaming.Preflop, seats).ToArray();
            var amountToCall = 0L;
            var potOdds = 0d;
            var actingSeat = -1;
            var lastActionSeat = -1;
            var potAwarded = 0L;
            if (state is not null)
            {
                // Only a live (running/paused) match has a seat to act. The ring is the turn cue, so a
                // completed or stopped hand exposes -1 instead of the last actor.
                if (_status is "running" or "paused" && !state.IsComplete && state.ActingSeat >= 0)
                {
                    actingSeat = state.ActingSeat;
                    amountToCall = Math.Max(0L, state.CurrentBet - state.StreetCommitted[state.ActingSeat]);
                    if (amountToCall > 0)
                    {
                        potOdds = (double)amountToCall / (state.Pot + amountToCall);
                    }
                }

                for (var index = _actions.Count - 1; index >= 0; index--)
                {
                    if (_actions[index].HandNumber == state.HandNumber)
                    {
                        lastActionSeat = _actions[index].Seat;
                        break;
                    }
                }

                if (state.IsComplete)
                {
                    potAwarded = HandRecord.From(state).PotAwarded;
                }
            }

            var buttonSeat = state?.ButtonSeat ?? _buttonSeat;
            var complete = state?.IsComplete ?? false;
            var winners = state?.Winners.ToArray() ?? [];
            var spectatorSeats = new SpectatorSeat[seats];
            for (var seat = 0; seat < seats; seat++)
            {
                spectatorSeats[seat] = new SpectatorSeat(
                    seat,
                    _agents[seat].Name,
                    stacks[seat],
                    Cards(holeCards[seat]),
                    state?.Folded[seat] ?? false,
                    state?.AllIn[seat] ?? false,
                    handNames[seat],
                    complete && winners.Contains(seat),
                    buttonSeat == seat);
            }

            return new SpectatorSnapshot(
                Id,
                _status,
                state?.HandNumber ?? 0,
                state?.Street ?? Street.Preflop,
                Cards(state?.Board),
                state?.Pot ?? 0,
                potAwarded,
                stacks,
                spectatorSeats[0].HoleCards,
                spectatorSeats[1].HoleCards,
                buttonSeat,
                actingSeat,
                lastActionSeat,
                handNames.ToArray(),
                amountToCall,
                potOdds,
                complete,
                state?.ShowdownReached ?? false,
                winners,
                _handHashes.ToArray(),
                actions,
                spectatorSeats);
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_status == "running")
            {
                _status = "paused";
            }
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_status == "paused")
            {
                _status = "running";
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _status = "stopped";
        }

        _cancellation.Cancel();
    }

    private async Task RunAsync()
    {
        var token = _cancellation.Token;
        try
        {
            for (var hand = 1; hand <= Hands; hand++)
            {
                await WaitWhilePausedAsync(token);
                token.ThrowIfCancellationRequested();

                var handSeed = SeedDerivation.Derive(Seed, hand, 0, 0, 0UL);
                var buttonSeat = (hand - 1) % _agents.Length;
                var table = HoldemHandEngine.Start(
                    handSeed,
                    _agents.Length,
                    hand,
                    _rules.StartingStack,
                    _rules.SmallBlind,
                    _rules.BigBlind,
                    buttonSeat,
                    _rules.StartingStacks);

                lock (_gate)
                {
                    _state = table;
                    _buttonSeat = table.ButtonSeat;
                }

                await PaceAsync(token);

                var actionCounts = new int[_agents.Length];
                while (!table.IsComplete)
                {
                    await WaitWhilePausedAsync(token);
                    token.ThrowIfCancellationRequested();

                    var seat = table.ActingSeat;
                    var observation = PlayerObservation.ForSeat(table, seat);
                    var context = new AgentTurnContext(
                        hand,
                        seat,
                        actionCounts[seat]);
                    var action = await _agents[seat].ActAsync(observation, context, token);
                    actionCounts[seat]++;

                    var actionStreet = table.Street;
                    table = HoldemHandEngine.Apply(table, action);

                    lock (_gate)
                    {
                        _state = table;
                        _actions.Add(new SpectatorAction(
                            hand,
                            actionStreet,
                            seat,
                            action.Kind.ToString(),
                            action.Amount,
                            table.Pot));
                    }

                    await PaceAsync(token);
                }

                var hash = HandRecord.From(table).Hash;
                lock (_gate)
                {
                    _handHashes.Add(hash);
                }
            }

            lock (_gate)
            {
                if (_status != "stopped")
                {
                    _status = "complete";
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _status = "stopped";
            }
        }
        catch (Exception)
        {
            lock (_gate)
            {
                _status = "stopped";
            }
        }
    }

    private async Task WaitWhilePausedAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_status != "paused")
                {
                    return;
                }
            }

            await Task.Delay(5, token);
        }
    }

    private async Task PaceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (DelayMs > 0)
        {
            await Task.Delay(DelayMs, token);
        }
        else
        {
            await Task.Yield();
        }
    }

    private static string[] Cards(IReadOnlyList<Card>? cards) =>
        cards is null ? [] : cards.Select(static card => card.ToString()).ToArray();
}
