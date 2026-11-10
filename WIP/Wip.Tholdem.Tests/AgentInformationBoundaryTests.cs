using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class AgentInformationBoundaryTests
{
    /// <summary>
    /// The exact nine cards the CLI recorded for session seed 42, hand 1 (the adversarial round's
    /// counterexample). The engine deals <c>deck[0], deck[2]</c> / <c>deck[1], deck[3]</c> and then
    /// <c>deck[4..8]</c> as the board, so these are the first nine cards of the session's deck.
    /// </summary>
    private const string RecordedSession42Hand1Cards = "Qs Tc 4s Jd 8h 8s 8d Th 6c";

    private const ulong RecordedSessionSeed = 42UL;

    private const int RecordedHand = 1;

    [Fact]
    public async Task ActAsync_GivenOneHundredObservations_ExpectedDeterministicLegalActions()
    {
        IPokerAgent[] agents =
        [
            new RandomLegalAgent("boundary-random"),
            new TightAggressiveAgent(name: "boundary-tight"),
            new PotOddsEquityAgent(name: "boundary-pot-odds"),
        ];

        for (var index = 0; index < 100; index++)
        {
            var seed = (ulong)(1000 + index);
            var state = HeadsUpHandEngine.Start(seed, handNumber: index + 1);
            var seat = index % 2;
            var observation = PlayerObservation.ForSeat(state, seat);
            var context = new AgentTurnContext(index + 1, seat, index % 5);

            foreach (var agent in agents)
            {
                var first = await agent.ActAsync(observation, context, CancellationToken.None);
                var second = await agent.ActAsync(observation, context, CancellationToken.None);

                Assert.Equal(first, second);
                Assert.Contains(first.Kind, observation.LegalActionKinds);
                Assert.InRange(first.Amount, 0L, observation.MaxRaiseTo);
            }
        }
    }

    [Fact]
    public void AgentSources_GivenAgentFiles_ExpectedNoHiddenInformationIdentifiers()
    {
        var files = AgentTestSupport.AgentSourceFiles();
        Assert.True(files.Length >= 6, $"Expected at least six agent source files, found {files.Length}.");

        var forbidden = new Regex(
            @"\b(TableState|HandRecord|HoleCardsSeat0|HoleCardsSeat1|Seed|Deck)\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var offenders = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            // The session seed itself is the value that made the deck reconstructible, so its name is
            // banned everywhere, including documentation comments: the regex above is anchored on word
            // boundaries and would miss it.
            if (text.Contains("SessionSeed", StringComparison.Ordinal))
            {
                offenders.Add($"{Path.GetFileName(file)}:SessionSeed");
            }

            // The identifier regex targets executable source. XML documentation comments are prose
            // that documents the boundary and cannot reference state; skipping them keeps the scan
            // from flagging the required explanation ("session seed", "deck"), while the unbounded
            // SessionSeed check above still covers every byte of the file. Ordinary comments and
            // string literals remain in scope of the regex.
            foreach (Match match in forbidden.Matches(WithoutXmlDocComments(text)))
            {
                offenders.Add($"{Path.GetFileName(file)}:{match.Value}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Agent sources reference hidden information: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The agent-facing turn context is public turn coordinates only. A seed of any width (or a
    /// string carrying one) would let an agent run the inversion the earlier rounds demonstrated.
    /// </summary>
    [Fact]
    public void AgentTurnContext_GivenReflection_ExpectedNoSeedValuedMember()
    {
        var properties = typeof(AgentTurnContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var seedMembers = properties
            .Where(static property =>
                (property.PropertyType == typeof(ulong)
                    || property.PropertyType == typeof(long)
                    || property.PropertyType == typeof(string))
                && property.Name.Contains("Seed", StringComparison.OrdinalIgnoreCase))
            .Select(static property => $"{property.Name}:{property.PropertyType.Name}")
            .ToArray();

        Assert.True(
            seedMembers.Length == 0,
            "AgentTurnContext must not expose a seed-valued property; found: " + string.Join(", ", seedMembers));

        var shape = properties
            .Select(static property => $"{property.Name}:{property.PropertyType.Name}")
            .OrderBy(static entry => entry, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "ActionIndex:Int32", "HandNumber:Int32", "Seat:Int32" }, shape);
    }

    /// <summary>
    /// Executable agent code must not mention a turn/session seed at all. The only permitted
    /// occurrences are comments that explain the absence of a seed.
    /// </summary>
    [Fact]
    public void AgentSources_GivenExecutableCode_ExpectedNoSeedIdentifier()
    {
        var offenders = new List<string>();
        var project = Path.Combine(AgentTestSupport.RepositoryRoot(), "WIP", "Wip.Tholdem.Agents");
        foreach (var name in new[] { "AgentTurnContext.cs", "IPokerAgent.cs" })
        {
            var path = Path.Combine(project, name);
            Assert.True(File.Exists(path), $"Missing agent boundary source: {path}");
            var code = WithoutComments(File.ReadAllText(path));
            if (code.Contains("Seed", StringComparison.Ordinal))
            {
                offenders.Add($"{name}:Seed");
            }
        }

        foreach (var file in AgentTestSupport.AgentSourceFiles())
        {
            var code = WithoutComments(File.ReadAllText(file));
            foreach (var token in new[] { "TurnSeed", "DeriveTurnSeed", "SessionSeed" })
            {
                if (code.Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{token}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Agent executable sources still reference a seed: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The runtime agent receives the same context; it must never read a seed from it. Its only
    /// input is the observation, which it serialises into the model task.
    /// </summary>
    [Fact]
    public void TholdemRuntimeAgent_GivenSource_ExpectedNeverReadsASeedFromContext()
    {
        var path = Path.Combine(
            AgentTestSupport.RepositoryRoot(),
            "WIP",
            "Wip.Tholdem.Agents",
            "Runtime",
            "TholdemRuntimeAgent.cs");
        Assert.True(File.Exists(path), $"Missing runtime agent source: {path}");

        var code = WithoutComments(File.ReadAllText(path));
        Assert.DoesNotContain("TurnSeed", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionSeed", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DeriveAgentSeed", code, StringComparison.Ordinal);
        var seedRead = Regex.Match(
            code,
            @"context\s*\.\s*\w*Seed\w*",
            RegexOptions.CultureInvariant);
        Assert.False(
            seedRead.Success,
            "TholdemRuntimeAgent reads a seed from the context: " + seedRead.Value);
        Assert.Contains("ActAsync(", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The replacement cheat-path test. Earlier rounds showed that a one-way digest of a
    /// low-entropy session seed is invertible by enumeration. The attack is re-run here against
    /// everything an agent actually receives; it must have no target value at all, and a deck
    /// rebuilt from the agent's own derived seed must differ from the recorded deal.
    /// </summary>
    [Fact]
    public void AgentInputs_GivenRecordedSeed42Hand1Enumeration_ExpectedNoCandidateSeedMatchAndDifferentDeck()
    {
        var probe = new ContextCapturingAgent(new RandomLegalAgent("enumeration-probe"));
        MatchSimulator.Run(
            probe,
            new RandomLegalAgent("enumeration-opponent"),
            new SimulationOptions(RecordedSessionSeed, RecordedHand));

        var turn = Assert.Single(probe.Turns);
        var context = turn.Context;
        var observation = turn.Observation;
        Assert.Equal(RecordedHand, context.HandNumber);
        Assert.Equal(0, context.Seat);
        Assert.Equal(0, context.ActionIndex);

        // Everything the agent can read at this turn: the observation JSON (its cards plus public
        // state), the context's textual form and every numeric context property.
        var inputs = AgentVisibleValues(observation, context);

        var recovered = new List<(ulong Candidate, ulong TurnSeed)>();
        for (ulong candidate = 0; candidate <= 100_000; candidate++)
        {
            var candidateTurnSeed = LegacyTurnDigest(
                candidate,
                context.HandNumber,
                context.Seat,
                context.ActionIndex,
                0UL);
            if (inputs.Contains(candidateTurnSeed))
            {
                recovered.Add((candidate, candidateTurnSeed));
            }
        }

        Console.WriteLine($"turn context         : {context}");
        Console.WriteLine($"agent-visible values : {inputs.Count}");
        Console.WriteLine("candidate seeds      : 0..100000");
        Console.WriteLine(
            "recovered seeds      : "
            + (recovered.Count == 0
                ? "none"
                : string.Join(
                    ", ",
                    recovered.Select(static match => $"{match.Candidate} (turn seed {match.TurnSeed})"))));

        Assert.True(
            recovered.Count == 0,
            "The enumeration recovered session seed(s) from the agent's inputs: "
            + string.Join(",", recovered.Select(static match => match.Candidate)));
        Assert.DoesNotContain(RecordedSessionSeed, recovered.Select(static match => match.Candidate));

        var agentSeed = InvokeDeriveAgentSeed(
            observation.ToCanonicalJson(),
            context.HandNumber,
            context.Seat,
            context.ActionIndex,
            0UL);
        Assert.True(
            agentSeed.HasValue,
            "SeedDerivation.DeriveAgentSeed(string,int,int,int,ulong) is missing; agent randomness must be "
            + "derived from the observation, not from the context.");

        var recordedDeal = FirstNineCards(
            SeedDerivation.Derive(RecordedSessionSeed, RecordedHand, 0, 0, 0UL));
        var agentSeedDeal = FirstNineCards(agentSeed.Value);
        Console.WriteLine($"recorded deal        : {recordedDeal}");
        Console.WriteLine($"agent-seed deal      : {agentSeedDeal}");
        Console.WriteLine($"agent seed           : {agentSeed.Value}");

        Assert.Equal(RecordedSession42Hand1Cards, recordedDeal);
        Assert.NotEqual(recordedDeal, agentSeedDeal);
        Assert.NotEqual(RecordedSession42Hand1Cards, agentSeedDeal);
        Assert.NotEqual(
            SeedDerivation.Derive(RecordedSessionSeed, RecordedHand, 0, 0, 0UL),
            agentSeed.Value);
    }

    /// <summary>
    /// Determinism fact for the new derivation: the same observation and turn coordinates produce
    /// the same agent seed and the same action twice, while a changed observation (one board card)
    /// produces a different seed.
    /// </summary>
    [Fact]
    public async Task DeriveAgentSeed_GivenObservationAndTurnCoordinates_ExpectedStableTwiceAndChangedForDifferentObservation()
    {
        var probe = new ContextCapturingAgent(new RandomLegalAgent("agent-seed-determinism"));
        MatchSimulator.Run(
            probe,
            new RandomLegalAgent("agent-seed-opponent"),
            new SimulationOptions(RecordedSessionSeed, RecordedHand));
        var turn = Assert.Single(probe.Turns);
        var canonical = turn.Observation.ToCanonicalJson();

        var first = InvokeDeriveAgentSeed(
            canonical,
            turn.Context.HandNumber,
            turn.Context.Seat,
            turn.Context.ActionIndex,
            0UL);
        var second = InvokeDeriveAgentSeed(
            canonical,
            turn.Context.HandNumber,
            turn.Context.Seat,
            turn.Context.ActionIndex,
            0UL);
        Assert.True(
            first.HasValue && second.HasValue,
            "SeedDerivation.DeriveAgentSeed(string,int,int,int,ulong) is missing; determinism cannot be observed.");
        Assert.Equal(first, second);

        var agent = new RandomLegalAgent("agent-seed-action");
        var firstAction = await agent.ActAsync(turn.Observation, turn.Context, CancellationToken.None);
        var secondAction = await agent.ActAsync(turn.Observation, turn.Context, CancellationToken.None);
        Assert.Equal(firstAction, secondAction);

        var flop = FlopObservation();
        var flopJson = flop.ToCanonicalJson();
        var boardCard = flop.Board[0].ToString();
        var replacement = new[] { "2c", "3c", "4c", "5c", "6c", "7c", "8c", "9c" }
            .First(card => !flopJson.Contains("\"" + card + "\"", StringComparison.Ordinal));
        var changedJson = flopJson.Replace(
            "\"" + boardCard + "\"",
            "\"" + replacement + "\"",
            StringComparison.Ordinal);
        Assert.NotEqual(flopJson, changedJson);

        var unchangedSeed = InvokeDeriveAgentSeed(
            flopJson,
            flop.HandNumber,
            flop.ActingSeat,
            1,
            0UL);
        var changedSeed = InvokeDeriveAgentSeed(
            changedJson,
            flop.HandNumber,
            flop.ActingSeat,
            1,
            0UL);
        Assert.True(
            unchangedSeed.HasValue && changedSeed.HasValue,
            "SeedDerivation.DeriveAgentSeed(string,int,int,int,ulong) is missing; determinism cannot be observed.");
        Assert.NotEqual(unchangedSeed, changedSeed);
    }

    private sealed record CapturedTurn(PlayerObservation Observation, AgentTurnContext Context);

    private sealed class ContextCapturingAgent : IPokerAgent
    {
        private readonly IPokerAgent _inner;
        private readonly List<CapturedTurn> _turns = [];

        public ContextCapturingAgent(IPokerAgent inner) => _inner = inner;

        public string Name => _inner.Name;

        public IReadOnlyList<CapturedTurn> Turns => _turns;

        public async ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
        {
            _turns.Add(new CapturedTurn(observation, context));
            return await _inner.ActAsync(observation, context, cancellationToken);
        }
    }

    private static PlayerObservation FlopObservation()
    {
        var state = HeadsUpHandEngine.Start(
            SeedDerivation.Derive(RecordedSessionSeed, RecordedHand, 0, 0, 0UL),
            handNumber: RecordedHand);
        state = HeadsUpHandEngine.Apply(state, PokerAction.Call());
        state = HeadsUpHandEngine.Apply(state, PokerAction.Check());
        Assert.Equal(Street.Flop, state.Street);
        return PlayerObservation.ForSeat(state, state.ActingSeat);
    }

    private static HashSet<ulong> AgentVisibleValues(
        PlayerObservation observation,
        AgentTurnContext context)
    {
        var values = new HashSet<ulong>();
        AddNumbers(values, observation.ToCanonicalJson());
        AddNumbers(values, context.ToString() ?? string.Empty);

        foreach (var property in typeof(AgentTurnContext).GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            switch (property.GetValue(context))
            {
                case int number:
                    values.Add(unchecked((ulong)(long)number));
                    break;
                case long number:
                    values.Add(unchecked((ulong)number));
                    break;
                case ulong number:
                    values.Add(number);
                    break;
                case string text:
                    AddNumbers(values, text);
                    break;
            }
        }

        return values;
    }

    private static void AddNumbers(HashSet<ulong> values, string text)
    {
        foreach (Match match in Regex.Matches(text, @"-?\d+", RegexOptions.CultureInvariant))
        {
            if (ulong.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                values.Add(number);
            }
        }
    }

    /// <summary>
    /// A local restatement of the deleted <c>SeedDerivation.DeriveTurnSeed</c> formula, so the old
    /// falsifier attack can be replayed after the API is gone: first eight big-endian bytes of
    /// SHA-256 over the invariant text. This is the exact procedure the falsifiers used.
    /// </summary>
    private static ulong LegacyTurnDigest(
        ulong sessionSeed,
        int handNumber,
        int seat,
        int actionIndex,
        ulong stream)
    {
        var material = string.Create(
            CultureInfo.InvariantCulture,
            $"tholdem-turn-seed|{sessionSeed}|{handNumber}|{seat}|{actionIndex}|{stream}");
        var digest = SHA256.HashData(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(material));
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(digest);
    }

    private static ulong? InvokeDeriveAgentSeed(
        string canonicalObservation,
        int handNumber,
        int seat,
        int actionIndex,
        ulong stream)
    {
        var method = typeof(SeedDerivation).GetMethod(
            "DeriveAgentSeed",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(string), typeof(int), typeof(int), typeof(int), typeof(ulong)],
            modifiers: null);
        return method is null
            ? null
            : (ulong?)method.Invoke(
                null,
                [canonicalObservation, handNumber, seat, actionIndex, stream]);
    }

    private static string WithoutXmlDocComments(string source)
    {
        var builder = new StringBuilder(source.Length);
        foreach (var line in source.Split('\n'))
        {
            if (line.TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    private static string WithoutComments(string source)
    {
        var builder = new StringBuilder(source.Length);
        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    private static string FirstNineCards(ulong seed) =>
        string.Join(' ', SeededDeck.Create(seed).Take(9).Select(static card => card.ToString()));
}
