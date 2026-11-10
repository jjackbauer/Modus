namespace Wip.Tholdem.Agents.Randomness;

/// <summary>
/// The xorshift64* generator used by the seeded Monte Carlo equity calculator. Constants and shift
/// order match the domain contract; the all-zero seed is replaced with the golden-ratio constant.
/// </summary>
internal struct Xorshift64Star
{
    public const ulong ZeroSeedReplacement = 0x9E3779B97F4A7C15UL;

    public const ulong Multiplier = 2685821657736338717UL;

    private ulong _state;

    public Xorshift64Star(ulong seed)
    {
        _state = seed == 0UL ? ZeroSeedReplacement : seed;
    }

    public ulong Next()
    {
        unchecked
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * Multiplier;
        }
    }
}