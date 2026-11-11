using System.Collections;
using Wip.Tholdem.Agents.Evaluation;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// Round-two block-level uncertainty contract: an inverse-normal quantile, conservative normal plus
/// bootstrap bounds and a pure, clock-free deterministic estimate.
/// </summary>
public sealed class BlockStatisticsTests
{
    [Fact]
    public void NormalQuantile_Given975_IsWithin1e9Of1960()
    {
        var quantile = BlockStatistics.NormalQuantile(0.975);

        Assert.True(
            Math.Abs(quantile - 1.959963984540054) < 1e-9,
            $"observed {quantile:R}");
    }

    [Fact]
    public void Compute_GivenConstantUnits_ReturnsZeroWidthInterval()
    {
        var estimate = BlockStatistics.Compute(
            new[] { 2.5, 2.5, 2.5, 2.5, 2.5 },
            0.95,
            20261110UL,
            500);

        Assert.Equal(2.5, estimate.MeanBbPer100);
        Assert.Equal(2.5, estimate.LowerBoundBbPer100);
        Assert.Equal(2.5, estimate.UpperBoundBbPer100);
        Assert.Equal(5, estimate.UnitCount);
        Assert.Equal(0.0, estimate.StandardError);
    }

    [Fact]
    public void Compute_GivenSameInputs_IsDeterministic()
    {
        var units = new[] { 1.3, -2.1, 0.7, 4.2, -0.5, 2.0, -1.2, 3.1 };

        var first = BlockStatistics.Compute(units, 0.95, 987654321UL, 500);
        var second = BlockStatistics.Compute(units, 0.95, 987654321UL, 500);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_GivenSyntheticNormalDatasets_CoverageWithinCalibrationBand()
    {
        const int datasets = 100;
        const int unitsPerDataset = 24;
        const int bootstrapSamples = 500;
        var random = new LocalXorshift(20261110UL);
        var covered = 0;

        for (var dataset = 0; dataset < datasets; dataset++)
        {
            var units = new double[unitsPerDataset];
            for (var unit = 0; unit < unitsPerDataset; unit++)
            {
                units[unit] = random.NextStandardNormal();
            }

            var estimate = BlockStatistics.Compute(units, 0.95, (ulong)(dataset + 1), bootstrapSamples);
            if (estimate.LowerBoundBbPer100 <= 0.0 && estimate.UpperBoundBbPer100 >= 0.0)
            {
                covered++;
            }
        }

        var coverage = covered / (double)datasets;
        Assert.InRange(coverage, 0.90, 0.99);

        // Zero units and the negative-cardinality edge (the list API cannot express a negative count
        // any other way) are both refused instead of producing a fabricated estimate.
        Assert.Throws<ArgumentException>(
            () => BlockStatistics.Compute(Array.Empty<double>(), 0.95, 1UL, bootstrapSamples));
        Assert.Throws<ArgumentException>(
            () => BlockStatistics.Compute(new NegativeCountList(), 0.95, 1UL, bootstrapSamples));
    }

    /// <summary>A local xorshift64* plus Box-Muller so the calibration data never depends on the clock.</summary>
    private sealed class LocalXorshift
    {
        private const ulong Multiplier = 2685821657736338717UL;

        private ulong _state;

        public LocalXorshift(ulong seed) => _state = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;

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

        public double NextStandardNormal()
        {
            var first = NextDouble();
            var second = NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(first)) * Math.Cos(2.0 * Math.PI * second);
        }

        private double NextDouble() => (Next() >> 11) * (1.0 / 9007199254740992.0);
    }

    /// <summary>Pathological list used only to prove a negative unit count is refused.</summary>
    private sealed class NegativeCountList : IReadOnlyList<double>
    {
        public int Count => -1;

        public double this[int index] => throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<double> GetEnumerator() => Enumerable.Empty<double>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
