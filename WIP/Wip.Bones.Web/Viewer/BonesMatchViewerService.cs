using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Web.Viewer;

public sealed class BonesMatchViewerService
{
    private readonly BonesGameEngine _engine;
    private readonly BonesRoundConfig? _roundConfig;
    private readonly BonesBoardVisualLayoutBuilder _layoutBuilder;

    public BonesMatchViewerService(
        BonesGameEngine? engine = null,
        BonesRoundConfig? roundConfig = null,
        BonesBoardVisualLayoutBuilder? layoutBuilder = null)
    {
        _engine = engine ?? new BonesGameEngine();
        _roundConfig = roundConfig;
        _layoutBuilder = layoutBuilder ?? new BonesBoardVisualLayoutBuilder();
    }

    public BonesMatchViewModel BuildSnapshot(
        BonesRoundState state,
        IReadOnlyDictionary<BonesPlayerId, int>? cumulativeScores = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var scores = cumulativeScores ?? CreateZeroScores();
        int? roundPipScore = null;
        if (state.Outcome is not null)
            roundPipScore = _engine.ScoreRound(state).Points;

        var playEvents = state.EventLog
            .Where(static e => e.Kind == BonesEventKind.Play)
            .ToArray();

        return new BonesMatchViewModel
        {
            SessionId = string.Empty,
            MatchId = state.GameId.Value,
            LeftEndPip = state.Board.LeftEnd?.Pip.Value,
            RightEndPip = state.Board.RightEnd?.Pip.Value,
            HandTileCountsBySeat = BuildHandCounts(state.Hands),
            BoardLayout = _layoutBuilder.BuildLayout(state.Board, playEvents),
            HandTilesBySeat = BuildHandTiles(state.Hands),
            PlayerColorsBySeat = BonesPlayerColorPalette.PlayerColorsBySeat,
            ActiveSeat = state.CurrentPlayer.Seat,
            CumulativeScoresBySeat = ToSeatScores(scores),
            FrameCount = state.EventLog.Length,
            Revision = 0,
            WinnerSeat = state.Winner?.Seat,
            RoundPipScore = roundPipScore,
            IsComplete = state.Outcome is not null,
        };
    }

    public BonesMatchViewModel BuildMatchSnapshot(
        SessionId sessionId,
        BonesRegisteredMatch registeredMatch)
    {
        ArgumentNullException.ThrowIfNull(registeredMatch);

        var finalState = ReplayMatchState(registeredMatch);
        var snapshot = BuildSnapshot(finalState, registeredMatch.MatchResult.CumulativeScores);
        int? lastRoundScore = registeredMatch.MatchResult.Rounds.Length > 0
            ? registeredMatch.MatchResult.Rounds[^1].Score.Points
            : null;

        return snapshot with
        {
            SessionId = sessionId.Value,
            MatchId = registeredMatch.MatchId.Value,
            FrameCount = registeredMatch.MatchResult.Transcript.Length,
            Revision = registeredMatch.Revision.Value,
            WinnerSeat = registeredMatch.IsComplete ? registeredMatch.MatchResult.Winner.Seat : null,
            RoundPipScore = lastRoundScore,
            IsComplete = registeredMatch.IsComplete,
        };
    }

    public IReadOnlyList<BonesMatchFrame> BuildTimeline(IReadOnlyList<BonesEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (_roundConfig is not null)
            return BuildRoundTimeline(_roundConfig, events);

        throw new InvalidOperationException(
            "Round configuration is required to build a timeline from event logs.");
    }

    public IReadOnlyList<BonesMatchFrame> BuildMatchTimeline(BonesRegisteredMatch registeredMatch)
    {
        ArgumentNullException.ThrowIfNull(registeredMatch);

        var frames = new List<BonesMatchFrame>();
        var transcript = registeredMatch.MatchResult.Transcript;
        if (transcript.Length == 0)
            return frames;

        if (registeredMatch.MatchResult.Rounds.Length == 0)
        {
            var roundConfig = CreateRoundConfig(registeredMatch, roundNumber: 1);
            return BuildRoundTimeline(roundConfig, transcript);
        }

        var roundEventCounts = registeredMatch.MatchResult.Rounds
            .Select(static round => round.EventLog.Length)
            .ToArray();

        var roundIndex = 0;
        var roundEventIndex = 0;
        var state = _engine.StartRound(CreateRoundConfig(registeredMatch, roundNumber: 1));

        for (var globalEventIndex = 0; globalEventIndex < transcript.Length; globalEventIndex++)
        {
            var roundEvent = transcript[globalEventIndex];
            var move = ToMove(state, roundEvent);
            state = _engine.ApplyMove(state, move);
            frames.Add(CreateFrame(roundEvent, state, registeredMatch.Revision.Value));

            roundEventIndex++;
            if (roundIndex < roundEventCounts.Length && roundEventIndex >= roundEventCounts[roundIndex])
            {
                roundIndex++;
                roundEventIndex = 0;
            }

            if (roundEventIndex == 0 && globalEventIndex + 1 < transcript.Length)
                state = _engine.StartRound(CreateRoundConfig(registeredMatch, roundIndex + 1));
        }

        return frames;
    }

