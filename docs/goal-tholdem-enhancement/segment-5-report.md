# Segment report — LLM vs control proposal comparison (round 22)

**Outcome:** `no-benefit`. No attribution or learning claim.

Pre-registration `f91724dd…`; four tasks x four candidates x five arms on identical plans.

| Arm | Verified | Unsafe |
|---|---|---|
| LLM | **0** | 11 |
| Random | 0 | 15 |
| Grid | 0 | 15 |
| Greedy | 0 | 7 |
| NoSearch | 0 | 0 |

The rule required the LLM arm to have >=2 verified, 0 unsafe, and strictly more verified than each
control. It fails all three; no arm cleared the gate.

**Limits:** small budget (4 candidates/task, 4 blocks x 2 hands); this is a controlled pilot, not a
definitive attribution experiment. The LLM proposals were the 16 stored responses; no new calls.

**State:** anchor 3 remains null; R3 (LLM value) is not demonstrated. Goal active.
