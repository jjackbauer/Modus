# Round 6 — Deterministic skills (R-09) and skill-aware refinement (R-10)

## Outcome

- **R-09 — deterministic skill decomposition.** `StrategyGenome` carries an optional
  `StrategySkillSet? Skills = null` as its last positional parameter
  (`WIP/Wip.Tholdem.Agents/Learning/StrategyGenome.cs:17`). The four skills are bounded data-only
  records (`Learning/Skills/StrategySkillSet.cs:9`, `:34`, `:59`, `:79`). `Clamp()` clamps them when
  present (`StrategyGenome.cs:35`), `BehaviorEquals` compares the effective skill values in addition
  to the four numeric fields (`:49`), and a prompt-only change stays inert. `StrategyAgent` keeps the
  exact legacy decision body when `Skills` is null (`StrategyAgent.cs:34-37`); with skills present it
  runs the fixed preflop/postflop/sizing/bluff pipeline (`:95`) and validates every returned action
  (`:165`). The artifact writer appends the `skills` object only when non-null
  (`StrategyArtifact.cs:99-103`), and `TrainingLoop.ReadArtifact` accepts an absent or present object
  (`TrainingLoop.cs:496-499`).
- **R-10 — opt-in skill refinement.** `SignatureDrivenRefinementPolicy` takes
  `bool enableSkillEdits = false` (`.../Refinement/SignatureDrivenRefinementPolicy.cs:32`) and, only
  for the strongest `aggression-by-street` signal, edits the bluff frequency of a skill set derived
  from the incumbent (`:48-57`, `:81`). `RefinementGuard` refuses out-of-domain, multi-parameter and
  behaviourally inert skill edits with stable codes (`.../Refinement/RefinementGuard.cs:16-22`,
  `:37-77`). `TrainingOptions` gains `bool SkillRefinement = false` as its last optional parameter
  (`TrainingOptions.cs:18`), and the loop installs the guard and the policy flag only in the opt-in
  refinement path (`TrainingLoop.cs:126-130`, `:195-198`, `:210-211`). With `SkillRefinement` false,
  every measured-arm byte and challenger id is unchanged.
