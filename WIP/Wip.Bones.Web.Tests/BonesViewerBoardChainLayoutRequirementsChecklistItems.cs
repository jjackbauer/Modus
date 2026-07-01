namespace Wip.Bones.Web.Tests;

internal static class BonesViewerBoardChainLayoutRequirementsChecklistItems
{
    public const string ReplayJsOnView =
        "Fix `viewer.js` bootstrap to resolve `sessionId` and `matchId` from `#bones-viewer` `data-session-id` / `data-match-id` (and from `/bones/sessions/{sessionId}/matches/{matchId}/view` path segments) when query params are absent - required for learning-host `/view` URL [foundation for replay] [mandatory - replay JS on view route]";

    public const string BootstrapInitialTurnIndex =
        "Fix `viewer.js` bootstrap: do not call `renderSnapshot` final-state board/hands over SSR when initial `?turnIndex` is present; load that frame first; default to final turn only when no turn specified [depends on JS bootstrap] [mandatory - replay scrub resets board]";

    public const string ScrubberInputFrameLoad =
        "On scrubber `input`, update turn label immediately, fetch frame, rebuild board/hands/ends/chrome; on fetch failure show error state and do not leave stale final board [depends on JS bootstrap] [mandatory - replay scrub resets board]";

    public const string TurnZeroReplayBehaviorProof =
        "Add DOM/integration test: `/view?turnIndex=0` on completed match has strictly fewer `.board-chain-tile` elements than final snapshot and hand tile counts match frame API at turn 0 [depends on JS bootstrap] [mandatory - replay behavior proof]";

    public const string ChainGeometryBuilder =
        "Add `BonesBoardChainGeometryBuilder` to assign non-overlapping `FineColumnStart`/`FineColumnEnd`/`FineRowStart`/`FineRowEnd` per placement; opening vertical flush to horizontal neighbors [foundation] [mandatory - non-overlapping fine grid]";

    public const string FineGridExtentMetadata =
        "Extend `BonesBoardVisualLayout` with `FineColumnCount`, `FineRowCount`, `OccupiedWidthPixels`, `OccupiedHeightPixels` from fine-grid occupancy [depends on geometry builder] [mandatory - accurate extent]";

    public const string TransformScaleOccupiedExtent =
        "Refactor scale calculator and `viewer.js` to use occupied extent, enforce MinReadableTilePixels, return transform scale without cell-shrink [depends on extent] [mandatory - readable scale to fit]";

    public const string TransformCssInnerWrapper =
        "Update `viewer.css`: `.board-chain-inner` with `transform: scale(var(--board-chain-scale))`; outer slot sized to unscaled occupancy [depends on scale refactor] [mandatory - transform-based fit]";

    public const string GridColumnStartEndPlacement =
        "Update `BonesDominoTileMarkup` and `viewer.js` to emit `grid-column: start / end` from geometry fields [depends on geometry builder] [mandatory - placement parity]";

    public const string BranchDoubleLayout =
        "Fix branch-double placement so main-line and perpendicular double at same `gridX` do not overlap [depends on geometry builder] [mandatory - branch layout]";

    public const string ChainSlotScaleMeasurement =
        "Measure scale against `#board-chain` slot width only, not full `.board` [depends on scale refactor]";

    public const string ScrubberChromeSync =
        "Sync replay scrubber: `#turn-index` matches `scrubber.value` immediately on input and after renderFrame [depends on viewer.js] [mandatory - replay chrome sync]";

    public const string EngineReplayedLongChainDomProof =
        "Add engine-replayed long-chain DOM test (at least 12 board tiles) proving legible dimensions and transform scale below 1 without cell-shrink below floor [depends on geometry tests] [mandatory - real match proof]";

    public const string GeometryBehaviorProof =
        "Replace metadata-only layout tests from replay-scaling with geometry proofs: tile dimensions at or above floor, non-overlapping boxes, chain width within slot, opening flush to neighbor [depends on all above] [mandatory - geometry behavior proof]";

    public const string ComplianceRegistry =
        "Register in `BehaviorProofComplianceRegistry` [depends on tests]";

    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification [mandatory - behavior-proof policy]";
}
