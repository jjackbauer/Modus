# Round 13 — strict persisted plan JSON (implementer)

Scope: strict persistence for `EvaluationPlanV2` exactly as specified in
`docs/goal-tholdem-enhancement/round-13/design.md`. Only the three files listed at the end were
created; no existing production file, test, evaluator, gate or CLI was edited.

## Files

- `WIP/Wip.Tholdem.Agents/Evaluation/EvaluationPlanV2Json.cs` (new)
- `WIP/Wip.Tholdem.Tests/Evaluation/EvaluationPlanV2JsonTests.cs` (new)
- `docs/goal-tholdem-enhancement/round-13/implementation-plan-json.md` (new, this file)

## Red (observed before implementation)

The five tests were written first against a compiling stub whose `Write` and `Parse` threw
`NotImplementedException("stub: ...")`.

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed: `Build succeeded.` with `0 Error(s)` (14 pre-existing warnings in untouched test files).

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.EvaluationPlanV2JsonTests
```

Observed (exit code 1):

```text
FAIL EvaluationPlanV2JsonTests.Parse_GivenCanonicalPlan_RoundTrips: NotImplementedException: stub: EvaluationPlanV2Json.Write
FAIL EvaluationPlanV2JsonTests.Parse_GivenMissingDomain_RefusesAsShapeInvalid: NotImplementedException: stub: EvaluationPlanV2Json.Write
FAIL EvaluationPlanV2JsonTests.Parse_GivenSemanticallyChangedMargin_HasDifferentPlanHash: NotImplementedException: stub: EvaluationPlanV2Json.Write
FAIL EvaluationPlanV2JsonTests.Parse_GivenStoredSeedMismatch_RefusesAsSeedMismatch: NotImplementedException: stub: EvaluationPlanV2Json.Write
FAIL EvaluationPlanV2JsonTests.Parse_GivenWhitespaceOrExtraKey_RefusesAsNotCanonical: NotImplementedException: stub: EvaluationPlanV2Json.Write
TOTAL 5 PASSED 0 FAILED 5 SKIPPED 0
```

## Green (observed after implementation)

Build (same command as the red build):

```text
Build succeeded.
    0 Error(s)
```

The warning count varies between clean and incremental builds (14 vs 12) and consists only of the
pre-existing nullable and xUnit-analyzer warnings in untouched test files.

Filtered tests (same command as the red run), observed exit code 0:

```text
TOTAL 5 PASSED 5 FAILED 0 SKIPPED 0
```

Full suite, observed exit code 0:

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
TOTAL 561 PASSED 561 FAILED 0 SKIPPED 0
```

The baseline before this change was observed at `TOTAL 556 PASSED 556 FAILED 0 SKIPPED 0`;
561 = 556 pre-existing tests + the five new facts. No pre-existing test was changed, skipped or
weakened.

## Round-trip plan

Test 1 creates the four-domain plan used by the existing `EvaluationPlanV2Tests` fixture:

- `GameConfiguration.FourSeatCash()` (4 seats, 5/10 blinds, 1000 chips, cash, 1000 action cap)
- pool `population-a`, compositions `["baseline", "candidate"]`
- domains: Training base 100 x 4, Selection base 200 x 4, Holdout base 300 x 12, Audit base 400 x 4
- hands per seat match 20, margin 2.0, confidence 0.95, planned candidates 5, anchor tolerance 2.0

`EvaluationPlanV2Json.Write(plan)` produces 1573 bytes of canonical JSON. The observed content
address of that plan, recomputed independently from the same canonical bytes and matched by the
round-trip assertions, is:

```text
baf6dddd6ad508775483a71c65d1b2f997a424d587de602598bbe93b7e3a8973
```

Test 5 rebuilds the same plan with margin 5.0 and parses its canonical JSON. Parsing succeeds and
the test observes that the hash differs from `baf6...`. The parser therefore does not decide whether
a semantic change is the pre-registered plan; the verifier compares `PlanHash`, which is the
integrity mechanism.

## Exact refusal messages (observed by passing tests)

Whitespace inserted after every `"schema":` (valid JSON, no longer canonical) and an extra
`"unexpected":true` member both produce:

```text
plan-not-canonical: the plan is not in its canonical form.
```

Removing the Audit domain object from the `domains` array produces:

```text
plan-shape-invalid: the plan is missing the required 'Audit' seed domain.
```

