# Harness State — goal-tholdem-enhancement

- **Round**: 27 (guarded proposal comparison)
- **Goal**: scientifically defensible four-player Hold'em benchmark preserving the 2–6-seat engine
- **Active milestone**: C started (equity); B complete
- **Definition of done**: 2/12 dimensions Covered (D6, D7); D1-D5, D8, D11 Partial; anchor 3 null; D9, D10, D12 None
- **Stop criteria**: S1 **met**; S2 unmet; **S3 round cap met (10/10)**; S4 not blocked

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G0 Intent | PASS | Re-audit: 15 KEPT / 0 REPLACED / 0 REMOVED, QUESTION: NONE (EVD-009) |
| G1 Evidence | PASS | EVD-001..071 resolve to commands/lines that were checked |
| G2 Falsification | PASS | VAL-001 supported; VAL-002 falsified a drafting claim and repaired it |
| G3 Coverage | PASS | D6/D7 Covered; D1/D2/D3/D4/D8/D11 Partial; evaluate4 CLI added; no dimension regressed |
| G4 Reproduction | PASS | Baseline 446/446 reproduced by the orchestrator |
| G5 Honesty | PASS | VSTest limitation and the weak default genome are recorded |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Contract goal, freeze baseline, audit intent | Contract | 2 | 9 | 2 | G0✓ G1✓ G2✓ G3✓ G4✓ G5✓ | baseline 446/446; audit 15/15 KEPT |
| 1 | Four-seat evaluation contract (GameConfiguration, RunDetailed, SeedDomain, PlanV2) | Implementer | 3 | 5 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | landed: 22 new tests, suite 468/468 |
| 2 | Frozen pool + block statistics + paired evaluator | Implementer | 2 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | 10 new tests; self-zero; dominated LCB +57.96; suite 484/484 |
| 3 | Statistical gate + independent verifier | Implementer | 2 | 1 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | 11 new tests; suite 495/495 |
| 4 | Pre-registered control experiment | Experiment | 0 | 3 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | VerifiedImprovement, verifier true, byte-identical rerun |
| 5 | Typed equity + N-opponent pot share + tool wiring | Implementer | 2 | 4 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | exhaustive oracle delta 0.0017; 12/12 equity tests; suite 507/507 |
| 6 | Range-conditioned sampling + confidence-gated decisions | Implementer | 2 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | AA-only vs uniform delta 0.642; suite 513/513 |
| 7 | Equal-budget proposer ablation + real 4-task run | Implementer + experiment | 1 | 2 | 1 | G1✓ G2✓ G3✓ G4✓ G5✓ | null: no-benefit; Greedy 0 verified, Random 1/4 + 3 unsafe; suite 525/525 |
| 8 | Contribution/side-pot-aware call EV | Implementer | 1 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | side-pot EV 5.0, break-even 0.32; suite 531/531 |
| 9 | Equity-aware policy + pot-relative sizing | Implementer | 1 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | legal, deterministic, distinct pot targets; policy passive (19/22 fold/check); suite 540/540 |
| 10 | Observation totals + action history, canonical bytes preserved | Implementer | 1 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | probe byte-identical; suite 544/544; one approved surface-name update |
| 11 | Measured VPIP with nullability and denominators | Implementer | 1 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | 0/10 and 5/10 measured; missing -> null; suite 549/549; two authorized test updates |
| 12 | Preflop situations + call-EV integrated into the equity policy | Implementer | 1 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | side-pot bridge 280/EV -10; classifier 4/4; suite 556/556 |
| 13 | Persisted plan JSON + additive evaluate4 CLI | Implementer | 1 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | strict round-trip; report/gate/verifier via CLI; suite 566/566 |
| 14 | Equity-policy artifact + CLI agent kind | Implementer | 1 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | policy hash in report; strategy path unchanged; suite 570/570 |
| 15 | Pre-registered equity-policy vs legacy experiment | Experiment | 0 | 2 | 1 | G1✓ G2✓ G3✓ G4✓ G5✓ | HYP-002 falsified: -93.83 primary, gate Unsafe; verifier true |
| 16 | Final consolidation and release-claim assessment | Contract | 0 | 0 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | R1 substantially delivered; R2/R4 null; R3 untested; goal active |
| 17 | Four-seat training/promotion loop + train4 CLI (item 1) | Implementer | 1 | 4 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | inert rejected; isolated four-seat pointer; no v1 pointer; suite 575/575 |
| 18 | DIA-004/DIA-005 diagnostics repair (item 3a) | Implementer | 1 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | fold 1/1 and 1/3; Observed; suite 578/578 |
| 19 | Diagnosis-driven proposer adapter (item 2, partial) | Implementer | 1 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | call 0.45->0.37; deterministic fallback; suite 582/582 |
| 20 | Diagnosis-driven four-seat proposals + pilot | Implementer/Experiment | 1 | 3 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | call 0.37 from injected signal; capture; pilot 0 promotions both arms; suite 584/584 |
| 21 | Diagnosis equal-budget ablation + 16-request LLM pilot | Experiment | 0 | 3 | 1 | G1✓ G2✓ G3✓ G4✓ G5✓ | diagnosis no-benefit (0/16, 7 unsafe); LLM 16/16 parsed, 0 verified; no claims |
| 22 | LLM vs non-LLM equal-budget proposal comparison | Experiment | 0 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | no-benefit: LLM 0/11, random 0/15, grid 0/15, greedy 0/7 |
| 23 | Pre-registered diagnosis replication (v2r) | Experiment | 0 | 1 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | no-benefit: off 0/48 (7 unsafe), on 0/48 (20 unsafe) |
| 24 | Policy-family feasibility probe | Experiment | 0 | 1 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | 1/144 verified (+150.625/+71.452); top-mean Unsafe; existence only |
| 25 | Target-reachability probe | Experiment | 0 | 1 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | deterministic 1/8 (+66.09/+11.67); diagnosis 0/8 (7 unsafe) |
| 26 | Signature-guarded 0.02-step proposer | Implementer | 1 | 1 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | call 0.45->0.43; deterministic fallback; suite 588/588 |
| 27 | Guarded vs control proposal comparison | Experiment | 0 | 1 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | no-benefit: guarded 0/22, deterministic 0/15, random 0/32, grid 0/28 |

## Campaign status

The user directed a new segment: (1) four-seat training/promotion, (2) diagnosis-driven proposer for
an anchor-3 retry, (3) D5 diagnostics and LLM attribution. Item 1 is delivered; the diagnosis and
guarded proposers are wired and tested but **no-benefit** against controls; DIA-004/005 are repaired;
the 16-request LLM comparison is **no-benefit**. The family probe found one gate-passing genome in
144, and reachability found deterministic 1/8 vs diagnosis 0/8; the family is viable, the proposers
are not. Anchor 3 remains null; no LLM or learning claim.