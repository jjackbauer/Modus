# Round 6 — M5 refalsification (C)

**Tree:** `C:\Users\ricar\dev\Modus`, branch `WIP`, sampled 2026-11-07 (America/Sao_Paulo).
**Build:** `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj --configuration Release --no-incremental` → `Build succeeded`, `0 Error(s)` (2 pre-existing `Wip.Runtime` warnings); `dotnet build WIP/Wip.Tholdem.Host/Wip.Tholdem.Host.csproj --configuration Release` → `0 Warning(s) 0 Error(s)`. `dotnet test` was not run (local `Win32Exception (5)` testhost limitation); the established supplementary runner `.tmp/tholdem-verify/RunXunit` executed the same compiled test assembly.
**Verdict:** **M5 Supported.** All four clauses were re-derived from the current tree and survived concrete break attempts. No product, test, or configuration file was edited and no existing documentation file was edited; the only file added under `docs/` is this report, and all scratch data/logs are under `.tmp/refalsify-c6/`.

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| M5 | Supported | **(a)** Release CLI `play --seed 42 --hands 20 --allow-weak-seed` into three fresh data directories printed exactly `session: tholdem-06795fbeacfe528b` and `seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618`, exit 0; the written `session.json` SHA-256 is the same `87f90413…`; 20 hand files written.<br>**(b)** Every recorded hand under `artifacts/` replayed through the Release CLI: 35 files = 32 v1 (20 `probe-4242` + 5 `probe-777` + 3 `tholdem-9bae4b03e89b5865` + 2 `tholdem-f275b4ee69be0557` + 2 `artifacts/tholdem-run` v1) + 3 four-seat v2; 32/32 v1 exit 0 and every file SHA-256 equals its `session.json` `handHashes[handNumber-1]`; 20/20 additional v1 under `.tmp/tholdem-cli` also exit 0. Tampered v1 controls fail with exit 5 (manifest hash mismatch / replay invariant), so the check is not vacuous.<br>**(c)** Source/product invariant is `v1 iff SeatCount == 2 && all starting stacks equal`, else v2 (`HandRecordCanonicalWriter.cs:17-19`); v2 parses for 2..6 seats (`HandRecordJson.cs:141-145`). Fixture probe over the generated sweep: 18/18 PASS, exactly `fixture_v1_hands=10` (all `schema=V1 seats=2 startingStacks=[1000,1000]`), `twoSeatV2=3` (all `schema=V2 seats=2 startingStacks=[1000,600]`), `fourSeatV2=5` (all `schema=V2 seats=4 startingStacks=[1000,1000,1000,1000]`). Release CLI generated a 2-seat unequal-stack v2 hand; the fixture verification path accepted it: `PASS schema=V2 seats=2 startingStacks=[1000,600] hash=77459c9c…`, 2/2. CLI equal-stack 2-seat generated v1 and was accepted as v1; CLI 6-seat v2 was accepted as v2. A tampered 2-seat v2 file with `startingStacks:[1000,1000]` was rejected by CLI replay (exit 5) and by the fixture canonical round-trip (`Expected` v2 text vs `Actual` v1 text). Source grep of the test project for `artifacts[/\\]tholdem` is 0; compatibility tests pass 4/4 and the full suite passes 275/275 with the entire `artifacts/` directory absent.<br>**(d)** Full Tholdem suite: `TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0`, exit 0, on the real test bin; the same on a byte-identical isolated copy; the same on that copy with the entire `artifacts/` tree moved aside (`artifacts_present_during_run=False`); and a direct real-bin rerun with the entire `artifacts/` tree absent also produced `TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0`, exit 0. Source audit: 247 `[Fact]` + 28 `[InlineData]` = 275 cases, 0 `Skip=`, 0 `#if`, 0 `Trait`; `docs/plan/test-waivers.json` has `expectedTotals["Wip.Tholdem.Tests"]=275` and 0 Tholdem entries under `failures`/`knownAborted`. The operator `artifacts/` tree was restored byte-identically: 141/141 SHA-256 entries match. | **(a)** Two clean data directories, then a third after the final Release rebuild; all produced the exact session id, manifest hash and exit 0; no divergence.<br>**(b)** Replayed every recorded v1 file, including `artifacts/tholdem-run` and the 20-file `.tmp/tholdem-cli` session; made canonical edits in copies of v1 files (one outside a session, one inside a copied session with its manifest) to test whether hash/replay verification would catch them; it did (exit 5).<br>**(c)** Generated with the Release CLI a 2-seat unequal-stack session (v2), a 2-seat equal-stack session (v1), and a 6-seat session (v2), then ran the fixture verification path on each; tampered a v2 2-seat file to equal stacks; moved the entire `artifacts/` tree aside and re-ran the compatibility class and full suite.<br>**(d)** Moved the entire `artifacts/` directory aside and ran the full suite. A first real-bin attempt was invalidated by a concurrent `dotnet build` rewriting the shared test bin mid-run (53 `FileNotFoundException` for `Wip.Tholdem.Host`/`Wip.Tholdem.Web`); the valid rerun used a byte-identical isolated copy of the test bin and passed with artifacts absent. No skip or waiver was found. | The exact oracle values, the 32/32 v1 hash-anchored replays, the three-shape probe (10/3/5), the CLI-generated 2-seat unequal-stack v2 acceptance, the 2..6-seat breadth (2 and 6 tested), the tamper rejections, the zero artifact references and the 275/275/0 full-suite result with artifacts absent all match the corrected claim. The negative controls show the hash/replay and canonical checks actually fail on mutation, so the green results are not vacuous. |

