# Round 13 — refuse guessable session seeds at the operator entry points

- **Repository**: `C:\Users\ricar\dev\Modus`, branch `WIP`.
- **Scope**: only `WIP/Wip.Tholdem.Host/`, `WIP/Wip.Tholdem.Web/`, `WIP/Wip.Tholdem.Tests/`,
  `docs/tholdem/*.md`, `docs/plan/test-waivers.json` and this file. The engine, history, agents,
  learning loop, `Wip.slnx`, the architecture tests and `.github/` were not touched. Project count
  stays 48.
- **Verdict**: implemented. `SessionSeedPolicy` (CLI) and its web mirror require a session seed at or
  above `1,099,511,627,776` (2^40); `play`, `train` and `evaluate` refuse a weaker seed with exit 2
  unless `--allow-weak-seed` is present, and `POST /api/live` refuses it with HTTP 400
  `{"error":"weak-seed","minimum":1099511627776}` unless the body says `"allowWeakSeed": true`. The
  viewer form generates a random 64-bit seed on load and exposes the opt-in as a checkbox. The
  absolute non-invertibility documentation claims were replaced with a precise threat model, and the
  two unrelated documentation mismatches were fixed.
- **Tests**: `TOTAL 194 PASSED 194 FAILED 0 SKIPPED 0` (was 182; +12 facts). The guard tests were
  written before the implementation and observed red first; the suite was then mutated and restored.
- **Verification limits**: local `dotnet test` was not run (VSTest is unusable in this session; see
  [known-limits.md](../../tholdem/known-limits.md)); the supplementary runner
  `.tmp/tholdem-verify/RunXunit` over the compiled test assembly was used instead. CI was not
  observed. The browser UI was not driven end-to-end; the markup/JS contracts, a Node execution of
  the real seed helper, and the HTTP surface were verified instead.

## 1. Root cause this round closes

Round 12 removed the seed from everything an agent receives: `AgentTurnContext` is
`(HandNumber, Seat, ActionIndex)`, `DeriveTurnSeed` is gone, and agent randomness comes from
`SeedDerivation.DeriveAgentSeed(observation.ToCanonicalJson(), …)`. That closed the direct
dictionary attack on a value handed to the agent.

It did not make a **guessable session seed** safe. The deal is still
`SeedDerivation.Derive(sessionSeed, hand, 0, 0, 0)` → `SeededDeck.Create`, so an observer that holds
its own cards can try candidate seeds, recompute the deal, and keep the candidates that reproduce
those cards. The round-13 falsifiers recovered `42` from the `Qs,4s` own cards of
`play --seed 42 --hands 3` by enumerating `0..20000` in 388 ms (11 matches), recovered `777` from
the documented seed-777 session, and rebuilt the opponent's hole cards and the board from the derived
deck `Qs Tc 4s Jd 8h 8s 8d Th 6c`.

The product therefore must not accept a weak seed by default at its operator entry points. This
round adds that policy without changing the engine, the simulator's determinism or the learning
loop.

## 2. Test-first: red output against the pre-fix code

The guard tests were written before the policy existed. They deliberately used only the public CLI
surface and HTTP surface so the test project still compiled against the old binaries; the
`SessionSeedPolicy` boundary fact was added with the implementation because the type did not exist
yet.

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll SessionSeedGuardTests
```

Observed against the pre-fix build (log: `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/round13-evidence/red-guard-cli.log`):

```text
FAIL SessionSeedGuardTests.Evaluate_GivenWeakSeedWithAllowFlag_ExpectedExit0AndEvaluationWritten: EqualException: Assert.Equal() Failure: Values differ
Expected: 0
Actual:   2
FAIL SessionSeedGuardTests.Evaluate_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage: EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   0
FAIL SessionSeedGuardTests.Play_GivenWeakSeedWithAllowFlag_ExpectedExit0AndSessionWritten: EqualException: Assert.Equal() Failure: Values differ
Expected: 0
Actual:   2
FAIL SessionSeedGuardTests.Play_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage: EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   0
FAIL SessionSeedGuardTests.Train_GivenWeakSeedWithAllowFlag_ExpectedExit0: EqualException: Assert.Equal() Failure: Values differ
Expected: 0
Actual:   2
FAIL SessionSeedGuardTests.Train_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage: EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   0
TOTAL 9 PASSED 3 FAILED 6 SKIPPED 0
```

The pre-fix CLI accepted weak seeds silently (`Expected: 2 / Actual: 0`) and rejected the not-yet-
existing flag as an unknown option (`Expected: 0 / Actual: 2`).

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll TholdemWebViewerTests
```

