namespace Wip.Bones.Web.Tests;

internal static class BonesViewerVisualizationRequirementsChecklistItems
{
    public const string ViewerDtos =
        "Introduce typed viewer DTOs: `BonesBoardTilePlacement` (low/high pip, `IsDouble`, `Orientation`, grid coordinates, `PlayedBySeat`), `BonesHandTileView` (low/high pip, `OwnerSeat`), `BonesBoardVisualLayout`, and stable `PlayerColorsBySeat` map (seats 1–4 뿯↽ distinct color tokens) in `Wip.Bones.Web` [foundation for visual payload]";

    public const string LayoutBuilder =
        "Implement `BonesBoardVisualLayoutBuilder` deriving layout from `BonesBoard.Tiles` plus play `BonesEvent` sequence: opening tile at center `(0,0)` vertical; left/right extensions shift grid X; non-doubles horizontal; doubles vertical with branch-arm grid offsets for ramification visualization [depends on viewer DTOs] [mandatory - board chain layout]";

    public const string ExtendedViewModels =
        "Extend `BonesMatchViewModel` and `BonesMatchFrame` with `BoardLayout`, `HandTilesBySeat` (tile list per seat), and `PlayerColorsBySeat`; preserve existing count/end-pip fields for backward-compatible tests until migrated [depends on layout builder]";

    public const string ViewerServiceReplay =
        "Extend `BonesMatchViewerService.BuildSnapshot`, `CreateFrame`, and timeline builders to populate board layout, per-seat hand tiles, and played-by-seat attribution from engine replay (not inferred from counts alone) [depends on extended view models] [mandatory - engine-equivalent payload]";

    public const string JsonApiRoutes =
        "Expose new fields on existing JSON routes (`GET /bones/sessions/{sessionId}/matches/{matchId}`, `GET .../frames/{turnIndex}`) via camelCase serialization without breaking current consumers [depends on viewer service]";

    public const string TileCssMarkup =
        "Add domino tile rendering in `viewer.css` and shared markup conventions: pip dots on both halves, `data-orientation`, `data-low-pip`, `data-high-pip`, `data-seat-color`, `data-played-by-seat` / `data-owner-seat`, and centered `.seat-color-marker` circle element [depends on API payload]";

    public const string BoardChainUi =
        "Replace board-end-only UI (`left-end-pip` / `right-end-pip` / placeholder chain) with `#board-chain` container rendering ordered placement elements from `BoardLayout.Tiles` — opening vertical at center, chain growing horizontally, doubles vertical [depends on tile CSS] [mandatory - concatenated board visualization]";

    public const string HandTileUi =
        "Update hands panel to render each seat's tile list with owner seat color circle on every tile; retain active-seat highlight and cumulative score labels [depends on hand tile DTOs] [mandatory - hand tile visualization]";

    public const string SsrRenderer =
        "Update `BonesViewerPageRenderer` SSR HTML to emit the same board-chain and hand-tile DOM structure (with data attributes) as the SPA for integration-test assertions [depends on tile markup conventions]";

    public const string SpaReplayScrub =
        "Update `viewer.js` to render board chain and hand tiles from snapshot/frame JSON and refresh both on replay scrubber `input` events [depends on SPA markup and API fields]";

    public const string BehaviorProofTests =
        "Add `Wip.Bones.Web.Tests` coverage: layout builder orientation rules, viewer service payload parity with engine, HTTP JSON integration, and DOM assertions for board chain + hand tiles at snapshot and scrubbed turn index [depends on all above] [mandatory - behavior proof]";

    public const string ComplianceRegistry =
        "Register this requirements document in `BehaviorProofComplianceRegistry` with owning `Wip.Bones.Web.Tests` checklist bindings [depends on tests]";

    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
}
