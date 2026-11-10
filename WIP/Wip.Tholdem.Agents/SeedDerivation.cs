using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Wip.Tholdem.Agents;

/// <summary>
/// Deterministic derivations. <see cref="Derive"/> is the engine's hand/deck derivation and must not
/// change: the deck of a recorded hand depends on it. <see cref="DeriveAgentSeed"/> derives the
/// randomness base of an agent decision from the agent's own observation and public turn coordinates,
/// so no value an agent receives is a function of the session seed. Every agent choice is derived
/// from these functions; no clock, ambient random source or culture participates.
/// </summary>
public static class SeedDerivation
{
    private const string AgentSeedDomain = "tholdem-agent-seed";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static ulong Derive(ulong sessionSeed, int handNumber, int seat, int actionIndex, ulong stream)
    {
        unchecked
        {
            var z = sessionSeed
                ^ (0x9E3779B97F4A7C15UL * (ulong)(handNumber + 1))
                ^ (0xC2B2AE3D27D4EB4FUL * (ulong)(seat + 1))
                ^ (0x165667B19E3779F9UL * (ulong)(actionIndex + 1))
                ^ stream;
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// The randomness base of one agent decision: the first eight bytes, big-endian, of SHA-256 over
    /// the invariant text
    /// <c>tholdem-agent-seed|&lt;canonicalObservation&gt;|&lt;handNumber&gt;|&lt;seat&gt;|&lt;actionIndex&gt;|&lt;stream&gt;</c>.
    /// The observation is the agent's own input (its hole cards plus public state), so the seed is
    /// reproducible for an honest replay and reveals nothing about the session seed or the deck: it
    /// is not a function of either.
    /// </summary>
    public static ulong DeriveAgentSeed(
        string canonicalObservation,
        int handNumber,
        int seat,
        int actionIndex,
        ulong stream)
    {
        ArgumentNullException.ThrowIfNull(canonicalObservation);

        var material = string.Create(
            CultureInfo.InvariantCulture,
            $"{AgentSeedDomain}|{canonicalObservation}|{handNumber}|{seat}|{actionIndex}|{stream}");
        var digest = SHA256.HashData(Utf8.GetBytes(material));
        return BinaryPrimitives.ReadUInt64BigEndian(digest);
    }
}
