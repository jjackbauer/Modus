namespace Wip.Bones.Agents.Budget;

public sealed class BonesGameSimulationBudget
{
    private int _gamesSimulated;
    private readonly object _gate = new();

    public BonesGameSimulationBudget(int maxGamesPerRun = 0)
    {
        if (maxGamesPerRun < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxGamesPerRun),
                maxGamesPerRun,
                "MaxGamesPerRun cannot be negative.");
        }

        MaxGamesPerRun = maxGamesPerRun;
    }

    public int GamesSimulated => Volatile.Read(ref _gamesSimulated);

    public int MaxGamesPerRun { get; }

    public bool TryReserve(int gameCount)
    {
        if (gameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gameCount));
        }

        if (MaxGamesPerRun <= 0)
            return true;

        lock (_gate)
        {
            var current = Volatile.Read(ref _gamesSimulated);
            if (current + gameCount <= MaxGamesPerRun)
            {
                Interlocked.Add(ref _gamesSimulated, gameCount);
                return true;
            }

            return false;
        }
    }

    public void RecordGames(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (count > 0)
            Interlocked.Add(ref _gamesSimulated, count);
    }
}