- **Status — 15 new tests pass**, including all 11 required scenarios. The unfiltered assembly
  passes **369/369** (354 pre-existing + 15 new, 0 failed, 0 skipped). The build has 0 errors; every
  warning in the rebuild is in a pre-existing file outside this round's edits (`Wip.Runtime`,
  `Wip.Tholdem.Host/Commands/TrainCommand.cs`, and existing test files `TrainingLoopTests.cs`,
  `LearningWebIntegrationTests.cs`, `ThreatControlTests.cs`). No new file has a warning.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/Skills/StrategySkillSet.cs` | new: four bounded skills, `Clamp`, `IsWithinDomain`, `Default`, `FromGenome`, `BehaviorEquals`, `BehavioralChangeCount` |
| `WIP/Wip.Tholdem.Agents/Learning/Skills/StrategySkillSetJson.cs` | new: canonical `skills` writer (`:25`) and strict reader with the stable `strategy-skills-invalid` refusal (`:15`, `:44`) |
| `WIP/Wip.Tholdem.Agents/Learning/StrategyGenome.cs` | last positional `Skills` (`:17`), default constants (`:22-30`), skill-aware `Clamp` (`:35`) and `BehaviorEquals` (`:49`) |
| `WIP/Wip.Tholdem.Agents/Learning/StrategyArtifact.cs` | conditional `skills` object in the canonical payload (`:99-103`) |
| `WIP/Wip.Tholdem.Agents/Learning/StrategyAgent.cs` | opt-in skill pipeline (`:95`), legality fallback (`:165`), skill sizing target (`:198`) |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/SignatureDrivenRefinementPolicy.cs` | opt-in constructor flag (`:32`) and bluff-skill edit (`:48-57`, `:81`) |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/RefinementGuard.cs` | new: stable-code skill guard with a fixed probe session |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingOptions.cs` | last optional `SkillRefinement` (`:18`) |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs` | conditional guard installation (`:126-130`), policy flag (`:197`), effective guard use (`:210-211`), skills parse (`:496-499`) |
| `WIP/Wip.Tholdem.Tests/Learning/HarnessSkillsTests.cs` | new, 15 tests |
| `docs/goal-tholdem-harness/round-6/implementation.md` | this document |

No engine, history, simulator, evaluator, gate, verifier, frozen-manifest, solution, project, Host,
Web or runtime file was modified. `EvaluationCriteria.cs`, `PromotionPolicy.cs`, `PromotionVerifier.cs`,
`MatchEvaluator.cs`, `SeedDerivation.cs`, `FrozenComponents.cs`, `MetaLoop.cs`, `HarnessMemoryStore.cs`,
`StrategyLineageReader.cs`, `HarnessBootstrap*.cs`, `FailureSignatureExtractor.cs` and
`TrainingTrajectoryCapture.cs` are untouched, and no existing test was edited, deleted, skipped or
weakened.

## Test-first evidence

### 1. Red with the API surface present and the new behaviour stubbed

The test file was written first. The production surface (skill records, `Skills` parameter, options
flag, policy constructor flag, guard type, agent validator signature) was then added with the skill
pipeline, artifact emission, guard and policy edit stubbed, so the red run is a **runtime** failure,
not a compile failure. The build was clean and the focused run produced:

```text
FAIL HarnessSkillsTests.Agent_GivenDefaultSkills_ExpectedByteIdenticalDecisionsAcrossSeededCorpus: IllegalAgentActionException: ... red: the skill pipeline is not implemented yet.
FAIL HarnessSkillsTests.Agent_GivenIllegalSkillAction_ExpectedLegalFallback: NotImplementedException: red: the skill action validator is not implemented yet.
FAIL HarnessSkillsTests.Agent_GivenPreflopSkillParameter_ExpectedOnlyPreflopDecisionsChange: NotImplementedException: red: the skill pipeline is not implemented yet.
FAIL HarnessSkillsTests.Artifact_GivenMalformedSkills_ExpectedStableRefusal: NotEqualException: Assert.NotEqual() Failure: Strings are equal
FAIL HarnessSkillsTests.Artifact_GivenSkillsPresent_ExpectedCanonicalRoundTrip: NotEqualException: Assert.NotEqual() Failure: Strings are equal
FAIL HarnessSkillsTests.Guard_GivenInertSkillEdit_ExpectedRefusalBeforeEvaluation: NotImplementedException: red: the skill refinement guard is not implemented yet.
FAIL HarnessSkillsTests.Guard_GivenMultipleSkillEdits_ExpectedRefusal: NotImplementedException: red: the skill refinement guard is not implemented yet.
FAIL HarnessSkillsTests.Guard_GivenNonSkillProposal_ExpectedAcceptedUnchanged: NotImplementedException: red: the skill refinement guard is not implemented yet.
FAIL HarnessSkillsTests.Guard_GivenUnclampedSkillEdit_ExpectedRefusal: NotImplementedException: red: the skill refinement guard is not implemented yet.
FAIL HarnessSkillsTests.Policy_GivenSkillEditsEnabled_ExpectedSkillParameterEdit: NotNullException: Assert.NotNull() Failure: Value is null
FAIL HarnessSkillsTests.Run_GivenSkillRefinementOn_ExpectedGateAndVerifierUnchanged: NotNullException: Assert.NotNull() Failure: Value is null
TOTAL 15 PASSED 4 FAILED 11 SKIPPED 0
```

The four tests already green at that stage are the legacy pins that must not change: absent-skills
decisions, legacy artifact bytes, the skill-refinement-off challengers, and the
skill-edits-disabled numeric proposal.

### 2. Pre-change baseline

Before any edit, the supplementary runner reported:

```text
TOTAL 354 PASSED 354 FAILED 0 SKIPPED 0
```

### 3. Final green

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessSkills
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed:

```text
BUILD_EXIT=0 (0 errors; forced rebuild reports 16 warnings, all pre-existing and outside the new files)
TOTAL 15 PASSED 15 FAILED 0 SKIPPED 0
TOTAL 369 PASSED 369 FAILED 0 SKIPPED 0
```

`dotnet test` remains unusable in this session (Win32Exception 5 on the VSTest testhost handle), so
the supplied supplementary runner executes the same compiled assembly.

## Skill schema and domains

| Skill | Field | Domain | Default (`StrategySkillSet.Default`) |
|---|---|---|---|
| `PreflopSkillParameters` | `RaiseThreshold` | `[0, 1]` | `0.72` |
| `PreflopSkillParameters` | `CallThreshold` | `[0, 1]` | `0.45` |
| `PostflopSkillParameters` | `RaiseThreshold` | `[0, 1]` | `0.72` |
| `PostflopSkillParameters` | `CallThreshold` | `[0, 1]` | `0.45` |
| `SizingSkillParameters` | `Multiplier` | `[0.25, 3.0]` | `1.0` |
| `BluffSkillParameters` | `Frequency` | `[0, 1]` | `0.12` |

Every numeric field has its own `Clamp()` and `IsWithinDomain()`; `StrategySkillSet.Clamp()` clamps
all four, and `StrategySkillSet.FromGenome` derives the behaviourally equivalent set from a genome's
four numeric fields (`StrategySkillSet.cs:9-192`). `StrategySkillSet.Default` equals
`FromGenome(new StrategyGenome())`, asserted by the equivalence test.

Canonical artifact member (emitted after `promptText`, only when `Skills` is non-null):

```json
"skills":{"preflop":{"raiseThreshold":0.72,"callThreshold":0.45},"postflop":{"raiseThreshold":0.72,"callThreshold":0.45},"sizing":{"multiplier":1},"bluff":{"frequency":0.12}}
```

The key order is fixed by `StrategySkillSetJson.Write` (`StrategySkillSetJson.cs:25-42`) and every
number goes through `CanonicalJsonWriter.Number`. The reader requires exactly the four named skill
objects and their exact members, and any other shape throws `FormatException` with the stable prefix
`strategy-skills-invalid` (`StrategySkillSetJson.cs:15`, `:44-137`); `TrainingLoop.ReadArtifact`
leaves an absent member as `null` (`TrainingLoop.cs:496-499`).

## R-09 equivalence evidence

The pinned corpus is a real 240-hand seeded session (seed `20261112`, default and custom genomes).
`HarnessSkillsTests.Agent_GivenSkillsAbsent_...` (`HarnessSkillsTests.cs:44`) asserts the recorded
pre-change manifest hashes and decision digests:

| Genome | Manifest hash | Decision digest |
|---|---|---|
| default `StrategyGenome()` | `689e0953320890e0ba0c20af0d69ee07515f8f127552995471917086b807f6bd` | `54da607ff6055e9347b66e434d1991160b3961cb9a50d79faf447f942189d5c1` |
| custom `(0.61, 0.33, 0.27, 1.65)` | `f566937c2ecb2b98d8dfdac59cca1b11b533d6f0a620a53e86e12b49d2a9bf49` | `f7bd208e615dfc2f9767cbd2fda5fb03950ea4b06b497a6e82988b39a0ec7b32` |

`Agent_GivenDefaultSkills_...` (`:65`) runs the same corpus with `Skills = FromGenome(genome)` and
asserts the identical session id, identical manifest hash (the manifest covers every per-hand hash),
identical decision digest, identical action sequence and identical observation bytes. The preflop
test (`:89`) replays all 627 recorded custom-genome decisions: lowering only
`PreflopSkillParameters` changes at least one preflop decision and leaves every postflop decision
byte-identical. The legality test (`:130`) injects a bet outside `LegalActionKinds` and an
out-of-range raise into `EnsureLegalSkillAction`; both fall back to `LegalActionSelection.FirstLegal`,
and a real 40-hand default-skills session is replayed with every action legal.

## Pinned-challenger evidence (R-10 off)

`Run_GivenSkillRefinementOff_ExpectedPinnedChallengersUnchanged` (`HarnessSkillsTests.cs:277`) runs
the measured arm (`Seed = 20261111`, 3 iterations, 24 training hands, 2 x 2 evaluation) with
`HarnessRefinement = true` and the default `SkillRefinement = false`. It observes exactly the
pre-change ids:

```text
champions  : tholdem-strategy-v1-f100e793 (all three iterations)
challengers: tholdem-strategy-v2-d24dcf6d, tholdem-strategy-v2-c34333fe, tholdem-strategy-v2-86f42856
outcomes   : rejected for all three
final      : tholdem-strategy-v1-f100e793
```

The artifact test (`:188`) independently pins the default v1 artifact: id
`tholdem-strategy-v1-f100e793`, content hash
`f100e793f3023b324cbc91514e31294d39043e6cbb6d55b37c339bfbbda202c5`, canonical-file SHA-256
`6675d7975b69eafd7cd64f334e70ad5adbfa09fbcc32176a6098aa9649124c14`, and the exact legacy bytes.
The canonical bytes contain no `skills` member.

## Guard behaviour

`RefinementGuard` is installed by `TrainingLoop` only when both `HarnessRefinement` and
`SkillRefinement` are true (`TrainingLoop.cs:126-130`), in front of any caller-supplied guard
(`:210-211`). A non-skill proposal delegates unchanged to that guard (`RefinementGuard.cs:46-56`,
`:107-115`). A skill edit is processed in this order:

| Check | Stable code | Evidence |
|---|---|---|
| genome/skills finite and inside every documented domain | `skill-out-of-domain` (`:16`) | `Guard_GivenUnclampedSkillEdit_...` (`:387`) uses a preflop threshold `1.5`; refused, and `candidate != candidate.Clamp()` |
| at most one skill field changed | `skill-multiple-edits` (`:19`) | `Guard_GivenMultipleSkillEdits_...` (`:408`) changes preflop raise and bluff; refused |
| the edit changes at least one decision on the fixed probe session | `skill-inert` (`:22`) | `Guard_GivenInertSkillEdit_...` (`:446`): a default-skill enable is refused before evaluation; the loop writes the challenger artifact and an inert ledger row with reason `skill-inert`, `Report == null`, and no evaluation file |
| otherwise | passed through | `Run_GivenSkillRefinementOn_...` reaches evaluation |

The probe is fixed at seed `20_261_114` and 400 hands (`RefinementGuard.cs:25-28`); it runs the
incumbent and the candidate through `MatchSimulator` and compares the manifest hash, which covers
every hand hash (`:95-105`). A bluff edit `0.12 -> 0.20` is accepted; a default-skill enable is
refused. These are real simulations, not a metadata check.

## R-10 gate and verifier evidence

`Run_GivenSkillRefinementOn_ExpectedGateAndVerifierUnchanged` (`HarnessSkillsTests.cs:488`) performs a
real one-iteration run with both flags. The strongest signal is `aggression-by-street-low` (the same seed and shape record that rationale in `WIP/Wip.Tholdem.Tests/Learning/HarnessMemoryTests.cs:343`), so the
challenger has `Skills` with exactly one changed bluff field (`0.12 + 0.08`), the same numeric fields
and the same prompt as the champion. The test then:

1. re-runs `MatchEvaluator.Evaluate` with the recorded ids, genomes, derived base seed and options and
   asserts the full canonical report and `ReportHash` are identical, including the persisted
   `evaluations/<challenger>-vs-<champion>.json` bytes;
2. replays `StatisticalPromotionPolicy.Decide` and `PromotionVerifier.Verify` on that report and
   asserts `Verified`, the exact decision record, and the promoted/rejected/verification-failed
   outcome formula;
3. compares `FrozenComponents.Capture()` before and after the run (equal `ManifestHash`, pinned
   manifest matches, 5 components) and verifies the five frozen source hashes equal the round-1
   baseline file `docs/goal-tholdem-harness/round-1/frozen-baseline-hashes.txt`;
4. asserts the pointer rules: on a verified promotion the pointer names the challenger at iteration 1
   with the report hash; otherwise it still names the initial champion at iteration 0 with no report
   hash.

Observed frozen component source hashes after the change (all equal to round 1):

```text
EvaluationCriteria.cs 9ff873f0c7727d79957815b29556c7028b37c11e980b65bed8689ef4ee7f71ee
PromotionPolicy.cs    2bb7a80083500b34b43280a5adc67342f3b57c201c75b909a6dfa0a8cf788cbe
PromotionVerifier.cs  72252cdd9bdbf838a3ddc0a5e5abb60298ca3d356ee4bb455fd521802e0029f0
MatchEvaluator.cs     56c85e2de11fc5f4b30acd3423c65b6ffcea0cbaa2e7fc9a2cbe87ec8b8c251d
SeedDerivation.cs     ec4e7845746a930bc115387e25176ad2bf6a417261550939571630a1a944b123
```

## Deviations (all deliberate, none weakening an invariant)

1. **A public validator seam is used for the illegal-action test.** The data-only pipeline cannot
   naturally construct an illegal action (thresholds, legal-kind checks and the sizing clamp keep it
   legal), so the pipeline's own validator is public as
   `StrategyAgent.EnsureLegalSkillAction` (`StrategyAgent.cs:165`) and the test injects an illegal
   candidate into that exact method. The validator is what the pipeline calls on every returned
   action; the seeded default-skills session also proves the pipeline's real output is legal.
2. **The inert check uses a fixed 400-hand probe session, not a decision table.** This is the
   documented R-07 "probe corpus" interpretation; it is deterministic, bounded and independent of
   training data. Seed `20261114` was chosen because a `0.12 -> 0.20` bluff edit changes a hand hash
   there, so the guard demonstrably accepts a real edit and refuses a behaviourally inert one.
3. **`BehaviorEquals` treats absent skills as equal only to absent skills.** The brief specifies
   `null == null`; therefore a default-skill enable is *not* considered equal by `BehaviorEquals`
   even though the agent decisions are identical at defaults. The guard's probe supplies the stronger
   behavioural test and refuses the shape-only enable as inert.
4. **`SkillRefinement` is ignored unless `HarnessRefinement` is true.** The policy is constructed only
   inside the refinement branch (`TrainingLoop.cs:192-201`), so the legacy path cannot see the new
   flag. The guard is likewise installed only when both flags are true.
5. **The guard wraps the caller-supplied proposal guard instead of replacing it.** It runs first and
   delegates every accepted non-skill result, so existing callers that pass
   `ProposalGuards.RejectBehaviorallyInert` keep their exact semantics. No caller passes a guard in
   the measured arm, and `SkillRefinement=false` installs no guard at all.
6. **Skill values are absolute, not deltas.** `FromGenome` derives the default set from the genome's
   four fields, which makes the default skill set behaviourally inert for any genome (not only the
   default genome). A skill edit that changes the bluff skill therefore leaves the numeric fields
   untouched, as the R-10 policy intends.

## Anything not passing

Nothing. All 15 new tests and all 354 pre-existing tests pass (369/369), with zero skips and zero
failures, and the build has zero errors. The only warnings are in pre-existing files outside this
round's changes; no new source file emits a warning. The only environment limitation remains the
known `dotnet test` Win32Exception (5), which is why the supplied supplementary runner is cited.