## Exact observations

### (a) 2-seat CLI oracle

Command (repository root; fresh data directory each run):

```powershell
dotnet WIP/Wip.Tholdem.Host/bin/Release/net10.0/Wip.Tholdem.Host.dll play --seed 42 --hands 20 --allow-weak-seed --data-dir <fresh>
```

Observed (`oracle1.txt`, `oracle2.txt`; `oracle3` repeated after the final Release rebuild):

```text
session: tholdem-06795fbeacfe528b
seed: 42  hands: 20  manifest sha256: 87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
agent                    hands    bb/100       95% CI (bb/100)  win rate     95% CI (win rate)  aggression  showdown
tight-aggressive            20  -1883.00   -3511.94 .. -254.06      0.15          0.05 .. 0.36        0.10      0.20
random-legal                20   1883.00     254.06 .. 3511.94      0.85          0.64 .. 0.95        3.25      0.20

exit_code=0
session_dirs=1: tholdem-06795fbeacfe528b
manifest_file_sha256=87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618
hands=20
```

The second and third runs produced the same session id and manifest hash; the second run's manifest bytes were identical to the first (`manifests_identical=True`).

### (b) Recorded v1 hand files

Schema counts by recorded session (computed from the file bytes: a v2 file starts with `{"schema":"tholdem.hand.v2"`):

```text
artifacts\tholdem\sessions\probe-4242\session.json                          v1=20 v2=0
artifacts\tholdem\sessions\probe-777\session.json                           v1=5  v2=0
artifacts\tholdem\sessions\tholdem-9bae4b03e89b5865\session.json            v1=3  v2=0
artifacts\tholdem\sessions\tholdem-d56ac5f4637e44ef\session.json            v1=0  v2=3
artifacts\tholdem\sessions\tholdem-f275b4ee69be0557\session.json            v1=2  v2=0
artifacts\tholdem-run\sessions\tholdem-f275b4ee69be0557\session.json        v1=2  v2=0
```

Every one of the 35 files was passed to the Release CLI `replay` command; every file replayed with exit 0 and its SHA-256 equalled the manifest anchor:

```text
SUMMARY files=35 v1=32 v2=3 failures=0
```

