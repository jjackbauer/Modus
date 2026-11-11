# LLM attribution pilot (v1) — proposals parsed, gate rejected all

**Status:** pilot, not an attribution claim. Live DeepSeek was exercised for the first time.

- 4 tasks x 4 iterations = **16 requests**; all 16 responses parsed into bounded genomes.
- Each proposal was evaluated through the four-seat gate against its task start on a small plan
  (4 holdout blocks x 2 hands per seat match + anchor): **0 VerifiedImprovement**, 8 Rejected,
  8 Unsafe. Mean range -875.31..690.94 bb/100 (small-n, high variance).
- No non-LLM control arm was run on this same candidate budget, so this is **not** an attribution
  comparison and no learning claim is made.
- CAP-002: the live model is reachable and the parser accepts its outputs; the key was never printed.

Files: `probe.json` (16 proposals), `gated.json` (gate verdicts), `probe.log`, `gate.log`.

**Interpretation:** the live proposal process produces parseable candidates, but none cleared the
gate in this pilot; half were unsafe at the anchor. A controlled equal-budget comparison remains
open.
