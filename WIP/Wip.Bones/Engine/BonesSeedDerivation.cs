using System.Text;

namespace Wip.Bones.Engine;

/// <summary>
/// Process-stable seed derivation for Bones matches, rounds, and evaluations.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="System.HashCode"/> and <see cref="string.GetHashCode()"/> are randomized per
/// process in .NET. Any seed derived from them changes between runs, which makes promotion
/// decisions, benchmarks, and A/B experiments non-reproducible. This type derives seeds from a
/// fixed FNV-1a 64-bit hash followed by a SplitMix64 finalizer so the same inputs always produce
/// the same seed on every process, platform, and runtime.
/// </para>
/// <para>
/// The output is a non-negative <see cref="int"/> so it can be passed to <see cref="Random"/>.
/// </para>
/// </remarks>
public static class BonesSeedDerivation
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>
    /// Derives a stable non-negative seed from a base seed and an ordered list of parts.
    /// The order of <paramref name="parts"/> is significant.
    /// </summary>
    public static int Derive(int seed, params ReadOnlySpan<int> parts)
    {
        var hash = FnvOffsetBasis;
        MixInt(ref hash, seed);
        foreach (var part in parts)
            MixInt(ref hash, part);

        return Finalize(hash);
    }

    /// <summary>
    /// Derives a stable non-negative seed from a base seed and a string label.
    /// </summary>
    public static int DeriveWithLabel(int seed, string label, params ReadOnlySpan<int> parts)
    {
        ArgumentNullException.ThrowIfNull(label);

        var hash = FnvOffsetBasis;
        MixInt(ref hash, seed);
        foreach (var character in label)
        {
            MixByte(ref hash, (byte)character);
            MixByte(ref hash, (byte)(character >> 8));
        }

        foreach (var part in parts)
            MixInt(ref hash, part);

        return Finalize(hash);
    }

    /// <summary>
    /// Derives a stable non-negative seed from a string value alone.
    /// </summary>
    public static int DeriveFromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return DeriveWithLabel(0, value);
    }

    /// <summary>
    /// Combines two seeds without allocating and without process-randomized hashing.
    /// </summary>
    public static int Combine(int left, int right) => Derive(left, right);

    private static void MixInt(ref ulong hash, int value)
    {
        var unsigned = (uint)value;
        MixByte(ref hash, (byte)unsigned);
        MixByte(ref hash, (byte)(unsigned >> 8));
        MixByte(ref hash, (byte)(unsigned >> 16));
        MixByte(ref hash, (byte)(unsigned >> 24));
    }

    private static void MixByte(ref ulong hash, byte value)
        => hash = (hash ^ value) * FnvPrime;

    private static int Finalize(ulong hash)
    {
        hash ^= hash >> 33;
        hash *= 0xFF51AFD7ED558CCDUL;
        hash ^= hash >> 33;
        hash *= 0xC4CEB9FE1A85EC53UL;
        hash ^= hash >> 33;
        return (int)(hash & 0x7FFFFFFF);
    }
}