The 32 v1 files include the two v1 recordings under `artifacts/tholdem-run` (they are outside `artifacts/tholdem`, so the claim was checked more broadly than the 30 files in `artifacts/tholdem/sessions`). The 20 v1 files in `.tmp/tholdem-cli/sessions/tholdem-06795fbeacfe528b` were also replayed:

```text
extra_v1_files=20 failures=0
SUMMARY_REPLAY=20/20
```

Negative controls (copies under `.tmp/refalsify-c6/`, never the operator files):

- A canonical edit inside a copied v1 session file (`"potAfter":20` → `"potAfter":21`) with the sibling `session.json` present:
  `exit 5`, `replay hash mismatch for hand 1: file sha256 c206ffda… does not match session manifest … handHashes[0] 709c0816…`.
- The same edit without a manifest anchor:
  `exit 5`, `replay invariant failed: Replayed pot 20 does not match recorded pot 21 after action 1.`

Both controls prove the 32/32 v1 passes are real parse/hash/replay checks, not a vacuous command.

### (c) Corrected schema invariant and generated sweep

Product truth:

- `WIP/Wip.Tholdem/History/HandRecordCanonicalWriter.cs:17-19` — `Write` returns `WriteV1` only when `record.SeatCount == 2 && AllStartingStacksEqual(record)`; every other record is v2.
- `WIP/Wip.Tholdem/History/HandRecordCanonicalWriter.cs:22-39` — `AllStartingStacksEqual` requires `StartingStacks.Count == SeatCount` and every stack equal.
- `WIP/Wip.Tholdem/History/HandRecordJson.cs:141-145` — a v2 record is accepted for `seats` 2..6.
- `WIP/Wip.Tholdem.Tests/MultiwayHistoryTests.cs:181-218` — the suite already pins a 2-seat `[1000,600]` v2 record.

Fixture/test truth:

- `WIP/Wip.Tholdem.Tests/HistorySessionFixture.cs:54-130` — generates one sessions root with `heads-up-v1` (2 seats, `[1000,1000]`, seed 42, 10 hands), `heads-up-unequal-v2` (2 seats, `[1000,600]`, seed 777, 3 hands) and `four-seat-v2` (4 seats, `[1000,1000,1000,1000]`, seed 9001, 5 hands) via the public `MatchSimulator.Run`.
- `WIP/Wip.Tholdem.Tests/HistorySessionFixture.cs:201-229` — `AssertSeatCountMatchesSchema`: v1 → exactly 2 seats and all starting stacks equal; v2 → 2..6 seats, and a 2-seat v2 must have unequal starting stacks. `VerifyHandFile` (`:160-194`) also asserts the file bytes equal `record.ToCanonicalJson()`.
- `WIP/Wip.Tholdem.Tests/HistorySessionCompatibilityTests.cs:47-111` — the schema-aware sweep verifies every generated file, buckets v1 / 2-seat v2 / 4-seat v2, asserts all three buckets are non-empty and equal the generator's exact counts (10 / 3 / 5), and throws for any other v2 seat count in the generated root.
- `WIP/Wip.Tholdem.Tests/HistorySessionCompatibilityTests.cs:147-182` — the dedicated 2-seat unequal-stack v2 regression selects a real sessions-root path, checks the v2 prefix/`"seats":2`, runs the shared `VerifyHandFile` path and asserts `[1000,600]`.

Fixture probe over the generated root (the probe is the existing scratch reflection probe `.tmp/refalsify-A-r6/probe`, which only loads the compiled test assembly and calls `HistorySessionFixture.EnumerateHandFiles` / `ReadSchema` / `VerifyHandFile`):

```text
fixture_all_files=18
PASS schema=V2 seats=4 startingStacks=[1000,1000,1000,1000] … (5 files)
PASS schema=V2 seats=2 startingStacks=[1000,600] …             (3 files)
PASS schema=V1 seats=2 startingStacks=[1000,1000] …             (10 files)
fixture_v1_hands=10 twoSeatV2=3 fourSeatV2=5
PROBE_TOTAL=18 PASS=18 FAIL=0
```

