# Goal — Tholdem positive-evidence signal floor

- **One-sentence outcome**: A positive-evidence signal floor in the Tholdem signature-driven refinement policy is evaluated with a pre-registered dev tuning pass and a fresh, never-used holdout, and the shipped policy either adopts a floor that preserves exploration while removing arbitrary zero-rate edits, or keeps the current floor-0 policy with the rejection recorded.

## Definition of done (falsifiable)

| # | Dimension | Check | Expected observable |
|---|---|---|---|
| F1 | Floor option | Build and run the focused policy tests | `SignatureDrivenRefinementPolicy` and `TrainingOptions` expose a bounded `SignalRateFloor` (default 0); floor 0 preserves every pinned challenger and the 387-test baseline; floor 0.10 rejects rate 0/0.05 signals and selects a 0.20 signal; an out-of-range floor throws |
| F2 | Pre-registration | Read `round-1/experiment-design.md` | Floors `0.00, 0.05, 0.10, 0.20`, the six DEV seeds, the selection rule and the fresh holdout seeds are fixed before any floor result is observed |
| F3 | DEV tuning | Run the floor probe on six DEV seeds for baseline + four floors | 150 raw rows, 0 errors; per-floor divergence, zero-effect and promotions; the pre-registered selection rule picks one floor or explicitly picks 0 |
| F4 | Fresh holdout | Run baseline, floor 0 and the chosen floor on six never-used seeds | 90 raw rows, one attempt; chosen arm vs baseline divergence ratio >= 1.5 with paired 95% bootstrap lower bound > 1.0; zero-effect not higher than baseline; chosen floor non-inferior to floor 0 on the same holdout seeds (median ratio >= 0.9) and zero-effect not higher than floor 0 |
| F5 | Decision and report | Read the final report | If a floor is adopted, the default stays unchanged unless a new DEC justifies it; if no floor beats 0, the shipped policy is retained and the rejection is recorded; all claims trace to raw rows |

## Pre-registration

- **DEV seeds** (tuning; already used for the original experiment, reused only for development):
  `1099511627776 + i * 1000` for `i = 0..5`.
- **Fresh holdout seeds** (never used in this session):
  `1099511827776 + i * 1000` for `i = 0..5`.
- **Arms during dev**: `baseline` and `refinement` with `SignalRateFloor` `0.00`, `0.05`, `0.10`, `0.20`.
- **Arms during holdout**: `baseline`, `refinement` with floor 0, and `refinement` with the single floor chosen on dev. Floor 0 is included on the same fresh seeds so the pre-registered non-inferiority comparison is computed directly; it is not a second experimental use of the holdout.
- **Settings**: 5 iterations, 120 training hands, 24 evaluation seeds, 10 evaluation hands per match; default criteria.
- **Primary metric**: decision divergence per iteration, as in `docs/goal-tholdem-harness/round-1/experiment-design.md` — same evaluation base seed, 120 probe hands, element-wise `handHashes` comparison between champion and challenger.
- **Secondary metrics**: zero-effect evaluation rate (`|mean| < 0.05` and `|lcb| < 0.05`), verified promotions, mean bb/100.
- **Selection rule (pre-registered)**: among positive floors, choose the smallest floor that (a) has a zero-effect rate no higher than floor 0, (b) has a median divergence at least 0.9x floor 0's median, and (c) has no promotion advantage claim. If none qualifies, choose floor 0.
- **Holdout success**: chosen floor vs baseline median ratio >= 1.5, paired 95% bootstrap lower bound > 1.0, zero-effect not higher than baseline; and chosen floor vs floor 0 median ratio >= 0.9 with zero-effect not higher than floor 0. If the chosen floor is 0, the holdout is a fresh replication of the original claim under the same rule.
- **No p-hacking**: the holdout is run once after the floor is chosen; no floor is added after seeing the holdout.

## Hard constraints

- Modify only `WIP/Wip.Tholdem.Agents/Learning/Refinement/SignatureDrivenRefinementPolicy.cs`, `TrainingOptions.cs`, `TrainingLoop.cs` (minimal pass-through), new tests under `WIP/Wip.Tholdem.Tests/Learning/`, and the goal/probe scratch under `docs/goal-tholdem-floor/` and `.tmp/goal-refinement/**`.
- Do not change the default `SignalRateFloor = 0` behavior, the five frozen gate components, `FrozenComponents`, the meta-loop decision, or any existing test.
- Legacy mode stays byte-identical; the 387-test baseline must remain green.
- No model/network/process/compile on the training path; no clock/GUID in artifacts.
- Offline only; no release, publish, merge or deploy; no CI-green claim.

## Stop criteria

- F1–F5 Covered at High confidence, zero Falsified delivery claims, zero P0 OQ; round cap **6**. If the holdout fails, the floor is rejected and the result is reported honestly.
