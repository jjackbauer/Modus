# Round 8b — replay integrity anchor, truthful docs, Tholdem on the security CI surface

- **Date**: 2026-11-06
- **Branch**: `WIP`
- **Objective closed**: (a) the CLI `replay` integrity check is anchored to an external hash so a
  canonical semantic edit is detected; (b) the truthful documentation set exists under
  `docs/tholdem/`; (c) the Tholdem agent-security tests run on the offline/security CI surface.
- **Test count**: **177** (172 previously; the weak byte-tamper test was replaced by a
  non-canonical-file fact and five new anchored-integrity facts were added). Suite:
  `TOTAL 177 PASSED 177 FAILED 0 SKIPPED 0`.
- **No release, publish, sign or deploy is claimed or performed** by this round; nothing was pushed.

## Files changed

### Modified

| Path | Change |
|---|---|
| `WIP/Wip.Tholdem.Host/Commands/ReplayCommand.cs` | External-anchor integrity: keeps the canonical-bytes check, adds the session-manifest `handHashes[handNumber - 1]` anchor, adds `--expected-hash <64 lowercase hex>`, and prints `integrity: unanchored` when neither anchor exists. A malformed hash value is exit 2; a mismatch is exit 5 naming the anchor and both hashes. |
| `WIP/Wip.Tholdem.Host/TholdemCli.cs` | **Necessary minimal scope extension**: the per-command option sets live in this file, not in `CliOptions.cs`. Added `expected-hash` to `ReplayValued` and to the `replay` usage line. Without this registration `CliOptions.Parse` rejects the required flag as unknown. Two lines changed; no other CLI behaviour. |
| `WIP/Wip.Tholdem.Tests/Host/ReplayCommandTests.cs` | The weak `Run_GivenByteTamperedCopy_ExpectedExit5` was replaced by six integrity facts (semantic edit in a session, correct/wrong `--expected-hash`, non-canonical bytes, unanchored copy, malformed hash) alongside the recorded-hand and malformed-file facts. |
| `.github/workflows/tests-ci.yml` | Added the `Run Tholdem agent security suite` step to both `wip-security` and `wip-security-linux`, after the existing agent-safety step, using each job's existing results directory. No existing step removed or weakened. |
| `docs/plan/test-waivers.json` | `expectedTotals["Wip.Tholdem.Tests"]` `172 → 177` (the real count; no new waivers). |
| `docs/goal-tholdem/round-8b/implementation.md` | This report. |

### Created

| Path | Purpose |
|---|---|
| `docs/tholdem/README.md` | Index of the five documents plus the CLI command summary and exit codes. |
| `docs/tholdem/engine-rules.md` | Heads-up format, blinds/order, deal order (no burns), action semantics, legality, short all-in, street progression, showdown/split-pot defence, chip conservation, replay, observation boundary. |
| `docs/tholdem/strategy-artifact-schema.md` | Genome fields/ranges, artifact JSON + content-hash id, ledger entry, CLI evaluation document, training evaluation reports, champion pointer, session/hand files, data-never-executed rule. |
| `docs/tholdem/training-promotion-runbook.md` | `train`/`evaluate`/`promote --yes`, the paired held-out gate and verifier, ledger inspection, reproduction, why a rejection is normal. |
| `docs/tholdem/known-limits.md` | Heads-up only, VSTest/`%TEMP%` limits, no CI claim, viewer build lock, VPIP = 0, browser-only checks, live-model gate, defensive odd chip, no real money, no release claim. |
| `docs/tholdem/viewer.md` | Copy-and-run procedure, tabs/controls, data directory, HTTP endpoints, spectator-vs-agent hole-card boundary, loopback/offline. |

## Part A — replay integrity

`ReplayCommand.Execute` now performs, in order:

1. **Usage/format validation** — one positional; `--expected-hash` must be exactly 64 lowercase hex
   characters (a malformed value is `CliUsageException` → exit 2).
2. **Canonical-bytes check (kept)** — the file's SHA-256 must equal `record.Hash`; a non-canonical
   file (even one that still parses) is still exit 5.
3. **Session manifest anchor** — if the hand's directory contains a sibling `session.json` with a
   `handHashes` array, the file bytes must equal `handHashes[handNumber - 1]`; mismatch → exit 5
   naming the manifest path, the index and both hashes. A manifest with no entry for that hand, or
   unreadable JSON, is also an integrity failure (exit 5) rather than a silent unanchored pass.