Observed (log: `round13-evidence/red-guard-web.log`; the Kestrel request log is omitted):

```text
FAIL TholdemWebViewerTests.LiveMatchCreation_GivenWeakSeed_Expected400WeakSeedUnlessExplicitlyAllowed: EqualException: Assert.Equal() Failure: Values differ
Expected: BadRequest
Actual:   Created
FAIL TholdemWebViewerTests.ViewerAssets_GivenLiveForm_ExpectedStrongGeneratedSeedAndWeakSeedOptIn: ContainsException: Assert.Contains() Failure: Sub-string not found
String:    "<input id="seed" name="seed" type="number"··· 
Not found: "value="1099511627776""
TOTAL 12 PASSED 10 FAILED 2 SKIPPED 0
```

## 3. Implementation

### 3.1 CLI policy

New `WIP/Wip.Tholdem.Host/SessionSeedPolicy.cs`:

```csharp
public static class SessionSeedPolicy
{
    public const ulong MinimumSeed = 1_099_511_627_776UL;

    public static bool IsStrong(ulong seed) => seed >= MinimumSeed;

    internal static void RequireStrongOrExplicitOptIn(ulong seed, bool allowWeakSeed)
    {
        if (allowWeakSeed || IsStrong(seed))
        {
            return;
        }

        throw new CliUsageException(
            "seed " + seed.ToString(CultureInfo.InvariantCulture)
            + " is below the minimum " + MinimumSeed.ToString(CultureInfo.InvariantCulture)
            + "; use --allow-weak-seed for reproducible demos");
    }
}
```

`CliUsageException` already maps to exit code 2. `play`, `train` and `evaluate` register the
`--allow-weak-seed` flag (`TholdemCli.PlayFlags/TrainFlags/EvaluateFlags`) and call the guard after
parsing their options and before any I/O:

- `Commands/PlayCommand.cs` — after reading `seed`, `hands`, `agent0`, `agent1`;
- `Commands/TrainCommand.cs` — after reading `seed` and `iterations`;
- `Commands/EvaluateCommand.cs` — after reading the effective base seed.

`EvaluateCommand.DefaultBaseSeed` changed from `42UL` to `SessionSeedPolicy.MinimumSeed`. Leaving a
weak default would make the documented `evaluate` command refuse every run that omits `--seed`, which
is a broken default rather than a policy; a strong default keeps the command usable and the guard
meaningful. `--seed 42` still needs `--allow-weak-seed`.

`--help` now lists `[--allow-weak-seed]` on the three commands and explains the policy:

```text
  --allow-weak-seed  permit a seed below 1099511627776 for a reproducible demo or test

seeds:
  Sessions are dealt deterministically from --seed, so a guessable seed lets an observer
  enumerate candidates and reconstruct the deck. play, train and evaluate require seed >= 1099511627776
  unless --allow-weak-seed is given; weak seeds are for reproducible demos and tests only.
```

### 3.2 Web guard

New `WIP/Wip.Tholdem.Web/SessionSeedPolicy.cs` mirrors the CLI constant (the two projects do not
reference each other):

```csharp
public static class SessionSeedPolicy
{
    public const ulong MinimumSeed = 1_099_511_627_776UL;

    public static bool IsStrong(ulong seed) => seed >= MinimumSeed;
}
```

`StartLiveMatchRequest` gained `bool? AllowWeakSeed` and
`[property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]` on `Seed`. The attribute
is needed because the viewer form now sends a full 64-bit seed as a decimal string; a JSON number
would be rounded (and `2^64-1` would overflow) when parsed by JavaScript. The endpoint accepts both
numeric and string seeds.

`MapLiveRoutes` now checks the seed immediately after the body check:

