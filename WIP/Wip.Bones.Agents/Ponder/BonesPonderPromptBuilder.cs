using System.Text;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Ponder;

internal static class BonesPonderPromptBuilder
{
    internal const string ScriptOutputFormatHeader = "## Required output format";

    internal static BonesStrategyAuthoringRequest BuildAuthoringRequest(
        BonesPlayerId playerId,
        IReadOnlyList<BonesObservationTranscript> observations,
        IReadOnlyList<string> observationArtifactPaths)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(observationArtifactPaths);

        var systemPrompt = BuildSystemPrompt(playerId);
        var userPrompt = BuildUserPrompt(playerId, observations);

        return new BonesStrategyAuthoringRequest(
            Messages:
            [
                new BonesStrategyAuthoringMessage("system", systemPrompt),
                new BonesStrategyAuthoringMessage("user", userPrompt),
            ],
            ObservationArtifactPaths: observationArtifactPaths);
    }

    private static string BuildSystemPrompt(BonesPlayerId playerId)
    {
        return $"""
            You are authoring an initial Block Dominoes strategy script for seat {playerId.Seat}.
            Output exactly one compilable C# source file: a public sealed class implementing {BonesStrategyScriptApiReference.InterfaceName}.
            The host executes {BonesStrategyScriptApiReference.ChooseMoveMethodName} deterministically at play time with no further LLM calls.
            {BonesStrategyScriptApiReference.LegalMoveConstraint}
            {BonesStrategyScriptApiReference.MoveSelectionConstraint}
            Do not output markdown rules, commentary outside the code fence, or multiple classes.
            """;
    }

    private static string BuildUserPrompt(
        BonesPlayerId playerId,
        IReadOnlyList<BonesObservationTranscript> observations)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Strategy script authoring request");
        builder.AppendLine();
        builder.AppendLine($"Player seat: {playerId.Seat}");
        builder.AppendLine($"Observation count: {observations.Count}");
        builder.AppendLine();
        builder.AppendLine(BonesStrategyScriptApiReference.ContractExcerpt);
        builder.AppendLine();
        builder.AppendLine("## Observation summaries");

        if (observations.Count == 0)
        {
            builder.AppendLine("No observation transcripts were loaded for this player.");
        }
        else
        {
            foreach (var observation in observations)
            {
                builder.AppendLine($"### Game {observation.GameId.Value}");
                builder.AppendLine(observation.Markdown);
                builder.AppendLine();
            }
        }

        builder.AppendLine(ScriptOutputFormatHeader);
        builder.AppendLine("Reply with a single fenced ```csharp block containing the complete class source.");
        builder.AppendLine($"The class must implement {BonesStrategyScriptApiReference.InterfaceName} and compile against the allowed namespaces.");
        builder.AppendLine("Use observation summaries to inform tile-selection heuristics inside ChooseMove.");

        return builder.ToString();
    }

    internal static BonesStrategyAuthoringRequest AppendCompileRetryMessage(
        BonesStrategyAuthoringRequest priorRequest,
        string failureReason)
    {
        ArgumentNullException.ThrowIfNull(priorRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        var retryUserPrompt = new StringBuilder();
        retryUserPrompt.AppendLine("# Compile retry request");
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine("The previous C# script failed compilation. Fix the script and reply with a single fenced ```csharp block.");
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine("## Compiler diagnostics");
        retryUserPrompt.AppendLine(failureReason);
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine(BonesStrategyScriptApiReference.ContractExcerpt);
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine(ScriptOutputFormatHeader);
        retryUserPrompt.AppendLine($"The class must implement {BonesStrategyScriptApiReference.InterfaceName} and compile against the allowed namespaces.");

        var messages = priorRequest.Messages
            .Append(new BonesStrategyAuthoringMessage("user", retryUserPrompt.ToString()))
            .ToArray();

        return priorRequest with { Messages = messages };
    }

    internal static BonesStrategyAuthoringRequest AppendCompileRetryMessage(
        BonesStrategyAuthoringRequest priorRequest,
        string failureReason,
        string priorAssistantResponse)
    {
        ArgumentNullException.ThrowIfNull(priorRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        ArgumentException.ThrowIfNullOrWhiteSpace(priorAssistantResponse);

        var retryUserPrompt = new StringBuilder();
        retryUserPrompt.AppendLine("# Compile retry request");
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine("The previous C# script failed compilation. Fix the script and reply with a single fenced ```csharp block.");
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine("## Compiler diagnostics");
        retryUserPrompt.AppendLine(failureReason);
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine(BonesStrategyScriptApiReference.ContractExcerpt);
        retryUserPrompt.AppendLine();
        retryUserPrompt.AppendLine(ScriptOutputFormatHeader);
        retryUserPrompt.AppendLine($"The class must implement {BonesStrategyScriptApiReference.InterfaceName} and compile against the allowed namespaces.");

        var messages = priorRequest.Messages
            .Append(new BonesStrategyAuthoringMessage("assistant", priorAssistantResponse))
            .Append(new BonesStrategyAuthoringMessage("user", retryUserPrompt.ToString()))
            .ToArray();

        return priorRequest with { Messages = messages };
    }
}