4. **Explicit anchor** — if `--expected-hash` is supplied, the file bytes must equal it; mismatch →
   exit 5 naming the flag and both hashes. If both anchors are present, both must match.
5. **Replay** — unchanged `HeadsUpHandEngine.Replay`, with failures wrapped as exit 5.
6. **Honest footer** — with neither anchor, the hand still replays (exit 0) and the last line is
   exactly `integrity: unanchored`; no verified claim is printed for that case.

## Test-first evidence (red)

The new test file was written and built first, before any production change; it compiled and
executed against the old implementation, so the failures below are behavioural.

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:22.08
RED_BUILD2_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll ReplayCommandTests
FAIL ReplayCommandTests.Run_GivenCanonicalSemanticEditInsideSession_ExpectedExit5NamingManifestAndBothHashes: EqualException: Assert.Equal() Failure: Values differ
Expected: 5
Actual:   0
FAIL ReplayCommandTests.Run_GivenCopyOutsideSessionWithManifestExpectedHash_ExpectedExit0: EqualException: Assert.Equal() Failure: Values differ
Expected: 0
Actual:   2
FAIL ReplayCommandTests.Run_GivenCopyOutsideSessionWithWrongExpectedHash_ExpectedExit5: EqualException: Assert.Equal() Failure: Values differ
Expected: 5
Actual:   2
FAIL ReplayCommandTests.Run_GivenMalformedExpectedHash_ExpectedExit2: ContainsException: Assert.Contains() Failure: Sub-string not found
String:    "tholdem: unknown option '--expected-hash'"···
Not found: "64 lowercase hexadecimal"
FAIL ReplayCommandTests.Run_GivenUnanchoredCopy_ExpectedExit0WithUnanchoredIntegrityNote: ContainsException: Assert.Contains() Failure: Sub-string not found
String:    "hand: 1\r\nboard: 8h 8s 8d Th 6c\r\nseat 0: Q"···
Not found: "integrity: unanchored"
TOTAL 8 PASSED 3 FAILED 5 SKIPPED 0
RED_REPLAY_RUN_EXIT=1
```

The first failure is the P1 (EVD-086 / TODO-013): the canonical semantic edit exited **0**, exactly
as the review found.

## Green evidence

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:24.02
GREEN_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll ReplayCommandTests
TOTAL 8 PASSED 8 FAILED 0 SKIPPED 0
GREEN_REPLAY_RUN_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 177 PASSED 177 FAILED 0 SKIPPED 0
FULL_SUITE_EXIT=0
```

## Mutation

Mutant: the session-manifest comparison was removed (the anchor is still read for the unanchored
footer, but the bytes are no longer compared to `handHashes`), making the manifest check a no-op.

Pre-mutation and backup hashes of `WIP/Wip.Tholdem.Host/Commands/ReplayCommand.cs`:

```text
pre_mutation_sha256=95e26ebf589f058b1af5ee914e7666026cc0ce48d6bdf92630283035d0ae6f25
backup_sha256=95e26ebf589f058b1af5ee914e7666026cc0ce48d6bdf92630283035d0ae6f25
match=True
```

Forced rebuild and the focused replay suite:

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental
    2 Warning(s)
    0 Error(s)

Time Elapsed 00:00:46.49
MUTANT_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll ReplayCommandTests
FAIL ReplayCommandTests.Run_GivenCanonicalSemanticEditInsideSession_ExpectedExit5NamingManifestAndBothHashes: EqualException: Assert.Equal() Failure: Values differ
Expected: 5
Actual:   0
TOTAL 8 PASSED 7 FAILED 1 SKIPPED 0
MUTANT_RUN_EXIT=1
```

Exactly the intended fact fails (the canonical edit is once again accepted). The two warnings are
the pre-existing `Wip.Runtime` `CS8425`/`CA2024` warnings; no Tholdem project emits a warning.

Restored from the backup and verified by hash:

```text
original_sha256=95e26ebf589f058b1af5ee914e7666026cc0ce48d6bdf92630283035d0ae6f25
restored_sha256=95e26ebf589f058b1af5ee914e7666026cc0ce48d6bdf92630283035d0ae6f25
match_pre_mutation=True
mutant_marker_removed=True
```

Forced rebuild and green re-run after the restore:

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental
    2 Warning(s)
    0 Error(s)

Time Elapsed 00:00:41.16
RESTORED_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 177 PASSED 177 FAILED 0 SKIPPED 0
RESTORED_RUN_EXIT=0
```

## Verification commands (final tree, exact observed output)

