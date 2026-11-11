using Wip.Tholdem.Agents.Randomness;

namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>
/// One block-level population estimate. The reported bounds are conservative: the lower bound is the
/// minimum of the normal and bootstrap lower bounds and the upper bound is the maximum of the two
/// uppers.
/// </summary>
public sealed record PopulationEstimate(
    double MeanBbPer100, double LowerBoundBbPer100, double UpperBoundBbPer100,
    double ConfidenceLevel, string Method, int UnitCount, double StandardError,
    double NormalLowerBoundBbPer100, double BootstrapLowerBoundBbPer100);

/// <summary>
/// Pure block-level statistics: no clock, no culture and no ambient randomness. The normal interval
/// uses the sample standard deviation and the bootstrap interval resamples whole units with the
/// assembly's seeded xorshift64* generator, so the same inputs always produce the same estimate.
/// </summary>
public static class BlockStatistics
{
    public const int DefaultBootstrapSamples = 2000;

    public const string Method = "block-normal+bootstrap";

    // Wichura's AS241 (PPND16) inverse-normal rational coefficients. The approximation is
    // accurate to double precision; Math.Erfc is unavailable in this target framework, so the
    // Acklam sketch cannot be refined with its erf-based Halley step.
    private static readonly double[] A =
    [
        3.3871328727963666080e+00,
        1.3314166789178437745e+02,
        1.9715909503065514427e+03,
        1.3731693765509461125e+04,
        4.5921953931549871457e+04,
        6.7265770927008700853e+04,
        3.3430575583588128105e+04,
        2.5090809287301226727e+03,
    ];

    private static readonly double[] B =
    [
        4.2313330701600911252e+01,
        6.8718700749205790830e+02,
        5.3941960214247511077e+03,
        2.1213794301586595867e+04,
        3.9307895800092710610e+04,
        2.8729085735721942674e+04,
        5.2264952788528545610e+03,
    ];

    private static readonly double[] C =
    [
        1.42343711074968357734e+00,
        4.63033784615654529590e+00,
        5.76949722146069140550e+00,
        3.64784832476320460504e+00,
        1.27045825245236838258e+00,
        2.41780725177450611770e-01,
        2.27238449892691845833e-02,
        7.74545014278341407640e-04,
    ];

    private static readonly double[] D =
    [
        2.05319162663775882187e+00,
        1.67638483018380384940e+00,
        6.89767334985100004550e-01,
        1.48103976427480074590e-01,
        1.51986665636164571966e-02,
        5.47593808499534494600e-04,
        1.05075007164441684324e-09,
    ];

    private static readonly double[] E =
    [
        6.65790464350110377720e+00,
        5.46378491116411436990e+00,
        1.78482653991729133580e+00,
        2.96560571828504891230e-01,
        2.65321895265761230930e-02,
        1.24266094738807843860e-03,
        2.71155556874348757815e-05,
        2.01033439929228813265e-07,
    ];

    private static readonly double[] F =
    [
        5.99832206555887937690e-01,
        1.36929880922735805310e-01,
        1.48753612908506148525e-02,
        7.86869131145613259100e-04,
        1.84631831751005468180e-05,
        1.42151175831644588870e-07,
        2.04426310338993978564e-15,
    ];

    /// <summary>
    /// The inverse standard-normal CDF via Wichura's AS241 (PPND16) rational approximation, accurate
    /// to double precision (observed error at 0.975 is 4.4e-16). Acklam's unrefined approximation is
    /// 1.58e-9 away from the reference at 0.975, above the required 1e-9 tolerance, and no erf-based
    /// refinement is available in this target framework, so the higher-precision AS241 rational form
    /// is used instead.
    /// </summary>
    public static double NormalQuantile(double probability)
    {
        if (!(probability > 0.0 && probability < 1.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(probability),
                probability,
                "The probability must lie strictly between 0 and 1.");
        }

        var q = probability - 0.5;
        if (Math.Abs(q) <= 0.425)
        {
            var r = 0.180625 - (q * q);
            return q * Horner(A, r) / ((Horner(B, r) * r) + 1.0);
        }

        var tail = probability < 0.5 ? probability : 1.0 - probability;
        var t = Math.Sqrt(-Math.Log(tail));
        double value;
        if (t <= 5.0)
        {
            var shifted = t - 1.6;
            value = Horner(C, shifted) / ((Horner(D, shifted) * shifted) + 1.0);
        }
        else
        {
            var shifted = t - 5.0;
            value = Horner(E, shifted) / ((Horner(F, shifted) * shifted) + 1.0);
        }

        return q < 0.0 ? -value : value;
    }

