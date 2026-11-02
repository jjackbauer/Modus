# Round 2 — Corrected Claim Verdicts (orchestrator verification)

The two round-2 falsifier agents were stopped after they exceeded the round budget without writing
artifacts. This is recorded as RISK-004. The commands below were run by the orchestrator; each row
cites the command and the observed output. No claim is marked Supported from an agent report.

| # | Corrected claim | Verdict | Evidence (command / file) | Counterexample attempted | Reason |
|---|---|---|---|---|---|
| 1 | The host refuses effective MaxGamesPerRun=0 unless an explicit opt-in is true | **Supported** | `Wip.Bones.Host.exe` with `BONES_MAX_GAMES_PER_RUN=0`, no `BONES_ALLOW_UNBOUNDED`, stub, temp dir: exit `-532462766`; log contains `InvalidOperationException ... MaxGamesPerRun=0 disables the per-run game cap`; `.tmp/goal-round2/verify-host.log` | Direct host start, no opt-in, canonical zero | The host no longer starts; it fails in `BonesStartupPlanner.ValidateOrThrow` before Kestrel. |
| 2 | The launcher parses the cap and refuses any effective zero without the opt-in | **Supported** | `BONES_MAX_GAMES_PER_RUN='00'`, no opt-in, `powershell -File bones.ps1 help`: exit 1, message `BONES_MAX_GAMES_PER_RUN=0 (unlimited) requires explicit BONES_ALLOW_UNBOUNDED=true` | Non-canonical `"00"` (the round-1 bypass) | The string-comparison bypass is gone; the value is parsed. |
| 3 | Split gate arena: dev opponents, held-out truth | **Supported** | Gate command with `--arena heldout2000.json --opponents-arena dev2000.json`: `gate arenas: truth='heldout' ...; opponents='dev' ...`; printed opponents = dev maxima; `confidence_paired_n8` delta **+0.041 [+0.028, +0.055]**; `.tmp/goal-round2/verify-gate-clean.json` | Passing held-out as opponents; passing dev as truth | The opponent field is now auditable and independent of the truth arena. |
| 4 | Absent `--opponents-arena` preserves the frozen single-arena result | **Supported** | Gate without the option: opponents = held-out maxima, delta **+0.042 [+0.031, +0.053]**, identical to `.tmp/bench/gate-h1.json` excluding elapsed time | Different option spelling / missing file | Backward compatibility holds. |
| 5 | Checked-in configuration remains bounded and a bounded host starts | **Supported** | `appsettings.json`: MaxGamesPerRun=200, AllowUnbounded=false; bounded host probe (round 1/2) starts and reaches iterations | Empty/zero config | The shipped default is safe and still runnable. |
| 6 | Focused host control-plane suites pass 126/126 | **Supported** | `dotnet test WIP/Wip.Bones.Host.Tests ... --filter <pool/options/status/budget/startup/stage>` -> 126 passed / 0 failed | Running the stale restart test as part of the filter | 126/126 is the focused set; the stale co-learning restart test is outside it and documented in OQ-003. |

Supported: 6 rows — 1, 2, 3, 4, 5, 6
Falsified: 0 rows
Unresolved: 0 rows (the missing independent agents are a process risk, not a claim verdict)

Claim 2 from round 1 (match deadline) is intentionally not part of this table; it is the round-3
objective.
