# Round 13 design — persisted plan JSON (D1 prerequisite)

**Objective:** make `EvaluationPlanV2` persistable and strictly parseable so a CLI/verifier can
reconstruct the pre-registered plan from a file and recompute its hash. No command wiring yet.

## API — `Evaluation/EvaluationPlanV2Json.cs` (new)

~~~csharp
public static class EvaluationPlanV2Json
{
    public const string NotCanonicalReason = "plan-not-canonical";
    public const string ShapeReason = "plan-shape-invalid";

    public static string Write(EvaluationPlanV2 plan);      // plan.ToCanonicalJson()
    public static EvaluationPlanV2 Parse(string json);      // strict canonical round-trip
}
~~~

Parse reads the schema, evaluator version, embedded game object, pool id, compositions, four seed
domains (schema, id, purpose, baseSeed, count, stored seeds), and the inferential fields; reconstructs
`GameConfiguration` and `SeedDomain` values, re-derives each seed list and requires it to equal the
stored list, calls `EvaluationPlanV2.Create`, and finally requires
`created.ToCanonicalJson() == json` exactly. Any failure throws `FormatException` whose message starts
with a stable code: `plan-not-json`, `plan-shape-invalid`, `plan-not-canonical`, `plan-seed-mismatch`.

Strictness: whitespace, key reordering, extra keys, missing members and a stored seed list that does
not match the derivation are all refused. A semantic mutation that is re-serialized canonically (for
example a different margin) parses successfully but has a different `PlanHash`; the gate/verifier
compares that hash, so it cannot be used to swap a plan silently. Document this explicitly.

## Tests — `WIP/Wip.Tholdem.Tests/Evaluation/EvaluationPlanV2JsonTests.cs` (five)

1. `Parse_GivenCanonicalPlan_RoundTrips`: create a four-domain plan, Write it, Parse it, assert equal
   JSON and `PlanHash`.
2. `Parse_GivenWhitespaceOrExtraKey_RefusesAsNotCanonical`: insert a space / add a key ->
   `FormatException` with `plan-not-canonical`.
3. `Parse_GivenMissingDomain_RefusesAsShapeInvalid`.
4. `Parse_GivenStoredSeedMismatch_RefusesAsSeedMismatch`: alter one number in a domain seed array.
5. `Parse_GivenSemanticallyChangedMargin_HasDifferentPlanHash`: change the margin consistently and
   re-serialize canonically (rebuild via Create); parse succeeds and the hash differs from the
   original, proving the content address (not the parser) is the integrity mechanism.

## Forbidden

Do not change `EvaluationPlanV2`, the evaluator, the gate, the CLI, or any existing test. No clock,
culture, GUID or ambient randomness. No permissive parsing.
