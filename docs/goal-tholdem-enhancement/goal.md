# Goal — WiP four-player Hold'em enhancement benchmark

**Slug:** `goal-tholdem-enhancement`
**Repository:** `C:\\Users\\ricar\\dev\\Modus` (branch `WIP`, commit `11eca9e83602b8ae485c815788d1e8b3df271a5e`)
**Created:** 2026-11-10
**Campaign phase now active:** Milestone A (freeze baseline) then Milestone B (trustworthy four-player evaluation).

## One-sentence goal

Turn WiP Texas Hold'em into a scientifically defensible four-player strategic-learning testbed: four
independent seats by default, a pre-registered seat-rotated paired bb/100 promotion gate with
block-level confidence and independent recomputation, multiway equity, a frozen diverse opponent pool,
and equal-budget LLM ablations — each claim backed by a verified artifact or an honest null result,
with the legacy two-seat engine byte-identical.

## User request (verbatim excerpts)

> **Target:** Four independent, individually scored agents playing no-limit Texas Hold'em, while
> preserving the existing two-to-six-seat engine

> **Delivery objective:** A challenger may be called *strategically improved* only if its measured
> payoff is better on a pre-registered, four-player, seat-rotated, held-out evaluation **and** it
> clears regression guards against diverse, unseen opponents. WiP may be called *better at learning*
> only if its proposal process outperforms equal-budget, non-LLM controls over multiple starting
> strategies and evaluation tasks.

> **Preserve:** the existing engine and two-seat compatibility, deterministic seeds and canonical
> history, local-first execution, explicit permissions, frozen independent verification, no execution
> of arbitrary model-authored strategy code, and no automatic merge/deployment.

> **Mode choice:** default evaluation consists of independent 100-big-blind cash-game hands with
> stacks reset between hands, matching the current simulator. Persistent-stack tournaments are a
> later separate benchmark, never silently mixed with cash-game metrics.

Full requirement index: [source-requirements.md](source-requirements.md).

## Named mechanisms — intent anchor table

| # | Mechanism (user's words, abbreviated) | Status | Where the goal carries it |
|---|---|---|---|
| A1 | "Make **four independent players** the default learning, evaluation, and promotion environment" (SIM-001) | KEPT | D1, D6; four-seat `GameConfiguration` and evaluator |
| A2 | "replacing **exactly one focal seat** at a time while freezing the other opponents" (EVA-002) | KEPT | D6 evaluator algorithm |
| A3 | "**paired net chip EV in bb/100**, computed from per-hand differences" (EVA-005) | KEPT | D6 report metric |
| A4 | "confidence bounds at the independent *seed block*" (EVA-006) | KEPT | D6 block bootstrap + normal CI |
| A5 | "pre-registered minimum meaningful improvement ... before any candidate search" (EVA-008) | KEPT | D6 plan hash + pre-registration artifact |
| A6 | "a versioned, frozen opponent pool with at least tight-passive, tight-aggressive, loose-aggressive, calling-station, and adversarial-raiser" (OPP-001) | KEPT | D7 |
| A7 | "Extend Monte Carlo equity ... to the exact number of live eligible opponents (1–5)" (EQ-002) | KEPT | D3 (later round) |
| A8 | "expected pot share" (EQ-003) | KEPT | D3 (later round) |
| A9 | "Require ablation against a no-LLM search with the same grid/compute budget" (LLM-004) | KEPT | D9 (later round) |
| A10 | "Make the promotion decision independently recomputable" (EVA-010) | KEPT | D6 verifier |
| A11 | "introduce a versioned four-player v2 gate without mutating the historical heads-up frozen v1 gate" (ART-004) | KEPT | D11 / separate v2 namespace |
| A12 | "never promote simply to show progress" (EVA-011) | KEPT | D6 `no-improvement` verdict |
| A13 | "preserving the existing two-to-six-seat engine" / "Keep exact two-seat historical replay compatibility" (Target, SIM-008) | KEPT | D1: TST-001 + SIM-008 explicit checks (446/446 legacy suite, v1 golden replay) |

No mechanism is REPLACED or REMOVED. No user-owned scope decision is pending. R2–R4 are later
milestones of this same goal, not exclusions.

## Definition of done

Dimensions D1–D12 mirror the release claims and P0 requirement groups. A dimension is **Covered**
only when every P0 row in it has: a stable contract, a test that can fail, observed four-player
execution evidence where the claim is behavioural, and a hashed manifest. "Partial" means at least
one P0 row has a verified artifact. `None` means no verified artifact.

