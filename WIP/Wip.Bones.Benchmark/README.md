# Wip.Bones.Benchmark

Deterministic held-out benchmark and promotion-rule experiments for the Bones learning loop.

This tool exists so learning changes can be judged by **held-out playing strength**, not by
promotion rate, iteration count, or token throughput.

## What it does

| Command | Purpose |
|---|---|
| `history` | Scans a Bones data directory's session artifacts and writes a content-keyed snapshot plus exact-content candidate/incumbent pairs. Read-only. |
| `arena` | Plays a fixed-seed arena for every study strategy. Strategies rotate through all four seats; opponents and archetypes are fixed. Reports win rate (Wilson 95% CI) and mean score differential (95% CI). |
| `gate` | Runs promotion-gate arms over the historical candidate pairs and reports selected held-out strength, promotion rate, true/false positives, and cost. `--arena` supplies the scored truth strengths; optional `--opponents-arena` supplies the per-seat opponent field (for example dev opponents scored against held-out truth). Without `--opponents-arena`, both come from `--arena`. |
| `smoke` | Fast throughput probe. |
| `match` | Cross-process determinism probe: prints a transcript hash for a fixed strategy/seed. |

## Reproducibility contract

- Seeds come from `BonesSeedDerivation` (FNV-1a + SplitMix64), never `HashCode`.
- The opponent field is fixed for every strategy.
- `gate` prints both the truth arena label and the opponents arena label, so a run records
  whether dev or held-out strategies generated the opponent field.
- `arena --master-seed X` repeated in two processes produces identical wins and score
  differentials.
- Synthetic `deterministic-seed-*` fallback scripts from pre-v3 runs are excluded because their
  source contains process-randomized `System.HashCode`.

## Typical study

```powershell
$exe = "WIP/Wip.Bones.Benchmark/bin/Debug/net10.0/Wip.Bones.Benchmark.exe"

& $exe history --data WIP/Wip.Bones.Host/bones-data --out .tmp/bench/history.json

& $exe arena --snapshot .tmp/bench/history.json --out .tmp/bench/dev.json \
    --games 2000 --master-seed 910001 --label dev --target 12

& $exe arena --snapshot .tmp/bench/history.json --out .tmp/bench/heldout.json \
    --games 2000 --master-seed 910002 --label heldout --target 12

& $exe gate --snapshot .tmp/bench/history.json --arena .tmp/bench/heldout.json \
    --opponents-arena .tmp/bench/dev.json \
    --out .tmp/bench/gate.json --reps 200 --matches 8 --seed 700001 --target 12
```

Use **dev** seeds to choose a design and **held-out** seeds only to report the result.
Do not tune on the held-out split. Selecting the per-seat opponent field from the dev arena
(`--opponents-arena`) while scoring truth from the held-out arena (`--arena`) keeps the
evaluation design independent of the reported truth.

## Reading the result

The gate study's primary quantity is `MeanSelectedScoreDifferential`: the held-out strength of
the script the gate would keep. Compare it against `NeverPromoteMeanFinalScoreDifferential`
(the incumbent) and `AlwaysPromoteMeanFinalScoreDifferential` (the candidate). A gate that
"improves over the old gate" but remains below never-promote is not improving the library; it
is only reducing harm. `DeltaVsBaseline` is paired by replication and includes a 95% CI.

Historical artifact caveat: pre-v3 runs were not reproducible (randomized seeds), and the same
strategy ID can label different content across sessions. The snapshot keys strategies by
`<id>@<source-hash>` for this reason.

## Limits

- The opponent field is historical; absolute score levels are field-specific. Compare deltas,
  not absolutes, across studies.
- 2,000 games/strategy gives a mean 95% CI width of about 2.4 score points and 3.8 percentage
  points win rate. Use more games for final claims.
- The tool runs model-authored C# in-process, like the host. Do not run untrusted scripts with it
  until the process-isolation work in the v3 roadmap lands.
