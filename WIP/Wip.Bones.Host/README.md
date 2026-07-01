# Wip.Bones.Host

Standalone Bones learning host that runs continuous four-player learning iterations and serves the match viewer in one process.

On startup the host:

1. Binds Kestrel and begins the background learning loop immediately (no shell commands or per-iteration HTTP triggers).
2. Runs **Observe -> Ponder -> Play -> Enhance** stages each iteration via `Wip.Bones.Agents`.
3. Calls DeepSeek for strategy authoring, play-turn selection, and strategy enhancement when `ModelProvider=deepseek` (production default).
4. Publishes live match frames to the in-process viewer catalog.

Model providers live in `Wip.Bones.ModelProviders.DeepSeek` and delegate to `DeepSeekModelProvider` from `Wip.Runtime`.

---

## Agentic Improvement Architecture

Bones is a **self-improving strategy system** where four seats play Block Dominoes against each other while LLM agents author, evaluate, and iteratively refine strategies. Every iteration runs a full **Observe → Ponder → Play → Enhance** pipeline, with the system's own promotion evaluation deciding whether a new strategy is good enough to replace the incumbent. Over many iterations, strategies evolve through adversarial co-learning: each seat adapts to counter the others, and the system learns which improvement approaches work.

### The Co-Learning Loop (per iteration, per seat)

```
┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐
│ OBSERVE  │───▶│  PONDER  │───▶│   PLAY   │───▶│ ENHANCE  │
│ (games)  │    │ (author) │    │ (match)  │    │ (refine) │
└──────────┘    └──────────┘    └──────────┘    └──────────┘
```

Each stage delegates to a specialized agent via `Wip.Bones.Agents`:

| Stage | Agent | What it does |
|-------|-------|--------------|
| **Observe** | `BonesObserveAgent` | Plays N observation games (default 3) to gather data on how the current strategy performs against opponents. Produces game transcripts and match histories. |
| **Ponder** | `BonesPonderAgent` + `BonesCoLearningPonderAgent` | Takes the observation data and asks an LLM to **author a compilable C# strategy script** from scratch. Supports compile-retry (up to 3 attempts) — if the LLM produces non-compilable code, the agent feeds back compiler diagnostics and asks for a fix. |
| **Play** | `BonesPlayAgent` | Runs one real match with the newly-authored (or enhanced) strategy to produce a concrete win/loss/score outcome. |
| **Enhance** | `BonesEnhanceStrategyAgent` | Takes the current strategy, the match outcome, and rich enhancement context (see below) and asks an LLM to **produce an improved version**. The enhanced candidate then goes through promotion evaluation. |

### Strategy Lifecycle

```
Markdown (observation notes)
    │
    ▼ Ponder (LLM authors C# script)
  Script (compilable IBonesPlayerSlot)
    │
    ▼ Play (match tested) → Enhance (LLM refines)
  Enhanced Script (candidate)
    │
    ▼ Promotion Evaluation (OR-gate + hard floors)
    ├── Promoted → new active strategy, persisted to library
    └── Rejected → soft-retained if showing merit (score improvement)
```

Strategies exist in two forms:
- **Markdown** — natural-language strategy descriptions derived from game observations. Non-executable; must go through Ponder to become Script.
- **Script** — compilable C# source implementing `IBonesPlayerSlot.ChooseMove()`. Executable; can play games and be enhanced.

The **strategy library** (`BonesStrategyLibrary`) persists each seat's best-known strategy to `{DataDirectory}/.bones/strategy-library/seat-{N}.json`. On restart, seats resume from their library entries, skipping Ponder for seats that already have viable Script strategies with match history.

### Promotion Evaluation (the quality gate)

Before a newly-enhanced candidate replaces the incumbent, it must prove itself in a head-to-head evaluation (`BonesStrategyPromotionEvaluator`):

1. **Parallel evaluation matches**: The candidate and incumbent each play the same number of games (default 20) with the same opponents, same seeds, same game config — only the learning player's strategy differs.
2. **OR-gate decision** with hard floors: A candidate is promoted if **either** its win-rate improvement **or** its score-differential improvement passes a configurable threshold, provided neither metric falls below a hard floor:
   - Win-rate hard floor: ≥ −0.10 (candidate can lose slightly more but still promote if scores are much better)
   - Score-differential hard floor: ≥ −1.0 (prevents promoting score regressions)
   - Default configurable thresholds: +5% win rate or +1 average score
