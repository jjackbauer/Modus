namespace Wip.Bones.Web.Tests;

internal static class BonesViewerDominoRenderingRequirementsChecklistItems
{
    public const string JsonDomPipParity = "Add frame API + DOM parity test: for each board tile at a seeded turn, `facingLowPip`/`facingHighPip` in JSON equal `.domino-half-low`/`.domino-half-high` `data-pip` after scrub [foundation for blank-tile diagnosis] [mandatory - JSON DOM pip parity]";

    public const string HandPipParity = "Add hand parity test: each hand tile JSON `lowPip`/`highPip` equals DOM half `data-pip`; non-zero halves have expected `.pip` child count [depends on pip parity foundation] [mandatory - hand pip parity]";

    public const string BlankTileRenderFix = "Fix client `viewer.js` board/hand tile builders when JSON pip fields are present but render blank [depends on parity tests reproducing failure] [mandatory - blank tile render fix]";

    public const string StandardPipLayoutSync = "Keep `BonesStandardDominoPipLayout` and `viewer.js` `pipPositions` in lockstep [foundation] [mandatory - standard pip layout]";

    public const string StandardPipGridRender = "Render standard pip grid on board tiles for all orientations and `pipAxis` values (horizontal on non-double vertical openings, vertical on all doubles) [depends on pip layout sync] [mandatory - standard pip layout]";

    public const string ChainFacingCorrectness = "Prove `ResolveChainFacing` assigns chain-left and chain-right pips so `FacingHigh[i] == FacingLow[i+1]` for engine-replayed boards including left prepends [depends on facing resolver] [mandatory - chain facing correctness]";

    public const string BidirectionalChainLeft = "Prove left-arm plays produce negative `gridX` placements flush to opening with spatial connecting pip matching engine left end [depends on facing + geometry] [mandatory - bidirectional chain]";

    public const string BidirectionalChainRight = "Prove right-arm plays produce positive `gridX` with matching junction pips [depends on geometry] [mandatory - bidirectional chain]";

    public const string DoubleSixRender = "Fix vertical double-six (and other doubles) clipping: container overflow, fine-row extent, pip half height at chain edge [depends on geometry extent] [mandatory - double-six render]";

    public const string HandBoardLayoutParity = "Align hand tile CSS with board tile contract; hand tiles readable inside seat panel [depends on pip render] [mandatory - hand board layout parity]";

    public const string HandReadability = "Add DOM test: hand `.domino-tile` width/height at or above readable floor inside `.hand-tiles` [depends on hand CSS] [mandatory - hand readability]";

    public const string LeftArmProof = "Add engine-replayed match DOM test: left play before right play shows tile at `data-grid-x` less than opening [depends on bidirectional layout] [mandatory - left arm proof]";

    public const string RightArmSpatialVisibility = "Add greedy engine-replayed DOM test: when frame `maxGridX > 0`, at least one positive `gridX` tile bbox is strictly right of opening bbox inside `#board-chain` [foundation for right-arm visibility] [mandatory - right arm spatial visibility]";

    public const string OpeningDoubleSixExhibition = "Add greedy engine-replayed DOM test: 6-6 opening at `gridX==0` shows six visible pips per half and `BoardTilePipsContainedInTileBounds` passes [foundation for exhibition diagnosis] [mandatory - opening double-six exhibition]";

    public const string OpeningDoubleSixExhibitionFix =
        "Render all doubles with `pipAxis` vertical per bones standard — portrait tile with stacked halves and horizontal seat divider [depends on opening exhibition test] [mandatory - opening double-six exhibition]";

    public const string OpeningDoubleSixExhibitionDivider =
        "Add DOM gate `OpeningDoubleDividerIsHorizontal` to opening 6-6 test so pip-bounds-only proof cannot pass with wrong divider layout [depends on divider fix] [mandatory - opening double-six exhibition divider proof]";

    public const string FineGridNormalization =
        "Normalize fine-grid placement for SSR and `viewer.js`: emit grid lines relative to `minFineColumnStart`/`minFineRowStart`; size `--board-chain-columns`/`--board-chain-rows` to normalized extent so right-arm tiles are not placed past grid edge [depends on right-arm visibility test] [mandatory - fine grid normalization]";

    public const string ClientFineGridParity =
        "Ensure `viewer.js` always uses API `fineColumnStart`/`fineRowStart` when present; remove silent legacy fallback for frames from `BonesMatchViewerService` [depends on fine grid normalization] [mandatory - client fine grid parity]";

    public const string EndChainDoubleSixRender =
        "Add DOM test: end-chain vertical 6-6 double (`gridY > 0` or main line) retains six pips per half inside bounds at max `gridX` [depends on opening fix] [mandatory - end-chain double-six render]";

    public const string BidirectionalSpatialProof =
        "Add engine-replayed match with both arms: `minGridX < 0` and `maxGridX > 0` — DOM shows tiles on both sides with junction pips matching frame JSON [depends on both arm tests] [mandatory - bidirectional spatial proof]";

    public const string ComplianceRegistry = "Register regression checklist rows in `BehaviorProofComplianceRegistry` with trait bindings [depends on tests]";

    public const string BehaviorProofPolicy = "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
}