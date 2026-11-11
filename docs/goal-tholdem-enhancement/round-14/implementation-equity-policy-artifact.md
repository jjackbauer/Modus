# Round 14 — data-only equity-policy artifact and CLI agent kinds (implementer)

Scope: the canonical equity-policy artifact and the additive `evaluate4` agent-kind surface exactly
as specified in `docs/goal-tholdem-enhancement/round-14/design.md`. Only the files listed at the end
were created or edited. The evaluator, gate, verifier, plan JSON, `StrategyAgent`,
`EquityPolicyAgent`, v1 commands, strategy store and every pre-existing test are untouched.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/EquityPolicyJson.cs` | New: canonical write/parse/hash for `EquityPolicy`. |
| `WIP/Wip.Tholdem.Host/Storage/EquityPolicyStore.cs` | New internal store under `<data>/equity-policies`. |
| `WIP/Wip.Tholdem.Host/Commands/Evaluate4Command.cs` | Edit: one agent-kind factory; all evaluation and verifier-recomputation passes build fresh agents through it. |
| `WIP/Wip.Tholdem.Host/TholdemCli.cs` | Edit: two valued `evaluate4` options and usage lines only. |
| `WIP/Wip.Tholdem.Tests/Learning/EquityPolicyJsonTests.cs` | New: three facts. |
| `WIP/Wip.Tholdem.Tests/Host/Evaluate4CommandTests.cs` | Edit: one added fact, no existing test changed. |
| `docs/goal-tholdem-enhancement/round-14/implementation-equity-policy-artifact.md` | New: this record. |

## Artifact

```text
{"schema":"tholdem.equity-policy.v1","equitySamples":400,"valueEdge":0.1,"raiseEdge":0.18,"bluffFrequency":0.08,"valueBetPotFraction":0.65}
```

- Fixed key order, whitespace-free, invariant numbers through `CanonicalJsonWriter.Number`; UTF-8
  without BOM is the store's concern (`CliJson.Utf8`). No clock, culture, GUID or ambient randomness.
- Validation: `equitySamples` in [50, 5000] (must be an integer), `valueEdge`/`raiseEdge`/
  `bluffFrequency` finite and in [0, 1], `valueBetPotFraction` finite and in (0.01, 2.0].
- Refusal codes: `equity-policy-not-json` (the text is not JSON), `equity-policy-shape-invalid`
  (wrong shape, missing member, non-finite or out-of-domain value) and `equity-policy-not-canonical`
  (all members valid, but the canonical re-serialization differs byte-for-byte — whitespace, key
  order, extra key or a non-canonical number spelling).
- `Hash = StrategyArtifact.ComputeHash(Write(policy))`, so the address covers exactly the persisted
  bytes. Default policy hash observed: `760ac54c77d737a71a4f488e1752574fb6dde1f4367fa6372959506e180091ac`.

**Scope note (schema v1 has five members).** The design lists five members; the record's sixth
member, `PreflopRaiseMargin`, is a frozen component and is deliberately not part of
`tholdem.equity-policy.v1`. `Parse` reconstructs it with the record default (0.03); `Write` refuses a
non-default margin with `equity-policy-shape-invalid` instead of persisting bytes that would evaluate
differently from the supplied record. Widening the artifact requires a new schema version. No
supplied test exercises a non-default margin.

## Store

- Files: `<data>/equity-policies/<safeId>.json`; ids pass through `CliPaths.SafeStrategyId`.
- `Write(dataDirectory, policy)` derives the content-addressed id
  `tholdem-equity-policy-v1-<hash8>`, writes the canonical bytes with `CliJson.Utf8` and returns the
  path. It never writes a champion pointer.
- `Load(dataDirectory, policyId)` reads and parses the artifact; a missing file, I/O failure or a
  parse refusal is wrapped in `CliArtifactException` (CLI exit 4).

## CLI and factory

New valued options on the existing command only:

```text
tholdem evaluate4 --plan <path> --challenger <id> --incumbent <id>
                 [--challenger-kind <strategy|equity-policy>]
                 [--incumbent-kind <strategy|equity-policy>]
                 [--anchor-plan <path>] [--out <dir>] [--json] [--data-dir <path>]
