using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// The canonical data-only equity-policy artifact: fixed key order, invariant numbers, a strict
/// parse that reconstructs and re-serializes, and a content address over exactly the persisted
/// bytes.
/// </summary>
public sealed class EquityPolicyJsonTests
{
    private const string CanonicalPolicy =
        "{\"schema\":\"tholdem.equity-policy.v1\",\"equitySamples\":400,\"valueEdge\":0.1,"
        + "\"raiseEdge\":0.18,\"bluffFrequency\":0.08,\"valueBetPotFraction\":0.65}";

    [Fact]
    public void WriteThenParse_GivenCanonicalPolicy_RoundTripsAndHashesStably()
    {
        var policy = new EquityPolicy();

        var json = EquityPolicyJson.Write(policy);

        Assert.Equal(CanonicalPolicy, json);
        Assert.Equal(policy, EquityPolicyJson.Parse(json));
        Assert.Equal(json, EquityPolicyJson.Write(EquityPolicyJson.Parse(json)));
        Assert.Equal(EquityPolicyJson.Hash(policy), EquityPolicyJson.Hash(EquityPolicyJson.Parse(json)));
        Assert.Equal(StrategyArtifact.ComputeHash(json), EquityPolicyJson.Hash(policy));
        Assert.Matches("^[0-9a-f]{64}$", EquityPolicyJson.Hash(policy));

        Console.WriteLine("observed equity-policy json: " + json);
        Console.WriteLine("observed equity-policy hash: " + EquityPolicyJson.Hash(policy));
    }

    [Fact]
    public void Parse_GivenOutOfRangeOrNonFiniteValue_Refuses()
    {
        var invalid = new[]
        {
            PolicyJson(equitySamples: "49"),
            PolicyJson(equitySamples: "5001"),
            PolicyJson(equitySamples: "400.5"),
            PolicyJson(valueEdge: "-0.01"),
            PolicyJson(valueEdge: "1.01"),
            PolicyJson(raiseEdge: "-0.01"),
            PolicyJson(raiseEdge: "1.01"),
            PolicyJson(bluffFrequency: "-0.01"),
            PolicyJson(bluffFrequency: "1.01"),
            PolicyJson(valueBetPotFraction: "0.01"),
            PolicyJson(valueBetPotFraction: "0"),
            PolicyJson(valueBetPotFraction: "2.01"),
            PolicyJson(valueEdge: "1e999"),
            PolicyJson(valueBetPotFraction: "1e999"),
        };

        foreach (var json in invalid)
        {
            var exception = Assert.Throws<FormatException>(() => EquityPolicyJson.Parse(json));
            Assert.StartsWith("equity-policy-shape-invalid", exception.Message, StringComparison.Ordinal);
        }

        Console.WriteLine("observed equity-policy refusals: " + invalid.Length + " out-of-domain values");
    }

    [Fact]
    public void Parse_GivenWhitespaceOrExtraKey_RefusesAsNotCanonical()
    {
        var canonical = EquityPolicyJson.Write(new EquityPolicy());
        var spaced = " " + canonical.Replace("\"equitySamples\":", "\"equitySamples\": ", StringComparison.Ordinal) + "\n";
        var extraKey = canonical[..^1] + ",\"extra\":1}";

        foreach (var json in new[] { spaced, extraKey })
        {
            var exception = Assert.Throws<FormatException>(() => EquityPolicyJson.Parse(json));
            Assert.StartsWith("equity-policy-not-canonical", exception.Message, StringComparison.Ordinal);
        }

        Console.WriteLine("observed equity-policy refusals: whitespace and an extra key are not canonical");
    }

    private static string PolicyJson(
        string equitySamples = "400",
        string valueEdge = "0.1",
        string raiseEdge = "0.18",
        string bluffFrequency = "0.08",
        string valueBetPotFraction = "0.65") =>
        "{\"schema\":\"tholdem.equity-policy.v1\",\"equitySamples\":" + equitySamples
        + ",\"valueEdge\":" + valueEdge
        + ",\"raiseEdge\":" + raiseEdge
        + ",\"bluffFrequency\":" + bluffFrequency
        + ",\"valueBetPotFraction\":" + valueBetPotFraction + "}";
}