```csharp
if (request.AllowWeakSeed is not true && !SessionSeedPolicy.IsStrong(request.Seed))
{
    return Results.BadRequest(
        new { error = "weak-seed", minimum = SessionSeedPolicy.MinimumSeed });
}
```

Observed response body: `{"error":"weak-seed","minimum":1099511627776}` with HTTP 400.

### 3.3 Viewer form

`wwwroot/viewer/index.html`:

- `#seed` keeps the constant strong default `value="1099511627776"` (also exposed as
  `data-minimum-seed`) so the markup alone is never weak;
- a new `#allow-weak-seed` checkbox, wired to the request as `allowWeakSeed`;
- a short hint that a random 64-bit seed is generated on load and that smaller seeds are guessable.

`wwwroot/viewer/app.js`:

- `MINIMUM_SEED = 1099511627776n` and `randomSeedText()` draw 64 bits from
  `crypto.getRandomValues(new Uint32Array(2))` with `BigInt`, bumping values below the minimum into
  range, and fall back to the constant only when `crypto` is unavailable;
- `initialize()` replaces the markup default with `randomSeedText()`;
- `startLiveMatch()` sends the seed as a decimal string (preserving all 64 bits), validates it as
  digits, includes `allowWeakSeed: byId('allow-weak-seed').checked`, and surfaces a specific message
  for the `weak-seed` 400 instead of a generic failure;
- `viewer.css` styles the hint and the checkbox row.

## 4. CLI guard matrix (observed)

Exact stderr lines were captured with `Start-Process -RedirectStandardError` so no PowerShell error-
record prefix appears. Evidence: `round13-evidence/cli-guard-messages.txt`, `cli-guard-matrix.log`.

| Command | Exit | Observed |
|---|---|---|
| `play --seed 42 --hands 1` | 2 | `tholdem: seed 42 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos` |
| `train --iterations 1 --seed 7` | 2 | `tholdem: seed 7 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos` |
| `evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 1 --hands 1 --seed 42` | 2 | `tholdem: seed 42 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos` |
| `play --seed 42 --hands 1 --allow-weak-seed` | 0 | session written |
| `train --iterations 1 --seed 7 --allow-weak-seed` | 0 | ledger + artifacts written |
| `evaluate … --seed 42 --allow-weak-seed` | 0 | CLI evaluation written |
| `play --seed 1099511627776 --hands 1` | 0 | session written |
| `train --iterations 1 --seed 1099511627776` | 0 | ledger + artifacts written |
| `evaluate … --seed 1099511627776` | 0 | CLI evaluation written |
| `evaluate …` (no `--seed`, default is the minimum) | 0 | CLI evaluation written |

Raw capture:

```text
reject-play exit=2
stdout-empty=True
stderr-first-line=tholdem: seed 42 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos
reject-train exit=2
stdout-empty=True
stderr-first-line=tholdem: seed 7 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos
reject-evaluate exit=2
stdout-empty=True
stderr-first-line=tholdem: seed 42 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos
```

Accepting runs (excerpts from `cli-guard-matrix.log`):

```text
=== play --seed 42 --allow-weak-seed
session: tholdem-c3d4ff060ef47ae4
seed: 42  hands: 1  manifest sha256: c5517a29fdf8875acdbcc60e9e80fda2b0b95926c894fa0aa3b4cb96ee88677b
...
exit=0

=== play --seed 1099511627776 (strong, no flag)
session: tholdem-88f28b70086c22cf
seed: 1099511627776  hands: 1  manifest sha256: 4f67cce2ab4985e8b2cb802563d139006f2baaac40d56bc55fde47b4a175e0ff
...
exit=0

=== train --seed 7 --allow-weak-seed
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-b155b98f promoted=false verified=true outcome=rejected meanBbPer100=0.00 lcb=0.00 seeds=24
final champion: tholdem-strategy-v1-f100e793
exit=0

=== train --seed 1099511627776 (strong, no flag)
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-0ceaa8fd promoted=false verified=true outcome=rejected meanBbPer100=0.63 lcb=-0.45 seeds=24
final champion: tholdem-strategy-v1-f100e793
exit=0

=== evaluate --seed 42 --allow-weak-seed
... decision: lcb-below-margin
verifier: verified (rejection-justified)
exit=0
```