3. **Adaptive evaluation budget** (`BonesAdaptiveEvaluationBudget`): Stops evaluation early when the candidate is clearly dominant (≥90% win rate at checkpoint) or clearly inferior (≤10% win rate), saving up to 50% of evaluation games. Reserved for borderline cases only.

**Process rewards** (`BonesProcessRewardCalculator`) enrich the evaluation beyond binary win/loss:
- **Score delta reward** — positive even when the candidate loses but scores higher on average
- **Compilation success reward** — small positive signal for syntactically valid output
- **Diversity penalty** — negative signal when a candidate is near-identical to previously rejected strategies

### Enhancement Signal (what the LLM sees)

The enhancement prompt (`BonesEnhancePromptBuilder`) feeds the LLM rich context so it can make informed improvements rather than guessing from a single game:

| Signal | Source | Purpose |
|--------|--------|---------|
| **Multi-match history** (≥3 games) | `BonesPlayerKnowledgeStore` | Shows patterns across games — not just one lucky/unlucky outcome. The LLM sees full transcripts including move-by-move action logs and final scores for all 4 seats. |
| **Opponent strategy summaries** | `BonesEnhanceStrategyAgent` → opponent library entries | Tells the LLM what each opponent is doing ("Seat 3 plays a greedy high-pip strategy") so it can **counter-adapt** rather than optimize in a vacuum. |
| **Strategy lineage** | `BonesStrategyLineageStore` | Shows the version chain — what was tried before, what got promoted, what got rejected and why. Prevents the LLM from repeating failed approaches. |
| **Long-context memory** | `BonesLongContextMemoryStore` | Accumulates discovered game patterns ("seat 1 favors early corner play") and **dead-end markers** for approaches that failed 3+ times. Persists across sessions so the system doesn't forget between restarts. |
| **Meta-prompt optimization** | `BonesMetaPromptOptimizer` | Analyzes the last N iterations' outcomes. When it detects 3 consecutive iterations with declining win rate but improving scores, it appends guidance ("prioritize endgame win probability") to the system prompt. |

### Co-Learning Orchestration

`BonesCoLearningPonderAgent` runs Ponder for **all 4 seats** simultaneously when `AllSeatsLearning=true`. Each seat:

1. Checks whether it already has a **viable library entry** (Script or Markdown with ≥1 match played) — if so, skips Ponder and goes directly to Enhance.
2. If not viable, runs Ponder to author a new strategy from observation data.
3. Partial failures are recorded as artifacts without aborting the iteration — other seats continue learning.

This means the system evolves all four strategies in parallel, creating an **adversarial co-learning dynamic**: seat 1's improvements force seats 2-4 to adapt, which in turn forces seat 1 to adapt further, and so on.

### Move Selection Architecture

During Play and Evaluation matches, each seat's strategy needs to choose moves. Bones provides two complementary mechanisms:

**Skill decomposition** (`BonesSkillCompositor` in `Wip.Bones.Agents/Skills/`): Instead of one monolithic `ChooseMove()` implementation, strategies can be composed from reusable heuristic skills evaluated in priority order:

| Skill | What it does | When it activates |
|-------|-------------|-------------------|
| `TileEvaluationSkill` | Picks the highest total-pip-value tile | Always |
| `BlockingSkill` | Identifies opponents close to winning and avoids leaving their pips open | When an opponent has ≤3 tiles remaining |
| `EndgameSkill` | Prioritizes doubles then high-pip tiles to go out | When the current player has ≤3 tiles |
| `OpponentModelSkill` | Analyzes recent opponent moves and avoids pips matching their patterns | When opponent move history is available |

If a skill cannot produce a move (returns null), the compositor delegates to the next skill. If no skill produces a move, it falls back to the first legal move as a safety net.

**Sub-agent delegation** (`BonesSubAgentDelegator` in `Wip.Bones.Agents/Delegation/`): When a strategy is uncertain (multiple candidate moves with similar heuristic scores), it can delegate the decision to a **deep-think sub-agent** — a separate LLM call with extended context and potentially a different model. If the sub-agent times out, the delegator falls back to the default heuristic.

### Meta-Learning

Two mechanisms allow the system to improve not just strategies but the improvement process itself:

