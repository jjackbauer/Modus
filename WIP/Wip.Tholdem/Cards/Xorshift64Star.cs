namespace Wip.Tholdem.Cards;

/// <summary>
/// The xorshift64* generator that drives the deterministic deck shuffle.
/// </summary>
/// <remarks>
/// The algorithm is contractual. Constants: the all-zero seed is replaced with
/// <c>0x9E3779B97F4A7C15</c>; the state is advanced with shifts 12, 25 and 27; and every result is
/// multiplied by <c>2685821657736338717</c>. All arithmetic is unchecked and every operation is
/// culture-free and clock-free.
/// </remarks>
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
