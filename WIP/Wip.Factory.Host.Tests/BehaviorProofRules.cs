namespace Wip.Factory.Host.Tests;

/// <summary>
/// The policy predicates the compliance gate applies to declared tests. They exist so the gate's
/// judgement can be tested directly instead of being buried in reflection code (tests 64–66).
/// </summary>
public static class BehaviorProofRules
{
    /// <summary>Assertions that inspect only shape or existence, never behaviour.</summary>
    private static readonly string[] MetadataOnlyAssertions =
    [
        "Assert.NotNull", "Assert.Null(", "Assert.IsType", "Assert.IsAssignableFrom", "Assert.True(x is "
    ];

    /// <summary>
    /// True when every assertion in the body is metadata-only. A test whose assertions say nothing
    /// about a produced value is supporting evidence, never proof (verification policy).
    /// </summary>
    public static bool IsMetadataOnly(IReadOnlyList<string> assertionLines)
    {
        ArgumentNullException.ThrowIfNull(assertionLines);

        return assertionLines.Count == 0 || assertionLines.All(static line =>
            MetadataOnlyAssertions.Any(kind => line.Contains(kind, StringComparison.Ordinal)));
    }

    /// <summary>
    /// A negative-path item is compliant only when it asserts both the rejection and the absence of the
    /// side effect that must not have happened.
    /// </summary>
    public static bool NegativePathIsComplete(bool assertsRejection, bool assertsNoSideEffect)
        => assertsRejection && assertsNoSideEffect;

    /// <summary>
    /// A test that only exercises a scripted provider is a unit-test double and never counts as
    /// evidence that a real model drove the factory.
    /// </summary>
    public static bool CountsAsFactoryEvidence(bool usesScriptedProviderOnly, bool exercisesRealProviderPath)
        => exercisesRealProviderPath && !usesScriptedProviderOnly;
}
