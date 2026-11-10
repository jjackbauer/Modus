using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Wip.Abstractions.Chat;
using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>WP-2: canonical transcript round-trip, tamper refusal, replay and redaction.</summary>
public sealed class LlmTranscriptTests
{
    private const string SampleResponse =
        "{\"raiseThreshold\":0.66,\"callThreshold\":0.41,\"bluffFrequency\":0.08,\"sizingMultiplier\":1.25,\"reason\":\"bounded replay\"}";

    [Fact]
    public void Transcript_GivenRecordedResponse_ExpectedReplayWithoutModelCall()
    {
        var transcript = LlmTranscript.Create(
            iteration: 2,
            correlationId: "llm-2",
            requestedModel: "deepseek-chat",
            servedModel: "deepseek-flash",
            prompt: "bounded prompt",
            response: SampleResponse,
            usage: new LlmUsage(120, 30),
            latencyMilliseconds: 250L,
            outcome: LlmTranscriptOutcomes.Ok);

        var replayed = new LlmTranscriptReplay().Propose(transcript);
        var direct = LlmProposalParser.Parse(SampleResponse);

        Assert.Equal(direct, replayed);
        Assert.Equal(0.66, replayed.Genome.RaiseThreshold);
        Assert.Equal(0.41, replayed.Genome.CallThreshold);
        Assert.Equal(0.08, replayed.Genome.BluffFrequency);
        Assert.Equal(1.25, replayed.Genome.SizingMultiplier);
        Assert.Equal("bounded replay", replayed.Reason);

        var replayType = typeof(LlmTranscriptReplay);
        var propose = replayType.GetMethod(nameof(LlmTranscriptReplay.Propose));
        Assert.NotNull(propose);
        Assert.Single(propose!.GetParameters());
        Assert.Equal(typeof(LlmTranscript), propose.GetParameters()[0].ParameterType);

        var modelType = typeof(IChatModel);
        var modelReferences = replayType
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(member => member switch
            {
                FieldInfo field => field.FieldType == modelType,
                PropertyInfo property => property.PropertyType == modelType,
                MethodInfo method => method.ReturnType == modelType
                    || method.GetParameters().Any(parameter => parameter.ParameterType == modelType),
                ConstructorInfo constructor => constructor.GetParameters()
                    .Any(parameter => parameter.ParameterType == modelType),
                _ => false,
            })
            .ToArray();
        Assert.Empty(modelReferences);
    }