No session directory was created by either rejected `play` run and no `ledger.json` by the rejected
`train` run (`cli-guard-matrix.log` plus the test assertions).

## 5. Viewer guard matrix over HTTP (observed)

The copied Release output was started on a free loopback port (see section 11 for the port-5099
note), and the four requests below were posted. Evidence:
`round13-evidence/viewer-guard-matrix.log`.

```text
=== seed 42, no allowWeakSeed
body: {"seed":42,"hands":1,"delayMs":0,"seat0Agent":"random-legal","seat1Agent":"random-legal"}
status=400
body={"error":"weak-seed","minimum":1099511627776}

=== seed 42, allowWeakSeed=true
body: {"seed":42,"hands":1,"delayMs":0,"seat0Agent":"random-legal","seat1Agent":"random-legal","allowWeakSeed":true}
status=201
body={"id":"7bd5afe219df3234","seed":42,"hands":1,"delayMs":0,"seat0Agent":"random-legal","seat1Agent":"random-legal"}

=== seed 1099511627776 (strong, field absent)
body: {"seed":1099511627776,"hands":1,"delayMs":0,"seat0Agent":"random-legal","seat1Agent":"random-legal"}
status=201
body={"id":"bcd4eb33530a909e","seed":1099511627776,"hands":1,"delayMs":0,"seat0Agent":"random-legal","seat1Agent":"random-legal"}

=== seed "18446744073709551615" (64-bit decimal string, field absent)
body: {"seed":"18446744073709551615","hands":1,"delayMs":0,"seat0Agent":"random-legal","seat1Agent":"random-legal"}
status=201
body={"id":"8ca2182515320a3a","seed":18446744073709551615,"hands":1,"delayMs":0,"seat0Agent":"random-legal","seat1Agent":"random-legal"}
```

## 6. Tests

New facts (12):

`WIP/Wip.Tholdem.Tests/Host/SessionSeedGuardTests.cs` (10):

1. `SessionSeedPolicy_GivenBoundary_ExpectedStrongOnlyAtMinimum` — exact constant, `IsStrong` at
   `0`, `MinimumSeed - 1`, `MinimumSeed`, `ulong.MaxValue`.
2. `Play_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage`
3. `Play_GivenWeakSeedWithAllowFlag_ExpectedExit0AndSessionWritten`
4. `Play_GivenStrongSeedWithoutAllowFlag_ExpectedExit0`
5. `Train_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage`
6. `Train_GivenWeakSeedWithAllowFlag_ExpectedExit0`
7. `Train_GivenStrongSeedWithoutAllowFlag_ExpectedExit0`
8. `Evaluate_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage`
9. `Evaluate_GivenWeakSeedWithAllowFlag_ExpectedExit0AndEvaluationWritten`
10. `Evaluate_GivenStrongSeedWithoutAllowFlag_ExpectedExit0`

`WIP/Wip.Tholdem.Tests/TholdemWebViewerTests.cs` (2):

