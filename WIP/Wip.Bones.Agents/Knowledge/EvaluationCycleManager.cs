namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// Tracks game count and triggers evaluation cycles for the strategy pool.
/// After a configurable number of cross-table games (default: poolSize × 8),
/// the orchestrator enters evaluation mode, stops composing new matches,
/// and calls EvaluateAndPromote on the pool.
///
/// While in evaluation, <see cref="ShouldComposeNewMatch"/> returns false,
/// preventing new strategies from entering the pool until the evaluation
/// cycle completes.
/// </summary>
public sealed class EvaluationCycleManager
{
    private readonly int _evaluationCycleSize;
    private int _totalGamesSinceLastEval;

    /// <summary>
    /// Creates an evaluation cycle manager with the configured cycle size.
    /// </summary>
    /// <param name="evaluationCycleSize">
    /// Number of cross-table games between evaluation cycles. Default: poolSize × 8.
    /// </param>
    public EvaluationCycleManager(int evaluationCycleSize)
    {
        if (evaluationCycleSize < 1)
            throw new ArgumentOutOfRangeException(
                nameof(evaluationCycleSize), evaluationCycleSize,
                "Evaluation cycle size must be at least 1.");

        _evaluationCycleSize = evaluationCycleSize;
        _totalGamesSinceLastEval = 0;
    }

    /// <summary>
    /// The number of games that triggers an evaluation cycle.
    /// </summary>
    public int EvaluationCycleSize => _evaluationCycleSize;

    /// <summary>
    /// The number of cross-table games played since the last evaluation cycle.
    /// </summary>
    public int TotalGamesSinceLastEval => _totalGamesSinceLastEval;

    /// <summary>
    /// True when the orchestrator is currently in an evaluation cycle.
    /// During evaluation, no new matches should be composed.
    /// </summary>
    public bool IsInEvaluation { get; private set; }

    /// <summary>
    /// Returns true when the accumulated game count meets or exceeds the
    /// evaluation cycle threshold. The orchestrator should then call
    /// <see cref="BeginEvaluation"/> to enter evaluation mode.
    /// </summary>
    public bool ShouldTriggerEvaluation()
    {
        if (IsInEvaluation)
            return false;

        return _totalGamesSinceLastEval >= _evaluationCycleSize;
    }

    /// <summary>
    /// Increments the game counter by one. Called after each cross-table
    /// game completes.
    /// </summary>
    public void RecordGamePlayed()
    {
        _totalGamesSinceLastEval++;
    }

    /// <summary>
    /// Resets the game counter to zero after an evaluation cycle completes.
    /// </summary>
    public void ResetAfterEvaluation()
    {
        _totalGamesSinceLastEval = 0;
    }

    /// <summary>
    /// Enters evaluation mode. After this, <see cref="ShouldComposeNewMatch"/>
    /// returns false and the orchestrator should call EvaluateAndPromote.
    /// </summary>
    public void BeginEvaluation()
    {
        IsInEvaluation = true;
    }

    /// <summary>
    /// Exits evaluation mode. After this, the orchestrator can resume
    /// composing matches and the game counter is reset.
    /// </summary>
    public void EndEvaluation()
    {
        IsInEvaluation = false;
        ResetAfterEvaluation();
    }

    /// <summary>
    /// Returns true when the orchestrator should compose a new cross-table
    /// match. During evaluation, this returns false so no new strategies
    /// enter the pool until evaluation completes.
    /// </summary>
    public bool ShouldComposeNewMatch()
    {
        return !IsInEvaluation;
    }
}
