# Round 14 design — data-only equity-policy artifact and CLI kinds

**Objective:** make the equity-aware policy a persisted, content-addressed policy family the CLI can
evaluate, so the next round can run a pre-registered promotion experiment without a bespoke harness.

## 1. Learning/EquityPolicyJson.cs (new)

~~~csharp
public static class EquityPolicyJson
{
    public const string Schema = "tholdem.equity-policy.v1";

    public static string Write(EquityPolicy policy);
    public static EquityPolicy Parse(string json);
    public static string Hash(EquityPolicy policy);
}
~~~

Canonical JSON, fixed key order:
{"schema":"tholdem.equity-policy.v1","equitySamples":400,"valueEdge":0.1,"raiseEdge":0.18,
 "bluffFrequency":0.08,"valueBetPotFraction":0.65}

Validation: samples in [50, 5000], valueEdge/raiseEdge/bluffFrequency in [0, 1],
valueBetPotFraction in (0.01, 2.0]; non-finite values refused. Parse is strict: reconstruct, then
require the canonical re-serialization to equal the input exactly; refusals throw FormatException
with codes equity-policy-not-json, equity-policy-shape-invalid, equity-policy-not-canonical.
Hash = StrategyArtifact.ComputeHash(Write(policy)).

## 2. Host/Storage/EquityPolicyStore.cs (new, internal)

~~~csharp
public static EquityPolicy Load(string dataDirectory, string policyId);
public static string Write(string dataDirectory, EquityPolicy policy); // returns path
~~~

Files live under <data>/equity-policies/<safeId>.json; ids use CliPaths.SafeStrategyId; malformed or
missing files throw CliArtifactException.

## 3. TholdemCli / Evaluate4Command (edit, additive)

- New valued options: --challenger-kind and --incumbent-kind, each "strategy" (default) or
  "equity-policy".
- Evaluate4Command resolves each side through one factory:
  - strategy -> StrategyArtifactStore.Load + new StrategyAgent(genome, id); hash = artifact.Hash;
  - equity-policy -> EquityPolicyStore.Load + new EquityPolicyAgent(policy, id); hash =
    EquityPolicyJson.Hash(policy);
  - any other kind -> CliUsageException.
- The verifier recomputation delegates must build fresh agents through the same factory; the report's
  challenger/incumbent policy hashes are those content hashes.
- Usage lines mention the kinds. No v1 command changes.

## 4. Tests

WIP/Wip.Tholdem.Tests/Learning/EquityPolicyJsonTests.cs (three):
1. WriteThenParse_GivenCanonicalPolicy_RoundTripsAndHashesStably.
2. Parse_GivenOutOfRangeOrNonFiniteValue_Refuses.
3. Parse_GivenWhitespaceOrExtraKey_RefusesAsNotCanonical.

WIP/Wip.Tholdem.Tests/Host/Evaluate4CommandTests.cs (add one):
4. Run_GivenEquityPolicyChallenger_WritesReportWithPolicyHash: write a policy artifact, run
   evaluate4 with --challenger-kind equity-policy against a strategy incumbent on the tiny plan;
   exit 0; report challengerPolicyHash equals EquityPolicyJson.Hash(policy); no pointer written.

## 5. Forbidden

Do not change StrategyAgent, EquityPolicyAgent behavior, the evaluator/gate/verifier, plan JSON, v1
commands or existing tests. No clock/culture/GUID/ambient randomness. No strength claim.
