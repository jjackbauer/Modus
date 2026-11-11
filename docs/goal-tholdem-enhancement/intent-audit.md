# Intent audit — goal-tholdem-enhancement

| # | Mechanism | Verdict | Proposal location | Citation | Consequence |
|---|---|---|---|---|---|
| 1 | Four independent players as default learning/evaluation/promotion environment (SIM-001) | KEPT | D1 | D1 rows "SIM-001..006, TST-001, SIM-008"; check: "a 4-seat run manifest declares seats/blinds/stacks/mode" | Gated by D1. |
| 2 | Focal-seat challenger/incumbent replacement, other opponents frozen (EVA-002) | KEPT | D6 | D6 rows "EVA-001..011"; check "self-comparison = 0.0 exactly" | Gated by D6. |
| 3 | Paired net chip EV in bb/100 from per-hand differences (EVA-005) | KEPT | D6 | D6 rows include "EVA-005"; "Paired four-seat evaluator" | Gated by D6. |
| 4 | Confidence bounds at the independent seed block (EVA-006) | KEPT | D6 | D6 rows include "EVA-006"; "block bootstrap + normal CI" (A4) | Gated by D6. |
| 5 | Pre-registered minimum effect before candidate search (EVA-008) | KEPT | D6 | D6 rows "EVA-001..011"; check "the plan hash (EVA-008) is persisted before candidates and a missing/mismatched plan is Unverified" | Gated by D6; restored. |
| 6 | Frozen versioned opponent pool with 5 archetypes (OPP-001) | KEPT | D7 | D7 rows "OPP-001..003"; check "5 archetypes ... pool hash pinned" | Gated by D7. |
| 7 | Held-out opponent identities, not only deck seeds (OPP-003) | KEPT | D7 | D7 rows include "OPP-003"; check "held-out pool separate" | Gated by D7; not listed in anchor table. |
| 8 | Multiway Monte Carlo over exact opponent count (EQ-002) | KEPT | D3 | D3 rows "EQ-001..006"; check "N-opponent expected pot share matches independent enumeration" | Gated by D3 (part of this goal, S2). |
| 9 | Expected pot share for ties/multiway (EQ-003) | KEPT | D3 | D3 rows include "EQ-003"; same enumeration check | Gated by D3. |
| 10 | Equal-budget ablation against no-LLM control (LLM-004) | KEPT | D9 | D9 rows "LLM-001..005"; check "equal-budget ablation report with no-LLM control" | Gated by D9. |
| 11 | Independently recomputable promotion decision / verifier (EVA-010) | KEPT | D6 | D6 rows include "EVA-010"; check "forced promotion refused" | Gated by D6. |
| 12 | Versioned v2 gate without mutating frozen heads-up v1 (ART-004) | KEPT | D11; C5 | D11 rows include "ART-004"; check "v1 gate untouched" | Gated by D11. |
| 13 | Legitimate no-improvement result, never promote for progress (EVA-011) | KEPT | D6 | D6 rows include "EVA-011"; check "forced promotion refused" | Gated by D6. |
| 14 | Preserve 2–6 seat engine and exact two-seat replay (TST-001, SIM-008) | KEPT | D1 | D1 rows "SIM-001..006, TST-001, SIM-008"; check "full legacy suite is 446/446 (or 446 + only the new tests), and the two-seat HistorySessionCompatibilityTests golden replay is unchanged" | Gated by D1; restored. |
| 15 | Data-only strategies, no model code execution, no auto deployment (ART-005, ART-006) | KEPT | D11; C3, C4 | D11 check "ART-005/006 source scan plus a deny-test show no generated-code execution and no automatic pointer promotion" | Gated by D11; dedicated deny-test now named. |

KEPT 15 / REPLACED 0 / REMOVED 0

QUESTION: NONE