    [Fact]
    public async Task Transcript_GivenRoundTrip_ExpectedCanonicalHashesAndNoTamper()
    {
        var directory = AgentTestSupport.CreateTempDirectory("tholdem-transcript-roundtrip");
        try
        {
            var store = new LlmTranscriptStore(directory);
            var first = LlmTranscript.Create(
                iteration: 1,
                correlationId: "llm-1",
                requestedModel: "deepseek-chat",
                servedModel: "deepseek-flash",
                prompt: "prompt-one",
                response: SampleResponse,
                usage: new LlmUsage(101, 21),
                latencyMilliseconds: 1234L,
                outcome: LlmTranscriptOutcomes.Ok);
            var second = LlmTranscript.Create(
                iteration: 2,
                correlationId: "llm-2",
                requestedModel: "deepseek-chat",
                servedModel: "deepseek-flash",
                prompt: "prompt-two",
                response: "{\"raiseThreshold\":0.5,\"callThreshold\":0.4,\"bluffFrequency\":0.1,\"sizingMultiplier\":1.0}",
                usage: new LlmUsage(102, 22),
                latencyMilliseconds: 2345L,
                outcome: LlmTranscriptOutcomes.Repaired,
                error: "the first response was malformed");

            await store.AppendAsync(first, CancellationToken.None);
            await store.AppendAsync(second, CancellationToken.None);

            var records = store.ReadAll();
            Assert.Equal(new[] { first, second }, records);
            Assert.Equal("sha256:" + Sha256Hex(first.Prompt), records[0].PromptHash);
            Assert.Equal("sha256:" + Sha256Hex(first.Response), records[0].ResponseHash);
            Assert.Equal("sha256:" + Sha256Hex(second.Prompt), records[1].PromptHash);
            Assert.Equal("sha256:" + Sha256Hex(second.Response), records[1].ResponseHash);

            var path = Path.Combine(directory, LlmTranscriptStore.FileName);
            var bytes = File.ReadAllBytes(path);
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "The transcript file must be UTF-8 without a BOM.");
            var text = Encoding.UTF8.GetString(bytes);
            var lines = text.Split('\n');
            Assert.Equal(3, lines.Length);
            Assert.Equal(string.Empty, lines[2]);
            Assert.Equal(first.ToCanonicalJson(), lines[0]);
            Assert.Equal(second.ToCanonicalJson(), lines[1]);

            var memberOrder = new[]
            {
                "\"schema\"",
                "\"iteration\"",
                "\"correlationId\"",
                "\"requestedModel\"",
                "\"servedModel\"",
                "\"promptHash\"",
                "\"prompt\"",
                "\"response\"",
                "\"responseHash\"",
                "\"usage\"",
                "\"promptTokens\"",
                "\"completionTokens\"",
                "\"latencyMilliseconds\"",
                "\"outcome\"",
                "\"error\"",
            };
            var previousIndex = -1;
            foreach (var member in memberOrder)
            {
                var index = lines[0].IndexOf(member, StringComparison.Ordinal);
                Assert.True(index > previousIndex, "Member " + member + " is out of canonical order.");
                previousIndex = index;
            }

            var tampered = text.Replace("prompt-one", "prompt-xne", StringComparison.Ordinal);
            Assert.NotEqual(text, tampered);
            File.WriteAllText(path, tampered, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var exception = Assert.Throws<LlmTranscriptException>(() => store.ReadAll());
            Assert.Equal("llm-transcript-hash-mismatch", exception.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Transcript_GivenSecretShapedText_ExpectedRedactionAndNoKey()
    {
        const string FakeSk = "sk-FAKEabcdefgh12345678";
        const string FakeBearer = "Bearer FAKEtokengggggggg";
        const string FakeEnv = "DEEPSEEK_API_KEY=FAKEenvvalue987654";
        const string FakeSkBody = "FAKEabcdefgh12345678";
        const string FakeBearerBody = "FAKEtokengggggggg";
        const string FakeEnvBody = "FAKEenvvalue987654";

        var directory = AgentTestSupport.CreateTempDirectory("tholdem-transcript-redaction");
        try
        {
            var store = new LlmTranscriptStore(directory);
            var response =
                "{\"raiseThreshold\":0.5,\"callThreshold\":0.4,\"bluffFrequency\":0.1,\"sizingMultiplier\":1.0,\"reason\":\""
                + FakeSk
                + "\"}";
            var transcript = LlmTranscript.Create(
                iteration: 1,
                correlationId: "llm-1",
                requestedModel: "deepseek-chat",
                servedModel: "deepseek-flash",
                prompt: "prompt " + FakeBearer + " " + FakeEnv,
                response: response,
                usage: new LlmUsage(10, 5),
                latencyMilliseconds: 50L,
                outcome: LlmTranscriptOutcomes.Ok);

            await store.AppendAsync(transcript, CancellationToken.None);

            var stored = Assert.Single(store.ReadAll());
            Assert.DoesNotContain(FakeSkBody, stored.Prompt);
            Assert.DoesNotContain(FakeBearerBody, stored.Prompt);
            Assert.DoesNotContain(FakeEnvBody, stored.Prompt);
            Assert.DoesNotContain(FakeSkBody, stored.Response);
            Assert.Contains("[redacted]", stored.Prompt);
            Assert.Contains("[redacted]", stored.Response);
            Assert.Equal("sha256:" + Sha256Hex(stored.Prompt), stored.PromptHash);
            Assert.Equal("sha256:" + Sha256Hex(stored.Response), stored.ResponseHash);

            var fileText = Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath));
            Assert.DoesNotContain(FakeSkBody, fileText);
            Assert.DoesNotContain(FakeBearerBody, fileText);
            Assert.DoesNotContain(FakeEnvBody, fileText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text)))
            .ToLowerInvariant();
}
