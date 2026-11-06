using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Runtime;

public static class BonesAgentToolIds
{
    public static readonly CapabilityId StrategyCompile = new("tool.bones.strategy-compile");
}

public sealed record BonesStrategyCompileToolRequest(string ScriptSource);

public sealed record BonesStrategyCompileToolResult(bool Succeeded, string? FailureReason);

/// <summary>
/// Deterministic compile capability exposed to the model loop. The tool never calls a model: it
/// compiles the supplied script with <see cref="BonesStrategyScriptHost"/> and returns the compiler
/// diagnostics so the agent can decide whether to retry.
/// </summary>
public sealed class BonesStrategyCompileTool : ITool<BonesStrategyCompileToolRequest, BonesStrategyCompileToolResult>
{
    private static readonly BonesStrategyId CompileStrategyId = new("tool-strategy-compile");

    private readonly BonesStrategyScriptHost _scriptHost;

    public BonesStrategyCompileTool(BonesStrategyScriptHost scriptHost)
    {
        _scriptHost = scriptHost ?? throw new ArgumentNullException(nameof(scriptHost));
    }

    public ValueTask<BonesStrategyCompileToolResult> ExecuteAsync(
        BonesStrategyCompileToolRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var compileResult = _scriptHost.TryCompile(CompileStrategyId, request.ScriptSource);
        return ValueTask.FromResult(
            new BonesStrategyCompileToolResult(compileResult.Succeeded, compileResult.FailureReason));
    }
}

public static class BonesAgentToolDefinitions
{
    private const string StrategyCompileDescription =
        "Compiles a Bones C# strategy script with the deterministic script host and returns compiler diagnostics.";

    public static ToolDefinition StrategyCompile { get; } = new(
        BonesAgentToolIds.StrategyCompile.Value,
        StrategyCompileDescription,
        CreateStrategyCompileSchema());

    private static JsonElement CreateStrategyCompileSchema()
        => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                scriptSource = new
                {
                    type = "string",
                    description = "Complete C# source of a class implementing IBonesPlayerSlot.",
                },
            },
            required = new[] { "scriptSource" },
            additionalProperties = false,
        });
}

public static class BonesAgentToolDescriptors
{
    /// <summary>
    /// Agent-loop tool descriptors for the Ponder runtime path. These are deliberately not
    /// <c>WipBuilder</c> capabilities: the compile tool belongs to the agent loop, not to the
    /// workflow capability catalog.
    /// </summary>
    public static IReadOnlyList<ICapabilityDescriptor> Create(
        IReadOnlyList<CapabilityPermission>? requiredPermissions = null,
        PluginOrigin? pluginOrigin = null)
    {
        return
        [
            new CapabilityDescriptor<BonesStrategyCompileToolRequest, BonesStrategyCompileToolResult>(
                BonesAgentToolIds.StrategyCompile,
                "Bones strategy compile tool",
                CapabilityKind.Tool,
                typeof(BonesStrategyCompileTool),
                requiredPermissions: requiredPermissions,
                pluginOrigin: pluginOrigin),
        ];
    }
}
