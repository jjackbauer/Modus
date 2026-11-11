using System.Text;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.ModelHarness;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine("docs", "goal-tholdem-enhancement", "experiments", "llm-attribution-v1"));
var results = Path.Combine(root, "results");
Directory.CreateDirectory(results);
var utf8 = new UTF8Encoding(false);
var secret = LlmSecretFile.Read(Path.Combine(" .secrets".Trim(), "deepseek.env"));
foreach (var pair in secret) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
Environment.SetEnvironmentVariable("THOLDEM_LIVE_MODEL", "1");
var config = LiveModelConfiguration.Resolve();
using var http = new HttpClient();
var model = config.CreateChatModel(http, TimeSpan.FromSeconds(90), maxTransientRetries: 1);
var capability = new CapabilityContext(new SessionId("llm-attribution-v1"), root);

var tasks = new (string Id, StrategyGenome Genome)[]
{
    ("default", new StrategyGenome()),
    ("tight", new StrategyGenome(0.85, 0.35, 0.05, 1.20)),
    ("loose", new StrategyGenome(0.55, 0.65, 0.25, 0.80)),
    ("aggressive", new StrategyGenome(0.65, 0.50, 0.30, 1.50)),
};
var analysis = new SessionAnalysis(40, 0.5, 0.0, 1.0, 0.5, 0.4);
var rows = new List<object>();
var successes = 0;
foreach (var task in tasks)
{
    for (var iteration = 1; iteration <= 4; iteration++)
    {
        var context = new LlmPromptContext(iteration, task.Genome, analysis, Array.Empty<FailureSignal>(), Array.Empty<HarnessMemoryEntry>(), Array.Empty<StrategyLineageEntry>());
        var userText = LlmPromptBuilder.BuildUserPrompt(context);
        var request = new ChatRequest("deepseek-chat", [ChatMessage.System(LlmPromptBuilder.SystemPromptText), ChatMessage.User(userText)], maxTokens: 512, temperature: 0.0, correlationId: $"llm-{task.Id}-{iteration}");
        try
        {
            var response = await model.CompleteAsync(request, capability, CancellationToken.None);
            var proposal = LlmProposalParser.Parse(response.Message.Content);
            successes++;
            rows.Add(new { task = task.Id, iteration, status = "parsed", proposal.Genome.RaiseThreshold, proposal.Genome.CallThreshold, proposal.Genome.BluffFrequency, proposal.Genome.SizingMultiplier, adjustments = proposal.Adjustments.Count });
            Console.WriteLine($"ok {task.Id}/{iteration} {proposal.Genome.RaiseThreshold}/{proposal.Genome.CallThreshold}/{proposal.Genome.BluffFrequency}/{proposal.Genome.SizingMultiplier}");
        }
        catch (Exception exception)
        {
            rows.Add(new { task = task.Id, iteration, status = "failed", errorType = exception.GetType().Name, error = exception.Message.Length > 300 ? exception.Message[..300] : exception.Message });
            Console.WriteLine($"fail {task.Id}/{iteration} {exception.GetType().Name}: {exception.Message}");
        }
    }
}
var json = JsonSerializer.Serialize(new { schema = "tholdem.llm-attribution-probe.v1", attempts = rows.Count, successes, rows }, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(results, "probe.json"), json, utf8);
Console.WriteLine($"successes={successes}/{rows.Count}");
return successes > 0 ? 0 : 1;