Replacing the first stored Holdout seed (`1800105171705767147`) with `1800105171705767146`
produces:

```text
plan-seed-mismatch: domain 'holdout' stored seed 0 is 1800105171705767146, but the derivation yields 1800105171705767147.
```

The parser's other routes, by construction rather than by these five tests, are: malformed JSON to
`plan-not-json` (with the `JsonException` text appended); missing or mistyped members, unknown
schema/purpose/mode/metric values, duplicate purposes and an invalid rebuilt plan to
`plan-shape-invalid`. Every public refusal is a `FormatException` whose message starts with one of
the four stable codes.

## Implementation notes

- `Write` null-checks the plan and returns `plan.ToCanonicalJson()`; it adds no formatting.
- `Parse` reads with `System.Text.Json` (`JsonDocument`/`JsonElement`). It requires the exact
  plan schema and evaluator version, then reads the embedded `game` object (schema, seats, blinds,
  stack, per-seat stacks as null or an integer array, ante, button rule, action cap, side-pot rule,
  mode, metric id), the pool id, compositions, the four domain objects (schema, id, purpose, base
  seed, count, stored seeds) and the five inferential fields. Missing or wrongly typed members are
  `plan-shape-invalid`.
- Each embedded domain is reconstructed with `new SeedDomain(domainId, purpose, baseSeed, count)`,
  which re-derives every seed from the purpose salt and one-based block index. The stored array is
  compared index by index; a length or value difference is `plan-seed-mismatch`, so a forged list
  never reaches `Create`.
- `EvaluationPlanV2.Create` is called with the reconstructed game and domains, then every
  inferential field; its argument/game/overlap refusals are mapped to `plan-shape-invalid`. Finally
  `created.ToCanonicalJson()` must equal the input string ordinal-exactly, otherwise
  `plan-not-canonical`. That final comparison is what refuses whitespace, key reordering, extra
  members, duplicate keys, alternative number spellings and alternative escapes.
- No clock, GUID, culture, ambient randomness or mutable global state participates: the hash is
  `StrategyArtifact.ComputeHash` (SHA-256, lower-case hex) over the canonical UTF-8 bytes.
- `SeedDomain`, `GameConfiguration`, `EvaluationPlanV2`, the evaluator, the gate and the CLI were
  not modified.

## Tests added

`WIP/Wip.Tholdem.Tests/Evaluation/EvaluationPlanV2JsonTests.cs`:

1. `Parse_GivenCanonicalPlan_RoundTrips` — Write then Parse; the reparsed JSON equals the input
   byte-for-byte and the `PlanHash` equals both the original plan's hash and
   `StrategyArtifact.ComputeHash(json)`.
2. `Parse_GivenWhitespaceOrExtraKey_RefusesAsNotCanonical` — a single space after a member colon
   and an appended extra key are each refused with the exact `plan-not-canonical` message.
3. `Parse_GivenMissingDomain_RefusesAsShapeInvalid` — the Audit domain object is removed from the
   canonical `domains` array; the exact `plan-shape-invalid` missing-Audit message is required.
4. `Parse_GivenStoredSeedMismatch_RefusesAsSeedMismatch` — the first Holdout seed is XORed by one;
   the exact `plan-seed-mismatch` message names the domain, index, stored value and derived value.
5. `Parse_GivenSemanticallyChangedMargin_HasDifferentPlanHash` — margin 5.0 is rebuilt through
   `Create`, written canonically and parsed successfully; its hash differs from the margin-2.0
   plan's hash, proving content addressing, not parsing, distinguishes the pre-registered plan.

## No stub remains

Observed content search after implementation:

```powershell
Select-String -Path WIP/Wip.Tholdem.Agents/Evaluation/EvaluationPlanV2Json.cs -Pattern 'NotImplementedException|stub:|TODO|FIXME'
```

No matches. Every refusal path throws a `FormatException` with one of the four codes; there is no
TODO/FIXME or throw-only member.

`dotnet test` was not run. The supplementary `.tmp/tholdem-verify/RunXunit` runner executed the
same compiled xUnit assembly; CI remains the authoritative gate.

## Files changed

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Evaluation/EvaluationPlanV2Json.cs` | new strict `Write`/`Parse` boundary |
| `WIP/Wip.Tholdem.Tests/Evaluation/EvaluationPlanV2JsonTests.cs` | five new facts |
| `docs/goal-tholdem-enhancement/round-13/implementation-plan-json.md` | this record |

No other file was edited.
