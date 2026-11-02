namespace Wip.Bones.Benchmark;

public sealed record Interval95(double Low, double High);

public static class Stats
{
    public static Interval95 WilsonInterval(int successes, int trials)
    {
        if (trials <= 0)
            return new Interval95(double.NaN, double.NaN);

        const double z = 1.959963984540054;
        var p = successes / (double)trials;
        var denominator = 1.0 + (z * z / trials);
        var center = (p + (z * z / (2.0 * trials))) / denominator;
        var margin = z * Math.Sqrt((p * (1.0 - p) / trials) + (z * z / (4.0 * trials * trials))) / denominator;
        return new Interval95(Math.Max(0.0, center - margin), Math.Min(1.0, center + margin));
    }

    public static (double Mean, double StandardDeviation) MeanAndStandardDeviation(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return (double.NaN, double.NaN);

        var mean = values.Average();
        if (values.Count == 1)
            return (mean, 0.0);

        var variance = values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1);
        return (mean, Math.Sqrt(variance));
    }

    public static Interval95 MeanConfidenceInterval95(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
            return new Interval95(double.NaN, double.NaN);

        var (mean, standardDeviation) = MeanAndStandardDeviation(values);
        var standardError = standardDeviation / Math.Sqrt(values.Count);
        var margin = 1.959963984540054 * standardError;
        return new Interval95(mean - margin, mean + margin);
    }

    public static double PairedStandardError(IReadOnlyList<double> differences)
    {
        if (differences.Count < 2)
            return double.NaN;

        var (_, standardDeviation) = MeanAndStandardDeviation(differences);
        return standardDeviation / Math.Sqrt(differences.Count);
    }
}
