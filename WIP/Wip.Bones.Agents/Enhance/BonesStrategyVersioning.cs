using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Enhance;

internal static class BonesStrategyVersioning
{
    private const string VersionPrefix = "-v";

    internal static BonesStrategyId NextVersion(
        BonesStrategyId currentStrategyId,
        BonesPlayerId playerId,
        IReadOnlySet<string>? existingIds = null)
    {
        var value = currentStrategyId.Value;
        var versionIndex = value.LastIndexOf(VersionPrefix, StringComparison.Ordinal);
        if (versionIndex >= 0
            && int.TryParse(
                value[(versionIndex + VersionPrefix.Length)..],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var version))
        {
            var baseName = value[..versionIndex];
            var candidateVersion = version + 1;
            var candidate = new BonesStrategyId($"{baseName}{VersionPrefix}{candidateVersion}");
            return SkipCollisions(candidate, baseName, candidateVersion, existingIds);
        }

        var fallbackBase = $"initial-seat-{playerId.Seat}";
        return FindFirstAvailableFallback(fallbackBase, existingIds);
    }

    private static BonesStrategyId FindFirstAvailableFallback(
        string baseName,
        IReadOnlySet<string>? existingIds)
    {
        if (existingIds is null || existingIds.Count == 0)
            return new BonesStrategyId($"{baseName}{VersionPrefix}1");

        var highestVersion = FindHighestExistingVersion(baseName, existingIds);
        var nextVersion = highestVersion + 1;
        return new BonesStrategyId($"{baseName}{VersionPrefix}{nextVersion}");
    }

    private static int FindHighestExistingVersion(string baseName, IReadOnlySet<string> existingIds)
    {
        var prefix = $"{baseName}{VersionPrefix}";
        var highest = 0;
        foreach (var id in existingIds)
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(
                    id[prefix.Length..],
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var version)
                && version > highest)
            {
                highest = version;
            }
        }

        return highest;
    }

    private static BonesStrategyId SkipCollisions(
        BonesStrategyId initialCandidate,
        string baseName,
        int startVersion,
        IReadOnlySet<string>? existingIds)
    {
        if (existingIds is null || existingIds.Count == 0)
            return initialCandidate;

        for (var v = startVersion; ; v++)
        {
            var candidate = $"{baseName}{VersionPrefix}{v}";
            if (!existingIds.Contains(candidate))
                return new BonesStrategyId(candidate);
        }
    }
}
