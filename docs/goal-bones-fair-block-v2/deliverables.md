# Deliverables — bones-fair-block-v2

### ART-001: Goal contract

- **Path**: `docs/goal-bones-fair-block-v2/goal.md`
- **Type**: document
- **Supporting evidence**: EVD-001, EVD-006, EVD-010
- **Confidence**: High
- **Verified by**: Re-read the file; every definition-of-done row names a command that exists in `.tmp/fair-block-v2/Program.cs` (`sweep`, `promote`, `verify`) and an observable that was actually printed
- **Last updated**: 2026-11-01

### ART-002: Append-only journal

- **Path**: `docs/goal-bones-fair-block-v2/journal.md`
- **Type**: document
- **Supporting evidence**: EVD-005, EVD-013, EVD-014, EVD-015, EVD-016, EVD-017
- **Confidence**: High
- **Verified by**: Cross-checked every numeric claim in OBS/VAL/DEC against the corresponding `result-*.json` and console transcript; HYP-002/003/004/005 statuses match VAL-001/004/002/005
- **Last updated**: 2026-11-01

### ART-003: Verified-fact table

- **Path**: `docs/goal-bones-fair-block-v2/evidence.md`
- **Type**: document
- **Supporting evidence**: EVD-001..EVD-019 (self-registering)
- **Confidence**: High
- **Verified by**: Each row's Source was opened or its command re-run during round 6; the two-proportion intervals in EVD-006/007/008 were recomputed independently from the raw win counts
- **Last updated**: 2026-11-01

### ART-004: Round snapshot

- **Path**: `docs/goal-bones-fair-block-v2/state.md`
- **Type**: document
- **Supporting evidence**: EVD-006, EVD-007, EVD-008, EVD-014, EVD-015, EVD-016, EVD-017
- **Confidence**: High
- **Verified by**: The approach-verdict table was rebuilt from the single `approaches.json` run output, not from memory
- **Last updated**: 2026-11-01

### ART-005: Search and measurement harness

- **Path**: `.tmp/fair-block-v2/` (`FairBlockV2.csproj`, `Program.cs`, `Audit.cs`, `Variants.cs`, `specs/`)
- **Type**: benchmark
- **Supporting evidence**: EVD-004, EVD-005, EVD-012, EVD-013
- **Confidence**: High
- **Verified by**: `dotnet build .tmp/fair-block-v2/FairBlockV2.csproj -c Release -m:1 -nodeReuse:false` → exit 0, 0 warnings, 0 errors. Commands `sweep`, `paired`, `diag`, `verify`, `promote` all executed in this session; `repro-next80` reproduced `fair-block` bit-exactly and `RunGameWon` reproduced Arena's count exactly
- **Last updated**: 2026-11-01

### ART-006: Champion strategy source

- **Path**: `.tmp/fair-block-v2/policies/candidates/v2-cur2.cs`
- **Type**: implementation
- **Supporting evidence**: EVD-006, EVD-007, EVD-008, EVD-010, EVD-011
- **Confidence**: High
- **Verified by**: 21,497 characters; SHA256 `0CB1F7DC390AD77092FFFF87881BB7E680DB5695C844B877A66DB8EC2A83E539`; identical to the source reloaded from the staged library; `verify` reports `fair=YES GetHand=1`
- **Note**: the source contains two unreachable private helpers (`SilentChain`, `BlockWinProbability`) emitted by the generator; see OBS-009. Behaviour was measured on exactly this source, so it was not trimmed
- **Last updated**: 2026-11-01

### ART-007: Staged champion library

- **Path**: `.tmp/fair-block-v2/library/.bones/strategy-library/seat-1.json`
- **Type**: dataset
- **Supporting evidence**: EVD-009, EVD-010, EVD-012
- **Confidence**: High
- **Verified by**: `promote --id v2-cur2` printed `persisted=True`, `reloaded id=v2-cur2 matchesExactly=True length=21497`; the recorded effectiveness row is `matchesPlayed=400000 wins=123068`, matching EVD-008. A second independent library at `.tmp/fair-block-v2/library-verify` reproduced the same reload result
- **Last updated**: 2026-11-01

### ART-008: Measurement result documents

- **Path**: `.tmp/fair-block-v2/result-{selection,confirmation,final,diagnostic,sweep,paired}-*.json`
- **Type**: benchmark
- **Supporting evidence**: EVD-005, EVD-006, EVD-007, EVD-008, EVD-013, EVD-014
- **Confidence**: High
- **Verified by**: `result-sweep-910235.json` holds the selection rows (61,601 / 59,073); `result-sweep-910335.json` the confirmation rows (61,505 / 59,032); `result-sweep-910435.json` the final rows (123,068 / 118,161). Re-read after each run
- **Last updated**: 2026-11-01