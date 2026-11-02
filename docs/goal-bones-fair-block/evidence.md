# Evidence — bones-fair-block

| ID | Fact | Source (`file:line` / command) | Method (how you checked) | Confidence | Round | Related |
|---|---|---|---|---|---|---|
| EVD-001 | Fair selection: `fair-block` 59,107/200,000 (29.553%) vs c5-newend 58,150/200,000 (29.075%), diff +0.478pp, CI [0.196, 0.761] | `.tmp/block-search/block-results.json`; `block-results.stdout.txt` | Fair fixed field excluding candidates; 100 seeds × 2,000 games | High | 1 | OBS-001, D1 |
| EVD-002 | Independent confirmation: `fair-block` 58,862/200,000 (29.431%) vs c5-newend 58,060/200,000 (29.030%), diff +0.401pp, CI [0.119, 0.683] | `.tmp/block-search/block-confirmation.json`; `block-confirmation.stdout.txt` | Same protocol on disjoint seeds 910135-910234 | High | 2 | D2 |
| EVD-003 | Combined 400,000 games: 117,969/400,000 (29.4923%) vs 116,210/400,000 (29.0525%), diff +0.4398pp, CI [0.2403, 0.6392] | Recomputed from EVD-001 + EVD-002 | Pooled wins/games; two-proportion normal CI | High | 2 | D3 |
| EVD-004 | `fair-block` uses no hidden opponent hands; source contains `NextMatchProbability` and no `state.Hands.GetHand(new ...)` | `.tmp/block-search/library-confirm/.bones/strategy-library/seat-1.json` | Parsed source; counted `NextMatchProbability=2`, hidden opponent access=0, `state.EventLog=1` | High | 3 | D4 |
| EVD-005 | The staged library reloads `fair-block` | `.tmp/champion-goal/verify-library` on `.tmp/block-search/library-confirm` | Real `LoadBestStrategy` returned `fair-block` / source length 16,398, exit 0 | High | 3 | D5 |
| EVD-006 | The four live library hashes are unchanged | `Get-FileHash WIP/Wip.Bones.Host/bones-data/.bones/strategy-library/seat-*.json` | Re-hashed; all four match the baseline | High | 3 | D6 |
| EVD-007 | The fair confirmation is deterministic | `block-confirmation-first.json` vs `block-confirmation.json` | Re-ran confirmation; identical wins/games/champion | High | 3 | D6 |
| EVD-008 | The oracle blocker does not beat the fair blocker | `.tmp/block-search/block-results.json`; `block-confirmation.json` | Oracle 58,186/200,000 (+0.018 vs c5, CI includes zero) and 58,264 (+0.102, CI includes zero) | High | 2 | DEC-001 |