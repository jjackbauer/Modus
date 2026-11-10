# Evidence — tholdem-promotion-experiment

| ID | Fact | Source (`file:line` / command) | Method (how you checked) | Confidence | Round | Related |
|---|---|---|---|---|---|---|
| EVD-001 | The live-model ponder path is gated off in this environment | `$env:THOLDEM_LIVE_MODEL` and `$env:DEEPSEEK_API_KEY` | Read both environment variables; both are unset | High | 0 | OBS-001 |
| EVD-002 | The current active champion is the default v1 (`tholdem-strategy-v1-f100e793`, iteration 0) and no 0.70/0.70 artifact exists yet | `artifacts/tholdem/active-champion.json`; `artifacts/tholdem/strategies/` | Read the pointer and listed the strategy artifacts | High | 0 | OBS-001 |