11. `LiveMatchCreation_GivenWeakSeed_Expected400WeakSeedUnlessExplicitlyAllowed` — asserts the web
    policy mirrors the CLI bound, 400 `weak-seed` + minimum for seed 42 without the field, 201 with
    `allowWeakSeed: true`, 201 for the minimum seed without the field, and 201 for a numeric-string
    seed (the form's transport).
12. `ViewerAssets_GivenLiveForm_ExpectedStrongGeneratedSeedAndWeakSeedOptIn` — markup keeps
    `value="1099511627776"` (and no `value="42"`), contains `#allow-weak-seed`, and `app.js` contains
    `crypto.getRandomValues`, `randomSeedText`, the minimum and the `allowWeakSeed` wiring.

Updated existing tests (fixtures use small deterministic seeds on purpose; assertions unchanged):

| File | Change |
|---|---|
| `Host/PlayCommandTests.cs` | 4 `play` calls gained `--allow-weak-seed` |
| `Host/ReplayCommandTests.cs` | 7 `play` calls gained the flag |
| `Host/RetentionCommandTests.cs` | 2 `play` calls gained the flag |
| `Host/TrainCommandTests.cs` | 3 `train` calls gained the flag |
| `Host/EvaluateCommandTests.cs` | 2 `evaluate` calls with `--seed 123` gained the flag; the missing-challenger test uses the now-strong default |
| `Host/HelpCommandTests.cs` | asserts `--allow-weak-seed` and `1099511627776` in `--help` |
| `ViewerTestSupport.cs` | `StartLiveAsync` sends `allowWeakSeed = true` (with a comment that the production guard is covered by direct `POST /api/live` tests) |
| `TholdemWebViewerTests.cs` | the three invalid-request bodies now opt in, so they still exercise `unknown-agent` / `invalid-hands` rather than the new seed guard first |

`docs/plan/test-waivers.json` `expectedTotals["Wip.Tholdem.Tests"]`: `182` → `194`.

## 7. Documentation changes

### 7.1 Threat model (`docs/tholdem/known-limits.md`)

New `## Threat model` section (linked from `viewer.md`, `engine-rules.md` and `README.md`) states,
precisely:

- **(a)** no agent API or agent-side artifact exposes the session seed, the deck order, the undealt
  cards or the other seat's hole cards; session manifests and hand files are operator replay
  artifacts, never agent inputs;
- **(b)** because the deal is deterministic from the session seed, an adversary holding its own
  observation can enumerate candidate seeds and reconstruct the deck when the seed is guessable — no
  one-way digest protects this, the cards are the observable; includes the round-13 falsifier
  numbers;
- **(c)** `play`/`train`/`evaluate` and `POST /api/live` require seeds `>= 1,099,511,627,776`; weak
  seeds exist only behind `--allow-weak-seed` / `"allowWeakSeed": true`, for demos and tests;
- **(d)** an operator who disables or bypasses the guard is outside the secrecy guarantee.

The old absolute "non-invertible" sentences in `viewer.md` and `engine-rules.md` were replaced with
this precise claim (no agent input contains a stored value to invert; the deal is still
deterministic and a guessable seed is recoverable). A grep over `docs/tholdem` for
`invert|non-invert|reveals nothing|nothing an agent` now finds only the precise wording.

### 7.2 Documentation mismatches fixed

- **`training-promotion-runbook.md`**: step 4 now says `StrategyArtifact.Create` builds the
  challenger id/payload and `TrainingLoop` persists
  `<data>/strategies/<challengerId>.json` (previously it claimed `Create` persists the challenger;
  `TrainingLoop.Run` does the write).
- **`strategy-artifact-schema.md`**: the `sessions/<sessionId>` path is now attributed to
  `PlayCommand`, which stages `sessions/.inprogress` and moves it; `MatchSimulator.Run` writes only
  into the `HistoryDirectory` it is given and never constructs a session path.

### 7.3 Examples regenerated / made runnable

- The train example now passes `--allow-weak-seed` (demo seed `7`); its three iteration lines and
  final champion are byte-identical to the previous document.
- The seed-777 `session.json` / `hand-00001.json` examples are now explicitly labelled as coming
  from `play --seed 777 --hands 2 --allow-weak-seed`; the artifacts are byte-identical.
- `evaluate` documents the new default `--seed 1099511627776`; the runbook evaluation command needs
  no flag. The CLI evaluation JSON example was regenerated because that default changed
  (`challengerWins`/`incumbentWins`/`ties`: `2/2/4` → `4/4/0`).
- All absolute `C:\work\…` example data directories became `artifacts\tholdem-run`, so the commands
  are runnable from the repository root as the runbook's opening sentence already claimed.
- `README.md` usage text and seed-policy paragraph updated; `viewer.md` documents the random default
  seed, the checkbox, the 400 `weak-seed` response and the numeric-string seed transport.

## 8. Verification (observed output)

All commands were run from the repository root in the harness PowerShell host (Windows PowerShell
5.1.26100.4202). `dotnet test` was not run.

### 8.1 Final solution build

```powershell
dotnet build Wip.slnx --configuration Release --no-incremental
```

```text
    126 Warning(s)
    0 Error(s)

Time Elapsed 00:01:35.06
exit: 0
```

The warnings are the pre-existing solution warnings; the Tholdem projects build with 0 warnings. (A
plain incremental `dotnet build Wip.slnx --configuration Release` also exited 0.)

### 8.2 Full Tholdem suite (supplementary runner)

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
TOTAL 194 PASSED 194 FAILED 0 SKIPPED 0
exit: 0
```

### 8.3 Architecture runner and project count

`tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs:16` asserts `Assert.Equal(48, graph.Count)`.

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
```

```text
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
exit: 0
```

### 8.4 Solution coverage

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
```

```text
solution=Wip.slnx
wip_project_count=48
solution_wip_project_count=48
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
exit: 0
```

### 8.5 HarnessSync

```powershell
dotnet run --project tools/HarnessSync -- --check
```

```text
exit: 0
```

(no output)

### 8.6 CLI help

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe --help
```

Key lines observed (full log `round13-evidence/help.txt`):

```text
  tholdem play --seed <n> [--hands <n>=200] [--allow-weak-seed]
  tholdem train --iterations <n> --seed <n> [--allow-weak-seed]
               [--seed <n>=1099511627776] [--allow-weak-seed]
  --allow-weak-seed  permit a seed below 1099511627776 for a reproducible demo or test
seeds:
  Sessions are dealt deterministically from --seed, so a guessable seed lets an observer
  enumerate candidates and reconstruct the deck. play, train and evaluate require seed >= 1099511627776
  unless --allow-weak-seed is given; weak seeds are for reproducible demos and tests only.
exit=0
```

### 8.7 Documented commands run verbatim

Every CLI command block in `docs/tholdem/` was executed verbatim from a fresh
`artifacts\tholdem-run` (`round13-evidence/doc-examples.log`). Observed:

```text
=== runbook 1. train
PS> ... train --iterations 3 --seed 7 --allow-weak-seed --data-dir artifacts\tholdem-run
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-b155b98f promoted=false verified=true outcome=rejected meanBbPer100=0.00 lcb=0.00 seeds=24
iteration=2 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-e1ba56f7 promoted=false verified=true outcome=rejected meanBbPer100=1.67 lcb=-0.76 seeds=24
iteration=3 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-ea96d95d promoted=false verified=true outcome=rejected meanBbPer100=-1.25 lcb=-3.40 seeds=24
final champion: tholdem-strategy-v1-f100e793
exit=0

=== runbook 2. evaluate
PS> ... evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 24 --data-dir artifacts\tholdem-run
challenger: tholdem-strategy-v2-b155b98f  incumbent: tholdem-strategy-v1-f100e793  seeds: 24  hands per match: 10
mean bb/100: 0.00  LCB: 0.00  margin: 5.00
decision: lcb-below-margin
verifier: verified (rejection-justified)
exit=0

=== runbook 3. promote
PS> ... promote --challenger tholdem-strategy-v2-b155b98f --yes --data-dir artifacts\tholdem-run
promotion refused: lcb-below-margin
verifier: rejection-justified
exit=3

=== schema session example: play
PS> ... play --seed 777 --hands 2 --allow-weak-seed --data-dir artifacts\tholdem-run
session: tholdem-f275b4ee69be0557
seed: 777  hands: 2  manifest sha256: ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee
exit=0

=== schema CLI evaluation example
PS> ... evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 4 --data-dir artifacts\tholdem-run
mean bb/100: 0.00  LCB: 0.00  margin: 5.00
decision: lcb-below-margin
verifier: verified (rejection-justified)
exit=0
```

`promote` without a justifying report exits 3 by design (documented in the runbook), so the exit-3
above is the documented successful refusal, not a failed command.

The seven JSON examples in `strategy-artifact-schema.md` were compared byte-for-byte with the
regenerated files:

```text
cli-example-in-doc=True
session-example-in-doc=True
hand1-example-in-doc=True
v1-example-in-doc=True
ledger-first-in-doc=True
training-report-in-doc=True
pointer-in-doc=True
```

The document's "real `train --iterations 1 --seed 7 --allow-weak-seed` run" was also reproduced
exactly with a fresh directory; the version-1 strategy, the whole `ledger.json` array, the training
evaluation report and `active-champion.json` all matched the document byte-for-byte:

```text
train1-v1-in-doc=True
train1-ledger-exact-array-in-doc=True
train1-report-in-doc=True
train1-pointer-in-doc=True
```

The viewer run procedure (`viewer.md`) was executed as written for the build and copy steps; the
copied DLL was then started with the same arguments on a free port (section 11). The probe of the
served assets returned:

```text
copied-viewer-ready=True
status-body={"mode":"spectator","dataDirectory":"C:\\Users\\ricar\\dev\\Modus\\artifacts\\tholdem-run","sessions":1,"strategies":4,"liveMatches":0,"offline":true}
viewer-status=200
served-default-seed=True
served-weak-optin=True
served-random-seed-js=True
served-weak-seed-js=True
```

(The host was started on port `5102` rather than the documented `5099` because a stale viewer from a
previous round owns `5099`; see section 11. The command itself is the documented one.)

### 8.8 Viewer seed helper executed

Node (`v24.16.0`) extracted the real `randomSeedText()` from `app.js` and called it 10,000 times
(`round13-evidence/seed-helper-check.txt`):

```text
samples=10000 distinct=10000
minimum=3717484665634501
maximum=18446404753719652005
all>=1099511627776=true
64-bit=true
fallback=1099511627776
```

The fallback was measured by temporarily removing `globalThis.crypto`.

### 8.9 `app.js` syntax

```powershell
node --check WIP\Wip.Tholdem.Web\wwwroot\viewer\app.js
```

```text
node-check-exit=0
```

## 9. Mutation: remove the CLI guard, observe the guard test fail, restore

The guard all three CLI commands call is
`SessionSeedPolicy.RequireStrongOrExplicitOptIn`; the mutation emptied that method so weak seeds were
accepted silently.

Original file hash (`Get-FileHash -Algorithm SHA256`):

```text
ORIGINAL SHA256: 8D9672B60BB5CD11D57F63BFA8685578D556BEA20982E36108E40C2AA65D09FA
backup-bytes-equal: True
```

Temporary mutation:

```csharp
internal static void RequireStrongOrExplicitOptIn(ulong seed, bool allowWeakSeed)
{
    // MUTATION (round 13): the CLI guard is removed so a weak seed is accepted silently.
    _ = seed;
    _ = allowWeakSeed;
}
```

```text
MUTANT SHA256: AA11BE1A0619ECCA2C9EFEDC0DB48400A7D980F20D09F09A1FEDB052CA50672D

Build succeeded.
    0 Error(s)

FAIL SessionSeedGuardTests.Evaluate_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage: EqualException: Assert.Equal() Failure: Values differ
FAIL SessionSeedGuardTests.Play_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage: EqualException: Assert.Equal() Failure: Values differ
FAIL SessionSeedGuardTests.Train_GivenWeakSeedWithoutAllowFlag_ExpectedExit2WithMinimumMessage: EqualException: Assert.Equal() Failure: Values differ
TOTAL 10 PASSED 7 FAILED 3 SKIPPED 0
```

Restore from the byte backup, hash check, forced rebuild (`--no-incremental`) and rerun:

```text
RESTORED SHA256: 8D9672B60BB5CD11D57F63BFA8685578D556BEA20982E36108E40C2AA65D09FA
restored-equals-original: True

Build succeeded.
    0 Error(s)

TOTAL 10 PASSED 10 FAILED 0 SKIPPED 0
```

The final solution build and full suite above were rerun after the restore, so they describe the
restored tree.

## 10. Files changed

Product:

- `WIP/Wip.Tholdem.Host/SessionSeedPolicy.cs` (new)
- `WIP/Wip.Tholdem.Host/TholdemCli.cs` (flags, usage text)
- `WIP/Wip.Tholdem.Host/Commands/PlayCommand.cs`
- `WIP/Wip.Tholdem.Host/Commands/TrainCommand.cs`
- `WIP/Wip.Tholdem.Host/Commands/EvaluateCommand.cs` (guard + strong default)
- `WIP/Wip.Tholdem.Web/SessionSeedPolicy.cs` (new mirror)
- `WIP/Wip.Tholdem.Web/TholdemWebHost.cs` (400 `weak-seed`)
- `WIP/Wip.Tholdem.Web/Live/SpectatorSnapshot.cs` (`AllowWeakSeed`, string-number seed reading)
- `WIP/Wip.Tholdem.Web/wwwroot/viewer/index.html`
- `WIP/Wip.Tholdem.Web/wwwroot/viewer/app.js`
- `WIP/Wip.Tholdem.Web/wwwroot/viewer/viewer.css`

Tests:

- `WIP/Wip.Tholdem.Tests/Host/SessionSeedGuardTests.cs` (new)
- `WIP/Wip.Tholdem.Tests/TholdemWebViewerTests.cs`
- `WIP/Wip.Tholdem.Tests/ViewerTestSupport.cs`
- `WIP/Wip.Tholdem.Tests/Host/{Play,Train,Evaluate,Replay,Retention,Help}CommandTests.cs`

Docs:

- `docs/tholdem/{known-limits,viewer,engine-rules,training-promotion-runbook,strategy-artifact-schema,README}.md`
- `docs/plan/test-waivers.json`
- this file

No engine, history, agents, learning, `Wip.slnx`, architecture-test or `.github` file was modified.

## 11. Not fixed / limits of this round

- **Entry-point guard only.** `TrainingLoop.Run`, `MatchSimulator.Run`, `SimulationOptions` and
  `LiveMatchRegistry.Start` still accept any seed, because the engine, simulator and learning loop
  are out of scope and must not change. An embedder that calls those APIs directly bypasses the
  policy; the threat model therefore scopes the guarantee to the CLI and viewer entry points.
- **`evaluate`'s default seed changed** from `42` to `1099511627776`. A CLI evaluation that omits
  `--seed` now evaluates different held-out hands and writes different report numbers than before.
  This is documented in the runbook and reflected in the regenerated CLI evaluation example; it is
  the price of not shipping a default that the new policy would refuse.
- **The documented viewer command could not bind port 5099 on this machine.** A pre-existing viewer
  started on 2026-11-06 18:55 (PID 22960,
  `dotnet Wip.Tholdem.Web.dll --urls http://127.0.0.1:5099 --data-dir C:\Users\ricar\dev\Modus\artifacts\tholdem`)
  still owns `127.0.0.1:5099`. It was not started by this round and was not killed. The documented
  build/copy steps and the same copied host were verified on `http://127.0.0.1:5102`; on a machine
  where 5099 is free the command works unchanged. (The first probe of 5099 accidentally hit the
  stale server, which served the old assets and data directory; that result is discarded.)
- **Browser UI not exercised end-to-end.** No browser was driven. The form contract is covered by
  markup/JS assertions in the test suite, by serving the real assets from the copied host, and by
  executing the extracted seed helper in Node; that is not the same as a click-through test.
- **`dotnet test` not used.** Local VSTest is unusable in this session (Win32Exception 5 at testhost
  startup, documented in `known-limits.md`); the supplementary runner over the compiled test
  assembly is the evidence. CI was not triggered or observed.
- **Historical round documents were not rewritten.** `docs/goal-tholdem/round-{1..12}/*`,
  `journal.md` and `evidence.md` remain frozen history; the round-13 claim lives in the product docs
  and in this report.
- **Where the evidence and demo artifacts live.** Test scratch and every captured log live under
  `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/` (`test-temp/`, `round13-guard/`, `round13-viewer*/`,
  `round13-evidence/`), never `%TEMP%`. The documented demo commands wrote their product artifacts to
  the documented, git-ignored `artifacts\tholdem-run`, and the documented viewer copy lives in the
  git-ignored `.tmp\tholdem-viewer` — neither the viewer nor any host was run in place from
  `bin/Release`.
- **The mutation covered the shared policy helper, not each command separately.** Emptying
  `RequireStrongOrExplicitOptIn` removes the guard for `play`, `train` and `evaluate` at once, and the
  three per-command guard facts failed together, which is the behaviour this round promises.
