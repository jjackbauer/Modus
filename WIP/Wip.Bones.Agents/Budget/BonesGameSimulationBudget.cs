namespace Wip.Bones.Agents.Budget;

public sealed class BonesGameSimulationBudgetExhaustedException : InvalidOperationException
{
    public BonesGameSimulationBudgetExhaustedException(string message)
        : base(message)
    {
    }
}

public sealed class BonesGameSimulationBudget
{
    private int _gamesSimulated;
    private int _reservedGames;
    private readonly object _gate = new();

    public BonesGameSimulationBudget(int maxGamesPerRun = 0, SemaphoreSlim? gameConcurrencyThrottle = null)
    {
        if (maxGamesPerRun < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxGamesPerRun),
                maxGamesPerRun,
                "MaxGamesPerRun cannot be negative.");
        }

        MaxGamesPerRun = maxGamesPerRun;
        GameConcurrencyThrottle = gameConcurrencyThrottle;
    }

    public int GamesSimulated => Volatile.Read(ref _gamesSimulated);

    public int ReservedGames
    {
        get
        {
            lock (_gate)
                return _reservedGames;
        }
    }

    public int RemainingGames
    {
        get
        {
            if (MaxGamesPerRun <= 0)
                return int.MaxValue;

            lock (_gate)
                return Math.Max(0, MaxGamesPerRun - _gamesSimulated - _reservedGames);
        }
    }

    public bool IsExhausted => MaxGamesPerRun > 0 && RemainingGames <= 0;

    public int MaxGamesPerRun { get; }

    /// <summary>
    /// Optional semaphore that limits total concurrent games across all sessions.
    /// When set, play tools acquire this before running a game.
    /// </summary>
    public SemaphoreSlim? GameConcurrencyThrottle { get; set; }

    public bool IsPerSessionBudget { get; set; }

    public int SessionCount { get; set; } = 1;

    /// <summary>
    /// Atomically reserves capacity for a batch of games without incrementing
    /// <see cref="GamesSimulated"/>. Reservations must be settled with
    /// <see cref="RecordGames"/> (normal completion) or released with
    /// <see cref="ReleaseReservation"/> (failure/cancellation).
    /// </summary>
    public bool TryReserve(int gameCount)
    {
        if (gameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gameCount));
        }

        if (gameCount == 0)
            return true;

        if (MaxGamesPerRun <= 0)
            return true;

        lock (_gate)
        {
            if (_gamesSimulated + _reservedGames + gameCount <= MaxGamesPerRun)
            {
                _reservedGames += gameCount;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Records games that actually completed. Any matching reservation is settled
    /// first so GamesSimulated + ReservedGames never double-counts the same game.
    /// </summary>
    public void RecordGames(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
            return;

        lock (_gate)
        {
            _gamesSimulated += count;
            _reservedGames = Math.Max(0, _reservedGames - count);
        }
    }

    /// <summary>
    /// Releases capacity reserved for work that did not complete.
    /// </summary>
    public void ReleaseReservation(int gameCount)
    {
        if (gameCount <= 0)
            return;

        lock (_gate)
            _reservedGames = Math.Max(0, _reservedGames - gameCount);
    }

    /// <summary>
    /// Returns the per-session budget when <see cref="IsPerSessionBudget"/> is true.
    /// Each session gets MaxGamesPerRun / SessionCount games.
    /// </summary>
    public int GetPerSessionMaxGames()
    {
        if (!IsPerSessionBudget || SessionCount <= 1)
            return MaxGamesPerRun;

        return MaxGamesPerRun / SessionCount;
    }
}
