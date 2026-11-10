# Live-artifact mirror — durable copies

> The operator-run live artifacts originally live under `.tmp/goal-llm-harness/` (scratch). These
> byte-identical copies under `docs/goal-llm-harness/` keep the D2/D5/D6 audit surface durable if the
> scratch directory is cleaned. Both the source and the copy are hashed below; `match` is the
> comparison of the two SHA-256 values. The `.tmp` originals remain the operator-run source of truth
> for the recorded tests; the copies are the audit surface for a later reader.

| Source | Durable copy | Bytes | SHA-256 | Match |
|---|---|---:|---|---|
| `.tmp/goal-llm-harness/arms/arm-llm/llm-transcripts.jsonl` | `docs/goal-llm-harness/round-3/live-artifacts/llm-arm-transcripts.jsonl` | 30069 | `41e78a085d903a8696bd19498390aebbad4081f3cd205846e920df0b3b13f917` | true |
| `.tmp/goal-llm-harness/arms/arm-llm/active-champion.json` | `docs/goal-llm-harness/round-3/live-artifacts/llm-arm-active-champion.json` | 59 | `ba24279777d1c52c3219fef7e9184df0e15313d2a6cc28cfd2c96868358ad805` | true |
| `.tmp/goal-llm-harness/arms/arm-deterministic/active-champion.json` | `docs/goal-llm-harness/round-3/live-artifacts/deterministic-arm-active-champion.json` | 59 | `ba24279777d1c52c3219fef7e9184df0e15313d2a6cc28cfd2c96868358ad805` | true |
| `.tmp/goal-llm-harness/ponder/live/ponder-session.json` | `docs/goal-llm-harness/round-4/live-artifacts/ponder-session.json` | 120069 | `881604aff8e043a372aeb9727679911d70e7848b74021d65301daba00aabe0a9` | true |
| `.tmp/goal-llm-harness/ponder/live/llm-transcripts.jsonl` | `docs/goal-llm-harness/round-4/live-artifacts/ponder-transcripts.jsonl` | 105887 | `9f9325228f4a2143a56c76f39eb2d915e6d432b4c619669fe7d9b70cb0e588ec` | true |
| `.tmp/goal-llm-harness/ponder/live2/ponder-session.json` | `docs/goal-llm-harness/round-4/live-artifacts/ponder-session-live2.json` | 148266 | `b74d08cbbe13337dd6e230267129cc313dfd78317ca902b5a598812dafb14b1c` | true |
| `.tmp/goal-llm-harness/ponder/live2/llm-transcripts.jsonl` | `docs/goal-llm-harness/round-4/live-artifacts/ponder-transcripts-live2.jsonl` | 131505 | `4c178a383a4289b1be147884123271b847af3260f85bd0ed6de3230e4abc0f64` | true |
| `.tmp/goal-llm-harness/ponder/v-required/ponder-session.json` | `docs/goal-llm-harness/round-4/live-artifacts/ponder-session-winner.json` | 457563 | `ba9d2e84c91a6dd441d4e7df5aa9d8af0e8c4e1a7f7807517480a4b0f05fad62` | true |
| `.tmp/goal-llm-harness/ponder/v-required/llm-transcripts.jsonl` | `docs/goal-llm-harness/round-4/live-artifacts/ponder-transcripts-winner.jsonl` | 415085 | `aff0cf15f2cbbec5cb19b30c55cdbc86f51da0aaa253572287321fa276f5ca88` | true |

## What each copy proves

- **`llm-arm-transcripts.jsonl`** — the ten real refinement calls: requested/served model, prompt and
  response hashes, usage, latency, outcome.
- **`llm-arm-active-champion.json`** and **`deterministic-arm-active-champion.json`** — the champion
  pointer bytes before and after both arms (`ba242797…`, `tholdem-strategy-v1-c9d1a24b`).
- **`ponder-session.json`** — the 20-hand live match: 33 turns, 33 calls, action mix, streets,
  zero tool calls, latency and usage.
- **`ponder-transcripts.jsonl`** — the 33 canonical model-call records for the Ponder match.

## Regenerate the comparison

```powershell
Get-FileHash -Algorithm SHA256 <source>, <copy>
```

The copies were made by copying the source files without transformation; the hash equality above was
computed with `Get-FileHash -Algorithm SHA256` on both sides. No key appears in any copied artifact.