### 1. Test project build + supplementary runner

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:19.39
FINAL_TEST_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 177 PASSED 177 FAILED 0 SKIPPED 0
FINAL_RUN_EXIT=0
```

### 2. Whole solution

```text
$ dotnet build Wip.slnx --configuration Release
    124 Warning(s)
    0 Error(s)

Time Elapsed 00:01:31.02
FINAL_SOLUTION_BUILD_EXIT=0
```

The 124 warnings are the pre-existing solution-wide set (e.g. `Wip.Bones.Host.Tests` CS0105/CS8600
and `Wip.Runtime` CS8425/CA2024); no `Wip.Tholdem*` warning is among them. A separate forced
`dotnet build Wip.slnx --configuration Release --no-incremental` observed `125 Warning(s)`,
`0 Error(s)`, exit 0, and `THOLDEM_WARNINGS=0`.

### 3. Architecture runner

```text
$ dotnet build tests/Wip.Architecture.Tests -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:08.10
FINAL_ARCH_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
FINAL_ARCH_RUN_EXIT=0
```

The project-graph assertion remains `48` and passes.

### 4. Solution coverage

```text
$ powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
solution=Wip.slnx
wip_project_count=48
solution_wip_project_count=48
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
FINAL_COVERAGE_EXIT=0
```

### 5. Harness sync

```text
$ dotnet run --project tools/HarnessSync -- --check
FINAL_HARNESS_EXIT=0
```

`--check` prints nothing on success; the exit code is the observation. It also confirms every text
file is UTF-8 without BOM; a separate byte scan of the six docs, both Host sources, the test file,
the workflow and the waiver register found no BOM, no UTF-16 and no tab characters.

## CLI proof (real session, final Release binary)

Data directory `.tmp/round8b-cli` (workspace scratch, not `%TEMP%`). Session
`tholdem-64035e9af3d4c23e`, hand 1 manifest hash
`709c0816b59b18c7514198901d51ac0e0af050f02b27795a4c27521908324fc5`.

**Recorded hand, exit 0:**

```text
$ Wip.Tholdem.Host.exe replay ...\sessions\tholdem-64035e9af3d4c23e\hand-00001.json
hand: 1
board: 8h 8s 8d Th 6c
seat 0: Qs 4s
seat 1: Tc Jd
actions:
  preflop seat=0 kind=Call amount=0 potAfter=20
  preflop seat=1 kind=AllIn amount=0 potAfter=1010
  preflop seat=0 kind=Call amount=0 potAfter=0
showdown: true
winners: 1
pot awarded: 2000
final stacks: 0 / 2000
replayed stacks: 0 / 2000  pot: 0
REPLAY_EXIT=0
```

**Canonical semantic edit inside a copied session dir (`"smallBlind":5` → `"smallBlind":6`),
exit 5; stdout empty:**

```text
EDITED_HAND=C:\Users\ricar\dev\Modus\.tmp\round8b-cli-edit\sessions\tholdem-64035e9af3d4c23e\hand-00001.json
EDIT_APPLIED=True
ACTUAL_FILE_SHA256=46a4b95ef0eaec619e195b2d68d8e1d09d9a6b74eaf44ca6f9fa07f812f3be1b
MANIFEST_HAND_HASH=709c0816b59b18c7514198901d51ac0e0af050f02b27795a4c27521908324fc5
SEMANTIC_EDIT_EXIT=5
STDOUT_BYTES=0
STDERR:
tholdem: replay hash mismatch for hand 1: file sha256 46a4b95ef0eaec619e195b2d68d8e1d09d9a6b74eaf44ca6f9fa07f812f3be1b does not match session manifest 'C:\Users\ricar\dev\Modus\.tmp\round8b-cli-edit\sessions\tholdem-64035e9af3d4c23e\session.json' handHashes[0] 709c0816b59b18c7514198901d51ac0e0af050f02b27795a4c27521908324fc5.
```

**Unanchored copy, exit 0, exact footer:**

```text
$ Wip.Tholdem.Host.exe replay ...\standalone\hand-00001.json
hand: 1
board: 8h 8s 8d Th 6c
seat 0: Qs 4s
seat 1: Tc Jd
actions:
  preflop seat=0 kind=Call amount=0 potAfter=20
  preflop seat=1 kind=AllIn amount=0 potAfter=1010
  preflop seat=0 kind=Call amount=0 potAfter=0
