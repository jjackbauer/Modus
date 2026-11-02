using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Engine;

public enum BonesScriptExecutionMode
{
    TimeoutGuarded = 0,
    Direct = 1,
}

public sealed class BonesScriptStrategyPlayerSlot : IBonesPlayerSlot
{
    private readonly BonesStrategyScriptHost _host;
    private readonly BonesStrategyId _strategyId;
    private readonly string _source;
    private readonly IBonesScriptStrategyExecutionWarningSink? _warningSink;
    private readonly BonesScriptExecutionMode _executionMode;
    private BonesCompiledStrategy? _compiled;

    public BonesScriptStrategyPlayerSlot(
        BonesStrategyScriptHost host,
        BonesStrategyId strategyId,
        string source,
        IBonesScriptStrategyExecutionWarningSink? warningSink = null,
        BonesScriptExecutionMode executionMode = BonesScriptExecutionMode.TimeoutGuarded)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _strategyId = strategyId;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _warningSink = warningSink;
        _executionMode = executionMode;
    }

    public BonesScriptStrategyPlayerSlot(
        BonesStrategyScriptHost host,
        BonesCompiledStrategy compiled,
        IBonesScriptStrategyExecutionWarningSink? warningSink = null,
        BonesScriptExecutionMode executionMode = BonesScriptExecutionMode.TimeoutGuarded)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentNullException.ThrowIfNull(compiled);

        _strategyId = compiled.StrategyId;
        _source = string.Empty;
        _warningSink = warningSink;
        _executionMode = executionMode;
        _compiled = compiled;
    }

    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(legalMoves);

        if (legalMoves.Count == 0)
            throw new InvalidOperationException("No legal moves are available for the active player.");

        var compiled = EnsureCompiled();
        var selectedMove = _executionMode == BonesScriptExecutionMode.Direct
            ? _host.ExecuteChooseMoveDirect(compiled, state, legalMoves)
            : _host.ExecuteChooseMove(compiled, state, legalMoves);

        if (IsContainedInLegalMoves(selectedMove, legalMoves))
            return selectedMove;

        var fallbackMove = legalMoves[0];
        _warningSink?.Emit(new BonesScriptStrategyExecutionWarning(
            _strategyId,
            selectedMove,
            fallbackMove,
            "Script selected a move that is not in the legal set. Falling back to first legal move."));

        return fallbackMove;
    }

    private BonesCompiledStrategy EnsureCompiled()
    {
        if (_compiled is not null)
            return _compiled;

        var compileResult = _host.TryCompile(_strategyId, _source);
        if (!compileResult.Succeeded || compileResult.Compiled is null)
        {
            throw new InvalidOperationException(
                compileResult.FailureReason ?? "Strategy script compilation failed.");
        }

        _compiled = compileResult.Compiled;
        return _compiled;
    }

    private static bool IsContainedInLegalMoves(BonesMove selectedMove, IReadOnlyList<BonesMove> legalMoves)
    {
        foreach (var legalMove in legalMoves)
        {
            if (legalMove.Equals(selectedMove))
                return true;
        }

        return false;
    }
}
