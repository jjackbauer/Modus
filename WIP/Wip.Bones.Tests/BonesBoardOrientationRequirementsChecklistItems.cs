namespace Wip.Bones.Tests;

internal static class BonesBoardOrientationRequirementsChecklistItems
{
    public const string ChainTileType =
        "Introduce `BonesChainTile` holding canonical `BonesTile` identity plus `ChainLeftPip` and `ChainRightPip` along board order [foundation for oriented storage] [mandatory - chain tile type]";

    public const string BoardEndsFromFacings =
        "Change `BonesBoard.Tiles` to `ImmutableArray<BonesChainTile>`; derive `LeftEnd` from `Tiles[0].ChainLeftPip` and `RightEnd` from `Tiles[^1].ChainRightPip` [depends on chain tile type] [mandatory - board ends from facings]";

    public const string PlayAlignedAppendPrepend =
        "Update `PrependTile` and `AppendTile` to set `ChainLeftPip`/`ChainRightPip` explicitly from matched open end and free end [depends on board ends from facings] [mandatory - play-aligned append/prepend]";

    public const string ChainEdgeInvariant =
        "Add `BonesBoard.ValidateChainEdges()` asserting `ChainRightPip == next.ChainLeftPip` for every adjacent pair [depends on play-aligned append/prepend] [mandatory - chain edge invariant]";

    public const string HandEventIdentityUnchanged =
        "Keep `BonesEvent.Tile` and hand tiles as canonical `BonesTile`; only board chain uses oriented facings [depends on chain tile type] [mandatory - hand/event identity unchanged]";

    public const string DownstreamCompileParity =
        "Update `BonesBoard` consumers in `Wip.Bones.Agents` and tests for new board shape [depends on board ends from facings] [mandatory - downstream compile parity]";

    public const string PerMoveInvariant =
        "After every legal `ApplyMove` play in a greedy full-round simulation, chain edge invariant holds [depends on chain edge invariant] [mandatory - per-move invariant]";

    public const string LearningMatchRegression =
        "`BonesBoard_GivenLearningMatch79daFinalChain_ExpectedLinearHighLowEdgesMatch` passes when replayed through engine [depends on per-move invariant] [mandatory - learning match regression]";

    public const string BidirectionalArmsStorage =
        "Scripted left-arm then right-arm sequence maintains edge invariant and correct `LeftEnd`/`RightEnd` [depends on play-aligned append/prepend] [mandatory - bidirectional arms storage]";

    public const string OpenerOnlyDoubleJunction =
        "Opening double: equal chain facings; first left/right extensions attach to opener ends only [depends on play-aligned append/prepend] [mandatory - opener-only double junction]";

    public const string FacingFromStorage =
        "Simplify `BonesBoardTileFacingResolver.ResolveChainFacing` to return chain-left/chain-right from `BonesChainTile` [depends on chain edge invariant] [mandatory - facing from storage]";

    public const string SandwichSliceRegression =
        "`ResolveChainFacing_GivenLearningMatchSandwichSlice_ExpectedFacingHighMatchesNextFacingLow` passes [depends on facing from storage] [mandatory - sandwich slice regression]";

    public const string LayoutConnectivity =
        "`BonesBoardVisualLayoutBuilder.BuildLayout` uses oriented facings; adjacent `FacingHighPip`/`FacingLowPip` match [depends on facing from storage] [mandatory - layout connectivity]";

    public const string DeleteFalsePositiveTest =
        "Remove or rewrite `ResolveChainFacing_GivenOrientedZeroPipLeftArmChain` so it does not encode bogus junctions as expected [depends on facing from storage] [mandatory - delete false-positive test]";

    public const string DomJunctionParity =
        "Frame API + DOM: adjacent board tiles show matching exit/entry `data-pip` on learning-match replay final turn [depends on layout connectivity] [mandatory - DOM junction parity]";

    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    public const string ComplianceRegistry =
        "Register checklist rows in `BehaviorProofComplianceRegistry` with trait bindings [depends on tests]";
}