**Meta-prompt optimization** (`BonesMetaPromptOptimizer`): Tracks the last 3 iterations' win-rate and score-differential deltas. When it detects a pattern (e.g., "scores keep improving but win rate keeps declining"), it appends targeted guidance to the enhancement system prompt. When metrics are stable, the prompt remains unchanged.

**Curriculum learning** (`BonesCurriculumBootstrapper`): New strategies don't jump straight into the full 4-player game. Instead:

| Tier | Players | Target Score | Purpose |
|------|---------|-------------|---------|
| Simplified | 2 | 4 | Initial bootstrapping — learn basic Dominoes tactics |
| Intermediate | 3 | 6 | Handle multi-player dynamics |
| Full | 4 | 8 | Standard game, full complexity |

A seat graduates to the next tier when its library entry has sufficient match experience. This prevents new strategies from being overwhelmed before they've learned fundamentals.

### Persistence & Cross-Session Memory

All learning state persists to disk under `{DataDirectory}/.bones/`:

| Store | Path | What it holds |
|-------|------|---------------|
| `BonesStrategyLibrary` | `strategy-library/seat-{N}.json` | Each seat's best strategy (kind, source, effectiveness record) |
| `BonesStrategyLineageStore` | `sessions/{id}/artifacts/` | Version chain with predecessor→successor, promotion outcome, and diff summary for every enhance cycle |
| `BonesLongContextMemoryStore` | `long-context-memory/` | Discovered game patterns and failed approaches marked as dead-ends after 3+ rejections |
| Candidate storage (M1) | `strategy-library/seat-{N}-candidates.json` | Rejected candidates that showed merit (high score differential) — retained for potential future reconsideration |

The **M1 soft retention** policy means rejected strategies are not permanently discarded. Candidates with high score differentials are stored alongside their full evaluation context (win rates, score deltas, confidence intervals) and can be re-evaluated later if the incumbent changes.

### Budget & Efficiency

The learning loop runs with configurable game budgets to prevent runaway resource consumption:

- **`ComputePlannedIterationGameBudget`**: Computes the total games needed for an iteration — `GameCount` (observation) + 1 (play) + `EvaluationMatchCount × 2` (promotion evaluation, since each match runs 2 games). The formula accounts for the `× 2` factor to prevent budget under-estimation.
- **`BonesGameSimulationBudget`**: Enforces `MaxGamesPerRun` across all iterations. When the budget is exhausted, the loop stops gracefully and reports `CapReached`.
- **Adaptive evaluation** further reduces waste: clearly good or bad candidates are decided early, saving games for borderline cases.

---

## How to run

From the repository root (`Modus.slnx`):

### 1. Set API key (DeepSeek production default)

```powershell
$env:DEEPSEEK_API_KEY = "your-key-here"
```

```bash
# Linux/macOS
export DEEPSEEK_API_KEY="your-key-here"
```

### 2. Start the host

```powershell
dotnet run --project WIP/Wip.Bones.Host
```

By default the host listens on `http://127.0.0.1:8080` and begins learning iterations immediately.

Startup logs include `startup-config:` lines for data directory, listen URL, iteration delay, and active model provider (`deepseek` or `stub`).

### 3. Open the match viewer

```
http://127.0.0.1:8080/viewer/
```

### 4. Check loop status

```powershell
Invoke-RestMethod http://127.0.0.1:8080/api/bones/status
Invoke-RestMethod http://127.0.0.1:8080/health
```

```bash
curl http://127.0.0.1:8080/api/bones/status
curl http://127.0.0.1:8080/health
```

When DeepSeek is active, status JSON includes a `modelProvider` object. Confirm production wiring with `provider=deepseek`:

```json
{
  "isRunning": true,
  "iterationCount": 1,
  "currentStage": "play",
  "viewerUrl": "http://127.0.0.1:8080/bones/sessions/.../matches/...",
  "hostSessionId": "...",
  "modelProvider": {
    "provider": "deepseek",
    "model": "deepseek-chat",
    "baseUrl": "https://api.deepseek.com/",
    "timeoutSeconds": 30,
    "apiKeySource": "environment:DEEPSEEK_API_KEY"
  }
}
```

The response references the key source only; it never includes the API key value.

### 5. Stop

- Press **Ctrl+C** in the terminal, or
- Request a graceful stop after the current iteration:

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:8080/api/bones/stop
```

```bash
curl -X POST http://127.0.0.1:8080/api/bones/stop
```

### 6. Restart a stopped loop (optional)

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:8080/api/bones/start
```

