namespace Wip.Bones.Web.Viewer;

public static class BonesStandardDominoPipLayout
{
    /// <summary>
    /// Standard double-six domino pip positions on a 3x3 grid:
    /// <code>
    /// 0 1 2
    /// 3 4 5
    /// 6 7 8
    /// </code>
    /// </summary>
    public static IReadOnlyList<int> GetGridPositions(int pipCount)
    {
        if (pipCount is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(pipCount), pipCount, "Pip count must be between 0 and 6.");

        return PipGridPositions[pipCount];
    }

    private static readonly IReadOnlyList<int>[] PipGridPositions =
    [
        [],
        [4],
        [2, 6],
        [2, 4, 6],
        [0, 2, 6, 8],
        [0, 2, 4, 6, 8],
        [0, 3, 6, 2, 5, 8],
    ];
}