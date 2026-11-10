# Deliverables — Wip.Tholdem mini-RSI

| Artifact | Path | Evidence | Confidence |
|---|---|---|---|
| Training job API, queue, seed register, promote | `WIP/Wip.Tholdem.Web/Training/**` | EVD-004, EVD-005, EVD-008, EVD-009 | High |
| Mini-RSI core (proposer genome, artifacts, frozen manifest, meta loop, budgets) | `WIP/Wip.Tholdem.Agents/Learning/**` | EVD-007, EVD-008, EVD-009 | High |
| Training tab (form, timeline, promote, champion card) | `WIP/Wip.Tholdem.Web/wwwroot/viewer/{index.html,app.js,viewer.css}` | EVD-010, EVD-011 | High |
| Training API / promote / mini-RSI tests | `WIP/Wip.Tholdem.Tests/Training/**` | EVD-008, EVD-009 | High |
| Meta-loop and control tests | `WIP/Wip.Tholdem.Tests/Learning/MetaLoopTests.cs` | EVD-008, EVD-009 | High |
| Threat model A1–A15 | `docs/tholdem/rsi-threat-model.md` | mutation evidence + residual statements | High |
| How to run / claims / limits | `docs/tholdem/rsi.md` | EVD-004…011 | High |
| Viewer documentation update | `docs/tholdem/viewer.md`, `docs/tholdem/README.md` | review | High |
| Screenshots: tab, complete, promoted, running, 390 px, mini-RSI form/complete | `docs/goal-rsi/round-1/*.png` | EVD-010, EVD-011, EVD-018 | High |
| Mutation evidence | `docs/goal-rsi/round-1/mutation-evidence.md` + `run-mutations.ps1` | EVD-012, EVD-013: 11 controls red-then-green, all restores SHA-256 verified | High |
| Harness memory | `docs/goal-rsi/{goal,journal,evidence,state}.md` | this file | High |