    private static double Horner(double[] coefficients, double value)
    {
        var result = 0.0;
        for (var index = coefficients.Length - 1; index >= 0; index--)
        {
            result = (result * value) + coefficients[index];
        }

        return result;
    }

    /// <summary>
    /// Computes the block estimate for the supplied unit means. An empty unit list, a confidence
    /// level outside (0, 1), a non-positive bootstrap sample count and a non-finite unit mean are
    /// refused rather than guessed.
    /// </summary>
    public static PopulationEstimate Compute(
        IReadOnlyList<double> unitMeans,
        double confidenceLevel,
        ulong bootstrapSeed,
        int bootstrapSamples = DefaultBootstrapSamples)
    {
        ArgumentNullException.ThrowIfNull(unitMeans);

        var unitCount = unitMeans.Count;
        if (unitCount <= 0)
        {
            throw new ArgumentException("At least one unit mean is required.", nameof(unitMeans));
        }

        if (!(confidenceLevel > 0.0 && confidenceLevel < 1.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidenceLevel),
                confidenceLevel,
                "The confidence level must lie strictly between 0 and 1.");
        }

        if (bootstrapSamples < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bootstrapSamples),
                bootstrapSamples,
                "At least one bootstrap sample is required.");
        }

        var sum = 0.0;
        var allEqual = true;
        var first = unitMeans[0];
        for (var index = 0; index < unitCount; index++)
        {
            var value = unitMeans[index];
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("Every unit mean must be a finite number.", nameof(unitMeans));
            }

            sum += value;
            if (value != first)
            {
                allEqual = false;
            }
        }

        var mean = sum / unitCount;
        double standardError = 0.0;
        var normalLower = mean;
        var normalUpper = mean;
        var bootstrapLower = mean;
        var bootstrapUpper = mean;

        if (!allEqual)
        {
            var sumOfSquares = 0.0;
            for (var index = 0; index < unitCount; index++)
            {
                var deviation = unitMeans[index] - mean;
                sumOfSquares += deviation * deviation;
            }

            var standardDeviation = Math.Sqrt(sumOfSquares / (unitCount - 1));
            standardError = standardDeviation / Math.Sqrt(unitCount);
            var z = NormalQuantile(0.5 + (confidenceLevel / 2.0));
            normalLower = mean - (z * standardError);
            normalUpper = mean + (z * standardError);

            var bootstrapMeans = new double[bootstrapSamples];
            var random = new Xorshift64Star(bootstrapSeed);
            for (var sample = 0; sample < bootstrapSamples; sample++)
            {
                var sampleSum = 0.0;
                for (var draw = 0; draw < unitCount; draw++)
                {
                    var index = (int)(random.Next() % (ulong)unitCount);
                    sampleSum += unitMeans[index];
                }

                bootstrapMeans[sample] = sampleSum / unitCount;
            }

            Array.Sort(bootstrapMeans);
            var alpha = 1.0 - confidenceLevel;
            bootstrapLower = Percentile(bootstrapMeans, alpha / 2.0);
            bootstrapUpper = Percentile(bootstrapMeans, 1.0 - (alpha / 2.0));
        }

        var lower = Math.Min(normalLower, bootstrapLower);
        var upper = Math.Max(normalUpper, bootstrapUpper);

        return new PopulationEstimate(
            mean,
            lower,
            upper,
            confidenceLevel,
            Method,
            unitCount,
            standardError,
            normalLower,
            bootstrapLower);
    }

    /// <summary>Linear-interpolated percentile over an ascending sample.</summary>
    private static double Percentile(double[] sorted, double probability)
    {
        var position = probability * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        var fraction = position - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
    }
}