    public BonesMatchViewModel LoadFromTranscript(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var markdown = File.ReadAllText(path);
        var parsed = BonesMatchTranscriptParser.Parse(markdown);
        var roundConfig = new BonesRoundConfig(parsed.GameId, shuffleSeed: 0);
        var roundService = new BonesMatchViewerService(_engine, roundConfig);
        var finalState = parsed.Events.Count == 0
            ? _engine.StartRound(roundConfig)
            : ReplayFinalRoundState(parsed.Events, roundConfig);

        var snapshot = BuildSnapshot(finalState, parsed.CumulativeScores);
        return snapshot with
        {
            MatchId = parsed.GameId.Value,
            FrameCount = parsed.Events.Count,
            WinnerSeat = parsed.Winner?.Seat,
            IsComplete = parsed.Winner is not null,
        };
    }

    internal IReadOnlyList<BonesMatchFrame> BuildRoundTimeline(
        BonesRoundConfig roundConfig,
        IReadOnlyList<BonesEvent> events)
    {
        var frames = new List<BonesMatchFrame>(events.Count);
        var state = _engine.StartRound(roundConfig);

        foreach (var roundEvent in events.OrderBy(static e => e.TurnIndex))
        {
            var move = ToMove(state, roundEvent);
            state = _engine.ApplyMove(state, move);
            frames.Add(CreateFrame(roundEvent, state, revision: 0));
        }

        return frames;
    }

    private BonesRoundState ReplayMatchState(BonesRegisteredMatch registeredMatch)
    {
        var transcript = registeredMatch.MatchResult.Transcript;
        if (transcript.Length == 0)
            return _engine.StartRound(CreateRoundConfig(registeredMatch, roundNumber: 1));

        if (registeredMatch.MatchResult.Rounds.Length == 0)
            return ReplayFinalRoundState(transcript, CreateRoundConfig(registeredMatch, roundNumber: 1));

        if (!registeredMatch.IsComplete
            && transcript.Length > registeredMatch.MatchResult.Rounds.Sum(static round => round.EventLog.Length))
        {
            return ReplayMultiRoundTranscript(registeredMatch, transcript);
        }

        var lastRound = registeredMatch.MatchResult.Rounds[^1];
        var lastRoundConfig = CreateRoundConfig(registeredMatch, lastRound.RoundNumber);
        return ReplayFinalRoundState(lastRound.EventLog, lastRoundConfig);
    }

    private BonesRoundState ReplayMultiRoundTranscript(
        BonesRegisteredMatch registeredMatch,
        IReadOnlyList<BonesEvent> transcript)
    {
        var roundEventCounts = registeredMatch.MatchResult.Rounds
            .Select(static round => round.EventLog.Length)
            .ToArray();

        var roundIndex = 0;
        var roundEventIndex = 0;
        var state = _engine.StartRound(CreateRoundConfig(registeredMatch, roundNumber: 1));

        for (var globalEventIndex = 0; globalEventIndex < transcript.Count; globalEventIndex++)
        {
            var roundEvent = transcript[globalEventIndex];
            var move = ToMove(state, roundEvent);
            state = _engine.ApplyMove(state, move);

            roundEventIndex++;
            if (roundIndex < roundEventCounts.Length && roundEventIndex >= roundEventCounts[roundIndex])
            {
                roundIndex++;
                roundEventIndex = 0;
            }

            if (roundEventIndex == 0 && globalEventIndex + 1 < transcript.Count)
                state = _engine.StartRound(CreateRoundConfig(registeredMatch, roundIndex + 1));
        }

        return state;
    }

    private BonesRoundState ReplayFinalRoundState(IReadOnlyList<BonesEvent> events, BonesRoundConfig roundConfig)
    {
        var state = _engine.StartRound(roundConfig);
        foreach (var roundEvent in events.OrderBy(static e => e.TurnIndex))
        {
            var move = ToMove(state, roundEvent);
            state = _engine.ApplyMove(state, move);
        }

        return state;
    }

