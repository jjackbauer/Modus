namespace Wip.Bones.Web.Tests;

internal static class BonesViewerReplayScalingRequirementsChecklistItems
{
    public const string EngineOrientedChainConnectivity =
        "Refactor `BonesBoardVisualLayoutBuilder` to zip `board.Tiles` (engine-oriented chain order) with play events for attribution; emit `FacingLowPip`/`FacingHighPip` (or equivalent flip flag) so each tile's rendered halves match the pip on the face that touches the previous/next neighbor [foundation for correct connectivity] [mandatory - engine-oriented chain connectivity]";

    public const string ConnectionInvariant =
        "Add layout validation in builder/tests: for every adjacent pair in chain order, the touching faces' pips are equal (invariant enforced by `BonesGameEngine.OrientTileForEnd`) [depends on oriented board tiles] [mandatory - connection invariant]";

    public const string LayoutExtentMetadata =
        "Extend `BonesBoardVisualLayout` with chain extent metadata (`MinGridX`, `MaxGridX`, `MinGridY`, `MaxGridY`, `ColumnCount`, `RowCount`) computed by `BonesBoardVisualLayoutBuilder` so clients can position and scale without re-deriving bounds [depends on connectivity refactor]";

    public const string FrameCompletionFields =
        "Extend `BonesMatchFrame` with `IsRoundComplete`, `WinnerSeat`, and `RoundPipScore` derived from `BonesRoundState.Outcome` at frame time; populate in `BonesMatchViewerService.CreateFrame` [depends on layout metadata] [mandatory - per-turn completion state for replay chrome]";

    public const string JsonFrameCompletionFields =
        "Expose new frame fields on `GET /bones/sessions/{sessionId}/matches/{matchId}/frames/{turnIndex}` JSON (camelCase) without breaking existing consumers [depends on extended frame model]";

    public const string CssGridChainLayout =
        "Replace flex-wrap `#board-chain` layout with CSS grid (or absolute grid-cell placement) that positions each `.domino-tile` using `data-grid-x` and `data-grid-y` (and orientation) from `BonesBoardTilePlacement` — honoring `gridY` branch offsets for doubles; remove inter-tile gaps so shared edges abut (`gap: 0`, overlapping border, or single shared divider) [depends on layout metadata] [mandatory - spatial chain layout]";

    public const string StandardTileStyling =
        "Restyle `.domino-tile` to standard domino chrome per caller reference: legible pip halves, flush rectangular tile, seat color as centered `.seat-color-marker` disc **or** colored `.domino-divider` when disc conflicts with grid overlap — controlled by one markup path in `BonesDominoTileMarkup` and `viewer.js` [depends on grid layout] [mandatory - standard tile styling]";

    public const string BoardChainScaleCalculator =
        "Implement `BonesBoardChainScaleCalculator` (server) and mirror logic in `viewer.js` to compute a scale factor from layout extent vs board container size; apply via CSS custom property `--board-chain-scale` on `#board-chain` or an inner wrapper so the full chain fits without wrapping [depends on grid layout] [mandatory - scale to fit screen]";

    public const string SsrGridScaleMarkup =
        "Update `BonesDominoTileMarkup` and `BonesViewerPageRenderer` SSR output to emit grid-positioned board tiles with extent/scale data attributes matching the SPA [depends on grid layout and scale calculator]";

    public const string SpaReplayGridScale =
        "Update `viewer.js` `renderBoardChain` to clear and rebuild from frame `boardLayout` at each scrub, position tiles on the grid, recalculate scale after render (including `resize` listener), and call a shared `renderFrameChrome` that updates winner/round-score visibility from frame `isRoundComplete` (hide when false) [depends on frame completion fields and grid layout] [mandatory - replay scrub resets board and chrome]";

    public const string SsrRoundCompleteChrome =
        "Update `BonesViewerPageRenderer` to render winner and round pip score only when the selected frame is round-complete (or snapshot at final turn), not when `snapshot.IsComplete` alone is true at an earlier `turnIndex` [depends on frame completion fields] [mandatory - SSR replay reset]";

    public const string BehaviorProofTests =
        "Add `Wip.Bones.Web.Tests` coverage: layout extent metadata, frame completion fields, API JSON at mid-turn vs final turn, DOM tile pip parity and hand tile parity on scrub, grid position attributes, scale factor below 1 for long chains, and winner hidden at mid-turn on completed matches [depends on all above] [mandatory - behavior proof]";

    public const string ComplianceRegistry =
        "Register this requirements document in `BehaviorProofComplianceRegistry` with owning `Wip.Bones.Web.Tests` checklist bindings [depends on tests]";

    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
}
