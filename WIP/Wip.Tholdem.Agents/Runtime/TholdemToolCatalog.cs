using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;

namespace Wip.Tholdem.Agents.Runtime;

/// <summary>
/// The exact model-facing tool surface of the runtime poker agent: four tool definitions with JSON
/// schemas and four capability descriptors with their permission requirements. The two calculators
/// need no permission; the session read tools need <c>artifact.read</c>.
/// </summary>
public static class TholdemToolCatalog
{
    public const string EquityCalculatorName = "equity-calculator";

    public const string PotOddsName = "pot-odds";

    public const string HandHistoryName = "hand-history-query";

    public const string OpponentStatisticsName = "opponent-statistics";

    public static IReadOnlyList<ToolDefinition> CreateDefinitions() =>
    [
        new ToolDefinition(
            EquityCalculatorName,
            "Seeded Monte Carlo showdown equity of two hole cards against one to five random opponent hands.",
            Schema(
                Object(
                    ("holeCards", ArrayOf("string")),
                    ("board", ArrayOf("string")),
                    ("samples", Integer(1)),
                    ("seed", Integer(null)),
                    ("opponents", Integer(1))),
                "holeCards")),
        new ToolDefinition(
            PotOddsName,
            "Pot odds and the break-even equity a call needs for the given pot and call amount.",
            Schema(
                Object(
                    ("pot", Integer(0)),
                    ("amountToCall", Integer(0))),
                "pot",
                "amountToCall")),
        new ToolDefinition(
            HandHistoryName,
            "The newest recorded hands of the current session for this agent's own seat only.",
            Schema(Object(("limit", Integer(1))), "limit")),
        new ToolDefinition(
            OpponentStatisticsName,
            "Aggregate VPIP, PFR, aggression factor and showdown rate of the opposing seat this session.",
            Schema(Object())),
    ];

    public static IReadOnlyList<ICapabilityDescriptor> CreateDescriptors() =>
    [
        CapabilityDescriptor.For<EquityTool, EquityToolRequest, EquityToolResult>(
            new CapabilityId(EquityCalculatorName),
            EquityCalculatorName,
            CapabilityKind.Tool,
            requiredPermissions: Array.Empty<CapabilityPermission>()),
        CapabilityDescriptor.For<PotOddsTool, PotOddsRequest, PotOddsResult>(
            new CapabilityId(PotOddsName),
            PotOddsName,
            CapabilityKind.Tool,
            requiredPermissions: Array.Empty<CapabilityPermission>()),
        CapabilityDescriptor.For<HandHistoryTool, HandHistoryRequest, HandHistoryResult>(
            new CapabilityId(HandHistoryName),
            HandHistoryName,
            CapabilityKind.Tool,
            requiredPermissions: new[] { new CapabilityPermission(PermissionCatalog.ArtifactRead) }),
        CapabilityDescriptor.For<OpponentStatsTool, OpponentStatsRequest, OpponentStatsResult>(
            new CapabilityId(OpponentStatisticsName),
            OpponentStatisticsName,
            CapabilityKind.Tool,
            requiredPermissions: new[] { new CapabilityPermission(PermissionCatalog.ArtifactRead) }),
    ];

    private static JsonElement Schema(Dictionary<string, object?> properties, params string[] required)
        => JsonSerializer.SerializeToElement(Object(
            ("type", "object"),
            ("properties", properties),
            ("required", required),
            ("additionalProperties", false)));

    private static Dictionary<string, object?> Object(params (string Name, object? Value)[] properties)
    {
        var result = new Dictionary<string, object?>(properties.Length, StringComparer.Ordinal);
        foreach (var (name, value) in properties)
        {
            result[name] = value;
        }

        return result;
    }

    private static Dictionary<string, object?> ArrayOf(string itemType)
        => Object(
            ("type", "array"),
            ("items", Object(("type", itemType))),
            ("description", "Card texts such as As or Td."));

    private static Dictionary<string, object?> Integer(int? minimum)
        => minimum is null
            ? Object(("type", "integer"))
            : Object(("type", "integer"), ("minimum", minimum.Value));
}
