# Goal — Bones v3 Learning Loop Falsification and Repair

- **One-sentence outcome**: Every safety, measurement, and reproducibility claim in the v3 Bones delivery is independently verified or corrected; unbounded runs are refused; an infinite-loop generated script cannot hang an iteration indefinitely; the remaining behavior-proof gates are either green or have a concrete, evidenced blocker; and the frozen benchmark claims reproduce on this machine.

## Definition of done (falsifiable)

| # | Dimension | Check | Expected observable |
|---|---|---|---|
| D1 | v3 benchmark claims reproduce | Run the benchmark arena and gate commands on the recorded seeds | 65 strategies, 0 mismatches vs a repeat arena, gate delta vs OR baseline within the reported CI |
| D2 | Unbounded runs are refused | Start the host with `MaxGamesPerRun=0` and no explicit opt-in (stub provider, temp data dir) | Startup refuses or the launcher throws; no learning iteration starts |
| D3 | Infinite-loop script cannot hang an iteration indefinitely | Add/run a test with an infinite-loop `IBonesPlayerSlot` in the match path | The match/iteration fails within a configured deadline; the test process returns |
| D4 | Behavior-proof gates green or concrete blocker | Run the script-strategies and reliability compliance gates | Both pass, or an OQ names the exact missing executable test and why it cannot be added without weakening |
| D5 | No regression | `dotnet build` host + benchmark; `Wip.Bones.Tests`; focused host suites | Builds exit 0; Bones failures ≤ the known gate failures; focused host 116/116 or better |
| D6 | Docs truthful | Read v3 plan, host README, requirements addendum | Claims match observed state; no stale test counts or "green" claims for failing gates |

## Hard constraints

- No API key is available; no paid DeepSeek calls.
- Never run `MaxGamesPerRun=0` or checked-in defaults for an experiment.
- Protect `WIP/Wip.Bones.Host/bones-data`; use fresh `.tmp/...` data dirs.
- No unbounded loops; stop hosts; clean up jobs.
- Do not weaken, skip, or delete tests/assertions to make a gate pass.
- Preserve deterministic seeds and the frozen benchmark protocol.

## Stop criteria

- All six dimensions are Met, or
- Three rounds have run and each remaining unmet dimension has an OQ/RISK with concrete evidence, or
- The same blocking condition persists for three rounds (then `blocked` with the condition named).

Round cap: 4.
