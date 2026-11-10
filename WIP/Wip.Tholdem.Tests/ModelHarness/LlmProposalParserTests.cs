using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>WP-2: deterministic proposal parsing, clamping and stable refusal codes.</summary>
public sealed class LlmProposalParserTests
{
    [Fact]
    public void LlmProposalParser_GivenValidJson_ExpectedExactGenome()
    {
        const string Json = """
            {
              "raiseThreshold": 0.64,
              "callThreshold": 0.45,
              "bluffFrequency": 0.12,
              "sizingMultiplier": 1.0,
              "skills": null,
              "memory": { "kind": "pattern", "key": "Missed_Value!", "note": "bounded text" },
              "reason": "one short sentence"
            }
            """;

        var proposal = LlmProposalParser.Parse(Json);

        Assert.Equal(0.64, proposal.Genome.RaiseThreshold);
        Assert.Equal(0.45, proposal.Genome.CallThreshold);
        Assert.Equal(0.12, proposal.Genome.BluffFrequency);
        Assert.Equal(1.0, proposal.Genome.SizingMultiplier);
        Assert.Null(proposal.Genome.Skills);
        Assert.Equal("pattern", proposal.MemoryKind);
        Assert.Equal("missed-value-", proposal.MemoryKey);
        Assert.Equal("one short sentence", proposal.Reason);
        Assert.Empty(proposal.Adjustments);
        Assert.Equal(Wip.Tholdem.Agents.Learning.StrategyGenome.DefaultPromptText, proposal.Genome.PromptText);
    }

    [Fact]
    public void LlmProposalParser_GivenOutOfRangeValues_ExpectedClampAndRecordedAdjustments()
    {
        const string Json = """
            {"raiseThreshold":1.4,"callThreshold":-0.2,"bluffFrequency":2.0,"sizingMultiplier":0.1,"reason":"clamp"}
            """;

        var proposal = LlmProposalParser.Parse(Json);

        Assert.Equal(1.0, proposal.Genome.RaiseThreshold);
        Assert.Equal(0.0, proposal.Genome.CallThreshold);
        Assert.Equal(1.0, proposal.Genome.BluffFrequency);
        Assert.Equal(0.25, proposal.Genome.SizingMultiplier);
        Assert.Collection(
            proposal.Adjustments,
            adjustment =>
            {
                Assert.Equal("raiseThreshold", adjustment.Field);
                Assert.Equal(1.4, adjustment.Requested);
                Assert.Equal(1.0, adjustment.Clamped);
            },
            adjustment =>
            {
                Assert.Equal("callThreshold", adjustment.Field);
                Assert.Equal(-0.2, adjustment.Requested);
                Assert.Equal(0.0, adjustment.Clamped);
            },
            adjustment =>
            {
                Assert.Equal("bluffFrequency", adjustment.Field);
                Assert.Equal(2.0, adjustment.Requested);
                Assert.Equal(1.0, adjustment.Clamped);
            },
            adjustment =>
            {
                Assert.Equal("sizingMultiplier", adjustment.Field);
                Assert.Equal(0.1, adjustment.Requested);
                Assert.Equal(0.25, adjustment.Clamped);
            });
    }

    [Fact]
    public void LlmProposalParser_GivenMissingRequiredNumber_ExpectedInvalidWithStableCode()
    {
        const string Missing = """
            {"raiseThreshold":0.5,"callThreshold":0.4,"bluffFrequency":0.1}
            """;
        const string NonNumber = """
            {"raiseThreshold":"0.5","callThreshold":0.4,"bluffFrequency":0.1,"sizingMultiplier":1.0}
            """;
        const string NonFinite = """
            {"raiseThreshold":1e400,"callThreshold":0.4,"bluffFrequency":0.1,"sizingMultiplier":1.0}
            """;

        var missing = Assert.Throws<LlmProposalParseException>(() => LlmProposalParser.Parse(Missing));
        Assert.Equal("invalid-required-number", missing.Code);
        Assert.Contains("sizingMultiplier", missing.Message);

        var nonNumber = Assert.Throws<LlmProposalParseException>(() => LlmProposalParser.Parse(NonNumber));
        Assert.Equal("invalid-required-number", nonNumber.Code);

        var nonFinite = Assert.Throws<LlmProposalParseException>(() => LlmProposalParser.Parse(NonFinite));
        Assert.Equal("invalid-required-number", nonFinite.Code);
    }

    [Fact]
    public void LlmProposalParser_GivenUnknownFields_ExpectedIgnoredAndGenomeParsed()
    {
        const string Json = """
            {"raiseThreshold":0.72,"callThreshold":0.45,"bluffFrequency":0.12,"sizingMultiplier":1.0,
             "confidence":0.99,"notes":["a","b"],"memory":null,"skills":null}
            """;

        var proposal = LlmProposalParser.Parse(Json);

        Assert.Equal(0.72, proposal.Genome.RaiseThreshold);
        Assert.Equal(0.45, proposal.Genome.CallThreshold);
        Assert.Equal(0.12, proposal.Genome.BluffFrequency);
        Assert.Equal(1.0, proposal.Genome.SizingMultiplier);
        Assert.Null(proposal.MemoryKind);
        Assert.Null(proposal.MemoryKey);
        Assert.Null(proposal.Reason);
        Assert.Empty(proposal.Adjustments);
    }

    [Fact]
    public void LlmProposalParser_GivenSkillsObject_ExpectedSkillGenome()
    {
        const string Json = """
            {"raiseThreshold":0.60,"callThreshold":0.40,"bluffFrequency":0.10,"sizingMultiplier":1.20,
             "skills":{"preflop":{"raiseThreshold":0.62,"callThreshold":0.44},
                       "postflop":{"raiseThreshold":0.55,"callThreshold":0.33},
                       "sizing":{"multiplier":9.0},
                       "bluff":{"frequency":0.07}}}
            """;

        var proposal = LlmProposalParser.Parse(Json);

        var skills = proposal.Genome.Skills;
        Assert.NotNull(skills);
        Assert.Equal(0.62, skills!.Preflop.RaiseThreshold);
        Assert.Equal(0.44, skills.Preflop.CallThreshold);
        Assert.Equal(0.55, skills.Postflop.RaiseThreshold);
        Assert.Equal(0.33, skills.Postflop.CallThreshold);
        Assert.Equal(3.0, skills.Sizing.Multiplier);
        Assert.Equal(0.07, skills.Bluff.Frequency);
        var adjustment = Assert.Single(proposal.Adjustments);
        Assert.Equal("skills.sizing.multiplier", adjustment.Field);
        Assert.Equal(9.0, adjustment.Requested);
        Assert.Equal(3.0, adjustment.Clamped);
    }
}
