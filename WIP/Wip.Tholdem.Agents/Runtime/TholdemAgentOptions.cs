using Wip.Abstractions.Agents;

namespace Wip.Tholdem.Agents.Runtime;

/// <summary>
/// Configuration for <see cref="TholdemRuntimeAgent"/>. <see cref="EquitySamples"/> is the sample
/// budget the composed <see cref="EquityTool"/> is built with; the agent never lets the model pick a
/// larger budget than that. <see cref="ToolPromptMode"/> selects which surface carries the
/// authorized tool invitation and defaults to <see cref="TholdemToolPromptMode.System"/>.
/// </summary>
public sealed record TholdemAgentOptions(
    string ModelId = "scripted",
    AgentLoopOptions? LoopOptions = null,
    int MaxObservationCharacters = 4000,
    int EquitySamples = 2000,
    int MemoryMessages = 4,
    TholdemToolPromptMode ToolPromptMode = TholdemToolPromptMode.BothRequired);