Release CLI 2-seat unequal-stack v2 generation (the required concrete case):

```text
session: tholdem-f275b4ee69be0557
seed: 777  hands: 2  manifest sha256: 0ea96416c6fe3e74a844df5daacaf622b8179b999d4adedbb269ca0be628f577
exit_code=0
```

Hand bytes:

```json
{"schema":"tholdem.hand.v2","handNumber":1,…,"startingStack":1000,"startingStacks":[1000,600],"buttonSeat":0,"seats":2,…
```

The fixture verification path accepted both hands:

```text
cli_sessions_files=2
PASS schema=V2 seats=2 startingStacks=[1000,600] hash=77459c9ce22677551aea13937e5652e894aaf0c3127145ec70ed099a1fc0b787 …
PASS schema=V2 seats=2 startingStacks=[1000,600] hash=8ce84ce849d1a96ad7a2fafee36124f7dd2d366b7daa27cbc52e4595707a7e9d …
PROBE_TOTAL=2 PASS=2 FAIL=0
```

Equal-stack control (the product must select v1): CLI `play --seed 777 --hands 2 --seats 2 --starting-stacks 1000,1000 --allow-weak-seed` produced files beginning `{"handNumber":1,…` (no `schema`) and the probe returned:

```text
PASS schema=V1 seats=2 startingStacks=[1000,1000] hash=571049a9883fcc96d5ea0b85771030d63f66fe0169306d6a16f6bb39b7a8019b …
PASS schema=V1 seats=2 startingStacks=[1000,1000] hash=92aa3db0e586fd3b9b31bb1d45777d507aa8f931b7b912aca1c14964ace547f3 …
PROBE_TOTAL=2 PASS=2 FAIL=0
```

V2 breadth control: CLI `play --seed 9001 --hands 2 --seats 6 --starting-stacks 1000,1000,1000,1000,1000,1000 --allow-weak-seed` produced 6-seat v2 files; the fixture path accepted them:

```text
PASS schema=V2 seats=6 startingStacks=[1000,1000,1000,1000,1000,1000] …
PASS schema=V2 seats=6 startingStacks=[1000,1000,1000,1000,1000,1000] …
PROBE_TOTAL=2 PASS=2 FAIL=0
```

Tamper control for the claimed invariant: a copy of the CLI 2-seat v2 hand with `"startingStacks":[1000,600]` changed to `"startingStacks":[1000,1000]` (all else unchanged):

```text
cli_replay_exit=5
fixture_probe_exit=1
FAIL schema=V2 EqualException: Assert.Equal() Failure: Strings differ
Expected: "{"schema":"tholdem.hand.v2","handNumber":…
Actual:   "{"handNumber":1,"seed":975410481851812618…
PROBE_TOTAL=1 PASS=0 FAIL=1
```

The product canonical writer re-derives the schema from the parsed record, so a 2-seat v2 file with equal stacks is not a canonical product record; the fixture rejects it, as the corrected invariant requires.

Self-containment: a grep over the 61 test source files (`*.cs`, excluding `bin`/`obj`) found **0** references to `artifacts[/\\]tholdem`; the compatibility class is 4/4 and the full suite is 275/275/0 with the entire `artifacts/` directory moved aside (see (d)). The fixture writes its scratch root under `AppContext.BaseDirectory/test-temp`, not under the repository's `artifacts/`.

### (d) Full suite, skips, waivers, and the artifacts-aside condition

Four full-suite runs of the current compiled assembly:

```text
# real test bin, artifacts present
TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0
exit_code=0

# byte-identical isolated copy of the test bin, artifacts present
TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0
exit_code=0

# same isolated copy, entire artifacts/ tree moved aside
artifacts_present_during_run=False
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0        # compatibility class
full_without_artifacts_exit=0
TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0    # full Tholdem suite
artifacts_present_after_run=False
artifacts_restored=True

# direct real test bin, entire artifacts/ tree moved aside (final rerun)
artifacts_present_during_run=False
full_real_without_artifacts_exit=0
TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0
artifacts_present_after_run=False
artifacts_restored=True
artifacts_files_before=141 after=141 differences=0
```

