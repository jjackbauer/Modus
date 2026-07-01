using Wip.Bones.Domain;

namespace Wip.Bones.Engine;

public static class BonesStrategyScriptApiReference
{
    public const string InterfaceName = nameof(IBonesPlayerSlot);

    public const string StateTypeName = nameof(BonesRoundState);

    public const string MoveTypeName = nameof(BonesMove);

    public const string LegalMovesParameterType = "IReadOnlyList<BonesMove>";

    public const string ChooseMoveMethodName = nameof(IBonesPlayerSlot.ChooseMove);

    public const string GetLegalMovesSource = "BonesGameEngine.GetLegalMoves";

    public const string LegalMoveConstraint =
        "Return exactly one BonesMove from the supplied legalMoves list, or throw when no legal move can be chosen.";

    public const string MoveSelectionConstraint =
        "Choose a move from legalMoves only. Do not construct new BonesMove instances or invent property names.";

    public static IReadOnlyList<string> AllowedNamespaces { get; } =
    [
        "Wip.Bones.Domain",
        "Wip.Bones.Identifiers",
        "Wip.Bones.Engine",
    ];

    public const string ContractHeader = "## Bones strategy script API contract";

    public static string ContractExcerpt { get; } = BuildContractExcerpt();

    public static string BuildContractExcerpt()
    {
        var allowedNamespaces = string.Join(Environment.NewLine, AllowedNamespaces.Select(static ns => $"- {ns}"));
        var typeMembers = string.Join(
            Environment.NewLine,
            "Key domain types (use these exact member names):",
            $"- {MoveTypeName}: MoveId, PlayerId, Tile (BonesTile?), Side (BonesBoardSide?), IsPass",
            "- BonesTile: LowPip, HighPip (both BonesPipCount), TotalPips (int), IsDouble (bool)",
            "- BonesPipCount: Value (int, 0-6). Use .Value for numeric comparisons; do not assign BonesPipCount to int.",
            "- BonesBoardEnd: Pip (BonesPipCount). Board ends are not tiles.",
            "- BonesBoard: Tiles (ImmutableArray<BonesChainTile>), IsEmpty, LeftEnd (BonesBoardEnd?), RightEnd (BonesBoardEnd?). No helper methods beyond these members.",
            "- BonesChainTile: Tile (BonesTile), ChainLeftPip, ChainRightPip (BonesPipCount). Board chain orientation; use .Tile for canonical identity.",
            $"- {StateTypeName}: GameId, Board, Hands, CurrentPlayer, OpeningTile, PassStreak, EventLog",
            "- BonesBoardSide: Left, Right",
            "- BonesHands.GetHand(BonesPlayerId) returns the player's BonesHand",
            "- BonesHand.Tiles is the dominoes currently in that hand",
            string.Empty,
            "Forbidden (these members do not exist):",
            "- BonesBoardEnd.LowPip, BonesBoardEnd.HighPip",
            "- BonesBoard.GetAdjacent or any board adjacency helpers",
            "- Constructing new BonesMove instances",
            string.Empty,
            "Nullable guidance:",
            "- move.Tile is BonesTile?; check for null before reading LowPip, HighPip, TotalPips, or IsDouble.",
            "- board.LeftEnd and board.RightEnd are BonesBoardEnd?; use end.Pip.Value when an end exists.");
        var skeleton = string.Join(
            Environment.NewLine,
            "using Wip.Bones.Domain;",
            "using Wip.Bones.Engine;",
            string.Empty,
            $"public sealed class SeatStrategy : {InterfaceName}",
            "{",
            $"    public {MoveTypeName} {ChooseMoveMethodName}({StateTypeName} state, {LegalMovesParameterType} legalMoves)",
            "    {",
            "        foreach (var move in legalMoves)",
            "        {",
            "            if (move.IsPass)",
            "                continue;",
            string.Empty,
            "            var tile = move.Tile!.Value;",
            "            if (tile.TotalPips >= state.Board.LeftEnd?.Pip.Value)",
            "                return move;",
            "        }",
            string.Empty,
            "        return legalMoves[0];",
            "    }",
            "}");

        return string.Join(
            Environment.NewLine,
            ContractHeader,
            string.Empty,
            $"Implement a public sealed class that implements {InterfaceName}.",
            string.Empty,
            "Method signature:",
            $"{MoveTypeName} {ChooseMoveMethodName}({StateTypeName} state, {LegalMovesParameterType} legalMoves)",
            string.Empty,
            $"The host calls {GetLegalMovesSource} and passes the resulting legalMoves snapshot to your script.",
            LegalMoveConstraint,
            MoveSelectionConstraint,
            string.Empty,
            typeMembers,
            string.Empty,
            "Allowed namespaces:",
            allowedNamespaces,
            string.Empty,
            "Skeleton:",
            skeleton);
    }
}