```bash
curl -X POST http://127.0.0.1:8080/api/bones/start
```

## Configuration

Set options in `appsettings.json` under `BonesHost`, or via environment variables (double underscore for nesting):

| Setting | `appsettings.json` key | Environment variable |
|---|---|---|
| Data directory | `BonesHost:DataDirectory` | `BonesHost__DataDirectory` |
| Listen URL | `BonesHost:ListenUrl` | `BonesHost__ListenUrl` or `ASPNETCORE_URLS` |
| Delay between iterations (ms) | `BonesHost:IterationDelayMs` | `BonesHost__IterationDelayMs` |
| Model provider (`deepseek` or `stub`) | `BonesHost:ModelProvider` | `BonesHost__ModelProvider` |
| DeepSeek base URL | `BonesHost:DeepSeek:BaseUrl` | `BonesHost__DeepSeek__BaseUrl` |
| DeepSeek model | `BonesHost:DeepSeek:Model` | `BonesHost__DeepSeek__Model` |
| DeepSeek timeout (seconds) | `BonesHost:DeepSeek:TimeoutSeconds` | `BonesHost__DeepSeek__TimeoutSeconds` |
| DeepSeek API key source | `BonesHost:DeepSeek:ApiKeySource` | `BonesHost__DeepSeek__ApiKeySource` |
| `gameCount` | `BonesHost:DefaultParameters:GameCount` | `BonesHost__DefaultParameters__GameCount` |
| `seed` | `BonesHost:DefaultParameters:Seed` | `BonesHost__DefaultParameters__Seed` |
| `targetScore` | `BonesHost:DefaultParameters:TargetScore` | `BonesHost__DefaultParameters__TargetScore` |
| `learningPlayerId` | `BonesHost:DefaultParameters:LearningPlayerId` | `BonesHost__DefaultParameters__LearningPlayerId` |
| Max games per run | `BonesHost:MaxGamesPerRun` | `BONES_MAX_GAMES_PER_RUN` or `BonesHost__MaxGamesPerRun` |
| Resume from best | `BonesHost:ResumeFromBest` | `BONES_RESUME_FROM_BEST` or `BonesHost__ResumeFromBest` |

Default `MaxGamesPerRun` is `0` (unlimited). Default `ResumeFromBest` is `true`.

### Operator setup: bounded runs and resume

Use these environment variables when operating the learning host across restarts:

**Bounded runs** — cap total simulated games (observe + play + promotion evaluation) per process start:

```powershell
$env:BONES_MAX_GAMES_PER_RUN = "50"
dotnet run --project WIP/Wip.Bones.Host
```

```bash
BONES_MAX_GAMES_PER_RUN=50 dotnet run --project WIP/Wip.Bones.Host
```

When the cap is reached, the loop stops gracefully after the current iteration completes. Status reports `gamesSimulated` and `maxGamesPerRun`.

**Resume from prior bests** (default) — load each seat's best-known strategy from `{DataDirectory}/.bones/strategy-library/` at iteration start and skip ponder for bootstrapped seats:

```powershell
$env:BONES_RESUME_FROM_BEST = "true"   # BONES_RESUME_FROM_BEST=true
dotnet run --project WIP/Wip.Bones.Host
```

**Cold-start experiments** — ignore the strategy library and re-seed default opponent markdown v1 strategies; the learning player runs ponder again:

```powershell
$env:BONES_RESUME_FROM_BEST = "false"  # BONES_RESUME_FROM_BEST=false
dotnet run --project WIP/Wip.Bones.Host
```

Example combining a bounded run with resume enabled:

```powershell
$env:BONES_MAX_GAMES_PER_RUN = "50"
$env:BONES_RESUME_FROM_BEST = "true"
dotnet run --project WIP/Wip.Bones.Host
```

Default `appsettings.json` uses `ModelProvider=deepseek` with `deepseek-chat` against `https://api.deepseek.com`. Supported models: `deepseek-chat`, `deepseek-reasoner`.

### DeepSeek production setup (default)

Production runs use DeepSeek adapters. Before starting the host, set your API key (see step 1 above), then:

```powershell
dotnet run --project WIP/Wip.Bones.Host
Invoke-RestMethod http://127.0.0.1:8080/api/bones/status
```