| # | Dimension | P0 rows | Falsifiable check (command → expected observable) |
|---|---|---|---|
| D1 | Four-seat simulation contract and legacy preservation | SIM-001..006, TST-001, SIM-008 | `RunXunit ... GameConfigurationTests` → `FAILED 0`; a 4-seat run manifest declares seats/blinds/stacks/mode; ante>0 and tournament mode are refused with codes; full legacy suite is 446/446 (or 446 + only the new tests), and the two-seat `HistorySessionCompatibilityTests` golden replay is unchanged |
| D2 | Observation truthfulness | OBS-001..005 | `RunXunit ... DecisionContextTests` → FAILED 0; missing VPIP serializes as null/NotMeasured, never 0 |
| D3 | Equity separation and multiway equity | EQ-001..006 | `RunXunit ... MultiwayEquityTests` → FAILED 0; N-opponent expected pot share matches independent enumeration within tolerance |
| D4 | Policy semantics | POL-001..008 | `RunXunit ... PolicyTests` → FAILED 0; same hand, different position/situation → different legal action; pot-relative sizings distinct |
| D5 | Causal diagnostics | DIA-001..005 | `RunXunit ... FailureSignatureTests` → FAILED 0; losing +EV call not labelled an error; 0/0 ≠ 0/20 |
| D6 | Paired four-seat evaluator and pre-registered gate | EVA-001..011 | `RunXunit ... PopulationEvaluatorTests` + `PromotionGateV2Tests` → FAILED 0; self-comparison = 0.0 exactly; dominated control negative; forced promotion refused; the plan hash (EVA-008) is persisted before candidates and a missing/mismatched plan is Unverified; selection/holdout/audit seed domains are disjoint and overlap is refused; anchor non-regression is checked |
| D7 | Frozen opponent population | OPP-001..003 | `RunXunit ... OpponentPoolTests` → FAILED 0; 5 archetypes have distinct measured profiles; pool hash pinned; held-out pool separate |
| D8 | Semantic no-op gate | MUT-001..004 | `RunXunit ... EffectiveIdentityTests` → FAILED 0; sizing plateau and ignored skill fields hash equal / rejected as inert |
| D9 | LLM attribution and ablation | LLM-001..005 | equal-budget ablation report with no-LLM control and policy hashes; no claim without it |
| D10 | Meta-learning objectives | META-001..004 | meta-promotion needs multi-task matrix and fresh holdout; control outcomes reproducible |
| D11 | Reproducibility, safety, governance | ART-001..007, ART-012, RUN-001..002 | report contains game/evaluator/pool/policy/seed hashes; no-overlap validator fails closed; v1 gate untouched; ART-005/006 source scan plus a deny-test show no generated-code execution and no automatic pointer promotion |
| D12 | Operator visibility | UX-001..003 | four-seat viewer/report shows seats, actions, EV/CI, provenance |

### Baseline commands (this machine)

`dotnet test` is unusable here: the VSTest testhost aborts with `Win32Exception (5): Access is denied`
(confirmed 2026-11-10). Reproducible test command:

```
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release --   WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll <TypeNameFilter>
```

Expected observable: `TOTAL n PASSED n FAILED 0 SKIPPED 0`. CI remains the authoritative gate; this
runner only observes the same compiled assembly locally.

## Constraints (gates, with provenance)

| # | Constraint | Provenance |
|---|---|---|
| C1 | Preserve the existing 2–6 seat engine and exact two-seat historical replay; no pre-existing golden hash changes | user (Preserve) |
| C2 | Deterministic seeds, canonical history, no clock/ambient randomness in evaluated paths | user |
| C3 | Data-only strategies; never execute model-authored code; no shell/network elevation | user |
| C4 | No automatic merge/deployment; production pointer changes need operator approval | user |
| C5 | Do not mutate the frozen heads-up v1 gate or its hashes; v2 is additive and versioned | user (ART-004) |
| C6 | Local-first: default evaluation runs with no model credentials | user |
| C7 | Text files are UTF-8 without BOM; `HarnessSync --check` must pass | repo |
| C8 | Use the compiled-assembly `RunXunit` runner for local test evidence, not `dotnet test` | repo/environment |
| C9 | Do not touch Bones, unrelated modules, or generated stubs in this campaign | repo/agent |
| C10 | No scope widening: implement only rows declared in the round objective | agent |

## Stop criteria

- **S1 (phase done):** D6 and D7 are Covered and the Milestone B exit evidence is reproduced by the
  orchestrator: (a) policy vs itself yields exactly 0.0 paired difference; (b) a known stronger
  scripted challenger beats a known dominated incumbent with a positive lower bound on
  pre-registered four-seat data; (c) a forced-false promotion is refused by the independent verifier;
  (d) the report is byte-identical on a cold re-run.
- **S2 (full goal):** D1–D12 Covered with R1–R4 each carrying an explicit demonstrated-or-null verdict.
- **S3 (cap):** 10 harness rounds.
- **S4 (blocked):** the same concrete blocking condition persists for 3 consecutive rounds.

Phase gate calibration: G3 (coverage) is evaluated against the dimensions the active milestone
claims. Dimensions outside the active milestone are listed as `None` and cannot be reported as
covered; they do not fail G3 while the campaign is still in Milestone A/B. This is recorded as
DEC-002.
