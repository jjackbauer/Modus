# Goal — Wip.Tholdem: deterministic heads-up Hold'em with a gated learning loop

- **One-sentence outcome**: On branch `WIP`, `Wip.Tholdem` plays deterministic, seed-reproducible heads-up no-limit Texas Hold'em through baseline bots and an LLM-backed runtime agent, and a learning loop can propose strategy changes that are promoted *only* when a statistically gated, reproducible challenger evaluation clears the incumbent by the configured margin — with a negative control proving the gate cannot be bypassed.

## Definition of done (falsifiable)

| # | Dimension | Check | Expected observable |
|---|---|---|---|
| D1 | Engine determinism and legality | `dotnet run --project WIP/Wip.Tholdem.Host -- replay <history>` twice and `tholdem play --seed 42` twice | Byte-identical hand-history JSON and identical SHA-256 hash both times; illegal actions rejected with a deterministic error; min-raise and all-in rules covered by tests |
| D2 | Evaluator correctness | Run `Wip.Tholdem.Tests` evaluator + legality + property suites | Known 7-card vectors classify correctly (straight flush > quads > full house > flush > straight > trips > two pair > pair > high card, plus wheel and kicker cases); 52-card uniqueness; chip conservation per hand; replay determinism |
| D3 | Baselines, simulator, metrics | Play N hands between fixed agents with fixed seed, twice | Histories byte-identical across runs; per-agent bb/100, win rate, aggression and showdown rate reported with confidence intervals |
| D4 | Runtime agent and tool boundary | Scripted `IChatModel` drives `IAgentExecutor` → `RuntimeToolInvoker` → `ArtifactAgentMemory`; agent sees only `PlayerObservation` | Equity (seeded Monte Carlo), pot-odds, hand-history (current session only) and opponent-stats tools return; budget stops the loop; malformed/illegal action rejected deterministically; no deck, opponent hole cards or future board is reachable from the agent surface |
| D5 | Learning loop and promotion gate | Run `tholdem train --iterations k` and `tholdem evaluate --challenger <id>` on held-out seeds | Strategy artifacts persist as data (parameters/thresholds/prompt text); champion/challenger ledger records evaluations; promotion requires lower confidence bound > incumbent + margin and sufficient sample size; an always-promote control **fails** the gate test |
| D6 | Host, operations, docs | Run `tholdem play/train/evaluate/promote/replay --help` and a full offline session; read docs | All five commands work, `promote` demands explicit confirmation, artifacts + retention status/purge work, no secrets in logs; `docs/tholdem/` documents engine rules, strategy artifact schema, training/promotion runbook, known limits |
| D7 | Security controls | Run the security suites | Hidden-info leak test, tool-permission denial test, prompt-injection (history/name/model output) test, and offline/no-network test all pass; untrusted history text cannot alter tool permissions or execute code |
| D8 | Integration and CI surface | `dotnet build Wip.slnx -c Release`, architecture tests, solution-coverage script, waiver/composition review | Build exit 0; architecture count equals the real WIP project count with new Tholdem rules; `verify-solution-coverage.ps1` exit 0; `test-waivers.json` expectedTotals match real counts with zero new waivers; offline/security CI surface lists the new suite; no release/publish/deploy claim |
| D9 | Visualization UI (added at user request) | Start `Wip.Tholdem.Web` on loopback, drive its HTTP surface and open the page in headless Chromium | `/viewer/` renders a poker table (two seats, stacks, pot, board, action log, controls); a live match started by `POST /api/live` advances and completes with both hole cards visible to the spectator; recorded sessions replay with a scrubber; `/api/strategies` and `/api/ledger` list recorded artifacts and promotion decisions (empty state when none); no external network references in the viewer assets; the spectator snapshot type is not reachable from any agent surface |

## Hard constraints

- No release claim, no publish, sign, or deploy; no push to a remote as part of delivery.
- No real-money gambling, accounts, or external betting APIs; offline by default; no telemetry.
- Live-model path gated behind an environment variable; the key is never printed; live tests are skipped by default.
- Determinism is contractual: same seed + same agents ⇒ byte-identical histories.
- The engine must reject illegal actions and must never expose hidden information through any agent API or artifact.
- Hand histories, opponent names and model outputs are untrusted data: never executed, never able to change tool permissions, observations truncated.
- Waivers stay zero for the new projects; no existing test may be weakened, skipped, or deleted.
- `Wip.Tholdem*` casing is fixed; do not introduce `Wip.Holdem`.
- Do not modify `WIP/Wip.Bones*`, `WIP/Wip.Shell*`, `WIP/Wip.Runtime`, or Bones behaviour to make Tholdem pass.

## Environments and known limits

- Local Windows VSTest aborts with `Win32Exception (5)` (documented at `docs/runbooks/operator-guide.md:109`, `docs/testing/test-status.md:17-18`). Local verification therefore uses `dotnet build`, the supplementary runner `.tmp/tholdem-verify/RunXunit`, and direct CLI/HTTP execution; CI remains the authoritative test gate.
- No `gh` CLI and no GitHub token on this machine: `Tests CI`/`Modus CI` cannot be triggered or observed. This is reported as an outstanding item, never as green.
- Playwright Chromium 1155 is installed locally (`%LOCALAPPDATA%\ms-playwright`), so the visualization UI can be opened in a real headless browser and screenshotted as evidence. The browser check stays outside the committed test suite (the suite drives the real HTTP surface); the screenshot is orchestrator evidence.

## Stop criteria

- All nine dimensions are `Covered` with `High` confidence evidence, **and** zero `Falsified` rows, **and** zero open `P0` questions.
- Or the same blocking condition persists for three rounds (then `blocked` with the condition named).
- Round cap: **12** (raised from 10 when the user added the UI enhancement + independent review + remediation cycle to the scope; see DEC-010).
