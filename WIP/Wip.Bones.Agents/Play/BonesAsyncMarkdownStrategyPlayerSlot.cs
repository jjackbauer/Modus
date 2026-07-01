using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Play;

public sealed class BonesAsyncMarkdownStrategyPlayerSlot : IAsyncBonesPlayerSlot
{
    private const int MaxInvalidSelectionRetries = 1;

    private readonly BonesStrategyDocument _strategy;
    private readonly IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult> _modelProvider;
    private readonly string _modelId;
    private readonly CapabilityContext _context;

    public BonesAsyncMarkdownStrategyPlayerSlot(
        BonesStrategyDocument strategy,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult> modelProvider,
        CapabilityContext context,
        string modelId)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _modelId = string.IsNullOrWhiteSpace(modelId) ? "bones-play-turn" : modelId;
    }

    public async ValueTask<BonesMove> ChooseMoveAsync(
        BonesRoundState state,
        IReadOnlyList<BonesMove> legalMoves,
        CancellationToken cancellationToken)
    {
        if (legalMoves.Count == 0)
            throw new InvalidOperationException("No legal moves available for markdown strategy player slot.");

        var retryAttempt = 0;
        while (true)
        {
            var turnRequest = BonesPlayMatchPromptBuilder.BuildTurnRequest(
                _strategy.PlayerId,
                _strategy,
                state,
                legalMoves,
                retryAttempt);

            var correlationId =
                $"bones-promotion-markdown-seat-{_strategy.PlayerId.Seat}-t{state.EventLog.Length}-r{retryAttempt}-{_context.SessionId.Value}";

            var modelResponse = await _modelProvider.ExecuteAsync(
                new ModelProviderRequest<BonesPlayTurnRequest>(
                    payload: turnRequest,
                    modelId: _modelId,
                    correlationId: correlationId),
                _context,
                cancellationToken).ConfigureAwait(false);

            var resolvedMove = ResolveSelectedMove(legalMoves, modelResponse.Payload.SelectedMoveId);
            if (resolvedMove is not null)
                return resolvedMove;

            if (retryAttempt >= MaxInvalidSelectionRetries)
                return legalMoves[0];

            retryAttempt++;
        }
    }

    private static BonesMove? ResolveSelectedMove(IReadOnlyList<BonesMove> legalMoves, string selectedMoveId)
    {
        if (string.IsNullOrWhiteSpace(selectedMoveId))
            return null;

        foreach (var legalMove in legalMoves)
        {
            if (string.Equals(legalMove.MoveId.Value, selectedMoveId, StringComparison.Ordinal))
                return legalMove;
        }

        return null;
    }
}