The status JSON should report `"provider":"deepseek"` under `modelProvider`, along with the configured model name, base URL, timeout, and an `apiKeySource` reference (never the secret value).

### Offline / CI stub mode

For offline development or tests without an API key, run with stub model providers:

```powershell
$env:BonesHost__ModelProvider = "stub"
dotnet run --project WIP/Wip.Bones.Host
Invoke-RestMethod http://127.0.0.1:8080/api/bones/status
```

```bash
BonesHost__ModelProvider=stub dotnet run --project WIP/Wip.Bones.Host
curl http://127.0.0.1:8080/api/bones/status
```

Status should report `"provider":"stub"`. Stub mode skips DeepSeek HTTP calls entirely.

Example with a custom port and no delay between iterations:

```powershell
$env:BonesHost__ListenUrl = "http://127.0.0.1:9090"
$env:BonesHost__IterationDelayMs = "0"
dotnet run --project WIP/Wip.Bones.Host
```

```bash
BonesHost__ListenUrl=http://127.0.0.1:9090 \
BonesHost__IterationDelayMs=0 \
dotnet run --project WIP/Wip.Bones.Host
```

## HTTP API

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/health` | Liveness probe |
| `GET` | `/api/bones/status` | Loop state, iteration count, granular stage (`Observe`/`Ponder`/`Play`/`Enhance`/`Complete`/`Failed`), viewer URL, learning player block, `learningPlayerStatus`, `lastIterationError`, `lastPromotionOutcome`, `libraryEffectiveness`, model provider diagnostics |
| `POST` | `/api/bones/stop` | Graceful stop after the current iteration completes |
| `POST` | `/api/bones/start` | Resume a previously stopped loop (not required for the first iteration) |
| `GET` | `/viewer/` | Match viewer SPA |

Match replay and catalog routes are mapped by `Wip.Bones.Web`.

## Operator diagnostics

When triaging a learning loop session, inspect session artifacts under `{DataDirectory}/.wip/sessions/{sessionId}/artifacts/` and poll `GET /api/bones/status`.

### Status response fields

| Field | Meaning |
|---|---|
| `currentStage` | Granular workflow stage: `Observe`, `Ponder`, `Play`, `Enhance`, `Complete`, `Failed`, `CapReached`, or `Stopped` |
| `failureStage` | Stage that failed when `currentStage` is `Failed` |
| `learningPlayer` | Active strategy and effectiveness for the learning seat once a pointer exists |
| `learningPlayerStatus` | `"pending"` when ponder/observe is in flight and no active pointer exists yet |
| `learningPlayerError` | Non-fatal knowledge-store read failure (for example corrupt active pointer JSON) |
| `lastIterationError.message` | Exception message from the most recent failed iteration |
| `lastIterationError.stage` | Workflow stage (`Ponder`, `Play`, `Enhance`, or `Workflow`) where the failure occurred |
| `lastPromotionOutcome` | `Promoted` or `Rejected` from the latest `bones-strategy-promotion` artifact |
| `libraryEffectiveness` | Best library strategy effectiveness snapshot for the learning seat |

### Key execution artifacts

| Artifact | Fields / purpose |
|---|---|
| `bones-ponder-execution` | `CompileAttempts`, `FinalCompileSucceeded`, optional `LastCompileFailureReason` after compile retry |
| `bones-play-match-execution` | Confirms play stage completed for the session |
| `bones-enhance-strategy-execution` | `PromotionOutcome`; absent when an upstream stage threw |
| `bones-strategy-promotion` | Full evaluation metrics and `Outcome` (`Promoted` or `Rejected`) on every enhance invocation |
| `bones-workflow-stage-failure` | `FailureStage` and `ErrorMessage` when `RunWorkflowAsync` aborts |

A failed iteration does not stop the background loop: `iterationCount` continues to advance and the host keeps running until stop, cap, or shutdown.

## Tests

```powershell
# Core agent tests (314 passing, 316 total)
dotnet test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj

# Host integration tests
dotnet test WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj

# DeepSeek provider tests
dotnet test WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/Wip.Bones.ModelProviders.DeepSeek.Tests.csproj
```

## Legacy shell path

The `WIP_BONES_E2E` shell integration under `Wip.ShellHost` remains available for legacy end-to-end tests only. Prefer this dedicated host for day-to-day Bones learning and viewer operation.