    private BonesRoundConfig CreateRoundConfig(BonesRegisteredMatch registeredMatch, int roundNumber)
    {
        var roundSeed = HashCode.Combine(registeredMatch.MatchSeed, roundNumber);
        var roundGameId = new BonesGameId($"{registeredMatch.MatchId.Value}-r{roundNumber}");
        return new BonesRoundConfig(roundGameId, roundSeed);
    }

    private static BonesMove ToMove(BonesRoundState state, BonesEvent roundEvent)
    {
        if (roundEvent.Kind == BonesEventKind.Pass)
            return BonesMove.Pass(CreateMoveId(state, roundEvent), roundEvent.PlayerId);

        if (roundEvent.Tile is null || roundEvent.Side is null)
            throw new InvalidOperationException($"Play event at turn {roundEvent.TurnIndex} is missing tile or side.");

        return BonesMove.Play(
            CreateMoveId(state, roundEvent),
            roundEvent.PlayerId,
            roundEvent.Tile.Value,
            roundEvent.Side.Value);
    }

    private static BonesMoveId CreateMoveId(BonesRoundState state, BonesEvent roundEvent)
    {
        if (roundEvent.Kind == BonesEventKind.Pass)
            return new BonesMoveId($"pass-{roundEvent.TurnIndex}-seat-{roundEvent.PlayerId.Seat}");

        var tile = roundEvent.Tile!.Value;
        return new BonesMoveId(
            $"play-{roundEvent.TurnIndex}-seat-{roundEvent.PlayerId.Seat}-{tile.LowPip.Value}-{tile.HighPip.Value}-{roundEvent.Side}");
    }

    private BonesMatchFrame CreateFrame(BonesEvent roundEvent, BonesRoundState state, long revision)
    {
        var playEvents = state.EventLog
            .Where(static e => e.Kind == BonesEventKind.Play)
            .ToArray();

        int? roundPipScore = null;
        if (state.Outcome is not null)
            roundPipScore = _engine.ScoreRound(state).Points;

        return new BonesMatchFrame
        {
            TurnIndex = roundEvent.TurnIndex,
            Revision = revision,
            ActiveSeat = state.CurrentPlayer.Seat,
            LeftEndPip = state.Board.LeftEnd?.Pip.Value,
            RightEndPip = state.Board.RightEnd?.Pip.Value,
            HandTileCountsBySeat = BuildHandCounts(state.Hands),
            BoardLayout = _layoutBuilder.BuildLayout(state.Board, playEvents),
            HandTilesBySeat = BuildHandTiles(state.Hands),
            PlayerColorsBySeat = BonesPlayerColorPalette.PlayerColorsBySeat,
            EventKind = roundEvent.Kind,
            PlayedTileLowPip = roundEvent.Tile?.LowPip.Value,
            PlayedTileHighPip = roundEvent.Tile?.HighPip.Value,
            BoardSide = roundEvent.Side,
            IsRoundComplete = state.Outcome is not null,
            WinnerSeat = state.Winner?.Seat,
            RoundPipScore = roundPipScore,
        };
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<BonesHandTileView>> BuildHandTiles(BonesHands hands)
    {
        var tilesBySeat = new Dictionary<int, IReadOnlyList<BonesHandTileView>>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            var handTiles = hands.GetHand(playerId).Tiles
                .Select(tile => new BonesHandTileView
                {
                    LowPip = tile.LowPip.Value,
                    HighPip = tile.HighPip.Value,
                    OwnerSeat = seat,
                })
                .ToArray();

            tilesBySeat[seat] = handTiles;
        }

        return tilesBySeat;
    }

    private static IReadOnlyDictionary<int, int> BuildHandCounts(BonesHands hands)
    {
        var counts = new Dictionary<int, int>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            counts[seat] = hands.GetHand(playerId).Tiles.Length;
        }

        return counts;
    }

    private static IReadOnlyDictionary<int, int> ToSeatScores(IReadOnlyDictionary<BonesPlayerId, int> scores)
    {
        var seatScores = new Dictionary<int, int>();
        foreach (var (playerId, score) in scores)
            seatScores[playerId.Seat] = score;

        return seatScores;
    }

    private static IReadOnlyDictionary<BonesPlayerId, int> CreateZeroScores()
    {
        var scores = new Dictionary<BonesPlayerId, int>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            scores[new BonesPlayerId(seat)] = 0;

        return scores;
    }
}
