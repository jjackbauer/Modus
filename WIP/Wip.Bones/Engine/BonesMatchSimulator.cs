using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Engine;

public sealed class BonesMatchSimulator
{
    private readonly BonesGameEngine _engine = new();

    public async ValueTask<BonesMatchResult> RunMatchAsync(
        BonesMatchConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!AllSlotsAreAsync(config))
        {
            var engine = new BonesGameEngine();
            return await Task.Run(() => RunMatchWithEngine(engine, config), cancellationToken).ConfigureAwait(false);
        }

        var cumulativeScores = InitializeScores();
        var rounds = new List<BonesMatchRoundRecord>();
        var transcript = new List<BonesEvent>();
        var roundNumber = 0;
        BonesPlayerId? matchWinner = null;

        while (matchWinner is null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            roundNumber++;
            var roundSeed = DeriveRoundSeed(config.Seed, roundNumber);
            var roundGameId = new BonesGameId($"{config.GameId.Value}-r{roundNumber}");
            var roundConfig = new BonesRoundConfig(roundGameId, roundSeed);
            var state = _engine.StartRound(roundConfig);

            while (!_engine.IsRoundComplete(state))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var activePlayer = state.CurrentPlayer;
                var legalMoves = _engine.GetLegalMoves(state, activePlayer);
                var asyncSlot = (IAsyncBonesPlayerSlot)config.PlayerSlots[activePlayer];
                var move = await asyncSlot.ChooseMoveAsync(state, legalMoves, cancellationToken)
                    .ConfigureAwait(false);
                state = _engine.ApplyMove(state, move);
            }

            var roundScore = _engine.ScoreRound(state);
            rounds.Add(new BonesMatchRoundRecord(roundNumber, roundScore, state.EventLog));
            AppendRoundToTranscript(transcript, state.EventLog);

            cumulativeScores = cumulativeScores.SetItem(
                roundScore.Winner,
                cumulativeScores[roundScore.Winner] + roundScore.Points);

            if (cumulativeScores[roundScore.Winner] >= config.TargetScore)
                matchWinner = roundScore.Winner;
        }

        return new BonesMatchResult(
            config.GameId,
            matchWinner.Value,
            cumulativeScores,
            rounds,
            transcript);
    }

    public BonesMatchResult RunMatch(BonesMatchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return RunMatchWithEngine(_engine, config);
    }

    private static BonesMatchResult RunMatchWithEngine(BonesGameEngine engine, BonesMatchConfig config)
    {
        var cumulativeScores = InitializeScores();
        var rounds = new List<BonesMatchRoundRecord>();
        var transcript = new List<BonesEvent>();
        var roundNumber = 0;
        BonesPlayerId? matchWinner = null;

        while (matchWinner is null)
        {
            roundNumber++;
            var roundSeed = DeriveRoundSeed(config.Seed, roundNumber);
            var roundGameId = new BonesGameId($"{config.GameId.Value}-r{roundNumber}");
            var roundConfig = new BonesRoundConfig(roundGameId, roundSeed);
            var state = engine.StartRound(roundConfig);

            while (!engine.IsRoundComplete(state))
            {
                var activePlayer = state.CurrentPlayer;
                var legalMoves = engine.GetLegalMoves(state, activePlayer);
                var move = config.PlayerSlots[activePlayer].ChooseMove(state, legalMoves);
                state = engine.ApplyMove(state, move);
            }

            var roundScore = engine.ScoreRound(state);
            rounds.Add(new BonesMatchRoundRecord(roundNumber, roundScore, state.EventLog));
            AppendRoundToTranscript(transcript, state.EventLog);

            cumulativeScores = cumulativeScores.SetItem(
                roundScore.Winner,
                cumulativeScores[roundScore.Winner] + roundScore.Points);

            if (cumulativeScores[roundScore.Winner] >= config.TargetScore)
                matchWinner = roundScore.Winner;
        }

        return new BonesMatchResult(
            config.GameId,
            matchWinner.Value,
            cumulativeScores,
            rounds,
            transcript);
    }

    private static ImmutableDictionary<BonesPlayerId, int> InitializeScores()
    {
        var scores = ImmutableDictionary.CreateBuilder<BonesPlayerId, int>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            scores.Add(new BonesPlayerId(seat), 0);

        return scores.ToImmutable();
    }

    private static int DeriveRoundSeed(int matchSeed, int roundNumber) =>
        HashCode.Combine(matchSeed, roundNumber);

    private static void AppendRoundToTranscript(List<BonesEvent> transcript, IReadOnlyList<BonesEvent> roundEvents)
    {
        var turnOffset = transcript.Count;
        foreach (var roundEvent in roundEvents)
        {
            transcript.Add(new BonesEvent(
                turnOffset + roundEvent.TurnIndex,
                roundEvent.PlayerId,
                roundEvent.Kind,
                roundEvent.Tile,
                roundEvent.Side));
        }
    }

    private static bool AllSlotsAreAsync(BonesMatchConfig config)
    {
        foreach (var slot in config.PlayerSlots.Values)
        {
            if (slot is not IAsyncBonesPlayerSlot)
                return false;
        }

        return true;
    }
}
