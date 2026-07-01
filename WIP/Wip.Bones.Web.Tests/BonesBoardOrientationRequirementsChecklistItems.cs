namespace Wip.Bones.Web.Tests;

internal static class BonesBoardOrientationRequirementsChecklistItems
{
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
}