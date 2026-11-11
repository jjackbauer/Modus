# Round 13b design — additive evaluate4 CLI command (D1)

**Objective:** expose the v2 four-seat evaluator/gate through a new CLI command that reads a persisted
pre-registered plan, writes the canonical paired report, and optionally runs the anchor + gate +
independent verifier. The v1 commands stay unchanged and no champion pointer is written.

## Command

~~~
tholdem evaluate4 --plan <path> --challenger <id> --incumbent <id>
                 [--anchor-plan <path>] [--out <dir>] [--json] [--data-dir <path>]
~~~

- --plan is parsed with EvaluationPlanV2Json.Parse; malformed input is a CliArtifactException (exit 4).
- The pool is resolved from plan.OpponentPoolId: tholdem.opponents.v1 -> SelectionV1,
  tholdem.opponents.holdout.v1 -> HoldoutV1; any other id is a CliArtifactException.
- Both strategies are loaded with StrategyArtifactStore.Load; policy hashes are the artifact content
  hashes; agents are fresh StrategyAgent(genome, id) instances.
- The report is written as report-<hash8>.json in --out (default <data>/evaluations4).
- With --anchor-plan, the anchor report is computed with the same pool rules;
  PopulationPromotionGate.Decide produces the decision and PopulationPromotionVerifier.Verify
  recomputes both reports with fresh agents. decision-<hash8>.json and verification-<hash8>.json are
  written. A refused verification throws CliInvariantException (exit 5).
- --json prints the canonical report; otherwise print plan hash, mean, lower bound, units and paths.
- No pointer write, no proposal, no LLM.

## Wiring

TholdemCli: add Evaluate4Flags = { "json" }, Evaluate4Valued = { "plan", "challenger", "incumbent",
"anchor-plan", "out", "data-dir" }, the dispatch entry, and usage lines. Do not change the existing
commands, flags or exit codes.

## Tests — WIP/Wip.Tholdem.Tests/Host/Evaluate4CommandTests.cs (four)

Use HostCliTestSupport and a tiny plan (2/2/4/2 domains, 2 hands, one composition, margin 2).
1. Run_GivenPlanAndArtifacts_WritesCanonicalReport: exit 0; exactly one report-*.json; JSON planHash
   equals the plan hash; units count is 4; no champion pointer is written.
2. Run_GivenAnchorPlan_WritesDecisionAndVerifiedVerification: exit 0; decision-*.json and
   verification-*.json exist; verification verified is true and names the gate verdict/reason.
3. Run_GivenUnknownPool_ExitArtifact: a plan whose pool id is unknown -> exit 4, error mentions the pool.
4. Run_GivenMalformedPlan_ExitArtifact: a file containing a single open brace -> exit 4.

## Forbidden

Do not change the evaluator, gate, verifier, plan parser, v1 commands, strategy store or any existing
test. No clock/culture/GUID/ambient randomness. No pointer promotion.
