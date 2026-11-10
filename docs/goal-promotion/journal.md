# Goal Journal — tholdem-promotion-experiment

> Append-only. See the stretch-goal-harness evidence protocol for entry format.

---

<!-- New entries go below this line -->

### OBS-001: The live-model ponder path is unavailable

- **Source**: environment check in this session
- **Fact**: `THOLDEM_LIVE_MODEL` is unset and `DEEPSEEK_API_KEY` is unset. The learning loop is deterministic and does not call a model; the runtime agent's live path is gated and not part of training.
- **Implications**: "Full ponder" in this experiment means the deep deterministic budget and the full refinement stack (all signatures, skill edits, memory, lineage), not an LLM call.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: DEC-001

### DEC-001: Run a two-arm promotion experiment from the documented 0.70/0.70 demo start

- **Decision**: Pre-register a legacy arm and a full-refinement+skills arm from the 0.70/0.70 start with a deep budget; verify any promotion through the frozen gate; report a no-promotion result honestly if neither arm promotes.
- **Rationale**: OBS-001; the 0.70/0.70 start is the documented promotion demo, and the two arms answer whether refinement changes the promotion outcome.
- **Alternatives considered**: Use the default v1 (rejected: previous runs show it rarely clears the 5 bb/100 margin); call a live model (rejected: no key, no model path in the learning loop); lower the margin or seeds (rejected: that would weaken the gate).
- **Related**: OBS-001

### RISK-001: Neither arm promotes under the frozen gate

- **Risk**: The 0.70/0.70 start with signature edits may not clear 5 bb/100 even with 50 seeds; the experiment would be a valid no-promotion result, not a success.
- **Likelihood**: Medium
- **Impact**: Medium
- **Evidence**: The documented demo used the deterministic proposer and 50 seeds; the signature policy may choose a different edit.
- **Mitigation**: Report the best LCB/mean and exact refusal; do not lower the gate or rerun for a favourable result. A no-promotion result still tests the gate and the refinement path.
- **Gate**: —
