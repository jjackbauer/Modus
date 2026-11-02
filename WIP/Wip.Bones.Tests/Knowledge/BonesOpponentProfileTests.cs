using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Knowledge;

public sealed class BonesOpponentProfileTests
{
    [Fact]
    public void FromTranscriptMarkdown_GivenPlayEvents_ComputesPipFrequencies()
    {
        var opponentId = new BonesPlayerId(2);
        var markdown = """
            # Match Transcript
            Seat 1: Plays [3|5]
            Seat 2: Plays [1|2]
            Seat 3: Plays [4|6]
            Seat 4: Plays [0|5]
            Seat 1: Plays [2|3]
            Seat 2: Plays [3|5]
            Seat 3: Passes
            Seat 4: Plays [1|2]
            """;

        var profile = BonesOpponentProfile.FromTranscriptMarkdown(opponentId, markdown);

        Assert.Equal(2, profile.TotalMovesAnalyzed);
        Assert.Contains(3, profile.PreferredPipsFrequencies.Keys); // [1|2] = 3 pips
        Assert.Contains(8, profile.PreferredPipsFrequencies.Keys); // [3|5] = 8 pips
        Assert.Equal(0.5, profile.PreferredPipsFrequencies[3]);
        Assert.Equal(0.5, profile.PreferredPipsFrequencies[8]);
        Assert.Equal(0.0, profile.DoublePlayFrequency);
        Assert.Equal(0.0, profile.PassRate);
    }

    [Fact]
    public void FromTranscriptMarkdown_GivenDoublePlays_ComputesDoubleFrequency()
    {
        var opponentId = new BonesPlayerId(2);
        var markdown = """
            Seat 2: Plays double [4|4]
            Seat 1: Plays [1|2]
            Seat 2: Plays [3|4]
            Seat 3: Plays [2|2]
            Seat 4: Plays [5|6]
            Seat 2: Plays double [6|6]
            """;

        var profile = BonesOpponentProfile.FromTranscriptMarkdown(opponentId, markdown);

        Assert.Equal(3, profile.TotalMovesAnalyzed);
        Assert.Equal(2.0 / 3.0, profile.DoublePlayFrequency, precision: 3);
        Assert.Equal(0.0, profile.PassRate);
    }

    [Fact]
    public void FromTranscriptMarkdown_GivenPassEvents_ComputesPassRate()
    {
        var opponentId = new BonesPlayerId(3);
        var markdown = """
            Seat 1: Plays [1|2]
            Seat 2: Plays [3|4]
            Seat 3: Passes
            Seat 4: Plays [5|6]
            Seat 1: Plays [6|0]
            Seat 2: Plays [4|1]
            Seat 3: Passes
            Seat 4: Plays [5|5]
            """;

        var profile = BonesOpponentProfile.FromTranscriptMarkdown(opponentId, markdown);

        Assert.Equal(2, profile.TotalMovesAnalyzed);
        Assert.Equal(1.0, profile.PassRate);
        Assert.Equal(0.0, profile.DoublePlayFrequency);
        Assert.Empty(profile.PreferredPipsFrequencies);
    }

    [Fact]
    public void FromTranscriptMarkdown_GivenNoMoves_ReturnsEmptyProfile()
    {
        var opponentId = new BonesPlayerId(4);
        var markdown = """
            Seat 1: Plays [1|2]
            Seat 2: Plays [3|4]
            Seat 3: Plays [5|6]
            """;

        var profile = BonesOpponentProfile.FromTranscriptMarkdown(opponentId, markdown);

        Assert.Equal(0, profile.TotalMovesAnalyzed);
        Assert.Empty(profile.PreferredPipsFrequencies);
        Assert.Equal(0.0, profile.DoublePlayFrequency);
        Assert.Equal(0.0, profile.PassRate);
    }

    [Fact]
    public void ToPromptSummary_GivenProfileWithData_ReturnsFormattedSummary()
    {
        var opponentId = new BonesPlayerId(2);
        var frequencies = new Dictionary<int, double>
        {
            [3] = 0.40,
            [8] = 0.35,
            [12] = 0.25,
        };

        var profile = new BonesOpponentProfile
        {
            PreferredPipsFrequencies = frequencies,
            DoublePlayFrequency = 0.15,
            PassRate = 0.05,
            TotalMovesAnalyzed = 40,
        };

        var summary = profile.ToPromptSummary(opponentId);

        Assert.Contains("**Seat 2** (40 moves", summary, StringComparison.Ordinal);
        Assert.Contains("Double play frequency: 15.0%", summary, StringComparison.Ordinal);
        Assert.Contains("Pass rate: 5.0%", summary, StringComparison.Ordinal);
        Assert.Contains("3 pips: 40.0%", summary, StringComparison.Ordinal);
        Assert.Contains("8 pips: 35.0%", summary, StringComparison.Ordinal);
        Assert.Contains("12 pips: 25.0%", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ToPromptSummary_GivenEmptyProfile_ReturnsNoDataMessage()
    {
        var opponentId = new BonesPlayerId(3);
        var profile = new BonesOpponentProfile();

        var summary = profile.ToPromptSummary(opponentId);

        Assert.Contains("No move data available", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromTranscriptMarkdown_GivenMixedEvents_ComputesAllMetrics()
    {
        var opponentId = new BonesPlayerId(1);
        var markdown = """
            Seat 1: Plays [3|5]
            Seat 2: Plays [0|0]
            Seat 1: Plays double [6|6]
            Seat 3: Passes
            Seat 4: Plays [1|2]
            Seat 1: Passes
            """;

        var profile = BonesOpponentProfile.FromTranscriptMarkdown(opponentId, markdown);

        Assert.Equal(3, profile.TotalMovesAnalyzed);
        Assert.Equal(1.0 / 3.0, profile.DoublePlayFrequency, precision: 3);
        Assert.Equal(1.0 / 3.0, profile.PassRate, precision: 3);
        Assert.Contains(8, profile.PreferredPipsFrequencies.Keys); // [3|5]
        Assert.Contains(12, profile.PreferredPipsFrequencies.Keys); // [6|6]
    }
}