```

Both kinds default to `strategy`. Each side is resolved by one factory:

- `strategy` -> `StrategyArtifactStore.Load` + `new StrategyAgent(genome, id)`; hash =
  `artifact.Hash` (unchanged);
- `equity-policy` -> `EquityPolicyStore.Load` + `new EquityPolicyAgent(policy, name: id)`; hash =
  `EquityPolicyJson.Hash(policy)` (the constructor's name parameter is set by name because the
  second positional parameter is the sample override);
- anything else -> `CliUsageException` (exit 2).

`Evaluate` calls the factory for every pass, so the primary report, anchor report and both verifier
recomputation delegates each get freshly loaded data and fresh agents. The report's
`challengerPolicyHash`/`incumbentPolicyHash` are those content hashes. No v1 command, flag, exit
code or behavior changed.

## Red (observed before implementation)

Stubs (`NotImplementedException`) for `EquityPolicyJson`, `EquityPolicyStore` and the factory's
equity-policy branch were in place; the options, dispatch and tests were already wired.

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed: `Build succeeded.`, `14 Warning(s)`, `0 Error(s)` (all warnings in pre-existing files).

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.EquityPolicyJsonTests
```

Observed (exit code 1):

```text
FAIL EquityPolicyJsonTests.Parse_GivenOutOfRangeOrNonFiniteValue_Refuses: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.FormatException)
Actual:   typeof(System.NotImplementedException)
FAIL EquityPolicyJsonTests.Parse_GivenWhitespaceOrExtraKey_RefusesAsNotCanonical: NotImplementedException: stub: EquityPolicyJson.Write
FAIL EquityPolicyJsonTests.WriteThenParse_GivenCanonicalPolicy_RoundTripsAndHashesStably: NotImplementedException: stub: EquityPolicyJson.Write
TOTAL 3 PASSED 0 FAILED 3 SKIPPED 0
```

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Host.Evaluate4CommandTests
```

Observed (exit code 1): the five pre-existing facts passed; the new fact failed at its stub:

```text
FAIL Evaluate4CommandTests.Run_GivenEquityPolicyChallenger_WritesReportWithPolicyHash: NotImplementedException: stub: EquityPolicyJson.Write
TOTAL 6 PASSED 5 FAILED 1 SKIPPED 0
```

## Green (observed after implementation)

Build command as above: `Build succeeded.`, `14 Warning(s)`, `0 Error(s)`; no warning names any new
or edited file.

```text
observed equity-policy refusals: 14 out-of-domain values
observed equity-policy refusals: whitespace and an extra key are not canonical
observed equity-policy json: {"schema":"tholdem.equity-policy.v1","equitySamples":400,"valueEdge":0.1,"raiseEdge":0.18,"bluffFrequency":0.08,"valueBetPotFraction":0.65}
observed equity-policy hash: 760ac54c77d737a71a4f488e1752574fb6dde1f4367fa6372959506e180091ac
TOTAL 3 PASSED 3 FAILED 0 SKIPPED 0
```

Evaluate4 class (exit 0):

```text
observed evaluate4 gate: verdict=Unsafe reason=unsafe-anchor-regression verifier=verifier:verified decision=decision-920f9dcb.json verification=verification-920f9dcb.json
observed equity-policy report: challengerPolicyHash=9c8924a71baa7a84d47e643b0408384e1a63e146177b432a981ef07dcc96e4e4 reportHash=05ca3b3a9cb7a059e7a0772ef7f8e9786d03965c6085047e661f245338e6cad6 report=report-05ca3b3a.json
observed evaluate4 malformed-plan stderr: tholdem: evaluation plan '<test-temp>/plan.json' is not a valid persisted plan: plan-not-json: Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. LineNumber: 0 | BytePositionInLine: 1.
observed evaluate4 report: planHash=63eb2c968b6d51d3fe0ef7c7caca91435ae9351d6e07144eb788f012d5617257 units=4 file=report-920f9dcb.json
observed evaluate4 unknown-composition stderr: tholdem: evaluation-plan-invalid: the persisted plan cannot be evaluated: composition-mismatch: the plan composition 'missing-composition' does not resolve in pool 'tholdem.opponents.v1'. (Parameter 'request')
observed evaluate4 unknown-pool stderr: tholdem: unknown-opponent-pool: evaluation plan '<test-temp>/plan.json' names opponent pool 'tholdem.opponents.unknown.v1'; supported pools are 'tholdem.opponents.v1' and 'tholdem.opponents.holdout.v1'.
TOTAL 6 PASSED 6 FAILED 0 SKIPPED 0
```

Full suite (exit 0):

```text
TOTAL 570 PASSED 570 FAILED 0 SKIPPED 0
```

The recorded round-13 baseline is 566 facts, so 570 = 566 + the four new facts. `dotnet test` was
not run; the supplementary runner executed the same compiled xUnit assembly and CI remains the
authoritative gate.

## Equity-policy report (observed)

The added fact writes a policy with `EquitySamples = 50` (otherwise the record defaults), runs
`evaluate4 --challenger-kind equity-policy --incumbent-kind strategy` on the tiny pre-registered plan,
and asserts the report against the policy hash:

- policy artifact hash: `9c8924a71baa7a84d47e643b0408384e1a63e146177b432a981ef07dcc96e4e4`
- report `challengerPolicyHash`: `9c8924a71baa7a84d47e643b0408384e1a63e146177b432a981ef07dcc96e4e4`
  (equal to `EquityPolicyJson.Hash(policy)`);
- report `incumbentPolicyHash`: the strategy incumbent's `StrategyArtifact.Hash`;
- report content hash: `05ca3b3a9cb7a059e7a0772ef7f8e9786d03965c6085047e661f245338e6cad6`,
  persisted as `report-05ca3b3a.json`;
- plan hash `63eb2c968b6d51d3fe0ef7c7caca91435ae9351d6e07144eb788f012d5617257`, 4 units, schema
  `tholdem.population-report.v2`.

The test also recomputes the report hash over the persisted bytes and asserts it matches the file
name, and asserts that no `active-champion.json` exists.

## Strategy path is byte-identical

Before any edit, the pre-existing Evaluate4 class was run and printed:

```text
observed evaluate4 report: planHash=63eb2c968b6d51d3fe0ef7c7caca91435ae9351d6e07144eb788f012d5617257 units=4 file=report-920f9dcb.json
TOTAL 5 PASSED 5 FAILED 0 SKIPPED 0
```

After the change the same fact prints the same plan hash and the same report content address
(`report-920f9dcb.json`), and the anchor set is still `decision-920f9dcb.json` /
`verification-920f9dcb.json`. The strategy branch loads the same artifact, constructs
`new StrategyAgent(artifact.Genome, artifact.Id)` and passes `artifact.Hash`; the report writer,
evaluator, plans and v1 commands were not touched. No byte change in the strategy-path report was
observed.

## No-pointer rule unchanged

`Evaluate4Command` neither reads nor writes `active-champion.json`: a content search over the
command for `WriteChampionPointer`, `ReadChampionStrategyId` and `active-champion` returns no
match, and both the pre-existing report/anchor facts and the new equity-policy fact assert that
`<data>/active-champion.json` does not exist after a successful run. No promotion or pointer write is
performed by either agent kind.

## Tests added

`WIP/Wip.Tholdem.Tests/Learning/EquityPolicyJsonTests.cs`:

1. `WriteThenParse_GivenCanonicalPolicy_RoundTripsAndHashesStably` — exact canonical string,
   parse/write round trip, record equality, hash stability and
   `Hash == StrategyArtifact.ComputeHash(Write(policy))`.
2. `Parse_GivenOutOfRangeOrNonFiniteValue_Refuses` — samples, three unit edges, fraction boundaries
   and `1e999`/non-finite inputs all refuse with `equity-policy-shape-invalid`.
3. `Parse_GivenWhitespaceOrExtraKey_RefusesAsNotCanonical` — valid-but-non-canonical bytes refuse
   with `equity-policy-not-canonical`.

`WIP/Wip.Tholdem.Tests/Host/Evaluate4CommandTests.cs`:

4. `Run_GivenEquityPolicyChallenger_WritesReportWithPolicyHash` — exit 0, empty stderr, the policy
   hash equals the report's `challengerPolicyHash`, the strategy incumbent hash is preserved, 4
   units, the report content address matches its file name, and no pointer is written.

## Commands run

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.EquityPolicyJsonTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Host.Evaluate4CommandTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

No `dotnet test` was run. No file outside the scope list was created or edited.
