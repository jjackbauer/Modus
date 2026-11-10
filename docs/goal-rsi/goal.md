# Goal — Wip.Tholdem mini-RSI (bounded meta-loop, UI-triggered)

- **One-sentence outcome**: the Tholdem learning loop is startable and cancellable from the loopback viewer, a bounded meta-loop can evolve only the proposer policy and prompt template as data under the frozen statistics gate, and every claimed defense against A1–A15 has a committed control test that fails when the defense is removed (or an explicit, evidenced "not fully preventable" residual).

## Definition of done (falsifiable)

| # | Dimension | Check | Expected observable |
|---|---|---|---|
| R1 | Training API | `POST /api/training` with the documented body; poll `GET /api/training/{jobId}`; cancel with `DELETE` | 202 `{jobId, acceptedAtUtc, optionsHash}`; deterministic jobId from optionsHash; 400 field error per bound (iterations 1..50, trainingHands 1..2000, evalSeeds 2..500, evalHands 1..50, margin 0..100, minSeeds 2..1000); 409 while another job runs; progress visible within 500 ms; DELETE ends `cancelled` with no promotion |
| R2 | Server-verified promote | `POST /api/training/{jobId}/promote {confirm:true}` | 409 without confirm, for incomplete jobs, for unverified reports and for a report whose persisted bytes were tampered after completion; success only when the server re-reads the artifact, recomputes the report and re-runs `PromotionVerifier`; champion pointer then names the challenger |
| R3 | Mini-RSI | `ProposerGenome`, `MetaArtifact`, frozen manifest, meta evaluation over disjoint meta seeds, hit-rate CI, fresh-holdout ratchet, hard budgets, cancellation | identical proposer not eligible; frozen-manifest mismatch refuses; a candidate that wins on meta seeds and loses on fresh seeds is not eligible; hit-rate CI matches literal arithmetic; total inner iterations / wall clock / per-episode evaluation caps enforced; cancel leaves the proposer pointer unchanged |
| R4 | Threat model | `docs/tholdem/rsi-threat-model.md` with A1–A15 rows | each row has severity, attack, why vulnerable, defense, committed test name, residual; each defense test fails with the defense removed (mutation log) or states the honest limit |
| R5 | UI | Training tab in the viewer | form (iterations, training hands, eval seeds/hands, margin, min seeds), Start/Cancel, live phase timeline (champion → challenger, LCB vs margin, promoted/rejected), artifact links, champion card; Promote only for completed+verified jobs and behind an explicit confirm; no external assets; artifact text via `textContent`; page stays usable while a job runs |
| R6 | Regression | existing suite + integration gates | 275 pre-existing tests green with unchanged expectations; solution build 0 errors; architecture 4/4 at 48 projects; coverage 48/48; `HarnessSync --check` exit 0 |
| R7 | Evidence | real loopback run + determinism + mutation | `POST /api/training`, poll to complete, promote with confirm on a real Kestrel process; artifact files and ledger entry shown; same options in two data dirs produce identical artifact hashes; every A-control has a recorded removed-defense → red run and a restored hash-verified run |

## Hard constraints

- Modify only: `WIP/Wip.Tholdem.Agents/Learning/**`, `WIP/Wip.Tholdem.Web/**`, `WIP/Wip.Tholdem.Tests/**`, `docs/tholdem/**`, `docs/plan/test-waivers.json`, `docs/goal-rsi/**`.
- Do not touch the engine, history, simulator, N-seat behaviour, `Wip.slnx`, architecture tests or `.github/`. No new project (48 stays 48).
- The evaluator, gate, verifier and seed derivation are frozen: their canonical source and behaviour are hashed into a manifest and re-checked every meta-iteration. The meta-loop never grades itself, never executes model-authored code, and never changes the promotion criteria.
- Offline only; loopback only; no publish/merge/deploy; no real money; no secrets in output; no claim of CI green.
- Do not weaken, skip or delete an existing test. Any changed existing expectation is listed with its justification.

## Environments and known limits (carried)

- Local `dotnet test` aborts with `Win32Exception (5)`; use `dotnet build` plus `.tmp/tholdem-verify/RunXunit`.
- CI cannot be triggered or observed from this machine; no CI green is claimed.
- The viewer must run from a copied build output, never from `bin/Release`.
- The Tholdem tree is untracked on branch `WIP`; "committed" means present in the repository working tree, not published. State this in the final report.

## Stop criteria

- R1–R7 covered at High confidence, zero open falsified rows, zero P0 open questions; cap 8 rounds.
