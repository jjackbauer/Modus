# Harness State — Bones agents on the generic WIP agent runtime (final)

- **Round**: 7 of 12 — complete. The goal is closed; no release claim was made.
- **Goal**: The four Bones domain agents run on `IChatModel` → `IAgentExecutor` → `RuntimeToolInvoker` → `IAgentMemory`, with the private DeepSeek stack test-only, parity and fail-closed controls proven, the legacy workflow shim removed, and both CI workflows green.
- **Definition of done**: **8/8 met** (D1–D8).
- **Stop criteria**: **met** at code+docs HEAD `724d3a6` (then the final records commit, docs-only).

## Final verification

| Item | Result |
|---|---|
| D1 adapter | `dad2a72` — 14/14 focused; control fails 6/14; CI green (`42233605796`) |
| D2 Ponder | `3e0087a` — 13/13 focused; parity + both controls; full Bones 423/420/3 waived; CI green (`42242055107`) |
| D3 Enhance | `409b6f9` — 10/10 focused; parity + both controls; full Bones 433/430/3 waived; CI green (`42249467590`) |
| D4 provider retirement | `d7d9723` — production grep clean; architecture rule + real-edge control; host composition 1/1; CI green (`42267670768`) |
| D5 safety | `7158280` — 9/9 safety; tool-artifact redaction red→green (10/10); full Bones 455/454/1 waived / Runtime 152/146/6 documented; CI green (`42277560560`) |
| D6 legacy shim removal | `724d3a6` — legacy grep 0 hits; declarative-only runtime/builder tests; Shell 34/34, Builder 34/34, Bones 455/454/1 waived, Runtime 153/147/6 documented, architecture 4/4 |
| D7 suites | Full Bones/Runtime/Shell/Builder + architecture reproduced; real counts recorded (Bones 455, Runtime 153, Shell 34, Builder 34, provider 48, host composition 1/1); waiver floors intentionally left at 396/146 (regression floors; the concurrent Tholdem hunk remains uncommitted) |
| D8 CI | **Tests CI `42289775138` — all six jobs success; Modus CI `42289774965` — success at `724d3a6`** |

Real counts vs declared floors: the offline gate enforced the declared floors (`Wip.Bones.Tests` 396,
`Wip.Runtime.Tests` 146, `Wip.Shell.Tests` 34) and passed at the higher real counts (455 / 153 / 34);
the counts are recorded in EVD-128/EVD-131. No waiver was added, removed or relaxed; the three
Bones meta-tests and the documented platform failures are the pre-existing waived set.

## Residual risks (recorded, non-blocking)

- **RISK-008**: the DeepSeek `thinking` parameter is not part of the generic `IChatModel` contract, so
  a non-default `Thinking` setting is parsed/diagnosed but not applied (default `disabled` unchanged).
- **RISK-009**: first-party tools (the Bones compile capability) are trusted by the permission model;
  the deny/audit mechanism is proven with a plugin-origin descriptor, which is the shape a third-party
  tool would use.
- The `BonesViewerWorkflowTests` failure is pre-existing (reproduced on the pre-WP6 clone) and outside
  the 16-project offline gate; the viewer-stage flake remains a pre-existing CI risk.
- The concurrent Tholdem session still has uncommitted changes in the shared tree; they were excluded
  from every commit in this run.

## Final HEADs and records

- Code+docs: `724d3a623fab91aae09095f31f8401fe9c97ec7b` — both workflows green.
- Work packages: `dad2a72` → `3e0087a` → `409b6f9` → `d7d9723` → `7158280` → `724d3a6`.
- Evidence: `goal.md`, `journal.md` (OBS-030..043, VAL-018..027, DEC-018..025, TODO-001..008,
  RISK-003..009), `evidence.md` (EVD-086..131), `deliverables.md` (ART-012..018), `round-1/`..`round-6/`.
- **No release claim; no publish, sign or deploy was performed.**
