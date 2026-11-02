# Evidence — bones-actual-champion

| ID | Fact | Source (`file:line` / command) | Method (how you checked) | Confidence | Round | Related |
|---|---|---|---|---|---|---|
| EVD-001 | Fair selection: `c5-newend` 58,150/200,000 (29.075%) vs v9 57,024/200,000 (28.512%), diff +0.563pp, CI [0.282, 0.844] | `.tmp/aggression-search/fair-results.json`; `fair.stdout.txt` | Ran 100 seeds × 2,000 games on a fixed 64-strategy field excluding the candidate and v9 | High | 1 | OBS-001, D1 |
| EVD-002 | Independent confirmation: `c5-newend` 58,060/200,000 (29.030%) vs v9 56,839/200,000 (28.419%), diff +0.611pp, CI [0.330, 0.891] | `.tmp/aggression-search/fair-confirmation.json`; `fair-confirmation.stdout.txt` | Ran the same fixed-field protocol on disjoint seeds 910135-910234 | High | 2 | D2 |
| EVD-003 | Combined 400,000 games: 116,210/400,000 (29.0525%) vs 113,863/400,000 (28.4658%), diff +0.5867pp, CI [0.388, 0.785] | Recomputed from EVD-001 + EVD-002 | Pooled wins/games; two-proportion normal CI | High | 2 | D3 |
| EVD-004 | `c5-newend` is c5 weights plus the corrected new-end follow-on counter; source length 13,517 | `.tmp/aggression-search/library-fair/.bones/strategy-library/seat-1.json` | Parsed the library JSON; counted `newLeftPip` (5), pip weight 2, noFollow 140, chain 30; read the corrected method | High | 3 | D4 |
| EVD-005 | The staged library reloads `c5-newend` | `.tmp/champion-goal/verify-library` on `.tmp/aggression-search/library-fair` | Ran the real `BonesStrategyLibrary.LoadBestStrategy`; output `c5-newend` / source length 13,517, exit 0 | High | 3 | D4 |
| EVD-006 | The four live library hashes are unchanged | `Get-FileHash WIP/Wip.Bones.Host/bones-data/.bones/strategy-library/seat-*.json` | Re-hashed; all four match the baseline | High | 3 | D5 |
| EVD-007 | The fair confirmation is deterministic | `fair-confirmation-first.json` vs `fair-confirmation.json` | Re-ran the confirmation; identical wins/games/champion | High | 3 | D6 |
| EVD-008 | Aggressive variants were monotonically worse than v9 | `.tmp/aggression-search/selection-structural.stdout.txt` | a1-a5 selection rates 27.700 → 26.250; all below v9 in the same field | High | 0 | OBS-001 |