The isolated copy is byte-identical to the real test bin for the relevant assemblies (`Wip.Tholdem.Tests.dll`, `Wip.Tholdem.dll`, `Wip.Tholdem.Agents.dll`, `Wip.Tholdem.Host.dll`, `Wip.Tholdem.Web.dll`, `xunit.core.dll`, `xunit.assert.dll` all `identical=True`). The direct real-bin rerun was executed after the concurrent build had settled and produced the same 275/275/0 with `artifacts/` absent. `artifacts/tholdem` was not recreated by the suite; the operator tree was restored and verified:

```text
artifacts_files_before=141
artifacts_files_after=141
artifacts_tree_unchanged=True
diff_count=0
```

No skipped or waived Tholdem tests:

```text
source_files=61 fact_attributes=247 theory_attributes=8 inline_data=28 expected_cases=275
skip_attributes=0 ifdef=0 trait=0
tholdem_expected_total=275 tholdem_failures=0 tholdem_known_aborted=0
test_source_refs_to_artifacts_tholdem=0
```

`tools/run-wip-tests.ps1:38` includes `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj` in the offline project list, and `docs/plan/test-waivers.json` contains no Tholdem failure or aborted-project entry; the only Tholdem entry is `expectedTotals["Wip.Tholdem.Tests"] = 275`.

Transparency on the first moved-aside attempt: running the full suite from the real test bin with `artifacts/` absent produced `TOTAL 275 PASSED 222 FAILED 53 SKIPPED 0`, with every failure a `FileNotFoundException` for `Wip.Tholdem.Host`/`Wip.Tholdem.Web`. The shared test bin was being rewritten by a concurrent `dotnet build` during that run (assembly timestamps `11/7/2026 7:41:10 PM`–`7:41:17 PM`; a later build process was observed), so that attempt is environmental and invalid. After the build settled, both the byte-identical isolated-copy run and a direct real-bin rerun with artifacts absent passed 275/275/0.

## Limits / not checked

- `dotnet test` was not run (the local VSTest testhost aborts with `Win32Exception (5)`); the established supplementary `.tmp/tholdem-verify/RunXunit` runner executed the compiled test assembly. CI was not triggered or observed.
- The scope of "full suite" in (d) is the full `Wip.Tholdem.Tests` suite (275 tests), which is the suite this claim concerns. The full 48-project repository suite and the architecture test project were not run.
- A literal fresh `git clone` was not performed; the whole `WIP/Wip.Tholdem*` tree is currently untracked, so a clone would not contain the product/test tree. The data-dependency condition was tested by moving the entire gitignored `artifacts/` directory aside.
- To call the internal test fixture from outside the test assembly, I ran the existing scratch reflection probe at `.tmp/refalsify-A-r6/probe` (its `Program.cs` only loads the compiled test assembly and invokes `HistorySessionFixture` methods). No product, test, or configuration file was edited; the only file added under `docs/` is this report.
- The operator `artifacts/` data was moved aside only during the three documented windows and restored byte-identically; the 141-file SHA-256 manifest matched before/after.
- Evidence logs and scratch data: `.tmp/refalsify-c6/` (`oracle1.txt`, `oracle2.txt`, `recorded-replay.txt`, `compat-run.txt`, `cli-v2two.txt`, `probe-refinal.txt`, `tamper-v2-equal.txt`, `tamper-v1.txt`, `tamper-v1-session.txt`, `full-baseline.txt`, `full-copy-baseline.txt`, `full-copy-without-artifacts.txt`, `full-real-without-artifacts.txt`, `compat-copy-without-artifacts.txt`, `artifacts-before.txt`, `artifacts-restore-check.txt`, `source-audit.txt`, `build-tests.txt`, `cli-build.txt`).