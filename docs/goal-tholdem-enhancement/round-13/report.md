# Round 13 report — persisted plans and the additive evaluate4 CLI

**Status:** verified D1 slice; D1 remains Partial because training is still heads-up. Goal active.

## Delivered

- `EvaluationPlanV2Json`: strict Write/Parse with canonical round-trip, seed re-derivation and four
  named refusal codes.
- `evaluate4` CLI: `--plan`, `--challenger`, `--incumbent`, optional `--anchor-plan`, `--out`,
  `--json`, `--data-dir`. It writes the canonical paired report and, with an anchor, the gate
  decision and independently recomputed verification. No pointer write.

## Verified

| Check | Observed |
|---|---|
| Plan round-trip | hash `baf6dddd6ad508775483a71c65d1b2f997a424d587de602598bbe93b7e3a8973`; whitespace/extra key/missing domain/seed mismatch refused |
| evaluate4 report | `planHash 63eb2c96...`, **4 units**, `report-920f9dcb.json`, no BOM/trailing newline |
| Anchor gate | decision `Unsafe / unsafe-anchor-regression`; verifier `verifier:verified` |
| Refusals | unknown pool -> exit 4; unknown composition -> exit 4; malformed plan -> exit 4 |
| Pointer | `active-champion.json` absent after success runs |
| Tests / suite / build | 5/5 CLI, **566/566**, 0 errors |

## Limits

- `train` still defaults to heads-up, so SIM-001 is not fully met; `evaluate4` is additive evidence
  of the four-seat surface, not a replacement training loop.
- The test plan is tiny (2/2/4/2 blocks) and its anchor verdict is Unsafe; this is a mechanism test,
  not a strategy result.
- Anchor 3 remains the round-7 null.

## Next

A pre-registered promotion experiment of the equity-aware policy against the legacy policy on the
holdout pool, executed through `evaluate4` with an anchor plan, and recorded with its honest verdict
(including a null). Learning competence still requires the richer proposer ablation.