showdown: true
winners: 1
pot awarded: 2000
final stacks: 0 / 2000
replayed stacks: 0 / 2000  pot: 0
integrity: unanchored
UNANCHORED_EXIT=0
```

**Same copy with the correct `--expected-hash` (the manifest entry), exit 0:**

```text
$ Wip.Tholdem.Host.exe replay ...\standalone\hand-00001.json --expected-hash 709c...
hand: 1
board: 8h 8s 8d Th 6c
seat 0: Qs 4s
seat 1: Tc Jd
actions:
  preflop seat=0 kind=Call amount=0 potAfter=20
  preflop seat=1 kind=AllIn amount=0 potAfter=1010
  preflop seat=0 kind=Call amount=0 potAfter=0
showdown: true
winners: 1
pot awarded: 2000
final stacks: 0 / 2000
replayed stacks: 0 / 2000  pot: 0
EXPECTED_OK_EXIT=0
```

**Same copy with a wrong 64-zero expected hash, exit 5:**

```text
$ Wip.Tholdem.Host.exe replay ...\standalone\hand-00001.json --expected-hash 0000...
EXPECTED_WRONG_EXIT=5
stderr: tholdem: replay hash mismatch: file sha256 709c0816b59b18c7514198901d51ac0e0af050f02b27795a4c27521908324fc5 does not match --expected-hash 0000000000000000000000000000000000000000000000000000000000000000.
```

## Part C — CI surface (read back from `.github/workflows/tests-ci.yml`)

YAML parsed with a scratch YamlDotNet reader (`yaml=valid documents=1 jobs=6`); both new steps are
step 6, immediately after `Run agent safety suite`:

Windows job (`wip-security`, lines 138–139):

```yaml
      - name: Run Tholdem agent security suite
        run: dotnet test WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~TholdemRuntimeAgentTests|FullyQualifiedName~SessionHandStoreTests|FullyQualifiedName~LiveModelGateTests|FullyQualifiedName~RuntimeSourceBoundaryTests|FullyQualifiedName~ToolContractTests" --results-directory artifacts/security-test-results --logger "trx;LogFileName=TholdemSecurity.trx"
```

Linux job (`wip-security-linux`, lines 192–193):

```yaml
      - name: Run Tholdem agent security suite
        run: dotnet test WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~TholdemRuntimeAgentTests|FullyQualifiedName~SessionHandStoreTests|FullyQualifiedName~LiveModelGateTests|FullyQualifiedName~RuntimeSourceBoundaryTests|FullyQualifiedName~ToolContractTests" --results-directory artifacts/security-test-results-linux --logger "trx;LogFileName=TholdemSecurity.trx"
```

Parser output confirming placement and indentation (6 spaces before `- name`, 8 before `run`):

```text
job=wip-security runs-on=windows-latest steps=8
  [5] Run agent safety suite run
  [6] Run Tholdem agent security suite run
      previous=Run agent safety suite
      next=Dump containment diagnostics
job=wip-security-linux runs-on=ubuntu-latest steps=7
  [5] Run agent safety suite run
  [6] Run Tholdem agent security suite run
      previous=Run agent safety suite
      next=Upload security test results
YAML_CHECK_EXIT=0
```

The five filter names were matched against the real classes in `WIP/Wip.Tholdem.Tests/Runtime/`:
`TholdemRuntimeAgentTests`, `SessionHandStoreTests`, `LiveModelGateTests`, `RuntimeSourceBoundaryTests`
and `ToolContractTests` all exist. No existing CI step was removed or weakened.

## What I could not verify

- **`dotnet test` was not run** (forbidden by the dispatch; the local Windows testhost aborts with
  `Win32Exception (5)` as documented in `docs/goal-tholdem/goal.md` and `known-limits.md`). The green
  claim is the supplementary `RunXunit` runner over the compiled assembly, which executes the same
  facts. The exact CI `dotnet test … --no-build --filter "…"` step was not executed locally; only
  its YAML shape, results directory and class names were verified.
- **No CI run is claimed.** `Tests CI` cannot be triggered or observed from this machine (no GitHub
  CLI/token), so the two new steps are a source edit, not an observed green job.
- **The viewer was not started this round.** The five documents were written from the source (and
  from previously recorded viewer evidence); the viewer run procedure itself was not re-executed
  here.
- **Browser-only checks** (keyboard model, 390 px geometry) were not repeated; `known-limits.md`
  states they are browser evidence, not committed tests.
- `pwsh` is absent; the coverage script was run with Windows PowerShell exactly as requested. Test
  scratch stayed under the test output directory / workspace `.tmp`, never `%TEMP%`.
